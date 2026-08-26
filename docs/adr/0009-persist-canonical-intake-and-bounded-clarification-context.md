# ADR 0009: Persist Canonical Intake and Bounded Clarification Context

- **Status**: Accepted
- **Date**: 2026-08-22
- **Clarified**: 2026-08-26
- **Decision owners**: Project maintainer
- **Supersedes**: ADR 0006 for request-intake preparation state
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `docs/adr/0005-retain-terminal-request-intake-tombstones.md`, `docs/adr/0007-use-sparse-model-patches-and-a-deterministic-reducer.md`

## Context

The current preparation flow relies on process-local model conversation state and a
full candidate returned on every turn. Restart loses conversational context, and the
model is asked to reconstruct accepted draft state.

The target flow needs only a small amount of durable context:

- the canonical candidate;
- one ordered environment or role choice set;
- lifecycle state and timestamps;
- a candidate-change version plus optimistic-concurrency protection; and
- immutable ready preparation identity for stale-card safety.

Persisting raw conversations, prompts, model reasoning, complete tool payloads, or
provider sessions would increase privacy, retention, migration, and coupling costs
without making business state more authoritative.

A previous target design also proposed a second pending-revision candidate beside a
ready snapshot. That adds dual-state persistence and cancellation semantics. It is not
required when a ready preparation is immutable and a material revision receives a new
preparation identity.

## Decision

Persist one canonical preparation aggregate per `PreparationId`. At most one active
`Collecting` or `Ready` preparation exists for one authenticated actor and exact Teams
conversation.

Persist only:

- immutable `PreparationId`;
- authenticated actor/conversation binding;
- lifecycle state;
- sanitized canonical candidate;
- `CandidateVersion`;
- storage-managed `ConcurrencyVersion` or equivalent optimistic token;
- at most one bounded clarification context;
- `CreatedAt`, `UpdatedAt`, ready and terminal timestamps;
- mandatory predecessor preparation ID when created by revision; and
- bounded interpreter version/audit metadata without raw text.

Do not persist raw requester transcripts, raw prompts, provider conversation state,
model reasoning, agent-authored search queries, raw proposals, or complete MCP payloads.

Canonical justification is persisted because it is an intentional request domain field,
not retained conversation history.

### Preparation identity and ready immutability

A preparation is mutable only while `Collecting`. When it becomes `Ready`, its candidate
and `CandidateVersion` are immutable. `PreparationId` then identifies one exact card
scope.

A committed material revision never mutates `Ready A` back to collecting. In one atomic
commit it:

1. marks `A` `Superseded`; and
2. creates a new `Collecting` or `Ready` preparation `B` with a new `PreparationId`.

The ready card therefore needs only closed schema version plus `PreparationId`. A stale
card reloads a terminal old preparation and cannot submit the replacement scope.

There is no pending-revision candidate and no revision-cancellation command.

### Version definitions

`CandidateVersion` starts at zero when a preparation row is created. If the same
creation transaction persists a material initial or revised candidate, it increments
once to one. A clean `/new` preparation therefore remains at zero. It is monotonic
inside one preparation and increments once for each later committed material canonical
candidate change. It does not increment for:

- discussion, unrelated, unclear, or submission-intent turns;
- rejected operations;
- value-equal no-ops; or
- clarification-only persistence.

`ConcurrencyVersion` changes on every persisted aggregate update, including
clarification-only changes, lifecycle transitions, lazy expiry, and candidate commits.
It is used for optimistic concurrency and is not card/request authority.

`CandidateVersion` is not a clarification freshness token. Clarification correctness
depends on invoking the agent from the current candidate/context snapshot and verifying
`ConcurrencyVersion` before the short commit.

### Clarification context

Persist only:

- `PreparationId` through aggregate ownership;
- target (`environment` or `role`);
- no more than five choices in stable display order, each with its exact canonical ID
  and safe authoritative display fields needed to distinguish it; and
- `CreatedAt`.

The renderer derives 1-based numbering strictly from persisted order. The same ordered
records are reconstructed as bounded provider-neutral agent input after restart.
Environment records may include environment name/ID, authoritative client name/ID,
region, and primary/recovery classification. Role records include exact role ID and safe
display name.

The agent interprets every free-text reply—including numeric, ordinal, exact-ID,
descriptive, and multilingual wording—into the ordinary `updateDraft` environment or
role exact-ID operation. It returns `unclear` when the reference cannot be safely
resolved. Core exact-reloads and validates the proposed ID through the normal reducer;
it neither maps positions to IDs nor uses displayed-choice membership as an acceptance
condition.

Core does not parse the requester’s wording. Persisted context enables restart-safe
semantic references at the agent boundary; it is untrusted context, not authorization
evidence or a separate mutation protocol.

Accepted environment/role target-field operations consume matching context; an
accepted incident operation that establishes or changes environment scope consumes
environment context; and an accepted environment change clears role context.
Independent accepted justification changes preserve unrelated context. Rejected
operations, value-equal no-ops, non-mutating acts, and transient failures preserve
context unless authoritative reads prove its choices stale. Newly required context
replaces prior context using environment-before-role precedence. Exact `/new` and
terminal lifecycle transitions remove or make context unusable.

An active clarification context prevents `Ready`. Clarification created while revising
`Ready A` atomically supersedes A and creates predecessor-linked `Collecting B` with a
copied candidate and the new context. An accepted ordinary target-field operation on B
consumes the context and reevaluates readiness normally.

Clarification-only persistence retains `CandidateVersion` but changes
`ConcurrencyVersion`. There is no independent clarification TTL; context is usable only
while its preparation is active and until the deterministic lifecycle consumes,
replaces, or invalidates it.

### Lifecycle and expiry

The persisted states are:

- `Collecting`;
- `Ready`;
- `Submitted`;
- `Superseded`; and
- `Expired`.

`ReadyDeadline` is exactly 30 minutes after `ReadyAt`. Expiry is evaluated lazily on
load or confirmation; no background sweeper is required. Non-mutating turns do not
refresh the deadline. `Collecting` has no feature-specific inactivity TTL in this
increment. Terminal row retention follows ADR 0005.

### Idempotency and concurrency

The request table has a durable unique constraint on `Request.PreparationId`. A
matching-owned replay against an already `Submitted` preparation returns the existing
request identity/status. A concurrent unique-key loser reloads and returns the same
request.

Agent/MCP execution occurs without a database transaction or SQLite write lock. The
agent receives the current candidate and active clarification context in one snapshot.
The application commits under a short boundary after checking `ConcurrencyVersion` and
active-preparation uniqueness. A proposal interpreted against changed candidate or
context is rejected rather than silently applied to newer state.

A process-local conversation gate may reduce contention, but the durable correctness
boundary is optimistic concurrency plus uniqueness.

## Rationale

Canonical candidate persistence lets every turn begin from application-owned state.
Model omission and provider-memory loss cannot erase accepted values.

One bounded ordered choice set with exact IDs and safe display fields is the minimum
durable context needed for restart-safe multilingual and descriptive clarification. It
exposes no raw conversation and keeps Core independent of language.

Immutable ready preparation identity solves stale-card safety without adding a second
card version field. A card references one exact immutable preparation; any revision gets
a different identity.

Separating candidate and concurrency versions prevents one overloaded version counter
from serving incompatible purposes. Candidate version records canonical candidate
progress; concurrency version protects candidate/context snapshots and commits.

One active candidate and immediate ready supersession avoid dual-state revision
machinery while preserving the critical rule that an old card cannot confirm a revised
scope.

## Consequences

### Positive

- Candidate and clarification context survive restart without raw conversation
  persistence.
- Multilingual and descriptive clarification remains at the agent boundary and produces
  the same ordinary sparse exact-ID operations as other updates.
- Stale card rejection uses one immutable preparation identity.
- Candidate progress and OCC have distinct, explicit version semantics, with no dead
  candidate-version clarification binding.
- Ready revisions have one atomic transition and no pending-revision state.
- Duplicate confirmation converges through a named durable idempotency key.
- No database lock is held across model/tool latency.
- Terminal tombstones preserve stale-action determinism.

### Negative and risks

- Persistence schema grows to include lifecycle timestamps, two version concepts,
  clarification context, predecessor linkage, and bounded interpreter metadata.
- Ready revision requires atomic old-terminal/new-active creation.
- A process crash after agent interpretation but before commit loses that interpreted
  turn; the requester must retry.
- No raw history means general coreference outside canonical state or active choices is
  intentionally unsupported.
- Collecting preparations do not expire automatically in this increment and rely on
  `/new` or later retention policy for replacement.
- Optimistic conflict handling must give safe retry guidance without replaying stale
  model output.

## Alternatives considered

### Persist complete conversation history

Rejected because it increases privacy, retention, token, migration, and provider-coupling
cost while remaining less authoritative than canonical state.

### Persist provider/MAF session objects

Rejected because provider session shape is infrastructure-specific and unnecessary for
business correctness.

### Use a mutable ready preparation plus candidate version in the card

Rejected for this bounded feature. A separate version field can be safe, but immutable
ready identity is simpler and aligns with retained terminal tombstones.

### Keep a ready snapshot active while a pending revision is collected

Rejected because it introduces two draft states, restoration/cancellation semantics,
additional races, and a risk that the old card remains confirmable while the requester
is revising scope.

### Parse ordinal or numeric replies deterministically

Rejected because it creates a language/format fast path and inconsistent multilingual
behavior. The agent receives bounded ordered choices and returns an ordinary exact-ID
sparse patch or conservative `unclear`.

### Persist a separate clarification-selection protocol

Rejected because target/index payloads, index-to-ID conversion, and selection-specific
candidate-version checks duplicate the ordinary reducer. Persisted ordered choices are
semantic agent context; exact authoritative reload and `ConcurrencyVersion` OCC already
provide the required Core correctness boundaries.

### Use one version counter for clarification and OCC

Rejected because candidate meaning and aggregate-write concurrency change at different
times. Clarification-only persistence is the clearest counterexample.

### Hold a database lock across the full model turn

Rejected as a durable requirement because model/tool latency would amplify contention.
A process-local gate may still be used, while the database commit remains short.

## Revisit criteria

Revisit this decision if:

- a real multi-instance deployment requires distributed coordination beyond SQLite/OCC;
- clarification choices must exceed the bounded single-context model;
- product requirements demand resumable natural-language context beyond canonical
  state;
- ready preparations must be editable in place for an external protocol reason;
- collecting inactivity retention becomes an operational requirement; or
- a formal privacy/retention program changes what preparation/audit metadata may be
  stored.

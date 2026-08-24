# ADR 0009: Persist Canonical Intake and Bounded Clarification Context

- **Status**: Proposed; supersedes ADR 0006 when accepted
- **Date**: 2026-08-22
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/adr/0006-persist-canonical-intake-state-not-conversation-history.md`, `SPEC-deterministic-request-intake.md`, `docs/deterministic-request-intake-design.md`

## Context

ADR 0006 correctly separates durable canonical intake state from process-local model
conversation history. It deliberately excludes clarification option lists from
persistence, so a restart loses the context needed to interpret replies such as "the
second one."

The deterministic reducer design makes application-rendered environment and role
choices part of the safe interaction contract. Exact ordinal replies can be resolved
without model history only when the active choice target and ordered canonical IDs are
available after restart.

The earlier feature draft also introduced a second persisted pending-revision
candidate behind an active ready snapshot. That would preserve an old confirmable
draft while a new incomplete revision is developed, requiring suspended confirmation,
rollback, cancellation, dual candidate state, and additional races. This is too much
state for the user value provided.

## Decision

Persist:

- one active sanitized canonical candidate;
- one lifecycle state and optimistic-concurrency version;
- one optional bounded clarification context containing the preparation ID, candidate
  version, target, ordered canonical environment or role IDs, and creation time; and
- immutable ready details, reserved request ID, and deadline only when the active intake
  is ready.

Do not persist raw messages, prompts, transcripts, model responses, model reasoning,
complete MCP payloads, or serialized MAF sessions.

Applying a choice consumes the matching context. A committed candidate change clears
stale context and stores only the next active context. Context whose preparation ID or
candidate version no longer matches is ignored and removed.

Do not persist a pending revision alongside an active ready snapshot. The first
accepted material revision atomically:

- supersedes the old ready intake; and
- creates a new collecting or ready intake with a new preparation identity.

Discussion, value-equal proposals, unsupported proposals, and model/source failures
preserve the old ready snapshot. There is no `/cancel-revision` command and no path to
revive a superseded ready identity.

This decision preserves ADR 0006's central principle—canonical application state is
durable and conversation history is not—but supersedes its explicit exclusion of all
clarification option lists.

## Rationale

A small structured choice record is application-owned, provider-neutral, bounded, and
sufficient to make exact ordinal replies deterministic after restart. It is not a
conversation transcript and cannot establish authorization or submission.

Maintaining only one active candidate avoids dual-state revision logic. Immediate
ready invalidation makes the security behavior simple: once an accepted material
change commits, the old card is stale. The requester sees the new collecting or ready
state rather than a hidden suspended revision.

## Consequences

### Positive

- Exact and ordinal environment/role selections survive restart without model history.
- Durable data remains compact and contains no free-form assistant or provider content.
- Only one active candidate exists for one actor/conversation.
- Ready revision and confirmation races have a simple commit-wins rule.
- Stale cards are rejected by terminal durable status rather than pending-revision
  flags.
- No cancellation command, rollback transition, or dual-candidate merge is needed.

### Negative and risks

- The first accepted incomplete revision invalidates a previously confirmable ready
  card; the requester must complete the new draft or re-enter the old values.
- Structured choices add persistence schema and cleanup/migration work.
- A stale visual Teams card may remain visible when best-effort replacement fails, even
  though durable confirmation rejects it.
- Ordered IDs reveal some reference identifiers in SQLite, though less content than a
  transcript or complete tool payload.
- The exact active choice context must be version-bound to avoid applying an ordinal to
  a newer candidate.

## Alternatives considered

### Keep all clarification context process-local

Rejected because restart would turn a previously valid ordinal reply into an
unnecessary model clarification and would make deterministic choice resolution depend
on session continuity.

### Persist complete model or Teams conversation history

Rejected because it adds privacy, retention, provider coupling, migration, and access
control obligations without becoming authoritative state.

### Persist a pending revision while preserving the old ready snapshot

Rejected because it requires confirmation suspension, `/cancel-revision`, two working
candidates, rollback semantics, and more concurrency cases for limited user benefit.

### Mutate one ready intake back to collecting

Rejected because a ready preparation ID should remain an immutable representation of
one card scope. Superseding it and creating a new intake keeps stale-card behavior and
audit evidence explicit.

## Revisit criteria

Revisit this decision if:

- measured users frequently abandon revisions and need one-click restoration of the
  previous ready snapshot;
- privacy or retention policy prohibits persisting even bounded choice IDs;
- multi-host deployment requires a broader durable conversation coordination design;
- Teams presentation requires durable card activity references; or
- choice sets grow beyond the current bounded catalog and require pagination or search
  sessions.

Any replacement must preserve exact version binding, stale-card rejection, and the
rule that conversational context is not authorization evidence.

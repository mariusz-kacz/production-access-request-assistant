# Specification: Agent-Interpreted Request Intake with Deterministic Core

- **Status:** Approved simplification target; isolated implementation and production cutover pending
- **Capability ID:** `deterministic-request-intake`
- **Target branch:** `feature/decouple-teams-approval-flow`
- **Scope:** Authenticated Microsoft Teams request preparation with model-based language interpretation and deterministic confirmation
- **Related decisions:** [ADR 0005](docs/adr/0005-retain-terminal-request-intake-tombstones.md), [ADR 0007](docs/adr/0007-use-sparse-model-patches-and-a-deterministic-reducer.md), [ADR 0008](docs/adr/0008-separate-read-only-context-capabilities-by-authoritative-source.md), [ADR 0009](docs/adr/0009-persist-canonical-intake-and-bounded-clarification-context.md), [ADR 0010](docs/adr/0010-exact-reload-agent-resolved-environments.md), [ADR 0011](docs/adr/0011-isolate-reference-authority-in-the-modular-monolith.md)
- **Target MCP contract:** [deterministic-request-intake-mcp-contract.md](docs/contracts/deterministic-request-intake-mcp-contract.md)
- **Target test matrix:** [deterministic-request-intake-test-matrix.md](docs/evaluation/deterministic-request-intake-test-matrix.md)

## 1. Authority and relationship to the running system

This specification defines the target replacement for request-intake preparation and
the modular persistence boundary required to support it. It changes the preparation
boundary before human confirmation, the model-visible read-only context-tool catalog,
and the ownership of reference facts versus request-lifecycle data. It remains one
executable ASP.NET Core host and one deployment artifact.

Until implementation and the required evidence are complete, current as-built product,
architecture, security, orchestration, MCP, testing, and operator documents continue to
describe the running system. After implementation passes its gates, those documents
must be reconciled in one focused documentation task.

This specification supersedes any feature task, design note, ADR wording, test
instruction, or partial implementation that:

- interprets requester free-text outside the model-backed agent, except exact `/new`;
- asks Core to prove that requester wording supports a structured proposal;
- returns or merges a model-owned full candidate snapshot;
- treats model-visible MCP results as application authority;
- parses numeric, ordinal, identifier-like, clear, reset, or submission wording in
  deterministic application code; or
- treats clarification replies as a separate target/index mutation protocol rather
  than ordinary sparse exact-ID proposals; or
- keeps a mutable ready snapshot behind a second pending-revision candidate.

## 2. Objective

Replace full-snapshot model-driven request preparation with a bounded architecture in
which:

1. one model-backed agent interprets every authenticated requester free-text turn except
   the exact `/new` protocol command;
2. the agent returns only a closed, provider-neutral structured proposal;
3. Core owns canonical state, deterministic proposal application, authoritative
   enterprise facts, dependency rules, readiness, persistence, lifecycle, and typed
   outcomes;
4. Core independently searches or reloads environment, client, role, and incident facts
   through application ports;
5. reference facts and request-lifecycle state are owned by separate modules and
   separate local databases behind Core ports;
6. application code renders every requester-visible response and card; and
7. an authenticated Adaptive Card action is the only path that can create a production
   access request.

The feature must not create a second natural-language interpretation layer beside the
agent.

## 3. Primary architectural rule

> **Requester free-text is opaque to deterministic business logic. The agent interprets language; Core validates and applies structured proposals.**

This is a hard type and behavior boundary.

After Teams authentication and transport validation, every nonblank requester free-text
turn except exact `/new` is sent to the agent before any semantic business operation is
selected. This includes short, numeric, identifier-like, apparently obvious, and
multilingual messages.

Deterministic code must not derive any of the following from requester free-text:

- dialogue act;
- field operation;
- field value;
- clarification-reference meaning;
- discussion topic;
- natural-language reset meaning;
- submission intent; or
- authoritative enterprise identity.

Core preparation APIs must not accept requester free-text as input required for proposal
validation, reduction, readiness, persistence, or lifecycle decisions.

The sole deterministic requester-text protocol command is exact `/new`, defined in
Section 9. Structured application-generated UI actions are separate protocol events and
may be handled deterministically through their closed payload contracts.

Supporting another human input language must require no Core rule change.

## 4. Preserved product invariants

1. Authenticated personal Microsoft Teams remains the only requester channel.
2. One bounded Microsoft Agent Framework agent owns requester-language interpretation.
3. The agent has no state-changing business tool.
4. The agent receives exactly four approved read-only MCP capabilities.
5. One requester has at most one active intake in one exact Teams conversation.
6. Separate Teams conversations may contain separate active intakes.
7. Requester identity comes only from authenticated server context.
8. Client is always derived from the authoritative environment.
9. Access duration remains fixed at eight hours.
10. Canonical state never depends on model conversation memory.
11. Business approval, DevOps approval, provisioning, retry, audit, and grant lifecycle
    remain deterministic downstream behavior.
12. Adaptive Card confirmation remains mandatory before request creation.
13. Submitted request scope is immutable.
14. Free-text handling can never directly create, approve, provision, retry, revoke, or
    grant access.
15. No second agent, generic workflow engine, requester channel, message broker,
    distributed lock, or deployable service is added.
16. The final target remains one executable modular monolith, while keeping the
    reference-authority boundary replaceable by a remote client without changing Core
    request-intake or workflow rules.

## 5. Glossary

| Term | Definition |
|---|---|
| **Intake** | The preparation capability and conversation flow before request creation. |
| **Preparation** | One persisted intake aggregate with an immutable `PreparationId`. It is mutable only while `Collecting`; once `Ready`, its candidate is immutable. |
| **Candidate** | The sanitized canonical request fields owned by the application inside one preparation. |
| **ConcurrencyVersion** | A storage-managed optimistic-concurrency token. It changes on every persisted aggregate update, including lifecycle or clarification-only changes. |
| **Clarification context** | A bounded, persisted ordered choice set with exact canonical IDs and safe authoritative display fields. It is supplied to the agent as semantic context and is neither an authorization allowlist nor a separate mutation protocol. |
| **Ready preparation** | An immutable preparation whose candidate is complete and eligible for card confirmation until `ReadyDeadline`. Its `PreparationId` identifies the exact reviewed scope. The accepted product baseline retains the 30-minute confirmation window. |
| **Ready card** | An application-rendered Adaptive Card whose payload references one exact immutable ready preparation. |
| **Request** | The immutable `AwaitingBusinessApproval` domain entity created only by successful card confirmation. |
| **Material change** | A committed canonical candidate-field change. Persisting or replacing clarification context without changing candidate fields is not itself a material candidate change, although a clarification revision against `Ready` still requires a successor preparation because ready rows are immutable. |
| **Structured proposal** | The provider-neutral closed `TurnProposal` returned by the agent. It is an interpretation proposal, not authority. |
| **Reference authority** | The module that owns direct access to clients, environments, environment-role assignments, and incidents and implements the authoritative Core ports. |
| **Workflow persistence** | The module that owns direct access to preparations, authenticated-principal snapshots, requests, approvals, operations, grants, and audit evidence. |

`PreparationId` is not a mutable draft version and is not an optimistic-concurrency
token. A card containing only `PreparationId` is safe because a ready preparation is
immutable and any material revision creates a new preparation identity.

## 6. Responsibility boundary

| Component | Responsible for | Must not own |
|---|---|---|
| Requester | Natural-language description, revision, selection, question, submission intent, and exact `/new` | Acting identity, client authority, approver, duration, approval, provisioning, grant state |
| Agent interpreter | Semantic interpretation of current free-text and active clarification choices; ordinary sparse exact-ID proposal; bounded read-only context gathering | Canonical state, authorization, enterprise truth, request creation, approval, provisioning, requester-visible prose |
| Core | Proposal legality; canonical merge; authoritative reloads; dependencies; readiness; persistence; lifecycle; typed outcomes | Requester-language interpretation |
| Authoritative ports | Environment, client, entitlement, and incident facts | Requester-language interpretation or model-visible tool trust |
| Reference-authority module | Reference database, deterministic search execution, exact reference reads, synthetic catalog seeding, and authority-port implementations | Request lifecycle, MCP wire contracts, requester interpretation, approval, or provisioning |
| Workflow-persistence module | Workflow database, preparation/request stores, migrations, OCC, uniqueness, and synthetic principal snapshots | Reference catalog tables, search, MCP, or requester interpretation |
| MCP adapter | Four model-facing wire contracts and translation through authority ports | Direct database access, Core canonical state, or workflow mutation |
| Web host | Composition, HTTP/Teams/AI adapters, authentication, and React hosting | EF entity ownership or direct reference/workflow database queries from controllers and AI adapters |
| Renderer | Canonical progress, choices, bounded guidance, ready cards, safe failures, localized fixed text | Inferring state from model prose |
| Teams adapter | Authentication, transport validation, exact `/new`, agent invocation, structured card actions, delivery | General free-text interpretation or business authority |

### 6.1 Modular-monolith and extraction boundary

The final source layout has one executable host and these one-directional project
dependencies:

```text
GovernedAccess.ReferenceAuthority -> GovernedAccess.Core
GovernedAccess.Workflow.Persistence -> GovernedAccess.Core
GovernedAccess.Mcp -> GovernedAccess.Core
GovernedAccess.Web -> Core + ReferenceAuthority + Workflow.Persistence + Mcp
GovernedAccess.Core -> no outer project
```

`GovernedAccess.Web` references infrastructure modules only to compose them. Core
application services consume ports. MCP handlers consume the same authority ports and
translate results into MCP-owned DTOs. Controllers, Teams adapters, AI adapters, and
renderers do not receive either `DbContext`.

The in-process authority-port implementation is the local client boundary. The target
must not add loopback HTTP merely to imitate a distributed system. If a security or
deployment boundary is approved later, Web replaces the in-process port implementation
with an authenticated remote client; Core, workflow persistence, and MCP wire contracts
do not require redesign.

## 7. End-to-end free-text turn flow

Every authenticated non-`/new` free-text turn follows this semantic path:

```text
authenticated Teams free-text
    -> load candidate + active clarification + ConcurrencyVersion snapshot
    -> bounded agent invocation
    -> closed provider-neutral TurnProposal
    -> structural proposal validation
    -> deterministic domain-operation evaluation
    -> authoritative enterprise search/reload
    -> deterministic reduction
    -> optimistic atomic commit
    -> application-owned typed response
```

Transport checks may reject unauthenticated, unsupported-channel, missing, blank,
oversized, or invalidly encoded payloads. Such checks assign no request-intake meaning.

No database transaction or SQLite write lock may be held while the model or MCP is
executing.

## 8. Agent input and execution envelope

For each non-`/new` free-text turn, the agent receives the smallest sufficient context:

- latest requester free-text;
- sanitized current canonical candidate;
- current preparation lifecycle summary;
- active bounded clarification context when present, including authoritative display
  choices, exact canonical IDs, safe distinguishing fields, and their 1-based rendered
  positions;
- fixed intake interpretation rules;
- exactly four approved read-only MCP capabilities.

The agent does not receive authority to mutate application state. Durable provider
conversation memory is not required for correctness.

The active context is provider-neutral and has this semantic shape:

```text
ActiveClarificationContext
- target: environment | role
- choices[] in persisted display order
  - position: 1-based display position
  - canonicalId
  - safe authoritative display fields needed to distinguish the choice
- createdAt
```

Environment choice fields may include environment name/ID, authoritative client
name/ID, region, and primary/recovery classification. Role choice fields include the
exact role ID and a safe display name. No more than five choices are persisted,
rendered, or supplied, and their stable persisted order makes this context
reconstructable after restart.

Requester text and clarification choices are untrusted model context. The exact IDs
help the agent express an interpretation, but they are not authorization evidence or
an allowlist for Core acceptance.

The agent input deliberately excludes arbitrary raw prior-turn history. Coreference is
supported only through canonical state and an active clarification context. Phrases such
as “the other one” or “same as before” outside that bounded context may produce
`unclear`; the system must not guess from unavailable conversation history.

### 8.1 Execution metadata

The adapter produces provider-neutral execution metadata beside the proposal:

- provider and model deployment identifier;
- provider model/version identifier when exposed;
- prompt-contract version;
- structured-output schema version;
- MCP contract version/hash;
- environment-search policy version;
- correlation ID and timestamps.

This metadata supports diagnostics, evaluation, and bounded audit evidence. It does not
make model output authoritative.

### 8.2 Bounds

Default startup-validated limits are:

- 4,000 Unicode characters per requester free-text turn;
- one call per MCP tool and four MCP calls total per turn;
- six provider iterations per turn;
- zero structured-output repair attempts; and
- one 30-second cumulative model/MCP timeout and cancellation budget per turn.

Malformed, schema-invalid, or structurally unacceptable provider output fails safely
after the first completed agent run. The application does not ask the model to repair
the output or invoke a second interpreter pass.

Timeout, provider-iteration, and MCP-call limits fail the current turn only and do not
reset durable candidate state. Ordinary requester-wide rolling rate limiting or
infrastructure throttling may bound repeated turns, but it must not create another
preparation lifecycle state or permanently exhaust one `PreparationId`.

Startup must fail closed when configured limits are missing, non-positive, internally
inconsistent, or exceed the documented hard maxima.

Outside the isolated local live-model evaluation evidence described in Section 23.3,
raw requester text, agent-authored search queries, raw prompts, model reasoning,
complete provider responses, and complete MCP payloads must not be persisted or
logged. Local evaluation artifacts use only the fixed synthetic dataset and retain the
exact synthetic requester message plus parsed proposal and canonical comparison values
needed to diagnose failures. They still exclude raw prompts, model reasoning, complete
provider responses, and complete MCP payloads.

## 9. Exact `/new` protocol command

The exact trimmed, case-insensitive text `/new` is handled before agent invocation.

For the authenticated requester and exact conversation, `/new` atomically:

1. marks any active `Collecting` or `Ready` preparation `Superseded`;
2. invalidates its clarification context and ready card through lifecycle state;
3. creates one clean `Collecting` preparation with a new `PreparationId`; and
4. renders application-owned reset guidance.

It must not change a submitted request or downstream approval, provisioning, audit, or
grant state.

Exact `/new` invokes neither the agent nor MCP. Every other nonblank text payload,
including `/new` combined with other words, follows the agent path. No additional
deterministic text command is introduced.

## 10. Agent output contract

The agent returns one provider-neutral `TurnProposal` using a closed schema:

```text
TurnProposal
- schemaVersion
- dialogueAct
- patch?
- discussionTopic?
```

Provider SDK, MAF, MCP, Teams, or raw JSON DOM types must not cross into Core.

### 10.1 Dialogue acts and payload compatibility

The closed dialogue-act set is:

- `updateDraft`;
- `discussDraft`;
- `requestSubmission`;
- `unrelated`; and
- `unclear`.

Exactly the following payload combinations are valid:

| Dialogue act | `patch` | `discussionTopic` | Mutation possible |
|---|---:|---:|---:|
| `updateDraft` | Required, nonempty | Forbidden | Yes, through accepted patch operations |
| `discussDraft` | Forbidden | Required, one closed topic | No |
| `requestSubmission` | Forbidden | Forbidden | No |
| `unrelated` | Forbidden | Forbidden | No |
| `unclear` | Forbidden | Forbidden | No |

Unknown acts, unknown properties, incompatible payloads, empty `updateDraft` patches,
malformed operations, or multiple semantic payloads are structural violations. The
entire turn is rejected immediately with zero mutation.

### 10.2 Sparse patch

`updateDraft` may change only:

- environment;
- role;
- justification; and
- optional incident.

Each field is omitted or contains exactly one operation:

```text
set(value)
clear
```

Rules:

1. Omitted field means no proposed change.
2. There is no required `keep` operation.
3. `clear` carries no value.
4. `set` carries exactly one closed field-specific value.
5. Requester, client, duration, approver, preparation/request identity, approval,
   provisioning, retry, audit, and grant state are never model-mutable.

### 10.3 Environment proposal

Environment `set` uses one closed reference:

```text
exactEnvironmentId(id)
searchQuery(query)
```

The query is agent-interpreted input to a deterministic search capability. Application
code does not check whether it appears verbatim in requester text.

The two reference forms are deliberately exclusive execution paths:

- `exactEnvironmentId` is used when one exact environment is uniquely justified,
  including when the requester supplied its stable ID or the agent observed exactly one
  result from `search_production_environments`. Core exact-reloads the proposed ID and
  does not replay the preceding model-side search query.
- `searchQuery` is used when the agent has not resolved one exact environment. Core
  executes the shared deterministic policy because it still needs the authoritative
  zero/unique/ambiguous/too-broad result.

The agent must not convert a two-or-more-result MCP search into an unprompted exact ID.
An exact proposal remains interpretation, not authority: only Core's independent exact
reload can establish current environment eligibility and derive the client. Search
replay cannot prove that the agent translated requester wording correctly, so the
prominent ready-card review remains the semantic-intent control.

### 10.4 Role proposal

Role `set` contains one exact role identifier. The agent may use
`get_environment_roles` to interpret requester wording. Core accepts the identifier only
after independently loading roles currently assignable in the final authoritative
environment.

This feature validates environment-role assignment, not whether the requester is
personally pre-entitled to the role. Human approval remains the authorization decision.

### 10.5 Incident proposal

Incident `set` contains one exact incident identifier. Core independently exact-loads
the incident and validates active state and environment compatibility.

### 10.6 Justification proposal

Justification `set` contains the proposed text directly. It does not contain a
model-authored self-certification field.

The persisted justification must remain requester-authored content in the requester's
language. The agent may:

- extract the intended justification from surrounding conversational framing;
- trim leading/trailing whitespace;
- normalize line endings; and
- combine the existing canonical justification with text the requester explicitly asks
  to append, remove, or replace.

The agent must not:

- translate;
- summarize;
- polish or professionally rewrite;
- invent business rationale or facts;
- add incident facts not supplied by the requester; or
- silently change meaning.

Canonical justification is permitted to be persisted because it is an intentional
domain field, not a retained raw transcript. Core does not compare the proposal with raw
requester text; requester-authorship fidelity is governed by the prompt contract,
targeted live evaluation, mandatory confirmation-card review, and human approval.

Core enforces deterministic storage constraints only: valid encoding, Unicode NFC,
normalized line endings, trimmed outer whitespace, nonblank value, and a maximum of
2,000 characters. If a set/replace/append result exceeds 2,000 characters, the
justification operation is `RejectedInvalid`; no truncation occurs and application-owned
guidance asks the requester to shorten the justification.

The business approver judges substantive adequacy. The target increment deliberately
does not translate requester-authored justification for approvers; approver-side
translation is a separate future product capability.

### 10.7 Clarification replies use the ordinary sparse patch

When the requester refers to an active application-rendered choice set, the agent uses
the same `updateDraft` operation used for every other field update:

```text
environment.set(exactEnvironmentId("ENV-B"))
role.set(exactRoleId("ROLE-RECOVERY-READER"))
```

Thus `the other one`, `first`, `pierwszy`, `el primero`, an exact displayed ID, or a
descriptive phrase such as `the recovery one` is interpreted at the agent boundary into
an ordinary exact-ID sparse operation. When the reference is not safely resolvable from
the canonical candidate and active clarification context, the agent returns `unclear`.
No deterministic numeric, ordinal, identifier, choice, or multilingual parser exists.

Core does not map positions to IDs and has no clarification-selection branch. It
processes the proposed exact ID through the ordinary Section 14 reducer: independently
exact-reload current authoritative data, validate eligibility or assignability, apply
coherence and dependency cascades, update clarification context, recompute readiness
and lifecycle, and commit atomically through optimistic concurrency.

Core does not require the proposed exact ID to belong to the displayed choice set. The
choices are semantic context for interpretation, not an authorization allowlist. A
requester may abandon the displayed choices and explicitly name another valid
environment or role in the same reply; that ID follows the same exact-reload path.
Nevertheless, the live-model contract requires a genuine displayed-choice reference to
produce the expected displayed ID rather than an unrelated valid ID.

### 10.8 Discussion, submission, unrelated, and unclear turns

The closed discussion topics are:

- `currentDraft`;
- `missingInformation`;
- `allowedChanges`;
- `confirmationProcess`;
- `resetInstructions`; and
- `unsupported`.

`discussDraft` never mutates state. Application code maps the topic to fixed guidance
and canonical data.

`requestSubmission` expresses intent only. If the active preparation is `Ready`, the
application re-renders its current card. Otherwise it renders canonical progress or
terminal-state guidance. It creates no request.

`unrelated` preserves state and renders bounded scope guidance.

`unclear` preserves state and asks for a safer rephrasing.

No model-generated prose is delivered to the requester in this feature.

## 11. MCP capability catalog

The agent receives exactly four typed, read-only capabilities:

```text
search_production_environments(query)
get_production_environment(environmentId)
get_environment_roles(environmentId)
get_incident(incidentId)
```

| Capability | Enterprise boundary represented |
|---|---|
| Environment search | Service-catalog / CMDB discovery projection |
| Exact environment lookup | Environment registry / CMDB authority |
| Environment-role lookup | IAM / entitlement authority |
| Exact incident lookup | ITSM authority |

Environment identity and entitlement assignment remain separate even when synthetic
adapters share the reference-authority database. They are independently implemented and
may fail independently at the authority-port boundary.

The co-hosted MCP adapter has no EF Core or database dependency. Both its model-facing
tools and Core's deterministic validation call authority ports implemented by the
reference-authority module. Only that module has direct access to the reference
database. MCP DTOs are transport projections and must not be reused as Core authority
models or EF entities.

Tool rules:

- all four tools are read-only, closed, typed, and allowlisted;
- unknown, additional, renamed, or non-read-only tools fail closed;
- no state-changing, generic-query, cross-environment role-search, submission, approval,
  provisioning, retry, or grant capability is exposed;
- tool-call order is diagnostic, not an authorization boundary;
- tool results are untrusted interpretation data, never Core authority;
- all display text and incident descriptions returned by tools are treated as untrusted
  data and never as instructions;
- raw tool arguments/results, including agent-authored search queries, are not logged;
- limits, timeout, and cancellation budgets are cumulative per turn.

### 11.1 Shared deterministic environment-search policy

The MCP search surface and the Core `searchQuery` path must use one shared deterministic
search-policy implementation, not duplicated algorithms. This rule governs every
place that executes a search; it does not require Core to replay a model-side search
after receiving `exactEnvironmentId`.

The policy searches only approved environment/client identity, region, and
primary/recovery fields; returns only active production environments eligible for
intake; uses deterministic normalization, matching, and stable ordering; and does not
depend on SQLite `NOCASE`. The target MCP contract owns the low-level Unicode,
tokenization, and storage-provider conformance detail.

Both surfaces expose the same complete cardinality result:

| Result count | Behavior |
|---:|---|
| 0 | No match |
| 1 | Exact-reload and accept when still eligible |
| 2-5 | Persist and render the complete ordered clarification set |
| >5 | Typed too-broad result; ask for a more specific description |

No larger hidden result set is exposed to the agent. Neither surface truncates, ranks,
or selects from a too-broad result.

When Core's `searchQuery` path gets exactly one result it may accept it after exact
reload because the deterministic search uniquely identified the environment. When the
agent instead observed one MCP result and proposed its exact ID, Core performs only the
exact reload. Core does not apply the same auto-selection rule to a single available
role. Explicit role selection remains a deliberate authorization-scope rule, and the
mandatory card review does not replace requester intent.

## 12. Core trust boundary

The agent is trusted to propose **what the requester appears to mean** inside the closed
schema. It is not trusted to establish enterprise truth or consequential authority.

Core validates two distinct layers.

### 12.1 Structural validation: whole-turn rejection

The whole turn is rejected immediately with zero mutation when:

- dialogue act is unknown;
- act/payload compatibility is invalid;
- a field, operation, reference form, discussion topic, or property is unknown;
- required values are missing or forbidden values are present;
- values violate closed structural bounds; or
- provider output cannot be translated into the provider-neutral contract.

### 12.2 Domain evaluation: two atomic application groups

After structural validation succeeds, Core evaluates only two application groups:

1. the atomic **scope group**: environment, incident, role, and deterministic cascades;
2. the independent **justification group**.

Each proposed group has one behavioral result: `Applied`, `NoOp`,
`Rejected(reason)`, or `NeedsClarification`. Typed reason codes may accompany a
rejection, but Core does not expose a general per-field verdict protocol.

Invalid, unavailable, conflicting, or ambiguous explicit scope operations discard the
entire temporary scope result for the turn. Current canonical scope remains unchanged,
although one bounded clarification context may be persisted. A valid justification may
still apply independently. An invalid justification does not discard an otherwise
valid scope transition. The final candidate, clarification context, lifecycle, and
metadata still commit atomically.

### 12.3 Authoritative enterprise validation

Core independently verifies:

- environment existence, active production classification, and intake eligibility;
- client derived from that exact environment;
- role currently assignable in that environment;
- incident existence, active state, and environment relationship; and
- all relevant facts again before request creation.

These reads cross only the authority ports. Core and Web do not query reference tables,
and MCP tool output is never substituted for an authority-port result. Client authority
includes the owning client facts required to resolve the configured business approver;
the MCP environment response continues to omit that hidden workflow fact.

Core does not independently reproduce natural-language understanding.

## 13. Canonical state and version model

A preparation persists only sanitized application-owned data:

- `PreparationId`, generated as an unguessable random UUIDv4 identifier;
- authenticated actor/conversation binding;
- lifecycle state;
- canonical candidate;
- `ConcurrencyVersion` or equivalent storage token;
- active clarification context when present;
- `CreatedAt` and `UpdatedAt`;
- `ReadyAt` and `ReadyDeadline` when ready;
- terminal timestamp where applicable;
- mandatory predecessor `PreparationId` for every preparation created by revision; and
- bounded interpreter version/audit metadata without raw text. Reaching a metadata
  retention bound must evict or summarize old diagnostic metadata; it must never
  exhaust or terminalize a preparation.

No separate candidate-progress counter is persisted. Chronology uses timestamps,
correlation/audit evidence, and predecessor linkage.

### 13.1 ConcurrencyVersion

`ConcurrencyVersion`:

- changes on every persisted aggregate update;
- protects optimistic commits, lifecycle transitions, clarification-only changes, lazy
  expiry, and audit-metadata updates that participate in the aggregate write;
- is never exposed as requester authority; and
- is checked during short database commits after agent/MCP execution.

The agent is invoked from a snapshot containing the current candidate and active
clarification context. If either changed while the agent was running, the aggregate's
`ConcurrencyVersion` also changed and the proposal must not commit against that stale
snapshot.

### 13.2 Canonical equality

Canonical equality used for no-op detection is field-specific:

- authoritative identifiers compare by their canonical exact identifier representation;
- justification compares after Core's permitted Unicode, line-ending, and
  outer-whitespace normalization;
- derived client is never compared as requester/model input; it is re-derived; and
- lifecycle, timestamps, clarification context, and version metadata are not candidate
  equality fields.

## 14. Normative grouped reduction order

Core evaluates a structurally valid proposal in this fixed order:

1. resolve all proposed environment, incident, and role facts without mutation;
2. evaluate one coherent scope-group transition against a temporary candidate;
3. either accept the complete scope transition including deterministic cascades, or
   discard every temporary scope mutation;
4. evaluate justification independently;
5. create at most one clarification context using the precedence below;
6. apply clarification-context consumption and preservation rules;
7. evaluate readiness and lifecycle; and
8. commit the complete candidate/context/lifecycle result atomically using optimistic
   concurrency.

There is no general per-field transaction or dependency-propagation framework.

An exact environment or role ID derived by the agent from active clarification context
is indistinguishable from the same ordinary exact-ID operation proposed on any other
turn. There is no preliminary choice-membership, target/index, or conversion step.

### 14.1 Environment operation

For `exactEnvironmentId`, Core exact-loads an active, eligible production environment.
It does not reconstruct or replay any model-side search. An invalid/ineligible ID
rejects the complete proposed scope group.

For `searchQuery`, Core uses the shared policy:

| Authoritative result | Outcome |
|---|---|
| Zero matches | Scope group rejected as no match |
| One match | Exact-reload and accept the environment if still eligible |
| Two to five matches | Environment operation becomes `NeedsClarification`; persist the complete ordered choice records with exact IDs and safe display fields |
| More than five | Typed too-broad result; reject the scope group without a choice list |

A unique result is accepted because Core independently produced it and exact-reloaded
the entity. Final card confirmation is still required.

**Environment ambiguity is non-destructive.** When an environment operation becomes
`NeedsClarification`, Core preserves all currently committed canonical candidate fields.
The clarification context represents a pending proposed environment change. If the
candidate had no environment, it remains unresolved; if it already had an environment,
that value remains canonical until a later accepted ordinary exact-ID operation applies
a replacement.
No dependent field is cleared merely because a new choice is ambiguous.

For a revision against `Ready`, ready immutability still applies: `Ready A` is
`Superseded` and a new `Collecting B` copies A's canonical candidate unchanged and stores
the clarification context. `B` cannot become `Ready` while a context is active. This
invalidates A's card without discarding the reviewed field values.

### 14.2 Incident operation

An exact incident `set` is accepted only when the incident exists and is active. `clear`
removes the incident.

The incident authority returns one nullable authoritative `EnvironmentId`. There is no
incident-to-many-environments cardinality in this feature.

- no environment relationship rejects the proposed scope group;
- one relationship with no explicit environment operation exact-reloads that environment,
  replaces any retained environment as part of the same scope transition, and derives
  client scope;
- an explicit environment matching the relationship is valid; and
- a conflicting explicit environment rejects the proposed scope group.

### 14.3 Scope coherence

The following rules resolve environment and incident interaction:

- environment, incident, and role operations form one atomic scope group;
- any invalid, unavailable, conflicting, or ambiguous explicit scope operation rejects
  the group and preserves all current scope fields; independent justification may still
  apply;
- environment `clear` plus incident `set` is a domain conflict and rejects the scope
  group;
- environment `set` plus incident `clear` may apply both;
- environment `clear` plus incident `clear` clears environment, client, role, incident,
  and related clarification context;
- an accepted incident set with no environment operation derives environment/client from
  its single authoritative relationship;
- an incident set with no environment operation replaces a different retained
  environment and performs the normal role/clarification cascades in the same scope
  transition;
- an accepted environment change clears a retained incident that no longer belongs to
  that environment and reports the cascade; and
- an environment ambiguity does not clear retained scope and creates at most one bounded
  environment clarification.

Core never silently chooses between two conflicting explicit scope proposals.

### 14.4 Role operation and role clarification

Role is evaluated only after final authoritative environment scope is known.

- role `set` without a final environment rejects the scope group;
- role `set` is accepted only when the entitlement authority currently assigns that
  exact role to the final environment;
- role `clear` preserves environment and client;
- an accepted environment change retains an existing role only when it remains currently
  assignable; otherwise Core clears it and reports the cascade;
- environment clear clears role; and
- no role is substituted automatically; and
- an invalid or unavailable explicit role rejects any same-turn environment/incident
  transition rather than partially applying it.

When a role is required but omitted after an otherwise valid scope transition, or the
requester expresses role ambiguity without a valid exact role, Core may render one
application-owned role clarification. An ambiguous explicit role operation does not
partially apply another scope change.

| Available roles | Outcome |
|---|---|
| Zero | Typed no-roles rejection; cannot become ready |
| One to five | Persist complete ordered role choice records with exact IDs and safe display names as one clarification context |
| More than five | No bounded choice context; ask the requester to specify the role more precisely |

Role clarification is also non-destructive: an existing canonical role is preserved
until a later accepted ordinary exact-ID operation replaces it. A candidate with no
role remains without a role. Core does not auto-select the only role because choosing
authorization scope remains requester intent even when the entitlement catalog has one
current option.

### 14.5 Justification operation

A structurally valid justification `set` that passes storage constraints is independent
of environment, incident, and role resolution. `clear` makes the candidate incomplete.
An over-2,000-character set/append/replace is `RejectedInvalid`; Core never truncates.

Core does not judge business quality or reproduce linguistic requester-authorship
checks. Justification fidelity is controlled by the agent contract, targeted
evaluations, mandatory card review, and human approval.

### 14.6 At most one clarification per turn

At most one clarification context may be persisted after a turn.

Precedence is:

1. environment clarification;
2. role clarification.

If environment clarification is required:

- it is persisted without clearing the current candidate;
- the temporary scope group is discarded for that turn;
- no role clarification is queued; and
- an independently valid justification may commit.

If no environment clarification exists, Core may persist one role clarification without
clearing the current candidate. Lower-precedence ambiguity is rejected for the turn,
never silently queued for later.

Newly required clarification replaces the prior context according to this same
environment-before-role precedence. An accepted ordinary target-field operation may
consume the old context and create the next required context in one atomic commit—for
example, an accepted environment exact-ID update can consume environment context and
create role context.

### 14.7 Clarification context contract and lifecycle

Persisted context contains:

- `PreparationId` through aggregate ownership;
- target (`environment` or `role`);
- no more than five choice records in stable display order;
- for each choice, the exact canonical ID and safe authoritative display fields needed
  to distinguish it; and
- `CreatedAt`.

Environment display fields may include environment name/ID, authoritative client
name/ID, region, and primary/recovery classification. Role fields include exact role ID
and safe display name. The provider-neutral agent input reconstructs 1-based positions
strictly from persisted order. Numbering remains a usability feature, not a Core
mutation protocol or authority boundary.

The lifecycle rules are:

- an accepted `environment.set` or `environment.clear` consumes active environment
  clarification context;
- an accepted incident operation that deterministically establishes or changes
  environment scope consumes active environment clarification context;
- an accepted environment change also clears any active role clarification context;
- an accepted `role.set` or `role.clear` consumes active role clarification context;
- a newly required clarification replaces prior context according to the existing
  environment-before-role precedence;
- an accepted independent justification change preserves unrelated active context;
- rejected operations, value-equal no-ops, `discussDraft`, `requestSubmission`,
  `unrelated`, `unclear`, and transient provider or authoritative-source failures
  preserve active context unless current authoritative reads prove its choices stale;
- exact `/new` and terminal lifecycle transitions remove the context or make it
  unusable;
- active context prevents transition to `Ready`, so a `Ready` preparation never carries
  clarification context;
- a clarification created while revising `Ready A` supersedes A and creates
  `Collecting B` with the copied canonical candidate and predecessor link; and
- once B receives an accepted ordinary target-field update, Core consumes context and
  reevaluates readiness normally.

Every proposed ID is independently exact-reloaded and validated through the ordinary
reducer even when it was present in the displayed choices. Core does not require choice
membership before accepting a valid ID.

Every context write changes `ConcurrencyVersion`. There is no independent clarification
TTL; context remains usable only while its preparation is active and until these
deterministic rules consume, replace, or invalidate it. The snapshot plus
`ConcurrencyVersion` commit check prevents a proposal interpreted against changed
candidate or context from committing.

## 15. Application-owned outcomes and responses

Core returns closed typed outcomes containing only canonical or safe structured data.
Representative outcomes are:

- `DraftUpdated` with at most one scope-group result and one justification result;
- `ClarificationRequired` with target and authoritative choices;
- `DraftUnchanged` for no-op or rejected proposals;
- `DraftDiscussion` with one closed topic;
- `SubmissionGuidance`;
- `UnrelatedGuidance`;
- `UnclearGuidance`;
- `ReadyForConfirmation`;
- `ConfirmationRevalidationFailed` with successor preparation identity/status when
  authoritative facts changed;
- `ConfirmationSourceUnavailable` when confirmation revalidation cannot complete;
- `TerminalPreparationGuidance`; and
- `Failed`.

Application code renders:

- canonical progress;
- compact scope and justification summaries;
- focused missing-field guidance;
- environment and role choices;
- bounded draft help;
- confirmation guidance;
- ready cards;
- stale, expired, foreign, and terminal guidance;
- confirmation-revalidation correction/retry guidance;
- rate/failure guidance; and
- downstream workflow outcomes.

For environment-search no-match/too-broad guidance, the renderer names the searchable
attributes: environment ID/name, client ID/name, region, and primary/recovery
classification.

No model-generated prose or raw MCP text reaches the requester.

### 15.1 Response locale

Response locale is derived from authenticated Teams/client locale, never inferred from
requester text. The initial implementation may provide only deterministic `en-US`
strings. Missing or unsupported locale falls back to `en-US`.

Adding another output locale is a renderer/localization change and requires no Core
semantic-rule change.

## 16. Preparation lifecycle

The lifecycle states are:

- `Collecting`: active mutable candidate, optionally with one active clarification;
- `Ready`: active immutable candidate eligible for confirmation until deadline and never
  carrying active clarification context;
- `Submitted`: terminal preparation bound to one request;
- `Superseded`: terminal preparation replaced by `/new`, revision, or changed facts at
  confirmation; and
- `Expired`: terminal ready preparation whose confirmation deadline elapsed.

At most one `Collecting` or `Ready` preparation exists for one authenticated
actor/conversation binding. This invariant is enforced durably by the partial unique
index defined in Section 19.1, not by a read-before-write check.

### 16.1 State transitions

| Current state | Event | Result |
|---|---|---|
| None | first non-mutating act (`unclear`, `unrelated`, `discussDraft`, `requestSubmission`) | Render guidance; create no preparation |
| None | first proposal with at least one accepted material operation or clarification | Create `Collecting`, then atomically persist accepted candidate/context |
| None | exact `/new` | Create clean `Collecting` |
| `Collecting` | accepted ordinary material update | Mutate the same preparation in one optimistic commit |
| `Collecting` | clarification only | Preserve candidate; store or replace context; change `ConcurrencyVersion` |
| `Collecting` | candidate complete and no active clarification | Transition same preparation to immutable `Ready`; set `ReadyAt` and `ReadyDeadline` |
| `Collecting` | exact `/new` | Mark old `Superseded`; create clean `Collecting` |
| `Ready` | discussion, submission intent, unrelated, unclear, no-op, rejected proposal, or failure | Preserve same `Ready` preparation and deadline |
| `Ready` | accepted material candidate change | Atomically mark old `Superseded`; create new `Collecting` or `Ready` successor with mandatory predecessor ID |
| `Ready` | accepted clarification revision | Atomically mark old `Superseded`; create new `Collecting` successor copying current candidate unchanged and storing context |
| `Ready` | valid confirmation | Transition to `Submitted` and create one immutable request |
| `Ready` | authoritative fact changed during confirmation revalidation | No request; mark old `Superseded`; create corrected successor and re-evaluate readiness |
| `Ready` | confirmation authoritative source unavailable | Preserve `Ready` and deadline; render retry guidance |
| `Ready` | deadline reached on load/confirmation | Transition lazily to `Expired` |
| `Ready` | exact `/new` | Mark old `Superseded`; create clean `Collecting` |
| `Submitted`, `Superseded`, `Expired` | non-`/new` free-text through normal agent path | No mutation; render terminal guidance |
| `Submitted`, `Superseded`, `Expired` | exact `/new` | Create clean `Collecting`; do not alter terminal entities |

### 16.2 Ready immutability and card safety

When a preparation becomes `Ready`, its candidate is immutable and it has no
clarification context. A material revision or revision clarification never
mutates that row back to collecting. It creates a new preparation identity in the same
atomic commit that supersedes the old one.

Therefore an action payload containing only `schemaVersion` and `PreparationId` is bound
to one exact reviewed scope. A stale card always reloads a terminal `Superseded` or
`Expired` preparation and cannot submit a replacement scope.

### 16.3 Ready expiry

Ready expiry remains because the accepted product baseline intentionally permits
confirmation only for an unexpired ready intake, and the current security model binds
that control to a 30-minute window. Immutable identity and confirmation-time
revalidation complement rather than replace this age limit.

- `ReadyDeadline = ReadyAt + 30 minutes`.
- Non-mutating turns do not refresh the deadline.
- A replacement ready preparation receives a new deadline.
- Ready expiry is evaluated lazily when a ready preparation is loaded or confirmed.
- No background expiry worker is required.
- `Collecting` has no feature-specific age warning, inactivity deadline, or stale
  lifecycle behavior.
- Terminal row retention follows ADR 0005.

## 17. Ready revision semantics

Against `Ready A`:

| Turn outcome | Required result |
|---|---|
| Discussion, submission guidance, unrelated, unclear | Preserve `Ready A` and its deadline |
| Value-equal patch | Preserve `Ready A` |
| Structurally invalid or all data-level operations rejected | Preserve `Ready A` |
| Model/MCP failure or failed commit | Preserve `Ready A` |
| Accepted complete material change | `Superseded A` + new `Ready B` with new ID/deadline/card and `PredecessorPreparationId=A` |
| Accepted incomplete material change | `Superseded A` + new `Collecting B` with `PredecessorPreparationId=A` |
| Accepted environment/role clarification | `Superseded A` + new `Collecting B` that copies A's candidate unchanged, stores the context, and records `PredecessorPreparationId=A` |

Clarification revisions are deliberately non-destructive. They invalidate the old card
because the requester has initiated a possible scope change, but they do not erase the
previously reviewed canonical environment, client, role, incident, or justification
while the new choice is unresolved. The eventual ordinary exact-ID `set` applies
through Section 14 with normal cascades and readiness evaluation.

There is no pending-revision candidate and no revision-cancellation command.

## 18. Adaptive Card confirmation

Adaptive Card confirmation is the only request-creation path.

The application-owned card prominently displays:

- authenticated requester;
- authoritative client name and ID;
- authoritative environment name and ID;
- authoritative role name and ID;
- exact persisted requester-authored justification;
- incident or explicit “no incident” value;
- **Requested access duration: 8 hours**; and
- **Confirm before:** localized timestamp including timezone/offset.

The card states that confirmation submits for business approval and does not approve,
provision, or grant access. Non-English requester-authored justification is displayed as
stored; approver-side translation is explicitly outside this increment.

The action payload is:

```json
{
  "schemaVersion": 1,
  "preparationId": "..."
}
```

`PreparationId` must be an unguessable random UUIDv4 identifier. The payload
contains no trusted requester, scope, role, duration, approval, provisioning, or grant
fields.

On confirmation, deterministic code:

1. derives actor and conversation from authenticated Teams context;
2. validates the closed action schema;
3. reloads exact `PreparationId`;
4. verifies ownership before returning any preparation/request detail;
5. if the matching-owned preparation is already `Submitted`, loads the request by the
   unique `Request.PreparationId` key and returns its existing identity/status as a safe
   replay;
6. lazily expires a matching-owned `Ready` preparation when the deadline has passed;
7. otherwise accepts only matching-owned, unexpired `Ready` state;
8. independently revalidates requester binding, environment eligibility, derived client,
   current role assignment, justification constraints, and incident;
9. on successful revalidation, atomically transitions the preparation to `Submitted`
   and creates one immutable `AwaitingBusinessApproval` request plus request-created
   audit evidence; and
10. if a concurrent insert wins the unique-key race, reloads and returns that existing
    request identity/status.

Confirmation-time revalidation failure is explicitly split:

- **authoritative fact changed** (for example environment became ineligible, role was
  de-assigned, incident became invalid, or derived client changed): create no request;
  atomically mark `Ready A` `Superseded`, create successor `B` with mandatory predecessor
  link, copy the candidate, apply only deterministic authoritative invalidation/update
  cascades, clear any field that can no longer be valid, re-evaluate readiness, and
  return `ConfirmationRevalidationFailed` with B's current status. If B remains complete
  after an authoritative derived-value update, it may become a new `Ready` with a new
  deadline/card; otherwise it is `Collecting`;
- **authoritative source unavailable/transient failure**: create no request; preserve
  `Ready A` and its existing deadline unchanged and return
  `ConfirmationSourceUnavailable` with deterministic retry guidance.

Deterministic correction rules for changed facts are:

| Changed fact | Successor correction |
|---|---|
| Environment missing/inactive/non-production/intake-ineligible | Clear environment, derived client, role, and incident; successor is `Collecting` |
| Owning client changed for same eligible environment | Replace derived client, then revalidate role/incident; readiness recalculated and a new card is required before submission |
| Role no longer assignable | Clear role; preserve valid environment/client/incident/justification; successor is `Collecting` |
| Incident inactive/missing/incompatible | Clear incident; preserve other still-valid fields; readiness recalculated according to incident optionality |

No confirmation-time correction silently substitutes a different environment or role.

The request table has a durable unique constraint on `Request.PreparationId`. This is the
named idempotency key. Optional Teams activity identifiers may provide additional
transport deduplication but are not the domain idempotency authority.

No agent output can bypass confirmation.

## 19. Persistence, restart, and concurrency

The target modular monolith uses two independent local SQLite databases with separate
EF Core contexts, connection strings, migration histories, seeders, and integration-test
fixtures.

The **workflow database** persists authenticated-principal snapshots, canonical
candidate, lifecycle, `ConcurrencyVersion`, timestamps, ready metadata, bounded clarification
context, predecessor linkage, requests, approvals, provisioning operations, grants,
and bounded interpretation/audit metadata.

The **reference database** persists synthetic clients and business-approver mappings,
production environments and searchable eligibility facts, environment-role
assignments, and incidents with one nullable authoritative environment relationship.

No EF entity, navigation, foreign key, migration, or transaction spans the two
databases. Relationships crossing the boundary are stable identifiers in workflow
state and are validated through current authority-port reads. A request-lifecycle write
never writes reference data, and reference reads never join against workflow tables.

Neither database persists raw conversation transcripts, raw prompts, model reasoning,
agent-authored search queries, or complete provider/MCP payloads.

After restart, a new agent invocation receives the durable candidate and active
clarification context required for the next turn.

Every revision-created preparation stores `PredecessorPreparationId`; this is mandatory,
not optional audit decoration.

### 19.1 Commit protocol

The normative durable protocol is:

```text
load active preparation (candidate + clarification context) + ConcurrencyVersion
    -> invoke agent/MCP without database transaction or write lock
    -> validate/reduce against loaded snapshot
    -> acquire short commit boundary
    -> verify ConcurrencyVersion
    -> commit or reject stale snapshot
    -> render committed outcome
```

Active-preparation uniqueness is a database invariant, not a pre-check. SQLite must have
one partial unique index equivalent to:

```sql
CREATE UNIQUE INDEX UX_Preparation_ActiveActorConversation
ON Preparation(ActorBinding, ConversationBinding)
WHERE LifecycleState IN ('Collecting', 'Ready');
```

The concrete persisted column names may differ, but the unique key is the authenticated
actor/conversation binding and the predicate is exactly the two active lifecycle states.
A concurrent initial-creation loser handles the constraint violation by reloading the
winning active preparation; it must not create a second active row. Ready supersession
plus successor creation occurs atomically so the same constraint also protects revision
races.

A process-local per-conversation async gate may serialize more of the turn as an
implementation optimization, but correctness must not depend on holding a database lock
across model execution.

On optimistic-concurrency mismatch, no proposal is applied against the newer candidate
or clarification context. The application returns safe retry guidance; it does not
silently replay the same model proposal against changed state.

Required race behavior:

- concurrent initial turns create at most one active preparation through the partial
  unique index and reload-on-conflict behavior;
- same-conversation commits do not both succeed from one stale snapshot;
- different conversations remain concurrent;
- confirmation committed first produces one immutable request; later revision cannot
  alter it;
- revision committed first supersedes the old ready preparation; old card creates no
  request;
- duplicate/concurrent confirmation creates one request and returns the same identity;
- failed ready replacement leaves the old ready preparation unchanged.

## 20. Failure behavior

| Failure | Required behavior |
|---|---|
| Malformed model output | Reject immediately; preserve committed state; render safe retry guidance |
| Structural proposal violation | Whole-turn rejection; preserve committed state |
| Model timeout/cancellation/provider failure | Preserve committed state |
| MCP contract/tool failure before valid proposal | Preserve committed state |
| One Core authoritative scope source unavailable | Reject the complete proposed scope group; a valid justification may still commit |
| Unknown/ineligible environment | Reject the scope group; preserve current scope |
| Environment search zero/too broad | Reject the scope group; renderer names searchable attributes |
| Invalid/unavailable role | Reject the scope group; preserve current scope |
| Invalid/inactive/incompatible incident | Reject the scope group; preserve current scope |
| Incident has no authoritative environment | Reject the scope group; derive no scope |
| Explicit environment/incident conflict | Reject the scope group; a valid justification may still commit |
| Proposed exact environment/role ID is invalid, ineligible, or unassignable | Reject it through the ordinary operation outcome; preserve candidate and active context unless current authoritative reads prove the choices stale |
| Candidate or clarification context changed while the agent was running | OCC rejects the proposal against the stale snapshot; preserve the newer committed aggregate and render retry guidance |
| Persistence/OCC failure | Do not claim change occurred |
| Active-preparation unique-index race | Reload winner; create no duplicate active preparation |
| Ready replacement failure | Preserve original ready preparation |
| Confirmation authoritative fact changed | Create no request; supersede old ready row; create/recompute successor; return `ConfirmationRevalidationFailed` |
| Confirmation authoritative source unavailable | Create no request; preserve ready row/deadline; return `ConfirmationSourceUnavailable` |
| Stale/foreign/expired/malformed card | Create no request |
| Duplicate confirmation | Return existing request identity |

No failed or grouped free-text turn may create a request, approval, provisioning
operation, or grant.

## 21. Security and threat model

### 21.1 Named threats

Untrusted data reaching the model includes:

- latest requester free-text;
- **persisted canonical requester-authored justification replayed on later turns**;
- environment/client display text;
- role display text; and
- incident title and other MCP result text.

Persisted justification is a durable re-injection vector: instruction-like text entered
as legitimate justification may be replayed to the model on every subsequent turn. It
must therefore be delimited and described as untrusted domain data, never as agent
instruction or policy.

These inputs may contain prompt-injection instructions. Controls are:

- a closed structured proposal schema;
- exact read-only allowlisted tools;
- no state-changing model capability;
- authoritative Core reloads;
- deterministic lifecycle and request creation;
- safe application-owned rendering; and
- human verification of prominent client/environment/role scope and exact justification
  on the ready card.

Tool-result text, requester text, and persisted requester-authored fields are data, never
policy or authorization instructions.

### 21.2 Required controls

- authenticate Teams context before `/new`, agent execution, or card handling;
- derive requester identity only from authenticated server context;
- expose exactly four approved read-only tools;
- validate closed agent output and act/payload compatibility;
- independently reload enterprise facts;
- encode untrusted display text before rendering;
- keep request creation and downstream workflow outside the agent surface;
- enforce optimistic concurrency, active-intake uniqueness, deadline checks, and replay
  idempotency;
- distinguish access duration from confirmation deadline visually;
- avoid raw requester/tool/query/prompt/model payload logging;
- record only bounded structured diagnostics and audit metadata.

## 22. Observability and audit evidence

Structured diagnostics may record:

- correlation ID;
- actor/conversation binding identifiers in approved safe form;
- dialogue act;
- proposed group categories, not raw values;
- group result and typed safe reason code when applicable;
- source/tool name, duration, and typed outcome;
- model deployment and provider version when available;
- prompt, schema, MCP contract, and environment-search policy versions;
- lifecycle transition;
- typed response outcome;
- consequential side-effect counts.

For each accepted material candidate commit, bounded audit metadata records:

- preparation ID;
- changed field categories, never raw field values;
- model deployment/provider version;
- prompt-contract and structured-output schema versions; and
- timestamp/correlation identifier in approved safe form.

Every revision-created preparation records its predecessor ID. Request-created audit
evidence references the confirmed `PreparationId` and the bounded interpreter versions
that contributed accepted material changes. It contains no raw requester transcript,
raw proposal, or complete tool result.

Justification itself is persisted as canonical request data and is shown to the business
approver; it is not duplicated into diagnostic logs.

## 23. Testing and evaluation strategy

The normative detailed matrix is
[`docs/evaluation/deterministic-request-intake-test-matrix.md`](docs/evaluation/deterministic-request-intake-test-matrix.md).

### 23.1 Deterministic tests

Deterministic Core tests construct structured proposals directly, while deterministic
adapter/component tests construct bounded agent inputs. Together they cover:

- act/payload structural rejection;
- sparse `set`/`clear` semantics and omission safety;
- canonical equality;
- two-group atomicity and environment/incident/role scope coherence;
- the single nullable incident-environment relationship;
- role dependency and cascades;
- exact environment proposals using only exact reload, with no search-port call;
- unique/multiple/too-broad search behavior through the shared policy;
- search normalization/case-insensitive substring semantics independent of SQLite
  collation;
- active clarification choices, exact canonical IDs, safe distinguishing fields, and
  persisted display order supplied in provider-neutral agent input;
- non-destructive clarification creation;
- accepted ordinary environment/role exact-ID operations consuming matching context;
- rejected target operations and non-mutating outcomes preserving appropriate context;
- accepted independent justification preserving unrelated context and accepted
  environment changes clearing role context;
- exact authoritative validation and normal cascades for clarification-derived patches;
- context restart survival and normative replacement;
- `ConcurrencyVersion` semantics;
- lifecycle, active-context-prevents-ready invariant, ready immutability, expiry, and
  stale cards;
- partial unique active-preparation index races, OCC rejection when candidate or context
  changed during interpretation, and confirmation idempotency;
- confirmation fact-drift successor behavior versus transient-source preservation;
- independent workflow/reference database creation, migration history, restart, and
  failure behavior with no cross-database relationship or transaction;
- justification storage constraints including over-2,000-character append rejection;
- zero consequential side effects from free-text processing.

Deterministic suites are not requester-language corpora.

Exact `/new` has focused protocol tests proving that it bypasses agent/MCP and that every
other nonblank text payload uses the agent path.

### 23.2 Agent contract and architecture tests

Verify that:

- raw requester text enters only the Teams/agent interpretation boundary;
- Core proposal/reducer APIs do not accept requester text;
- only the exact `/new` comparison exists as deterministic requester-text semantics;
- provider output enters Core only through the closed schema;
- active clarification context reaches the agent with ordered exact IDs and safe display
  fields, while the proposal contract has no separate clarification-selection payload;
- clarification replies use ordinary `updateDraft` exact-ID operations or `unclear`;
- no model-generated prose channel reaches renderer output;
- unknown tools and schema drift fail closed;
- MCP and the Core `searchQuery` path use one shared search-policy implementation;
- an exact environment proposal invokes exact authoritative reload without search
  replay;
- only `GovernedAccess.ReferenceAuthority` references the reference `DbContext`, only
  `GovernedAccess.Workflow.Persistence` references the workflow `DbContext`, and neither
  Core nor MCP references EF Core; and
- the project-reference graph matches Section 6.1 and Web endpoints/adapters do not
  receive a `DbContext`.

### 23.3 Live-model evaluation

Promotion requires a versioned credentialed suite with recorded passing evidence. The
suite must produce zero consequential side effects, validate every canonical identifier
through authoritative Core reads, handle ambiguous scope safely, preserve requester
justification wording without rewriting or invention, and cover English-language and
restart-safe clarification behavior.

The normative evaluation matrix owns the promoted dataset inventory, minimum scenario
counts, numerical thresholds, rerun and waiver policy, retained result schema,
promotion metadata, and re-baselining mechanics. Moving those controls out of this
feature specification does not relax any absolute safety gate.

The retained local result is evaluation evidence, not application telemetry or
workflow persistence. It records exact fixed synthetic requester messages, parsed
expected and observed proposal values, canonical candidate values, clarification IDs,
and tool names. Generated evaluation directories remain excluded from source control
and must not be treated as a place to store credentials or non-synthetic requester
data.

## 24. Acceptance criteria

### Language and response boundary

- **AC-01:** Exact `/new` is the only requester text handled as a deterministic business
  protocol command before agent invocation.
- **AC-02:** Every other authenticated nonblank free-text turn reaches the agent before a
  semantic operation is selected.
- **AC-03:** Core proposal, reducer, authoritative-resolution, readiness, persistence,
  and lifecycle APIs do not require requester free-text.
- **AC-04:** Numeric, ordinal, identifier-like, clear, reset, submission, and multilingual
  wording has no deterministic free-text path.
- **AC-05:** No model-generated prose reaches the requester.
- **AC-06:** Output locale comes from authenticated client context with deterministic
  `en-US` fallback.

### Structured proposal and reduction

- **AC-07:** Agent output is closed, provider-neutral, sparse, and act/payload compatible.
- **AC-08:** Omitted patch fields never erase canonical state.
- **AC-09:** Structural violations reject the whole turn with zero mutation.
- **AC-10:** Scope is one atomic group, justification is independent, and both follow
  the grouped reduction rules in Section 14.
- **AC-11:** At most one clarification context is created per turn; environment has
  precedence over role.
- **AC-12:** Accepted groups and clarification context commit atomically.
- **AC-13:** Core never validates proposal semantics against requester wording.
- **AC-14:** Justification remains requester-authored, un-translated, un-summarized, and
  un-invented; over-limit updates are rejected without truncation.

### Enterprise authority and MCP

- **AC-15:** The model-visible catalog contains exactly the four approved read-only
  capabilities.
- **AC-16:** Environment identity and environment-role assignment remain distinct source
  boundaries.
- **AC-17:** MCP and Core environment search use one shared deterministic policy, the
  same 0/1/2-5/>5 cardinality semantics, eligible-only stable results, and no reliance
  on SQLite `NOCASE`. Core invokes search only for `searchQuery`;
  `exactEnvironmentId` uses exact authoritative reload without search replay. The MCP
  contract owns low-level matcher conformance.
- **AC-18:** Only active production environments eligible for intake can become
  canonical.
- **AC-19:** Client is derived only from exact authoritative environment data.
- **AC-20:** Role means currently assignable in the environment; requester personal
  eligibility is not inferred.
- **AC-21:** An incident has one nullable authoritative environment relationship; Core
  independently exact-reloads the related environment and rejects a missing relationship
  or conflicting explicit environment operation.
- **AC-22:** Model-visible tool results and call order are never authorization authority.
  A uniquely resolved MCP result may inform an exact-ID proposal, but only Core's
  independent exact reload can make that environment canonical.

### Clarification

- **AC-23:** Active clarification input contains target, exact canonical IDs, safe
  authoritative distinguishing fields, `CreatedAt`, and 1-based positions derived
  strictly from stable persisted display order.
- **AC-24:** No more than five choices are persisted, rendered, or supplied to the
  agent.
- **AC-25:** Free-text choice replies are interpreted by the agent into ordinary
  `updateDraft` environment/role exact-ID operations or conservative `unclear`.
- **AC-26:** Core exact-reloads every proposed ID and evaluates it through the ordinary
  reducer without target/index resolution or displayed-choice membership as an
  acceptance condition.
- **AC-27:** Context consumption, preservation, replacement, and invalidation follow
  Section 14.7, and snapshot plus `ConcurrencyVersion` OCC prevents a proposal
  interpreted against changed candidate/context from committing.
- **AC-28:** Clarification creation is non-destructive to current canonical fields; an
  active clarification context prevents `Ready`, survives restart, and supports
  multilingual/descriptive references without deterministic requester-language parsing.

### Lifecycle, persistence, and confirmation

- **AC-29:** `PreparationId` identifies immutable ready scope and
  `ConcurrencyVersion` protects every candidate/context/lifecycle commit. No separate
  candidate-progress counter exists.
- **AC-30:** A ready preparation is immutable and its unguessable `PreparationId`
  identifies one exact card scope.
- **AC-31:** The first accepted material revision or revision clarification atomically
  supersedes the old ready preparation and creates a new preparation identity with a
  mandatory predecessor link; clarification successors copy the candidate unchanged.
- **AC-32:** Discussion, no-op, all-rejected proposal, model/source failure, or failed
  commit preserves the ready preparation and deadline.
- **AC-33:** Ready expiry remains 30 minutes, is evaluated lazily, and is not refreshed
  by non-mutating turns. Collecting preparations have no feature-specific stale policy.
- **AC-34:** Canonical state and clarification context survive restart without raw
  conversation history.
- **AC-35:** Agent/MCP execution holds no database transaction or SQLite write lock;
  stale candidate/context snapshot commits are rejected through OCC.
- **AC-36:** A durable partial unique index on authenticated actor/conversation where
  lifecycle is `Collecting` or `Ready` guarantees at most one active preparation;
  concurrent creation losers reload the winner.
- **AC-37:** Only authenticated confirmation of an owned, non-expired `Ready`
  `PreparationId` can create a request.
- **AC-38:** Unique `Request.PreparationId` guarantees one request and stable replay
  identity.
- **AC-39:** Confirmation fact drift creates no request and yields a superseded old ready
  plus deterministically corrected successor; confirmation source unavailability creates
  no request and preserves the old ready/deadline.
- **AC-40:** Confirmation/revision races converge to either one immutable submitted
  request or a stale old card, never a mixed scope.

### Security, execution bounds, and evaluation

- **AC-41:** Requester text, persisted canonical justification, and MCP display fields are
  treated as prompt-injection-capable untrusted data.
- **AC-42:** The card prominently distinguishes client/environment/role, eight-hour
  access duration, and confirmation deadline/timezone.
- **AC-43:** Logs/audit omit raw requester text, agent search queries, prompts, reasoning,
  proposals, and complete tool payloads; accepted material changes retain bounded
  field-category plus model/prompt version attribution.
- **AC-44:** Startup validates the per-message, per-turn, provider-iteration, MCP-call,
  timeout, and cancellation limits in Section 8.2 without creating a permanent
  preparation budget or lifecycle state.
- **AC-45:** Deterministic tests use structured proposals rather than language variants.
- **AC-46:** Live-model gates meet every blocking threshold in the normative evaluation
  matrix, including ambiguous-scope restraint, English clarification, and stored-
  justification re-injection.
- **AC-47:** Live-model preparation evaluation produces zero requests, approvals,
  provisioning operations, and grants.

### Modular persistence and extraction seam

- **AC-48:** The final application publishes one executable host while reference
  authority and workflow persistence are separate projects with the one-directional
  dependency graph in Section 6.1.
- **AC-49:** Reference and workflow data use separate SQLite databases, `DbContext`
  types, connection strings, migrations, seeders, and test fixtures, with no shared EF
  entity, navigation, foreign key, transaction, or cross-database query.
- **AC-50:** Only the reference-authority module directly accesses reference storage;
  Core and MCP use authority ports, MCP owns its wire DTOs, and Web adapters/controllers
  do not query reference tables or inject either `DbContext`.
- **AC-51:** The replacement reference authority, workflow persistence, intake, MCP, and
  downstream path are built and proven in an isolated target composition while the
  delivered production graph, unified database, and regression tests remain unchanged.
  There is no dual write, synchronization, data copy, or per-request routing.
- **AC-52:** Production composition switches once after isolated evidence and human
  approval; the immediately following cleanup removes the delivered graph, unified
  context/schema, and delivered-only tests so only the two-database target remains.

## 25. Acceptance-to-evidence traceability

| Acceptance area | Primary evidence |
|---|---|
| AC-01–AC-06 | Architecture/static tests, Teams routing tests, renderer tests |
| AC-07–AC-14 | Proposal-schema tests, Core unit matrices, targeted live justification evals |
| AC-15–AC-22 | MCP contract/transport tests, shared-policy component tests, authoritative-port integration tests |
| AC-23–AC-28 | Agent-input/context tests, ordinary reducer/context-lifecycle tests, restart/OCC tests, live English ambiguity evals |
| AC-29–AC-40 | Persistence migrations, partial-unique-index tests, OCC/race tests, lifecycle/expiry tests, card integration tests |
| AC-41–AC-47 | Threat tests, execution-bound/startup tests, logging checks, retained live-model evaluation evidence |
| AC-48–AC-52 | Project-reference/static tests, independent migration/fixture tests, isolated-target versus delivered-host regressions, cutover and deletion source checks |

## 26. Implementation and planning boundary

The mandatory one-directional dependency is:

```text
exact /new -> deterministic reset protocol

all other free-text -> agent -> structured proposal -> Core
```

Only the Teams boundary may compare requester text with exact `/new`. The reducer,
authoritative resolvers, clarification application, lifecycle services, and persistence
do not accept requester free-text.

Before further implementation, complete the simplification plan in
`tasks/deterministic-request-intake-simplification.md`. The ordinary clarification patch
path is already present in the isolated target code; do not reintroduce or adapt the
removed target/index protocol. Contract/schema contraction precedes reducer,
persistence, renderer, confirmation, cutover, and final evaluation work.

Planning must produce coherent dependency-aware tasks with explicit code touchpoints,
deletions, tests, and exit gates. It must not start implementation during the planning
run.

### 26.1 Parallel construction and atomic replacement

The delivered production graph and its unified `GovernedAccessDbContext` remain
authoritative and unchanged during target construction. In parallel, the target path
uses:

- `GovernedAccess.ReferenceAuthority` and its reference database;
- `GovernedAccess.Workflow.Persistence` and its workflow database;
- the target preparation aggregate, reducer, interpreter, MCP catalog, Teams adapter,
  and confirmation path; and
- an isolated full-host composition that includes the complete downstream approval and
  provisioning workflow.

The two paths share no preparation rows, reference rows, workflow rows, EF entities,
database files, writes, or runtime routing. Synthetic fixtures may describe the same
logical IDs, but each path seeds its own storage; no synchronization or row copy is
allowed.

After the isolated target passes deterministic full-host evidence and receives explicit
human approval, production dependency injection and endpoints switch once to the target
graph. There is no fallback or dual registration. Existing local data is disposable,
so cutover uses explicitly reset, freshly migrated target databases rather than a
backfill. The next cleanup task deletes the delivered intake, delivered persistence,
unified schema, and delivered-only tests. Current as-built documentation is reconciled
only after that deletion and final evidence.

## 27. Success statement

> **Probabilistic language understanding at the agent boundary; deterministic state, enterprise authority, and lifecycle in Core; deterministic explicit confirmation for consequential action.**

Deterministic business behavior must not depend on deterministic natural-language
interpretation.

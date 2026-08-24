# Specification: Deterministic Conversational Request Intake

- **Status:** Draft for approval
- **Capability id:** `deterministic-request-intake`
- **Scope:** Authenticated Microsoft Teams request preparation through deterministic Adaptive Card confirmation
- **Related decisions:** [ADR 0007](docs/adr/0007-use-sparse-model-patches-and-a-deterministic-reducer.md), [ADR 0008](docs/adr/0008-separate-read-only-context-capabilities-by-authoritative-source.md), [ADR 0009](docs/adr/0009-persist-canonical-intake-and-bounded-clarification-context.md)
- **Supporting detail:** [design notes](docs/deterministic-request-intake-design.md), [proposed MCP contract](docs/contracts/deterministic-request-intake-mcp-contract.md), [test matrix](docs/evaluation/deterministic-request-intake-test-matrix.md), [implementation tasks](tasks/deterministic-request-intake.md)

## 1. Authority and relationship to current behavior

This specification defines the target replacement for request-intake preparation. It
changes only the preparation boundary before human confirmation and the model-visible
read-only context-tool catalog.

Until implementation is complete and the required evidence passes, the current product
baseline, architecture, security model, request-intake orchestration, MCP contract, and
testing documentation remain the authoritative description of the running system.
After implementation, those as-built documents must be reconciled in one change.

This feature preserves:

- authenticated personal Microsoft Teams as the only requester channel;
- one bounded Microsoft Agent Framework interpreter rather than a multi-agent design;
- deterministic validation, authorization, lifecycle transitions, and request creation;
- Adaptive Card confirmation as the only request-creation action;
- immutable submitted request scope;
- authenticated business and DevOps approval;
- the fixed eight-hour access duration;
- protected request-keyed provisioning; and
- one modular ASP.NET Core host using synthetic local data.

The constitution amendment associated with this feature keeps MCP governance strict
while moving the exact tool names from the constitution into the approved product
baseline and machine-readable contract. The target catalog in this specification is
exactly four read-only tools.

## 2. Objective

Replace full-snapshot model-driven request preparation with a hybrid conversation in
which:

1. the model interprets only the latest requester turn and proposes a small, sparse,
   schema-constrained patch;
2. deterministic application code owns and persists the canonical request draft;
3. authoritative application ports independently resolve and validate every mutable
   enterprise fact;
4. application code renders all canonical fields, choices, clarifications, validation
   guidance, review cards, and workflow outcomes; and
5. an authenticated Adaptive Card action confirms one exact immutable ready snapshot.

The feature succeeds when natural multi-turn preparation no longer depends on the
model reproducing previously accepted state, preserving complete snapshots, choosing
authoritative scope, composing consequential response prose, or initiating submission.

## 3. Governing rule

> The model interprets language and may gather bounded context. The application owns
> canonical state, authoritative facts, responses, and every consequential action.

Requester text, model output, model-visible tool results, Teams payloads, and
presentation state are untrusted inputs. None is authorization, approval, submission,
or provisioning evidence.

## 4. Fixed product and architecture decisions

1. One authenticated requester has at most one active intake in one exact Teams
   conversation.
2. The exact trimmed, case-insensitive `/new` command remains the deterministic reset
   mechanism. Natural-language reset is not added.
3. The model returns a dialogue act and a **sparse** patch. Omitted fields mean no
   proposed operation. Only explicit `set` and `clear` operations exist; the model is
   not required to repeat `keep` operations for every field.
4. The model receives exactly four typed, read-only MCP tools:
   `search_production_environments`, `get_production_environment`,
   `get_environment_roles`, and `get_incident`.
5. The four tools preserve distinct capability, authority, freshness, and failure
   boundaries. The synthetic implementation may share local storage, but the contracts
   must not collapse environment metadata and entitlement assignments into one
   authoritative response.
6. Environment search is deterministic and requester-backed. Core independently runs
   the same search policy and uses its own result as authority:
   - zero matches produce no-match clarification;
   - one match may be accepted as canonical environment scope after authoritative
     exact reload;
   - multiple matches become complete, stable, application-rendered choices requiring
     exact or persisted-ordinal selection.
7. Model-side tool use supports interpretation but is not a correctness boundary. Core
   revalidates every environment, client, role, and incident relationship regardless
   of which valid tool sequence the model used.
8. A newly introduced environment normally leads to exact environment and
   environment-scoped role context. The runtime must not fail merely because the model
   omitted a redundant lookup or did not follow one ceremonial same-turn call order.
9. A ready snapshot is never kept active behind a separate pending revision. The first
   accepted material revision atomically supersedes the ready snapshot and starts a
   new active candidate. There is no `/cancel-revision` command.
10. Discussion, a value-equal proposal, an unsupported proposal, or a model/MCP failure
    does not supersede a ready snapshot.
11. Ready snapshots expire 30 minutes after becoming ready. Non-mutating turns do not
    refresh the deadline. A replacement ready snapshot receives a new deadline.
12. Process-local model conversation memory is convenience only. SQLite persists the
    sanitized canonical candidate and bounded structured clarification context, not raw
    prompts, transcripts, provider traces, or model reasoning.
13. Adaptive Card confirmation remains mandatory even when requester text clearly asks
    to submit, approve, grant, or provision.
14. The feature adds no requester channel, agent, generic workflow engine, deployable
    service, message broker, distributed lock, or real production integration.

## 5. Scope

### 5.1 Included

- Natural-language collection and revision of environment, role, justification, and
  optional incident.
- Derivation of client from the authoritative environment.
- Sparse model patches with explicit `set` and `clear` operations.
- Deterministic evidence checks for every proposed material change.
- Deterministic candidate merge, dependency cascades, canonicalization, validation,
  and readiness.
- Four read-only MCP capabilities representing environment discovery, exact
  environment metadata, environment-scoped entitlements, and exact incident context.
- Independent authoritative Core resolution and revalidation.
- Application-owned progress, choices, focused questions, corrections, ready summaries,
  submission guidance, and failures.
- Deterministic exact-field clearing commands.
- Persisted ordered environment and role choices sufficient for restart-safe ordinal
  replies.
- Whole-turn serialization for one actor and conversation.
- Immediate invalidation of a ready snapshot after the first accepted material
  revision.
- Exact card confirmation, replay protection, authoritative revalidation, and immutable
  request creation.
- Deterministic tests plus a small optional credentialed live-model evaluation focused
  on interpretation quality and correct restraint.

### 5.2 Excluded

- Natural-language request submission.
- Model-visible submission, approval, provisioning, retry, revocation, credential, or
  other state-changing tools.
- Model-selected requester identity, client ownership, approver, duration, request ID,
  approval state, provisioning state, or grant state.
- Generic environment search, fuzzy or semantic search, ranking, pagination, arbitrary
  query execution, or model-selected result subsets.
- Cross-environment or client-wide role search.
- Incident listing, title matching, partial-ID search, or semantic incident discovery.
- Open-ended model-authored requester response prose.
- A durable raw conversation transcript or serialized provider/MAF session.
- A second agent, multi-agent orchestration, RAG subsystem, generic dialogue engine,
  generic workflow engine, or additional deployable service.
- Changes to business approval, DevOps approval, provisioning, retry, audit, grant
  duration, or expiry behavior.
- Real enterprise identity, production reference systems, production credentials, or
  real access provisioning.

## 6. Actors and authority

| Actor or component | Permitted responsibility | Prohibited responsibility |
|---|---|---|
| Authenticated requester | Describe, revise, reset, review, and confirm a request | Choose acting identity, approver, duration, approval, or provisioning |
| Model interpreter | Classify the current turn, propose sparse field changes, and use the four read-only context tools | Own state, make an authoritative choice, submit, approve, provision, or assert validation success |
| Core application | Merge supported changes, resolve authoritative context, validate, persist, and select the next typed outcome | Depend on Teams, MAF, MCP SDK, React, or EF-specific contracts |
| Authoritative context ports | Search or load environment, entitlement, and incident facts for Core | Trust model-visible tool output as application authority |
| Response renderer | Render canonical progress, choices, questions, cards, and safe failures | Infer authoritative values or state from model prose |
| Teams adapter | Authenticate transport, invoke the application turn, deliver responses/cards, and receive card actions | Treat activity payload fields as identity or authority |
| Submission service | Reload and confirm one exact ready preparation | Accept model- or client-selected scope assertions |

## 7. Canonical request draft

The canonical candidate contains:

| Field | Source and rule |
|---|---|
| Requester | Authenticated server context; never model- or payload-selected |
| Client | Derived from the authoritative selected environment |
| Environment | Required; exact authoritative production-environment identifier |
| Requested role | Required; must be currently assigned to the selected environment |
| Justification | Required; requester-authored operational problem, task, or intended outcome |
| Incident | Optional; when present, must be active and belong to the selected environment |
| Duration | Fixed at eight hours; never part of the model patch |

The model may propose operations only for `environmentId`, `requestedRoleId`,
`justification`, and `incidentId`.

The durable intake state contains only the minimum provider-neutral information needed
to continue and govern preparation:

- authenticated actor/conversation binding;
- active preparation ID and lifecycle status;
- sanitized canonical candidate;
- bounded structured clarification context when one focused choice is active;
- ready timestamp, confirmation deadline, and reserved request ID when ready;
- correlation metadata and optimistic-concurrency version.

There is no pending-revision candidate alongside an active ready snapshot.

## 8. Intake lifecycle

The relevant lifecycle states remain:

- `Collecting`: one active mutable candidate;
- `Ready`: one immutable snapshot eligible for confirmation until expiry;
- `Superseded`: terminal preparation that cannot be confirmed;
- `Submitted`: terminal preparation bound to the created immutable request; and
- `Expired`: terminal ready preparation whose confirmation deadline passed.

A normal collecting turn updates the current active candidate. When deterministic
validation produces complete canonical details, the intake becomes `Ready` and
receives an immutable preparation identity, reserved request identity, and 30-minute
confirmation deadline.

### 8.1 Revision of a ready snapshot

A ready snapshot is immutable. A later normal message is handled as follows:

| Assessed turn | Ready snapshot behavior |
|---|---|
| Discussion, submission guidance, unclear turn, value-equal set, or all rejected unsupported changes | Preserve the exact ready snapshot and deadline |
| Model, MCP, timeout, cancellation, or persistence failure before commit | Preserve the exact ready snapshot and deadline |
| Accepted material change that remains complete | Atomically supersede the old snapshot and create a new ready intake, identity, deadline, and card |
| Accepted material change that becomes incomplete or needs clarification | Atomically supersede the old snapshot and create a new collecting intake containing the sanitized revised candidate and any bounded clarification context |

An accepted material environment search with multiple or zero results is a revision.
It supersedes the old ready snapshot and starts a collecting intake in which the old
environment, derived client, and role are no longer confirmable. Unrelated accepted
fields may be preserved. The old card is stale immediately after commit.

There is no rollback command that revives the old ready identity. The requester may
state the old values again or use `/new`.

## 9. Deterministic pre-model routing

The Teams boundary handles these inputs without invoking the model or MCP:

| Input | Behavior |
|---|---|
| Unauthenticated, unsupported tenant, or non-personal activity | Reject safely; create no intake |
| Missing, blank, attachment-only, or reaction-only content | Ask for text or ignore safely; no mutation |
| Exact trimmed case-insensitive `/new` | Supersede/expire the active unsubmitted intake; the next normal message starts clean |
| `/new` plus additional text | Explain that `/new` must be sent alone; preserve state |
| Exact supported field-clear command | Send one application-owned `clear` operation directly to the reducer |

Supported clear commands are the complete normalized message:

```text
clear environment      remove environment
clear role             remove role
clear justification    remove justification
clear incident         remove incident
```

Other removal wording does not clear a field. The application preserves state and
explains the exact supported commands rather than adding a second natural-language
parser to Core.

Persisted ordinal choice replies may also be resolved deterministically before model
invocation when the message is an unambiguous ordinal and the active clarification
context identifies exactly one target and ordered choice set.

## 10. Model turn contract

### 10.1 Dialogue acts

Every normal model turn returns exactly one dialogue act:

| Dialogue act | Meaning | Mutation allowed |
|---|---|---|
| `updateDraft` | Requester supplied, replaced, removed, or searched for request data | Only through the declared sparse patch or validated environment-search observation |
| `discussDraft` | Requester asked a bounded question or discussed a hypothetical without requesting a change | No |
| `submissionGuidance` | Requester asked to submit, approve, grant, or provision through text | No |
| `unclear` | Intent cannot be classified conservatively | No |

A message that revises the request and also asks to submit is `updateDraft`. The
revision may produce a new review card, but the text turn creates no request.

### 10.2 Sparse patch

Each present patch field contains one operation:

| Operation | Meaning | Value rule |
|---|---|---|
| `set` | Propose an explicit requester-backed value | Nonblank value required |
| `clear` | Explicitly remove the field | Value prohibited |

Omitted fields mean no proposed operation. There is no serialized `keep` operation.
The model cannot patch client, requester, duration, preparation identity, reserved
request identity, status, approver, decision, operation, or grant fields.

A provider-neutral shape equivalent to the following is required:

```csharp
public enum RequestTurnDialogueAct
{
    UpdateDraft,
    DiscussDraft,
    SubmissionGuidance,
    Unclear,
}

public abstract record FieldOperation<T>;
public sealed record SetField<T>(T Value) : FieldOperation<T>;
public sealed record ClearField<T>() : FieldOperation<T>;

public sealed record RequestCandidatePatch(
    FieldOperation<string>? EnvironmentId,
    FieldOperation<string>? RequestedRoleId,
    FieldOperation<string>? Justification,
    FieldOperation<string>? IncidentId);

public sealed record RequestTurnProposal(
    RequestTurnDialogueAct DialogueAct,
    RequestCandidatePatch Patch);

public sealed record EnvironmentSearchObservation(string Query);

public sealed record RequestTurnInterpretation(
    RequestTurnProposal Proposal,
    EnvironmentSearchObservation? EnvironmentSearch);
```

`EnvironmentSearchObservation` is created by the Web interpretation boundary from an
observed successful search call. It is not emitted by the model as trusted JSON, and
raw MCP result payloads do not cross into Core.

Example:

```json
{
  "dialogueAct": "updateDraft",
  "patch": {
    "justification": {
      "operation": "set",
      "value": "Investigate elevated customer errors and verify the mitigation."
    }
  }
}
```

The closed response schema must reject unknown properties, unsupported fields,
unsupported operations, missing values for `set`, values for `clear`, and strings over
domain limits. A non-update dialogue act requires an empty patch and no environment
search. `updateDraft` requires at least one field operation or one valid environment
search observation before normalization.

## 11. Deterministic change evidence

A schema-valid patch is still untrusted. Core applies a changed operation only when it
is supported by the current requester message or the exact active structured choice
context.

Evidence matching uses Unicode NFC normalization, collapsed whitespace, and ordinal
case-insensitive comparison. It does not search assistant text, model prose, MCP prose,
or earlier requester messages.

| Field/change | Required evidence |
|---|---|
| Exact environment set | Canonical environment ID in the current message, or exact/ordinal selection from active environment choices |
| Environment search | Normalized query is a contiguous substring of the current requester message and satisfies the search schema |
| Incident set | Exact canonical incident ID in the current message |
| Role set | Exact role ID or complete authoritative display name in the current message, or exact/ordinal selection from active role choices |
| Initial/replacement justification | Proposed normalized text is a contiguous substring of the current message |
| Justification append | Existing normalized value is preserved as an exact prefix and the nonblank appended suffix occurs in the current message |
| Clear | Complete message is one exact application-owned clear command |

A value-equal `set` is normalized to no change and does not require mutation evidence.
An unsupported changed operation is ignored, the canonical value is preserved, and a
bounded model-drift signal is recorded. Unsupported changes never supersede a ready
snapshot.

## 12. MCP capability boundary

The exact proposed contract is defined in
[`docs/contracts/deterministic-request-intake-mcp-contract.md`](docs/contracts/deterministic-request-intake-mcp-contract.md).
The four capabilities are:

| Tool | Capability and likely enterprise authority |
|---|---|
| `search_production_environments` | Human-readable deterministic discovery over a service-catalog or CMDB search projection |
| `get_production_environment` | Exact environment identity and owning client from the environment registry/CMDB |
| `get_environment_roles` | Current environment-scoped assignable roles from IAM or an entitlement catalog |
| `get_incident` | Exact incident state and affected environment from an ITSM system |

The separation is intentional even when synthetic adapters share SQLite. It preserves
independent ownership, permissions, freshness, latency, and failure semantics.

All four tools are read-only, use closed typed schemas, and expose no generic query or
workflow action. The interpreter rejects a missing tool, an additional model-visible
tool, a non-read-only annotation, or malformed contract data.

### 12.1 Tool-use policy

- Search requires one requester-backed query and never receives an empty catalog call.
- Exact environment lookup requires one stable environment ID and returns no roles.
- Role lookup requires one exact environment ID and returns only roles currently
  assigned to that environment.
- Incident lookup requires one exact incident ID; incident titles or descriptions are
  never converted to IDs.
- One normal turn permits at most one invocation of each tool, four total MCP calls,
  and six provider iterations.
- Concurrent tool calls remain disabled.
- Unknown calls, repeated calls beyond the bounds, malformed results, timeout, or
  cancellation produce a typed safe failure.

The model is instructed to use exact environment and role context when a turn needs to
interpret a newly introduced environment-role relationship. However, the Web boundary
does not reject a safe turn solely because an otherwise redundant exact lookup was
omitted or because the model used a different valid read-only order.

A role lookup may use an exact environment ID obtained from the current message,
canonical state, incident context, an exact environment lookup, or a unique search
result. A role-only revision for an unchanged canonical environment does not require a
redundant exact environment lookup in the same turn.

Core independently reloads all accepted facts. Model-visible tool results aid
interpretation only.

### 12.2 Environment search outcomes

Core independently executes the deterministic search policy against the observed
requester-backed query and treats its own result as authoritative:

| Authoritative result | Deterministic application outcome |
|---|---|
| Zero matches | Preserve unrelated fields; environment remains unresolved; render no-match guidance |
| One match | Exact-reload and accept that environment; derive client; revalidate/clear role and incident compatibility; continue to the next missing field |
| Two to twenty matches | Persist complete stable environment IDs as choices and ask for exact or ordinal selection |
| More than twenty matches | Return typed `environment_query_too_broad`; do not truncate or rank |

The model cannot filter, reorder, truncate, or choose from multiple results. A unique
result is accepted because deterministic Core—not the model—reproduces the query,
observes uniqueness, reloads the exact entity, and still requires final card review.

A mismatch between model-visible MCP results and Core's authoritative result is
observable drift. Core follows its own result and must not promote MCP payload content
to canonical state. A malformed tool result or contract violation still fails the
model turn safely.

## 13. Deterministic reducer

Core receives the authenticated binding, latest raw message, current canonical intake,
active structured choice context, provider-neutral interpretation, authoritative
context ports, server clock, and correlation metadata. It produces one closed typed
outcome.

For each present field operation, the reducer:

1. normalizes a value-equal `set` to no change;
2. verifies deterministic current-message or persisted-choice evidence;
3. ignores unsupported changed operations while preserving the canonical value;
4. applies supported operations to a temporary candidate;
5. applies dependency cascades in a fixed order;
6. discards any external client value and re-derives client from environment;
7. independently searches or reloads authoritative entities and relationships;
8. canonicalizes valid values and clears/rejects invalid changed values without
   replacing unrelated valid fields;
9. chooses one next issue or ready outcome; and
10. persists only the sanitized candidate, lifecycle, and applicable structured choice
    context in one focused commit.

The model never decides whether the candidate is rejected, incomplete, ready, or
submitted.

### 13.1 Resolution order

The first applicable issue is selected in this order:

1. incident existence, activity, and incident-to-environment compatibility;
2. environment identity, no-match, or ambiguity;
3. role availability and assignment for the selected environment; and
4. justification presence and syntactic sufficiency.

An absent incident is not an issue.

### 13.2 Dependency cascades

| Accepted change | Deterministic consequence |
|---|---|
| Environment changes | Re-derive client; retain role only if assigned to the new environment; surface an incident conflict rather than silently choosing when the incident belongs elsewhere |
| Environment clears | Clear client and role; preserve justification; retain incident for later explicit conflict/scope resolution |
| Incident sets with no environment | Reload incident and derive its environment/client; retain role only if assigned there |
| Incident conflicts with explicit environment | Preserve the conflict and ask the requester to choose; do not silently prefer either side |
| Incident clears | Preserve environment, client, role, and justification |
| Role sets or clears | No cascade beyond authoritative role validation |
| Justification sets or clears | No cascade beyond authorship and syntactic validation |

Every field is legally clearable. Clearing a required field produces a collecting
candidate rather than an invalid state transition.

### 13.3 Role handling

A role is canonical only when the entitlement authority currently assigns it to the
selected environment. The application never substitutes a different role.

When role is missing or invalid, the renderer receives all and only the current roles
for the selected environment, in stable application-owned order. A known environment
with no assigned roles returns a typed rejection and cannot become ready. Role source
failure preserves the last committed candidate and produces retry guidance; it does
not infer roles from environment metadata or earlier model context.

### 13.4 Justification handling

Core proves requester authorship, not semantic business quality. After evidence
succeeds it:

1. trims and Unicode-normalizes the value;
2. requires at least three non-whitespace tokens;
3. rejects a value equal to one canonical identifier or reference display name;
4. rejects a value composed only of canonical identifiers/reference display names; and
5. enforces the existing maximum length.

The business approver remains responsible for judging whether the explanation is
adequate for access.

### 13.5 Incident conflict

When an exact active incident and explicitly selected environment disagree, the
application asks the requester to choose one deterministic resolution:

- use the incident's authoritative environment;
- continue with the selected environment without the incident; or
- provide another exact compatible incident ID.

No candidate becomes ready while the conflict remains.

## 14. Closed application outcomes and rendering

One normal text turn returns exactly one of:

| Outcome | Application-owned data |
|---|---|
| `ClarificationRequired` | Target, canonical progress, authoritative choices when applicable |
| `CandidateRejected` | Safe field/source errors and remaining canonical progress |
| `DraftDiscussion` | Unchanged draft identity plus bounded deterministic help |
| `SubmissionGuidance` | Unchanged draft and card-confirmation guidance |
| `ReadyForConfirmation` | Exact immutable ready intake and reserved request identity |
| `Failed` | Typed safe failure with no uncommitted mutation |

Application code renders every canonical field and identifier, environment and role
choice, focused question, validation correction, accepted-progress summary, review
card, deadline, workflow status, and retry instruction.

`discussDraft` is deliberately narrow. The initial feature supports only bounded help
such as showing the current draft, explaining which field is missing, explaining why
an exact identifier or assigned role is required, and describing how to revise or
clear a field. It does not promise open-ended model-authored discussion.

The renderer must never echo raw model payloads, complete MCP payloads, internal
exception text, or wording that implies persistence, submission, approval,
provisioning, or grant success before the corresponding deterministic transition.

## 15. Adaptive Card confirmation

Adaptive Card confirmation is the only request-creation path.

The ready card displays application-owned requester identity, client and environment
names/IDs, role name/ID, exact persisted justification, incident information or an
explicit no-incident value, fixed eight-hour lifetime, deadline, and a statement that
confirmation submits for business approval but does not approve or grant access.

The action payload contains only a fixed schema version and exact preparation ID. It
contains no trusted identity, scope, role, duration, approval, or provisioning data.

On confirmation the application:

1. derives the actor from authenticated Teams context;
2. parses the closed action schema;
3. reloads the exact preparation ID;
4. verifies full actor and conversation ownership;
5. rejects collecting, expired, superseded, foreign, invalid, or malformed state;
6. independently revalidates requester, environment, client, role, justification, and
   incident;
7. atomically marks the intake submitted and creates one immutable
   `AwaitingBusinessApproval` request plus request-created audit evidence; and
8. returns the same request ID on safe replay.

A stale card can never submit a newer scope. Best-effort visual card replacement is a
usability feature, not an authorization control.

Text such as "submit it" produces only deterministic guidance and, when eligible,
re-renders the exact active ready card.

## 16. Persistence, restart, and concurrency

The application serializes the whole normal-message turn for one authenticated actor
and exact conversation:

```text
load/create active intake
  -> interpret latest message
  -> reduce and authoritatively validate
  -> persist canonical outcome
  -> select presentation
```

The process-local gate key includes channel, tenant, channel actor, conversation, and
requester. Different conversations remain concurrent.

SQLite uniqueness and optimistic concurrency remain the durable correctness boundary.
At most one `Collecting` or `Ready` intake may exist for one complete binding.

Structured clarification context stores only:

- preparation ID and candidate version;
- one clarification target;
- ordered canonical environment IDs or role IDs;
- creation timestamp.

It contains no model prose, complete tool result, or transcript. Applying a choice
consumes the context. Any committed candidate change clears stale context and stores
only the next applicable context.

After restart, a fresh model session receives the durable canonical candidate. Exact
or ordinal replies can continue only from matching persisted structured context.
Other relative replies are clarified rather than guessed. Card confirmation and all
downstream workflow actions ignore model memory.

Required race behavior:

- concurrent first messages create at most one active intake;
- same-conversation turns do not reduce from the same stale candidate;
- if confirmation commits before a revision, the immutable submitted request wins and
  the later turn cannot alter it;
- if a material revision commits first, the old preparation becomes superseded and its
  card fails as stale;
- duplicate or concurrent confirmation creates exactly one request and returns the same
  request identity.

## 17. Failure behavior

| Failure | Required state behavior |
|---|---|
| Malformed model output or unsupported schema | Preserve last committed candidate/ready snapshot |
| Model, MCP, source, network, timeout, or cancellation failure | Preserve last committed state; return typed retry guidance |
| Unknown environment or incident | Preserve unrelated fields; reject/clear only the invalid changed value |
| Invalid or unavailable role | Preserve environment and unrelated fields; show current authoritative roles when available |
| Environment search returns zero | Preserve unrelated fields; environment unresolved; render no-match guidance |
| Environment search is too broad | Preserve state; request a narrower description or exact ID |
| Persistence failure before commit | Do not claim the change occurred |
| Atomic ready replacement failure | Preserve the original ready snapshot |
| Stale, foreign, expired, or malformed card | Create no request; return state-specific safe guidance |
| Duplicate confirmation | Return the exact already-created request |

No failed text turn may create a request, approval, provisioning operation, grant, or
partial replacement state.

## 18. Security, privacy, and observability

- Authenticate and validate a personal Teams conversation before intake creation or
  model execution.
- Derive acting identity and claims exclusively from authenticated server context.
- Validate the closed model schema before translation into provider-neutral Core types.
- Treat all model strings, MCP values, choice identifiers, card payloads, and display
  data as untrusted.
- Encode requester and reference text in explicitly labeled renderer fields.
- Independently reload identifiers and relationships from authoritative Core ports.
- Expose only the exact four approved read-only tools and reject any additional or
  state-changing capability.
- Propagate cancellation and enforce explicit model, MCP, and enterprise-source
  timeouts.
- Log correlation ID, actor binding, dialogue act, operation verdicts, source/tool
  name, duration, typed outcome, lifecycle transition, and side-effect counts without
  logging secrets, raw prompts, transcripts, provider reasoning, or complete tool
  payloads.
- Record model-tool sequence as diagnostics, not as authorization evidence or the
  primary runtime correctness criterion.

## 19. Testing and evaluation

Deterministic tests own the combinatorial behavior. The optional live-model suite owns
only stochastic interpretation quality.

Required evidence is defined in
[`docs/evaluation/deterministic-request-intake-test-matrix.md`](docs/evaluation/deterministic-request-intake-test-matrix.md).
At minimum it must prove:

- omission, model context loss, and snapshot-shaped output cannot erase or overwrite
  canonical state;
- unsupported `set`/`clear` operations cannot mutate state;
- zero, unique, multiple, and too-broad environment searches are distinct and safe;
- environment, entitlement, and incident facts are independently revalidated;
- source failures preserve state and create no consequential side effect;
- dependency cascades and incident conflicts are deterministic;
- application-rendered values come from authoritative records;
- the first accepted material revision invalidates the old ready card immediately;
- stale, duplicate, concurrent, and replayed card actions converge safely; and
- natural-language submission creates no request.

The live-model dataset should contain approximately 10-12 high-value scenarios rather
than reproducing the deterministic matrix. It grades normalized canonical outcomes,
accepted/rejected operations, grounding, correct restraint, and zero consequential
side effects. Tool-call order may be reported diagnostically but is not the headline
pass condition when Core reaches the safe correct outcome.

## 20. Success criteria

The feature is complete when:

1. the model contract is sparse and provider-neutral, with no required `keep` fields;
2. Core is the only owner of canonical candidate merge, authoritative resolution,
   dependency cascades, readiness, and lifecycle transitions;
3. the exact four read-only MCP capabilities are implemented with closed contracts and
   independent source semantics;
4. unique deterministic environment search results can become canonical only after
   Core reruns the query and exact-reloads the environment;
5. multiple results remain application-owned choices and zero/too-broad results never
   invent or silently select scope;
6. role assignments come from the entitlement boundary and are revalidated before
   readiness and confirmation;
7. application code renders every authoritative value and consequential response;
8. no separate pending-revision state or `/cancel-revision` behavior exists;
9. the first accepted material revision atomically supersedes the old ready snapshot,
   while non-mutating or failed turns preserve it;
10. persisted bounded choices support restart-safe exact/ordinal replies without raw
    conversation retention;
11. ordinary text cannot create a request or expose a state-changing model capability;
12. exact card confirmation revalidates and creates at most one immutable request;
13. required build, unit, component, integration, and affected frontend gates pass;
14. the reviewed live-model suite passes with zero requests, approvals, provisioning
    operations, and grants; and
15. the product baseline, architecture, security model, request-intake orchestration,
    MCP contract, testing strategy, operator guidance, README, and roadmap are updated
    only after implementation evidence establishes the new as-built behavior.

## 21. Implementation boundary

The implementation may rename internal types to follow project conventions, but it
must preserve the externally observable behavior, trust boundaries, closed contracts,
and success criteria above. Any proposal to add natural-language submission, a fifth
model-visible tool, durable raw conversation history, model-authored consequential
responses, real production effects, another requester channel, or another deployable
service is a separate governed feature.

# Specification: Deterministic Conversational Request Intake

- **Status:** Draft for human review
- **Capability id:** `deterministic-request-intake`
- **Scope:** Authenticated Teams request preparation through deterministic Adaptive
  Card confirmation

## Authority and relationship to current behavior

This specification defines a replacement for the request-intake preparation design.
It is intentionally self-contained and does not depend on any existing feature
specification or implementation task list.

The project constitution remains authoritative. The current product baseline remains
authoritative outside the request-intake preparation behavior explicitly changed by
this specification. In particular, this specification preserves:

- authenticated personal Microsoft Teams as the only requester channel;
- exactly two model-visible, read-only MCP tools;
- deterministic validation and authorization;
- Adaptive Card confirmation as the only request-creation action;
- immutable submitted request scope;
- authenticated human business and DevOps approval;
- fixed eight-hour access duration;
- protected request-keyed provisioning; and
- the single modular ASP.NET Core host and synthetic local data boundary.

Once approved and implemented, the product baseline, architecture, security model,
request-intake orchestration, testing strategy, and operator guidance must be
reconciled with the verified as-built behavior.

## Objective

Replace full-snapshot model-driven request preparation with a hybrid conversation
pattern in which:

1. the model interprets the latest requester message as a small, schema-constrained
   intent and field patch;
2. deterministic application code owns the canonical request draft, merges proposed
   changes, resolves authoritative context, validates relationships, and decides the
   next state;
3. application code renders every authoritative field, choice, clarification,
   summary, and workflow outcome; and
4. an authenticated Adaptive Card action confirms the exact immutable ready snapshot.

The feature is successful when requesters can prepare a complete request naturally
over multiple turns without making the model responsible for preserving state,
choosing authoritative facts, composing consequential responses, or initiating
submission.

## Fixed assumptions and decisions

- One authenticated actor has at most one active intake in one exact Teams
  conversation.
- The exact trimmed, case-insensitive `/new` command remains the deterministic reset
  mechanism; natural-language reset is not added.
- A sanitized unfinished revision of a ready draft and its structured clarification
  context are persisted explicitly; raw conversation is not made durable.
- Ready snapshots expire 30 minutes after becoming ready. Discussion and value-equal
  turns do not refresh that deadline; a replacement ready snapshot receives a new
  deadline.
- An evidence-backed unfinished revision suspends confirmation of the old ready
  snapshot until the revision completes or the requester cancels it.
- Adaptive Card confirmation remains mandatory even when a requester types a clear
  submission instruction.
- The design requires no new dependency, project, requester channel, or deployable
  service.

## User experience

The requester may provide all details in one message or provide them incrementally.
The application acknowledges accepted canonical state, asks exactly one focused next
question when possible, and presents a deterministic review card when the draft is
ready.

Example:

```text
Requester: I need production access for INC-1042.

Assistant: I found active incident INC-1042 for Client Alpha in
PROD-ALPHA-EU. What operational work requires this access?

Requester: Investigate elevated customer errors and verify the mitigation.

Assistant: Which role is required?
- ProductionReadOnly
- ProductionSupport
- ProductionDeployment

Requester: ProductionSupport.

Assistant: [application-rendered Adaptive Card containing the exact ready snapshot]
```

Selecting **Confirm and submit** on the card creates an immutable request in
`AwaitingBusinessApproval`. It does not approve or grant access.

## Governing design rule

> The model interprets language. The application owns state and responses. The
> requester confirms through a deterministic structured action.

Model output, conversation text, MCP results, card payloads, and presentation state
are inputs to validation. None is authorization or approval evidence.

## Scope

### Included

- Natural-language collection and revision of environment, role, justification, and
  optional incident.
- Derivation of client from the authoritative environment.
- Patch-shaped model output with explicit `keep`, `set`, and `clear` operations.
- Deterministic candidate merge, canonicalization, validation, and readiness.
- Application-owned clarification questions, canonical progress, choices, validation
  guidance, and ready summaries.
- Ready-draft discussion and revision without accidental state replacement.
- Deterministic evidence checks for every proposed field change.
- Deterministic exact-field clearing commands instead of open-ended clear-intent
  interpretation.
- Whole-turn serialization for one authenticated actor and conversation.
- Atomic replacement of a ready draft.
- Persisted sanitized pending-revision and ordered-choice context.
- Process-local, bounded model conversation memory that is never canonical state.
- Exact Adaptive Card confirmation, replay protection, authoritative revalidation,
  and immutable request creation.
- Deterministic automated tests and optional credentialed live-model evaluation of
  interpretation quality.

### Excluded

- Natural-language request submission.
- Model-visible state-changing tools or local submission functions.
- Browser, Slack, CLI, email, or other requester channels.
- Model-selected requester identity, client ownership, approver, duration, request
  identifier, approval, provisioning, retry, or grant state.
- A separate role-listing MCP tool.
- Durable raw conversation transcripts, prompts, provider traces, or model reasoning.
- Model-generated conversational response prose.
- Model-proposed environment filtering, narrowing, ranking, or choice subsets.
- Heuristic classification or rewriting of authoritative display text.
- Card-delivery receipts or acknowledgement state.
- Collecting-intake idle expiry.
- Real identity, production reference systems, production credentials, or real access
  provisioning.
- A generic dialogue engine, workflow engine, second agent, RAG subsystem, separate
  deployable service, message broker, or distributed lock.
- Changes to business approval, DevOps approval, provisioning, retry, audit, or grant
  expiry behavior.

## Actors and authority

| Actor or component | Permitted responsibility | Prohibited responsibility |
|---|---|---|
| Authenticated requester | Describe, discuss, revise, reset, and confirm a request | Choose identity, approver, duration, approval, or provisioning |
| Model interpreter | Interpret the latest message, propose explicit field changes, use the two read-only context tools | Own canonical state, submit, approve, provision, or assert validation success |
| Core application | Merge patches, validate authoritative state, decide lifecycle and next clarification | Depend on Teams, MAF, MCP SDK, React, or EF contracts |
| Response renderer | Render canonical progress, choices, questions, cards, and safe failures | Infer scope or workflow state from model prose |
| Teams adapter | Authenticate transport, invoke the shared application turn, deliver text/cards, receive card actions | Treat activity payloads as identity or authority |
| Submission service | Reload and confirm one exact ready preparation | Accept model-selected or browser-selected scope |

## Canonical request draft

The persisted canonical candidate contains:

| Field | Source and rule |
|---|---|
| Requester | Authenticated server context; never model- or payload-selected |
| Client | Derived from the authoritative selected environment |
| Environment | Required; exact authoritative production-environment identifier |
| Requested role | Required; must be assigned to the selected environment |
| Justification | Required; requester-stated operational problem, task, or intended outcome |
| Incident | Optional; when present, must be active and belong to the selected environment |
| Duration | Fixed at eight hours; not part of the model patch |

All four model-patchable fields may be explicitly cleared. Clearing environment, role,
or justification is a valid revision that makes the working candidate incomplete;
clearing incident removes optional incident context.

The persisted intake may additionally contain:

| State | Purpose |
|---|---|
| Pending revision candidate | Sanitized working candidate for an unfinished revision of an immutable ready snapshot |
| Clarification context | Target, ordered canonical option identifiers, creation time, and candidate persistence version |

For a collecting intake, the canonical candidate is the working candidate. For a
ready intake without a pending revision, the immutable prepared details are the
working candidate. For a ready intake with a pending revision, that persisted pending
candidate is the working candidate supplied to the next reducer turn. The immutable
ready details remain stored but are not confirmable while pending revision state
exists.

Confirmation eligibility is derived rather than model-selected: the intake must be
owned, `Ready`, unexpired, otherwise valid, and have no pending revision state.

Conversation history may help interpret language, but it cannot override, replace, or
reconstruct the persisted working candidate or structured clarification context.

Clarification context is valid only when its bound preparation ID and candidate
persistence version match the current working state. Applying an option consumes the
context. Any committed candidate change clears the previous context and either stores
the next clarification context selected by the reducer or stores none. Stale context
is ignored and removed without guessing.

## Turn interpretation contract

### Dialogue acts

Every normal text turn must produce exactly one of these untrusted dialogue acts:

| Dialogue act | Meaning | Mutation allowed |
|---|---|---|
| `updateDraft` | The requester explicitly supplied, replaced, or removed request data | Only through the declared patch |
| `discussDraft` | The requester asked a question or discussed a hypothetical without requesting a change | No |
| `submissionGuidance` | The requester asked to submit, approve, grant, or provision through text | No; explain that the ready card is required |
| `unclear` | Intent cannot be determined conservatively | No |

The exact `/new` command is handled before model invocation and therefore is not a
model dialogue act.

Questions such as "could I use the recovery environment?" are discussion unless the
requester explicitly asks to change the draft. Instructions such as "change it to the
recovery environment" are updates.

A message that both revises the request and asks to submit is `updateDraft`. The
revision may be prepared and presented, but no request is submitted in that turn.

### Patch operations

The model returns a patch for exactly these requester-controlled fields:

- `environmentId`;
- `requestedRoleId`;
- `justification`; and
- `incidentId`.

Each field has one explicit operation:

| Operation | Meaning | Value rule |
|---|---|---|
| `keep` | Preserve the canonical value exactly | Value must be absent |
| `set` | Propose an explicit requester-supplied value | Nonblank value required |
| `clear` | Explicitly remove the current optional or replaceable value | Value must be absent |

The model does not patch `clientId`, requester, duration, preparation ID, reserved
request ID, status, approver, approval, operation, or grant fields.

### Provider-neutral contract shape

The Core-facing contract must use a closed discriminated model equivalent to:

```csharp
public enum RequestTurnDialogueAct
{
    UpdateDraft,
    DiscussDraft,
    SubmissionGuidance,
    Unclear,
}

public abstract record FieldChange<T>;

public sealed record KeepField<T>() : FieldChange<T>;

public sealed record SetField<T>(T Value) : FieldChange<T>;

public sealed record ClearField<T>() : FieldChange<T>;

public sealed record RequestCandidatePatch(
    FieldChange<string> EnvironmentId,
    FieldChange<string> RequestedRoleId,
    FieldChange<string> Justification,
    FieldChange<string> IncidentId);

public sealed record RequestTurnProposal(
    RequestTurnDialogueAct DialogueAct,
    RequestCandidatePatch Patch);
```

Exact names may follow existing conventions, but the closed semantics must remain.
MAF/provider JSON types belong in `GovernedAccess.Web` and must be translated into
provider-neutral Core port types.

The initial model response contract identifier is `request-intake-turn-v1`. A valid
serialized proposal equivalent to setting only justification is:

```json
{
  "dialogueAct": "updateDraft",
  "patch": {
    "environmentId": { "operation": "keep" },
    "requestedRoleId": { "operation": "keep" },
    "justification": {
      "operation": "set",
      "value": "Investigate elevated customer errors and verify the mitigation."
    },
    "incidentId": { "operation": "keep" }
  }
}
```

### Closed model schema rules

The model response schema must:

- reject unknown object properties;
- require only `dialogueAct` and `patch`;
- require the dialogue act and every field operation;
- restrict operations to `keep`, `set`, and `clear`;
- require a value only for `set`;
- reject a value for `keep` or `clear`;
- cap string lengths at the applicable domain maximum;
- contain no requester, mutable client, duration, approver, request, approval,
  provisioning, grant, response-prose, environment-filtering, ranking, or option-list
  fields.

Schema-valid output remains untrusted.

### Cross-field interpretation invariants

The specification distinguishes enforceable controls from instructions that only
guide probabilistic interpretation:

| Invariant | Enforced by |
|---|---|
| Non-update dialogue acts require an all-`keep` patch | Schema and reducer |
| `updateDraft` requires at least one `set` or `clear` before normalization | Schema and boundary parser |
| `set` requires a value; `keep` and `clear` reject one | Schema and boundary parser |
| Client is not a mutable patch field | Schema |
| Every applied `set` or `clear` reflects the current requester message or persisted ordered-choice context | Reducer evidence policy |
| Incident identifiers exist, are active, and match scope | Authoritative reload and reducer |
| Environment and role values exist and are compatible | Authoritative reload and reducer |
| Justification is requester-authored | Reducer evidence policy |
| A bare access request or identifier-only text fails the justification floor | Reducer syntactic policy |
| Questions and hypotheticals are distinguished from explicit revisions | Prompt only; evidence checks limit mutation and the card exposes final scope |
| Requests to bypass policy are not treated as justification | Prompt only beyond the syntactic floor; business approval remains the quality control |
| Titles, alerts, or descriptions are not inferred as incident identifiers | Prompt plus exact identifier evidence and authoritative reload |

A schema or reducer invariant violation produces a typed invalid-interpretation
outcome and no candidate mutation. A prompt-only instruction is not described as a
deterministic guarantee.

### Deterministic change evidence

Patch shape alone is insufficient: a model could emit `set` for every field and
recreate full-snapshot replacement behavior. The reducer therefore evaluates each
field operation against the latest raw requester message and the exact persisted
clarification context.

Evidence matching uses Unicode NFC normalization, ordinal case-insensitive comparison,
and collapsed whitespace. It does not search assistant text, model prose, MCP prose,
or earlier requester messages, except through the explicit persisted ordered-choice
context described below.

Every `set`, including one that fills a currently null field, must satisfy:

| Field | Required evidence |
|---|---|
| `environmentId` | Exact canonical identifier or complete authoritative display name in the message, or an application-validated ordinal selection from persisted environment choices |
| `incidentId` | Exact canonical incident identifier in the current message |
| `requestedRoleId` | Exact canonical identifier or complete authoritative display name in the message, or an application-validated ordinal selection from persisted role choices |
| `justification` | Initial/replacement value is a contiguous substring of the message, or the value is an append whose stored prefix is unchanged and whose nonblank suffix is a contiguous substring of the message |

Every `clear` must be backed by one exact application-owned command after trimming,
Unicode NFC normalization, case folding, and whitespace collapse:

```text
clear environment      remove environment
clear role             remove role
clear justification    remove justification
clear incident         remove incident
```

The command must be the complete requester message. Other removal wording is not
interpreted as deterministic clear evidence; the field is preserved and the
application explains the supported command. This deliberately narrow grammar avoids
building a second natural-language parser in Core.

The reducer assigns one verdict to every operation:

| Verdict | State effect |
|---|---|
| `Kept` | Existing value retained |
| `Applied` | Evidence-backed changed value enters the temporary candidate |
| `ValueEqualSet` | A `set` equal to the canonical value is normalized to `keep` |
| `RejectedNoEvidence` | Existing value retained; the proposed value is ignored |

`ValueEqualSet` does not require message evidence because it cannot change state.
`RejectedNoEvidence` is not shown as a requester field error: it is a model-drift
signal, and the application continues from the unchanged canonical candidate. If all
proposed changes normalize to `keep` or `RejectedNoEvidence`, the turn cannot create a
new ready identity or replace a ready snapshot.

## Deterministic pre-model routing

The Teams boundary handles these inputs without invoking the model or MCP:

| Input | Deterministic behavior |
|---|---|
| Unauthenticated, unsupported tenant, or non-personal activity | Reject safely; do not create an intake |
| Missing text part | Ask for a text request; attachments are ignored |
| Whitespace-only text after trim | Ask for request details |
| Attachment-only activity | Ask for a text request |
| Reaction-only activity | Ignore or acknowledge safely without intake mutation |
| Exact trimmed case-insensitive `/new`, including `/New ` | Atomically expire/supersede the active unsubmitted intake and start clean |
| Exact `new` or `/new` followed by additional non-whitespace text | Explain that `/new` must be sent by itself; preserve state |
| Exact field-clear command listed above | Apply deterministic `clear` without invoking the model |
| Exact trimmed case-insensitive `/cancel-revision` | If an owned, unexpired ready snapshot has a pending revision, discard it and re-present the unchanged snapshot; otherwise return state-specific guidance |

An exact field-clear command creates the corresponding application-owned all-`keep`
patch with one `clear` operation and sends it directly to the reducer. The model-side
schema retains `clear` so malformed or unexpected model output is still validated,
but normal supported clearing does not depend on model interpretation.

## Model and MCP behavior

The model receives:

- the latest requester message;
- the persisted working nullable candidate;
- the active persisted clarification target and ordered canonical option identifiers,
  when present;
- bounded process-local conversational context when available;
- the response schema; and
- exactly `get_production_environment` and `get_incident`.

The model may use MCP to resolve an exact identifier or complete display name and to
recognize that clarification is required. Partial readable scope cannot become a
field mutation. The application must independently reload every identifier and
relationship before accepting or displaying it.

Within deterministic application processing, one authoritative load of an identifier
or bounded catalog per turn may be reused by validation, choice construction, and
rendering. Authoritative values are never cached across requester turns. Model-side
MCP lookup does not replace the independent application load.

One normal turn permits at most:

- one `get_incident` invocation;
- two `get_production_environment` invocations;
- three total MCP invocations; and
- six total provider iterations, including tool-result continuation.

An unknown function name, non-read-only function, repeated call beyond these bounds,
or provider iteration overflow produces a typed invalid-interpretation failure. No
partial candidate is committed.

The application must fail closed if:

- the tool catalog is missing either required tool;
- an additional tool is exposed;
- either tool is not annotated read-only;
- the model output is malformed;
- a model, MCP, network, timeout, or cancellation boundary fails; or
- the model proposes an unsupported field or operation.

Normal text processing uses at most one interpretation phase. The application must
not make a second model call merely to phrase a clarification, progress message,
validation error, ready summary, or submission result.

## Deterministic turn reducer

Core owns a deterministic reducer that accepts:

- authenticated actor binding;
- latest raw requester message;
- current persisted working candidate and immutable ready snapshot, when present;
- persisted structured clarification context, when present;
- untrusted `RequestTurnProposal`;
- authoritative request context; and
- server correlation and clock state.

It produces a closed typed turn outcome.

### Merge algorithm

For each patched field:

1. `keep` copies the current canonical value.
2. A value-equal `set` is normalized to `keep` and records `ValueEqualSet`.
3. A changed `set` or `clear` is applied only after deterministic evidence succeeds;
   otherwise the existing value is retained and `RejectedNoEvidence` is recorded.
4. An evidence-backed `set` copies the proposed value into a temporary candidate; an
   evidence-backed `clear` removes it.
5. The dependency cascade is applied in the fixed order defined below.
6. Client is discarded from any external proposal and re-derived from the selected
   authoritative environment.
7. The temporary candidate is validated and canonicalized.
8. Invalid changed fields are cleared or rejected according to deterministic field
   policy; unrelated accepted canonical fields are preserved.
9. Only the sanitized candidate, pending revision, and structured clarification
   context required by the selected outcome may be persisted.

The model cannot decide that the request is valid, incomplete, rejected, or ready.

### Resolution order

The reducer resolves the first applicable issue in this order:

1. incident existence, activity, and incident-to-scope compatibility;
2. environment identity and ambiguity;
3. role availability for the selected environment; and
4. justification sufficiency.

An absent incident is not an issue.

The `Incident` clarification target is selected only when the current turn attempted
to set an exact incident that failed lookup or was inactive, and no earlier issue in
the resolution sequence applies. A deliberately cleared or simply absent incident
falls through to environment, role, or justification processing.

### Dependency cascade

After evidence evaluation and before readiness, the reducer applies these rules:

| Evidence-backed trigger | Deterministic consequence |
|---|---|
| `environmentId` set to a different value | Re-derive client; retain the stored role only if assigned to the new environment, otherwise clear it; if a stored incident belongs to another environment, preserve both proposed environment and incident in incident-conflict state rather than silently clearing either |
| `environmentId` cleared | Clear client and role; retain incident and justification; later incident resolution may derive its authoritative environment again |
| `incidentId` set with no environment | Reload the incident and derive its environment and client; role is retained only when assigned to that environment |
| `incidentId` set with a different stored environment | Preserve the stored environment and proposed incident in incident-conflict state; do not change environment or silently clear the incident |
| `incidentId` cleared | Retain environment, derived client, role, and justification |
| `requestedRoleId` set or cleared | No cascade beyond authoritative role validation |
| `justification` set | No cascade beyond requester-authorship and syntactic validation |
| `justification` cleared | Make the working candidate incomplete; affect no other field |

Every field is legally clearable. Clearing a required field does not fail the patch;
it moves a collecting candidate, or a pending revision of a ready snapshot, to an
incomplete state requiring clarification.

### Environment handling

- Exact environment identifiers require exact authoritative lookup.
- A complete authoritative environment display name may resolve to its exact
  identifier only when it matches exactly after the evidence normalization rules.
- A failed exact lookup must not silently fall back to discovery or identifier
  correction.
- Readable partial scope such as a client, region, or primary/recovery description is
  never converted directly into a field mutation and is not represented in the model
  contract.
- When environment is missing and the requester did not provide an exact identifier
  or complete display name, the application loads and renders the complete bounded
  authoritative production-environment catalog in stable application-owned order.
- The model cannot filter, rank, truncate, or supply environment options.
- Every displayed identifier and its exact order are stored as structured
  clarification context bound to the working candidate version.
- An ordinal reply resolves only against that persisted order. Otherwise the
  requester must supply an exact identifier or complete display name.
- Client is always derived from the selected environment.

### Role handling

- A role is accepted only when assigned to the selected environment.
- The application never substitutes a different role automatically.
- When the role is missing or invalid, the response renderer receives all and only
  the roles assigned to the selected authoritative environment.
- Role identifiers, ordering, and display names are application-owned. The exact
  rendered role order is persisted as clarification context bound to the working
  candidate version so an ordinal reply remains deterministic after restart.

### Justification handling

Core does not claim to understand justification quality semantically. It enforces this
explicit syntactic floor after requester-authorship evidence succeeds:

1. Trim and Unicode-normalize the value.
2. Require at least three non-whitespace tokens.
3. Reject a value equal to a canonical identifier or reference-data display name.
4. Reject a value composed solely of canonical identifiers or reference-data display
   names.
5. Enforce the existing domain maximum length.

For an initial or replacement justification, the complete normalized proposed value
must be a contiguous substring of the current requester message. For an append, the
stored normalized value must remain an exact prefix and the newly appended nonblank
suffix must be a contiguous substring of the current requester message. Model
paraphrase, synthesis from tool metadata, or copying an incident title is rejected as
`RejectedNoEvidence`.

This rule proves that persisted justification uses the requester's words; it does not
prove that those words are a good business justification. A low-quality statement
that clears the syntactic floor is deliberately left for the business approver to
evaluate.

The application selects `Justification` as the clarification target when scope and
role are valid but justification remains missing or insufficient.

### Incident conflict

When an exact incident conflicts with explicitly requested environment scope, the
application must not choose either side. It preserves unrelated valid justification
and asks the requester to choose one of these deterministic resolutions:

- use the incident's authoritative environment;
- continue with the explicitly requested environment without the incident; or
- provide another exact compatible incident identifier.

No candidate becomes ready until the conflict is explicitly resolved.

## Closed application outcomes

One normal text turn returns exactly one of:

| Outcome | Required application-owned data |
|---|---|
| `ClarificationRequired` | Target, canonical progress, authoritative choices where applicable |
| `CandidateRejected` | Safe field errors and remaining canonical progress |
| `DraftDiscussion` | Unchanged ready or collecting draft identity and application-owned generic discussion guidance |
| `SubmissionGuidance` | Unchanged ready or collecting draft and deterministic card guidance |
| `ReadyForConfirmation` | Exact immutable ready intake and reserved request identity |
| `Failed` | Typed safe failure with no uncommitted candidate mutation |

Only payloads applicable to the selected outcome may be populated. Consumers must not
infer behavior from nullable combinations.

## Application-owned response rendering

### Ownership rule

Application code must render:

- every canonical field and identifier;
- environment and role choices;
- missing-field questions;
- validation corrections;
- current accepted progress;
- ready-review cards;
- confirmation instructions and expiry;
- request identifiers and workflow statuses; and
- failure and retry guidance.

The model returns no requester-facing prose. For `discussDraft`, the application
states that the current draft is unchanged, renders canonical progress, and explains
that a revision must name an exact environment identifier or complete display name,
an authoritative role, an exact incident identifier, or requester-authored
justification. The initial feature does not attempt open-ended conversational answers.

### Deterministic clarification mapping

The renderer maps typed targets to application-owned questions equivalent to:

| Target | Required question meaning |
|---|---|
| Environment | Which production environment is required? |
| Role | Which of the authoritative roles assigned to this environment is required? |
| Justification | What operational work requires this access? |
| Incident | What is the exact incident identifier, or should the request continue without one? |
| Incident conflict | Should the request use the incident scope, continue without the incident, or use another incident? |

Exact wording may evolve without changing Core contracts, but it must retain the same
meaning and must not imply that a request, approval, or grant exists.

### Progress presentation

Every clarification following an accepted change must show a concise application-owned
summary of the non-null canonical fields accepted so far, followed by exactly one
focused next question. Empty or irrelevant fields are omitted.

The renderer must not echo raw model payloads, MCP payloads, internal exception text,
or implementation phrases such as "the assistant's candidate was rejected."

## Ready snapshot behavior

A draft is ready only when deterministic validation has produced canonical:

- requester;
- client;
- environment;
- assigned role;
- sufficient justification;
- optional compatible active incident; and
- fixed duration policy.

Becoming ready creates:

- an immutable ready preparation identity;
- a reserved immutable request identity;
- a fixed confirmation deadline exactly 30 minutes after the ready transition; and
- an application-rendered Adaptive Card.

Discussion and value-equal turns preserve the original deadline and do not refresh
it. A complete replacement ready snapshot receives a new 30-minute deadline.

Card send success or failure is not persisted as intake state. A send failure preserves
the ready snapshot and returns typed retry guidance. A later `submissionGuidance` turn
for an eligible ready snapshot re-renders its exact current card without relying on a
delivery receipt.

### Ready-draft discussion

A discussion or value-equal update with no pending revision preserves:

- preparation ID;
- reserved request ID;
- ready timestamp;
- expiry deadline;
- exact prepared details; and
- active confirmation card authority.

When a pending revision already exists, discussion preserves both the immutable ready
snapshot and the pending candidate, and confirmation remains suspended.

### Ready-draft revision

- A complete, valid changed candidate atomically supersedes the old ready intake and
  creates a new ready intake with a new preparation ID, reserved request ID, deadline,
  and card.
- A revision needing clarification preserves the existing ready snapshot while the
  requester resolves the proposed revision. The sanitized working candidate is saved
  as `PendingRevisionCandidate`, with its typed clarification target and ordered
  canonical choices, without mutating the ready snapshot.
- Persisting the first evidence-backed pending revision immediately suspends
  confirmation eligibility for the old ready snapshot. The application makes a
  best-effort visual replacement of the old card, but submission eligibility is
  determined only from durable intake state.
- The next turn receives the pending revision candidate as its working candidate. A
  role selected on that turn therefore applies to the pending environment rather than
  the immutable old environment.
- Pending revision state is bound to the ready preparation ID and persistence version.
  Stale pending state cannot be applied to another ready snapshot.
- If a pending revision becomes complete and valid, replacement atomically supersedes
  the old ready intake, creates the new ready intake/card, and clears pending state.
- If a pending revision returns exactly to the immutable ready details, pending state
  is cleared, confirmation eligibility is restored, and the exact existing ready card
  is re-presented without changing its identity or deadline.
- Exact `/cancel-revision` on an owned, unexpired ready snapshot clears pending
  revision and clarification context, restores confirmation eligibility for the
  unchanged ready snapshot, and re-presents its exact card without invoking the model
  or MCP. It never revives an expired snapshot.
- A card action received while pending revision state exists fails with a typed
  `RevisionPending` outcome and creates no request.
- A pending revision expires with its underlying 30-minute ready snapshot.
- A rejected revision preserves the old ready snapshot unless the requester explicitly
  used `/new` to abandon it or `/cancel-revision` to discard the pending revision. It
  also preserves the last accepted pending revision rather than committing the
  rejected change.

Replacing an active ready intake must use one focused atomic persistence boundary.
The system must not commit supersession if it cannot also establish the replacement
state required by the outcome.

## Adaptive Card confirmation

Adaptive Card confirmation remains the only request-creation path.

The ready card must show application-owned:

- requester identity;
- client display name and canonical identifier;
- environment display name and canonical identifier;
- role display name and canonical identifier;
- exact persisted justification;
- incident title and canonical identifier when present, otherwise an explicit "no
  incident" value;
- fixed eight-hour lifetime;
- confirmation deadline; and
- a statement that confirmation submits for business approval and does not approve or
  grant access.

The card action payload may contain only:

- a fixed contract schema version; and
- the exact preparation ID represented by that card.

It must not contain trusted identity, scope, role, duration, approval, or provisioning
assertions.

On confirmation, the application must:

1. derive the actor from authenticated Teams context;
2. parse the closed card-action schema;
3. reload the exact preparation ID;
4. verify full actor and conversation ownership;
5. reject collecting, expired, superseded, invalidated, foreign, or
   pending-revision intake state;
6. revalidate requester, environment, client, role, justification, and incident from
   authoritative data;
7. atomically mark the intake submitted and create one immutable request plus
   request-created audit evidence; and
8. return the same request ID on safe replay.

A stale card can never submit a newer or different scope. Visual replacement or
disabling of old cards improves usability but is not an authorization control.

Normal text such as "submit it" cannot create a request. When a ready card exists, the
application re-renders the exact current card when confirmation is eligible. When a
pending revision suspends confirmation, it explains that the revision must be
completed or cancelled with `/cancel-revision` before the old snapshot can be
confirmed.

## Turn serialization and concurrency

The application must serialize the complete normal-message turn for one authenticated
actor and exact conversation:

```text
load/create intake
  -> interpret latest message
  -> reduce and validate patch
  -> persist canonical outcome
  -> determine presentation
```

The process-local gate key must include channel, tenant, channel actor, conversation,
and requester identity. It must cover first-turn intake creation as well as later
turns. Different conversations must remain independently concurrent.

The gate is a latency and ordering control, not the durable correctness boundary.
SQLite uniqueness, optimistic concurrency, exact preparation identity, and atomic
persistence remain authoritative.

SQLite must enforce at most one active intake with a partial unique index over the
complete binding `(Channel, TenantId, ChannelActorId, ConversationId, RequesterId)`
where status is `Collecting` or `Ready`. Process-local gating cannot replace this
constraint.

Required race behavior:

- Concurrent first messages produce at most one active intake.
- Two messages for one intake are reduced in accepted order and never interpret from
  the same stale canonical candidate.
- A ready revision and card confirmation converge on whichever durable transition
  commits first.
- Confirmation committed before any pending revision exists preserves the immutable
  submitted request and prevents a later revision.
- Pending-revision persistence winning first suspends the old preparation and causes
  its card action to fail with `RevisionPending`.
- Completed replacement winning first causes the old card confirmation to fail as
  stale.
- Duplicate card confirmation creates exactly one request and returns its identity.

## Conversation memory and restart

Model conversation history is process-local convenience only.

- The canonical working candidate, pending revision, and structured ordered-choice
  context are persisted independently.
- Raw prompts and transcripts are not persisted solely for request intake.
- Conversation memory must have a configured bound by message count, token budget, or
  equivalent SDK-supported limit.
- Per-conversation gates and memory must be evicted after terminal state or bounded
  inactivity.
- Relative replies use only the active persisted clarification target and ordered
  canonical option identifiers. An ordinal such as "the second one" is converted to
  exact evidence before patch reduction and continues to work after restart.
- Relative replies without matching persisted clarification context produce a
  self-contained clarification instead of a guess.
- Card confirmation and every downstream workflow action ignore model memory.

## Failure behavior

Expected failures use typed outcomes and safe user guidance.

| Failure | State behavior |
|---|---|
| Malformed model output | Preserve last canonical draft; no mutation |
| Model or MCP timeout | Preserve last canonical draft; invite retry |
| Model or MCP unavailable | Preserve last canonical draft; invite retry |
| Caller cancellation | Stop work and preserve last committed state |
| Unknown environment or incident | Preserve unrelated valid fields; clear/reject invalid changed field |
| Invalid role | Preserve environment and unrelated fields; show authoritative roles |
| Persistence failure before commit | Report failure; do not claim state changed |
| Atomic ready replacement failure | Preserve original ready draft |
| Confirmation while revision is pending | No request; explain completion or `/cancel-revision` requirement |
| Stale card | No request; state-specific safe guidance |
| Duplicate confirmation | Return the exact already-created request |

A `Failed` outcome after any model or MCP call but before the persistence commit
contains no partial candidate, pending revision, clarification context, ready identity,
or submission effect. Only an earlier independently committed turn may be observed
afterward.

Model prose must never claim that persistence, submission, approval, provisioning, or
granting succeeded.

## Security and privacy requirements

- Authenticate and validate a personal Teams conversation before creating an intake
  or invoking the model.
- Browser and activity payloads cannot select acting identity or claims.
- Validate the closed model schema before translating it into Core types.
- Treat all model strings and option identifiers as untrusted.
- Reload all identifiers and relationships from authoritative application data.
- Treat authoritative display names and titles as untrusted display data: encode them
  for Teams and place them only in explicitly labeled fact/choice fields. Reference
  display text is data, not model instruction content.
- Encode requester text safely whenever the application must display it. Model prose
  is not part of the response contract.
- Keep submission unavailable to the model and MCP.
- Never log secrets, raw prompts, transcripts, model reasoning, full justification,
  or complete MCP/model payloads by default.
- Logs may retain correlation ID, authenticated actor binding, intake/request ID,
  dialogue-act category, changed-field names, outcome category, failure code, duration,
  and safe tool-count metadata.
- Do not log model-proposed field values as authoritative data.

## Observability and efficiency

For each normal turn, record safe metrics for:

- total turn duration;
- model duration and result category;
- MCP duration, tool name, and safe outcome category;
- dialogue act;
- deterministic reducer outcome;
- per-field `ValueEqualSet` and `RejectedNoEvidence` counts without values;
- MCP/tool-bound rejection count;
- card send failure and deterministic re-presentation category;
- persistence outcome; and
- number of model/provider iterations when available without retaining raw traces.

Efficiency invariants:

- Zero model calls for unauthenticated/unsupported activities, empty messages, exact
  `/new`, `/cancel-revision`, exact field-clear commands, defined command near-misses,
  Adaptive Card confirmation, approval, provisioning, retry, and request queries.
- At most one interpretation phase for a normal text turn.
- No model call solely for response phrasing.
- No authoritative lookup solely to repeat a value already loaded in the same
  application turn when a canonical typed result can be reused safely.
- MCP catalog validation and client lifetime may be optimized only if the exact
  two-tool fail-closed contract and cancellation behavior remain testable.
- Process-local gate, presentation, and conversation state must have bounded cleanup.

No fixed latency service-level objective is imposed for the synthetic MVP. Runtime
measurements must be captured before introducing caching or provider-specific
optimizations.

## Risks and tradeoffs

- Natural-language interpretation remains probabilistic. The model can misclassify a
  question as a revision or miss an intended change. Patch semantics limit the blast
  radius, deterministic validation rejects invalid scope, and the exact review card
  prevents an unseen interpretation from directly creating a request.
- Application-owned response rendering is more predictable but less generative than
  free-form assistant responses. Open-ended draft discussion is deliberately reduced
  to canonical progress and deterministic guidance in the initial feature.
- Revalidating model-proposed identifiers duplicates some model-side MCP lookup work.
  This is intentional because MCP output and model interpretation are not authority.
- Rendering the complete environment catalog is less tailored than model-driven
  narrowing. It is acceptable for the bounded synthetic catalog; expanding catalog
  size requires a separate deterministic search/selection feature.
- Exact field-clear commands and exact identifiers/display names accept less natural
  phrasing. This is deliberate: unsupported wording produces guidance instead of
  growing a language heuristic inside Core.
- Persisted ordered-choice context resolves the active clarification after restart,
  but unrelated relative references still require a self-contained clarification.
- The whole-turn gate provides ordering only inside the single host. Durable database
  constraints and optimistic concurrency remain necessary even in the synthetic MVP.
- Preserving a ready snapshot during an incomplete revision retains a recoverable
  fallback but suspends its confirmation. This requires one extra durable eligibility
  check and a deterministic `/cancel-revision` recovery path.
- Natural-language distinction between discussion and revision remains prompt-level.
  Deterministic evidence prevents unsupported field mutation, while the review card
  remains the final requester check for a legal but misinterpreted evidence-backed
  change.
- This feature provides no requester cancellation after a card has created an
  `AwaitingBusinessApproval` request. Corrections require a new intake and approval
  sequence; cancellation is a separate governed feature.

## Testing strategy

Automated tests must not require a live LLM, Teams tenant, Azure subscription, public
tunnel, or real provisioner.

### Unit tests

Place deterministic Core behavior in `GovernedAccess.UnitTests`, including:

- closed dialogue-act and field-operation invariants;
- `keep`, `set`, and `clear` merge behavior;
- evidence acceptance and rejection for every `set` and `clear` field;
- value-equal `set` normalization and drift counts;
- invalid operation/value combinations;
- client derivation;
- environment ambiguity;
- full authoritative environment-choice ordering;
- role assignment;
- justification authorship, append behavior, and the three-token syntactic floor;
- incident activity and scope compatibility;
- the complete dependency-cascade table;
- preservation of unrelated canonical fields;
- value-equal update behavior;
- discussion and submission-guidance non-mutation;
- ready-snapshot preservation;
- complete revision replacement rules; and
- missing-field resolution order.

### Component and integration tests

Place boundary behavior in `GovernedAccess.IntegrationTests`, including:

- strict provider JSON parsing and unknown-property rejection;
- exact two-tool read-only MCP catalog enforcement;
- model/MCP timeout, cancellation, malformed output, and unavailability;
- deterministic scripted multi-turn interpretation;
- deterministic requester prompt-injection attempts proving unsupported field
  changes and workflow instructions have no persisted effect;
- unknown tool, per-tool call limit, total call limit, and provider-iteration rejection;
- application-rendered environment and role choices;
- persisted ordered-choice ordinal resolution after simulated restart;
- persisted multi-turn pending revision behavior;
- whole-turn same-conversation serialization and cross-conversation concurrency;
- concurrent first-message intake creation;
- partial unique active-intake constraint;
- atomic ready replacement rollback;
- process-memory eviction and restart behavior;
- authenticated personal Teams transport;
- exact `/new`, `/cancel-revision`, and field-clear handling without model invocation;
- deterministic progress and clarification responses;
- ready card fields and closed action schema;
- ready-card send failure and stateless re-presentation;
- pending-revision confirmation suspension and restoration after cancellation;
- stale, foreign, expired, superseded, and malformed card actions;
- duplicate and concurrent confirmation;
- one immutable request and audit event; and
- absence of unauthorized workflow, approval, provisioning, or grant effects.

### Full-host acceptance test

Use one representative hosted journey:

```text
authenticated Teams message with incident
  -> application justification question
  -> justification answer
  -> application role choices
  -> role answer
  -> ready Adaptive Card
  -> authenticated card confirmation
  -> one AwaitingBusinessApproval request
```

Assert the exact persisted requester and scope, no approval decisions, no provisioning
operation, and no grant at submission time.

### Live-model evaluation

Live evaluation is optional, explicit, credentialed, and never a routine test gate.
It evaluates interpretation quality only. It must not confirm cards or create requests.

The evaluation dataset should include at least:

- all details in one message;
- incremental incident, purpose, and role collection;
- readable ambiguous environment scope;
- exact unknown identifiers;
- inactive and conflicting incidents;
- explicit field correction and clearing;
- questions versus explicit revisions;
- ready-draft discussion;
- revision plus textual submission request;
- prompt injection, in addition to required deterministic scripted coverage;
- relative reply with and without conversation history; and
- malformed or unavailable provider outcomes in deterministic tests.

Every evaluated final candidate is graded against canonical application output, not
raw tool order, prompt wording, or provider reasoning. Evaluation must create zero
requests, decisions, operations, and grants.

## Acceptance scenarios

### AC-01: Empty intake begins safely

Given an authenticated requester with no active intake, when they send a normal
request message, then one intake is created for the exact actor/conversation and the
model receives an empty canonical candidate.

### AC-02: Incremental fields are preserved

Given an accepted partial candidate, when the requester supplies one new field, then
the model proposes a patch, Core preserves every unrelated canonical field, and the
application shows accepted progress plus one next question.

### AC-03: All details can complete in one turn

Given an empty intake, when one message unambiguously supplies a valid environment,
role, justification, and optional compatible incident using exact identifiers or
complete authoritative display names where required, then authoritative validation
creates one ready snapshot and the application sends its review card.

### AC-04: Incident derives scope but not justification

Given an exact active incident, when no operational purpose is supplied, then the
application derives its environment/client, preserves the incident, and asks the
application-owned justification question.

### AC-05: Ambiguity is not guessed

Given a missing environment or readable partial scope without an exact identifier or
complete authoritative display name, then the application renders the complete
bounded authoritative environment catalog in stable application-owned order,
persists that exact order as clarification context, and persists no guessed
environment.

### AC-06: Role choices are authoritative

Given a selected environment without a valid role, then the application renders all
and only its assigned roles and the model does not provide authoritative role-list
text.

### AC-07: Discussion does not revise

Given a collecting or ready draft, when the requester asks a hypothetical or factual
question without explicitly requesting a change, then every canonical field and ready
identity remains unchanged.

### AC-08: Explicit revision changes only declared fields

Given a current candidate, when the requester explicitly changes one field, then the
patch changes only evidence-backed fields and the fixed cascade table clears, derives,
or retains dependent fields exactly as specified.

### AC-09: Model state loss cannot erase canonical state

Given a nonempty persisted candidate and missing process-local conversation history,
when a new turn occurs, then the canonical candidate is supplied again and no field is
lost merely because the model has no earlier transcript.

### AC-10: Complete ready replacement is atomic

Given a ready snapshot, when a complete valid revision is accepted, then the previous
snapshot is superseded and the replacement becomes ready atomically. If replacement
persistence fails, the original ready snapshot remains active.

### AC-11: Incomplete revision suspends ready confirmation

Given a ready snapshot, when a proposed revision requires clarification, then the
original snapshot remains unchanged but cannot be confirmed. Its sanitized pending
revision and ordered clarification context are persisted until a complete replacement
is accepted, the pending candidate returns to the old values, `/cancel-revision`
restores the old snapshot, the snapshot expires, or `/new` abandons it. Any old-card
action while revision is pending creates no request.

### AC-12: Text cannot submit

Given any intake state, when a requester asks in ordinary text to submit, approve,
grant, or provision, then no request or downstream workflow state is created. A ready
intake receives deterministic guidance to use its card.

### AC-13: Exact card confirmation submits once

Given an owned, unexpired ready card, when its authenticated requester selects
**Confirm and submit**, then one immutable matching request and audit event are created
in one save and status is `AwaitingBusinessApproval`.

### AC-14: Stale or foreign cards fail closed

Given a superseded, expired, invalidated, malformed, or foreign card action, when it is
received, then no request, decision, operation, or grant is created.

### AC-15: Duplicate confirmation converges

Given duplicate or concurrent delivery of one valid card action, then exactly one
request exists and every successful/replay outcome identifies that same request.

### AC-16: Same-conversation turns do not use stale state

Given two concurrent messages from one actor/conversation, then the complete turns are
serialized and the second interpretation receives the first turn's committed
canonical candidate. Another conversation may proceed concurrently.

### AC-17: Model and MCP failures preserve state

Given any last committed candidate, when interpretation, tool use, schema parsing,
timeout, or availability fails, then the candidate and any ready card authority remain
unchanged and no request is created.

### AC-18: `/new` is deterministic

Given an active unsubmitted intake, when the requester sends exact trimmed
case-insensitive `/new`, then the intake is expired or superseded according to policy
without invoking the model or MCP. `/New ` therefore resets after trimming, while
exact `new` or `/new please` returns an application-owned command hint without model
invocation. Submitted requests remain unchanged.

### AC-19: Unrequested change is rejected

Given stored role `ProductionSupport`, when the requester supplies only justification
and the model emits `set ProductionDeployment`, then role evidence fails, the stored
role remains unchanged, `RejectedNoEvidence` increments, and no role field error is
shown to the requester.

### AC-20: Model context loss cannot corrupt canonical state

Given a nonempty persisted working candidate and missing process-local history, when a
model `set` differs from stored state without current-message or ordered-choice
evidence, then no canonical or pending field changes.

### AC-21: Justification is requester-authored

Given requester justification text, when the model proposes a paraphrase that is not a
normalized substring or valid append, then the proposal records
`RejectedNoEvidence`, stored justification remains unchanged, and the application asks
for requester-authored justification when still required.

### AC-22: Value-equal sets normalize to keep

Given a nonempty candidate, when the model emits four `set` operations equal to its
four stored values, then all normalize to `keep`, no lifecycle or persistence identity
changes, and four `ValueEqualSet` observations are recorded.

### AC-23: Field clearing uses a narrow deterministic command

Given a stored incident, when the requester sends exact normalized `clear incident`,
then the incident is cleared without invoking the model. When the requester uses
other removal wording, the incident remains unchanged and the application explains
the supported exact command.

### AC-24: Multi-turn ready revision preserves its working candidate

Given ready `PROD-ALPHA-EU` with `ProductionSupport`, when the requester changes to a
recovery environment that requires another role, then the application persists the
pending recovery-environment candidate and ordered role choices without changing the
ready snapshot, suspends confirmation of its old card, and rejects that card if used.
After restart, selecting `ProductionReadOnly` applies to that pending recovery
environment and produces the exact intended replacement ready snapshot with a new
card.

### AC-25: Dependency cascades are exact

Given an environment change to one without the stored role and with a mismatched
stored incident, then client is re-derived, role is cleared, both explicit environment
and incident are preserved in incident-conflict state, and neither side is silently
selected or discarded.

## Tech stack

- .NET 10 and C# 14.
- ASP.NET Core single host.
- `GovernedAccess.Core` for provider-neutral domain and application behavior.
- `GovernedAccess.Web` for Teams, MAF, MCP client, rendering, persistence, and
  composition.
- `GovernedAccess.Mcp` for exactly two typed read-only tools.
- EF Core SQLite persistence.
- Microsoft Agent Framework and `Microsoft.Extensions.AI` through existing project
  dependencies.
- xUnit for backend tests and Vitest for the unchanged React client.

No new dependency or project is required by this specification.

## Project structure

| Area | Responsibility |
|---|---|
| `src/GovernedAccess.Core/Domain/Drafts` | Candidate and intake lifecycle invariants |
| `src/GovernedAccess.Core/Application/Drafts` | Patch reducer, validation orchestration, ready/revision policy |
| `src/GovernedAccess.Core/Ports` | Provider-neutral interpretation, turn outcome, context, and persistence contracts |
| `src/GovernedAccess.Web/Ai` | Closed provider schema, MAF execution, MCP allowlist, translation, bounded session memory |
| `src/GovernedAccess.Web/Teams` | Authentication boundary, whole-turn coordination, deterministic text/card rendering |
| `src/GovernedAccess.Web/Persistence` | Pending revision/clarification persistence, atomic active-intake replacement, concurrency, and submitted recovery |
| `tests/GovernedAccess.UnitTests` | Pure patch, reducer, validation, and lifecycle evidence |
| `tests/GovernedAccess.IntegrationTests` | AI/MCP, SQLite, concurrency, Teams, rendering, and hosted evidence |

Do not create another project, deployable service, generic orchestration module, or
channel framework for this feature.

## Code style and interface rules

- Preserve nullable reference types, analyzers, code style, and warnings as errors.
- Use closed enums and discriminated outcomes instead of flag combinations or
  loosely-related nullable properties.
- Validate provider payloads once at the Web boundary, then translate into Core
  contracts.
- Keep provider, Teams, MCP SDK, and EF types outside Core.
- Propagate `CancellationToken` through every asynchronous boundary.
- Use explicit timeouts and typed expected failures.
- Prefer a focused persistence operation for atomic ready replacement over exposing a
  generic transaction abstraction to Core.
- Do not add an abstraction without a concrete consumer in this feature.

## Commands

Restore only when dependencies require it:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

Run backend validation sequentially in this exact order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. Run the
frontend suite only if frontend behavior or its contracts change:

```powershell
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

For specification-only changes, validate repository links and run:

```powershell
git diff --check
```

## Boundaries

### Always

- Treat model output and MCP data as untrusted.
- Preserve authenticated actor/conversation binding through preparation and
  confirmation.
- Merge only explicit patch operations into canonical state.
- Require deterministic current-message or persisted ordered-choice evidence before
  applying every changed `set` or `clear` operation.
- Normalize value-equal `set` operations to `keep` and observe model drift.
- Reload identifiers and relationships from authoritative data.
- Let Core decide readiness and lifecycle transitions.
- Render canonical fields, choices, questions, cards, and workflow outcomes in
  application code.
- Confirm only through an authenticated action bound to one exact ready preparation.
- Revalidate the immutable snapshot before request creation.
- Test negative outcomes for both response and absence of unauthorized persisted
  effects.

### Ask first

- Add persistence beyond the narrowly authorized pending revision candidate,
  structured clarification context, and existing intake/workflow state.
- Add a dependency, project, requester channel, or deployable component.
- Change the MCP tool catalog or schemas.
- Change the 30-minute confirmation lifetime.
- Change requester confirmation away from an Adaptive Card structured action.
- Change fixed duration, approver resolution, approval order, immutable scope,
  provisioning, retry, or audit policy.
- Persist any additional conversation or model-generated content.

### Never

- Return a model-owned complete candidate as canonical state.
- Let omitted model fields clear existing canonical values.
- Apply a changed `set` or `clear` without deterministic requester evidence.
- Parse assistant prose back into candidate state.
- Let the model select client ownership, identity, approver, duration, request ID,
  approval, operation, or grant state.
- Expose submission, approval, provisioning, retry, or revocation to the model or MCP.
- Treat a card payload as identity or authorization evidence.
- Guess an ambiguous environment, role, incident, or relative reply.
- Mutate a ready snapshot partially.
- Supersede a ready snapshot before its required replacement state can commit.
- Persist raw prompts, transcripts, model reasoning, provider traces, or full MCP
  payloads.
- Add a second agent, generic workflow engine, large retrieval system, or distributed
  infrastructure.

## Success criteria

The feature is complete when:

1. Model output cannot implicitly replace canonical state or change any accepted field
   without deterministic evidence from the current requester message or exact
   persisted ordered-choice context.
2. Core deterministically merges `keep`, evidence-backed `set`, and evidence-backed
   `clear` operations, normalizes value-equal sets, and preserves unrelated canonical
   state.
3. The model cannot filter or rank environment choices; when exact environment
   evidence is absent, the application renders the complete authoritative bounded
   catalog in stable order.
4. Persisted justification is demonstrably requester-authored and satisfies the fixed
   syntactic floor without claiming semantic quality judgment.
5. Application-owned rendering produces all field values, choices, missing-field
   questions, validation guidance, progress summaries, ready cards, and submission
   outcomes.
6. One authenticated actor/conversation turn is serialized from load through commit,
   while different conversations remain concurrent.
7. Pending ready-draft revisions and ordered choices survive restart, suspend old-card
   confirmation, can be cancelled deterministically, and complete through an atomic
   replacement that cannot strand the requester after partial supersession.
8. Model/MCP failure, malformed output, restart, ambiguity, and prompt injection
   preserve the last committed canonical state and create no request.
9. Ordinary text cannot create a request or expose a state-changing model capability.
10. Exact Adaptive Card confirmation revalidates and creates one immutable
   `AwaitingBusinessApproval` request and audit event.
11. Stale, foreign, invalid, expired, malformed, duplicate, and concurrent card actions
   fail safely or replay the same request identity.
12. MCP still exposes exactly the two existing read-only tools, and per-turn tool and
    provider-iteration bounds fail closed.
13. No downstream approval, provisioning, retry, grant, React request-creation, or
    fixed-duration behavior changes.
14. The required backend build, unit, and integration commands pass sequentially, and
    any affected frontend tests pass separately.
15. Current product, architecture, security, testing, and operator documentation is
    reconciled only after implementation evidence establishes the new as-built truth.

## Open questions

None for the initial synthetic-MVP specification. Any request to add natural-language
submission, durable conversational continuation, real production effects, another
channel, or additional model tools is a separate governed feature.

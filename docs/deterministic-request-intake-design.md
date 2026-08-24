# Deterministic Request Intake: Design Notes

- **Status:** Proposed supporting design
- **Date:** 2026-08-22
- **Normative source:** `SPEC-deterministic-request-intake.md`
- **Purpose:** Record detailed reducer, state, authority, and rendering mechanics that would make the feature specification too large

This document explains one implementation shape. The specification remains normative.
Internal names may change if the same contracts and behavior are preserved.

## 1. Design goals

The implementation should make these properties obvious in code and tests:

1. persisted application state is canonical;
2. the model proposes only explicit changes;
3. omission is a no-op rather than state loss;
4. authoritative sources, not MCP payloads or model prose, establish facts;
5. application outcomes are closed and rendered without model-authored consequential
   prose;
6. one active candidate exists per actor/conversation; and
7. a ready card represents one immutable preparation identity.

The design should not introduce a reusable dialogue framework. Types should remain
specific to production-access request intake.

## 2. Logical component boundary

```text
Teams activity
  -> Teams authentication and deterministic command routing
  -> RequestDraftService whole-turn coordinator
       -> load/create active intake
       -> model interpretation adapter (MAF + four read-only MCP tools)
       -> provider-neutral RequestTurnInterpretation
       -> deterministic RequestTurnReducer
            -> authoritative environment search/lookup port
            -> authoritative entitlement port
            -> authoritative incident port
            -> candidate validation and lifecycle decision
       -> one persistence commit
       -> closed RequestTurnOutcome
  -> application response renderer
  -> Teams text/card delivery
```

The model interpretation adapter belongs in the Web/infrastructure boundary. The
reducer contract, candidate rules, and outcomes belong in Core. Persistence adapters
translate Core state without importing MAF or MCP SDK types.

## 3. Suggested provider-neutral types

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
    FieldOperation<string>? EnvironmentId = null,
    FieldOperation<string>? RequestedRoleId = null,
    FieldOperation<string>? Justification = null,
    FieldOperation<string>? IncidentId = null);

public sealed record EnvironmentSearchObservation(string Query);

public sealed record RequestTurnInterpretation(
    RequestTurnDialogueAct DialogueAct,
    RequestCandidatePatch Patch,
    EnvironmentSearchObservation? EnvironmentSearch);
```

The provider JSON schema may use compact operation objects, but it must be translated
before Core. Raw tool results do not cross the interpretation port.

### 3.1 Operation verdicts

Each present operation should produce one diagnostic verdict:

```csharp
public enum FieldOperationVerdict
{
    Applied,
    ValueEqual,
    RejectedNoEvidence,
    RejectedInvalidValue,
    RejectedRelationship,
}
```

The verdict is useful for tests, logs, and live evaluation. It is not automatically a
requester-facing error. `RejectedNoEvidence` usually indicates model drift and should
not expose the proposed value.

## 4. Canonical state model

### 4.1 Candidate

```csharp
public sealed record RequestCandidate(
    string RequesterId,
    string? EnvironmentId,
    string? ClientId,
    string? RequestedRoleId,
    string? Justification,
    string? IncidentId);
```

`ClientId` is stored only as sanitized derived state if that matches the existing
persistence model. Every reducer and confirmation boundary must be willing to derive
and compare it again. Duration is policy, not candidate input.

### 4.2 Clarification context

```csharp
public enum ClarificationTarget
{
    Environment,
    Role,
    Justification,
    Incident,
    IncidentConflict,
}

public sealed record ClarificationContext(
    Guid PreparationId,
    long CandidateVersion,
    ClarificationTarget Target,
    IReadOnlyList<string> OrderedCanonicalIds,
    DateTimeOffset CreatedAt);
```

`OrderedCanonicalIds` is populated only for environment and role choices. Other
clarifications use an empty collection and a typed target. The list is bounded by the
catalog contract.

A context is usable only when its preparation ID and candidate version match the
loaded active intake. A successful exact or ordinal choice consumes it.

### 4.3 Intake identity

A ready preparation ID represents one immutable card scope. A material revision does
not mutate that identity back to collecting. It atomically terminates the old intake
and creates a new active intake.

```text
Ready A --accepted material revision--> Superseded A + Collecting/Ready B
Ready A --discussion/value-equal/failure--> Ready A
Ready A --confirmation--> Submitted A + immutable Request A
```

This keeps stale-card rejection simple and preserves the intent of terminal intake
tombstones.

## 5. Turn classification and pre-model routes

The adapter should intercept these before model execution:

- invalid or unauthenticated Teams activity;
- no usable text;
- exact `/new`;
- `/new` mixed with other text;
- exact clear commands; and
- an exact ordinal reply when one matching persisted choice context exists.

Pre-model ordinal handling is optional if the model contract can represent the same
exact selected ID, but deterministic routing is preferable because it removes a model
call and guarantees restart behavior.

An ordinal parser should remain deliberately narrow, for example:

```text
first | 1 | 1st | option 1
the second one | second | 2 | 2nd | option 2
```

Only one active target and in-range index may resolve. Anything else goes through the
normal interpretation path or receives a self-contained clarification.

## 6. Evidence evaluation

Evidence is evaluated against the normalized current requester message and matching
persisted choice context only.

### 6.1 Normalization

Use one shared helper for:

- Unicode NFC normalization;
- trim;
- whitespace collapse; and
- ordinal case-insensitive comparison.

Do not remove punctuation for exact identifier evidence unless the identifier parser
already defines safe delimiters. Do not search assistant/model text or prior messages.

### 6.2 Field rules

| Operation | Evidence rule |
|---|---|
| Environment `set` | Exact canonical ID in current message or validated choice resolution |
| Environment search | Query is a normalized contiguous substring of the current message |
| Incident `set` | Exact canonical incident ID in current message |
| Role `set` | Exact role ID, complete current display name, or validated choice resolution |
| Justification `set` | Complete proposed text is requester substring; append preserves exact stored prefix and adds requester substring |
| Any `clear` | Complete message equals one supported deterministic clear command |

If a set equals the current canonical value after canonical comparison, return
`ValueEqual` and do not require mutation evidence. This cannot change state.

### 6.3 Unsupported operations

An unsupported operation does not fail the entire turn by default. Preserve the field,
record `RejectedNoEvidence`, and continue from canonical state. If the proposal has no
applied change or valid search after normalization:

- on a collecting intake, render unchanged progress or conservative clarification;
- on a ready intake, preserve the same ready identity and card.

A malformed schema, unsupported field, or impossible cross-contract combination is a
typed invalid interpretation and fails the turn without mutation.

## 7. Environment search and exact resolution

The model-visible search is a discovery aid. Core owns the authoritative search.

### 7.1 Search algorithm

The authoritative port should apply the same deterministic policy documented in the
MCP contract:

1. normalize query;
2. require 1-200 characters;
3. tokenize on Unicode whitespace and punctuation;
4. require every query token to match one allowed field;
5. match only environment ID/name, client ID/name, region, and primary/recovery class;
6. order by stable environment ID;
7. return all results up to 20; and
8. return `environment_query_too_broad` instead of truncation above 20.

### 7.2 Outcome handling

```text
0 matches -> environment unresolved + no-match guidance
1 match   -> exact reload -> canonical environment/client -> validate incident/role
2..20     -> persist complete ordered IDs -> environment clarification
>20       -> typed too-broad guidance
```

The unique result is safe to accept because Core independently produced it. The model
has not selected one item from a set. The mandatory ready card still exposes the exact
scope before submission.

### 7.3 Search/MCP drift

The adapter may record the identifiers returned to the model for diagnostics, but
Core should not use them to construct choices or canonical values. If Core sees a
different current result:

- use the Core result;
- record bounded drift metadata;
- do not expose raw result differences to the requester; and
- fail only when the MCP result itself violated its closed contract or the provider
  turn cannot be trusted.

This behavior is more tolerant of enterprise-source freshness differences than
requiring byte-for-byte equality while remaining safe.

## 8. Authoritative source matrix

| Fact | Core authority | Model-visible capability | Freshness expectation | Failure effect |
|---|---|---|---|---|
| Searchable environment projection | Environment catalog/search port | `search_production_environments` | May lag exact registry within bounded synthetic design | Core result controls; unresolved/choices/unique outcome |
| Environment identity and client | Environment registry port | `get_production_environment` | Relatively stable but reloaded before acceptance/confirmation | Cannot accept changed environment or become ready |
| Assigned roles | Entitlement port | `get_environment_roles` | Potentially more volatile | Cannot accept changed role or become ready; preserve state |
| Incident state and environment | Incident/ITSM port | `get_incident` | Operationally mutable | Cannot accept incident; preserve unrelated state |

The local implementation may call one shared repository behind these ports, but tests
should be able to fail each port independently.

## 9. Tool policy without brittle choreography

The interpreter exposes exactly the four approved tools and enforces:

- read-only annotations;
- closed inputs and outputs;
- one call per tool per turn;
- at most four tool calls and six provider iterations;
- no concurrent calls;
- termination on unknown or excessive calls; and
- shared cancellation/timeout budget.

Normal prompting should encourage:

```text
exact incident -> incident lookup -> exact environment context -> role context if needed
exact environment -> exact environment context -> role context if needed
readable environment -> deterministic search -> optional exact/role context when useful
role-only revision on unchanged environment -> role context
justification-only revision -> no tool required
```

The adapter should reject invalid arguments, unknown tools, contract violations, or
excessive calls. It should not reject a safe proposal merely because the model omitted
a redundant exact lookup. Core independently validates the final relationship.

## 10. Reducer algorithm

A practical ordering is:

```text
1. Validate dialogue-act/patch/search invariants.
2. Resolve deterministic ordinal or clear command if pre-routed.
3. Evaluate each present operation for value equality and evidence.
4. Independently execute a validated environment search observation.
5. Build a temporary candidate from canonical state plus applied operations/search.
6. Apply environment/incident dependency cascades.
7. Exact-reload environment and derive client.
8. Exact-reload incident and evaluate compatibility.
9. Load environment roles and validate retained/proposed role.
10. Validate justification authorship and syntactic floor.
11. Choose the first issue or ready result.
12. Apply collecting or ready lifecycle rules.
13. Commit one sanitized outcome.
```

No second model call is made to phrase the response.

## 11. Dependency details

### 11.1 Environment change

On a changed canonical environment:

- derive client from the exact environment authority;
- load roles from the entitlement authority;
- retain existing role only when currently assigned;
- otherwise clear role and ask from the current authoritative choices;
- if an incident belongs elsewhere, preserve an explicit incident conflict rather than
  silently changing either fact; and
- clear any previous environment/role choice context.

### 11.2 Incident-derived environment

When a valid incident is set and environment is absent, derive the incident's exact
environment and client. Retain role only if the entitlement authority confirms it.

When an explicitly selected environment already exists and differs, do not choose.
Return `IncidentConflict` with application-owned resolution options.

### 11.3 Environment clearing

Clearing environment also clears client and role. The incident and justification may
remain as sanitized inputs. A retained incident can derive environment again only
through an explicit resolution path, not silently in the same clear command unless the
specification's reducer tests deliberately define that outcome.

The simplest implementation is to ask the next focused incident-conflict/scope
question.

## 12. Justification rule

Justification validation remains intentionally syntactic:

- requester-authored substring/append evidence;
- trim and Unicode normalize;
- at least three non-whitespace tokens;
- not one identifier/display name;
- not only identifiers/display names; and
- within the existing maximum length.

Do not add sentiment, intent, policy-bypass, or semantic risk classification in this
feature. Human business approval remains the quality boundary.

## 13. Closed outcomes

Suggested Core result hierarchy:

```csharp
public abstract record RequestTurnOutcome;

public sealed record ClarificationRequired(...): RequestTurnOutcome;
public sealed record CandidateRejected(...): RequestTurnOutcome;
public sealed record DraftDiscussion(...): RequestTurnOutcome;
public sealed record SubmissionGuidance(...): RequestTurnOutcome;
public sealed record ReadyForConfirmation(...): RequestTurnOutcome;
public sealed record RequestTurnFailed(...): RequestTurnOutcome;
```

Each subtype should contain non-null data required for that outcome rather than one
large nullable DTO. Renderer adapters map these types to Teams text/cards.

### 13.1 Clarification priority

Use one focused question in this order:

1. incident error/conflict;
2. environment unresolved/ambiguous;
3. role missing/unavailable;
4. justification missing/insufficient.

Every clarification includes a concise application-owned summary of accepted non-null
fields.

### 13.2 Bounded discussion

The renderer may support deterministic topics:

- show current accepted draft;
- identify the next missing field;
- explain why exact environment/incident identity is required;
- explain why a role must be assigned to the selected environment;
- list supported clear/reset commands; and
- explain that submission requires the review card.

Unsupported open-ended questions receive concise guidance without model-authored prose.

## 14. Ready revision transaction

### 14.1 Material change definition

A turn is material when at least one operation or unique search changes canonical
candidate content, or a validated environment search starts a different unresolved
scope revision. Value-equal and rejected operations are not material.

### 14.2 Atomic persistence

For a material turn against `Ready A`, one transaction should:

1. verify A is still owned, active, unexpired, and unchanged by concurrency;
2. mark A `Superseded` and clear obsolete candidate content according to tombstone
   policy;
3. create B with a new preparation ID and sanitized revised candidate;
4. store B as `Collecting` with choice context or `Ready` with new reserved request ID
   and deadline; and
5. save one audit/diagnostic transition if the current persistence model records it.

If the transaction fails, A remains ready. Do not commit supersession without B.

### 14.3 Search revision from ready

When the requester explicitly searches for a replacement environment:

- zero/multiple results are material revision intent;
- A is superseded;
- B clears environment, derived client, and role rather than leaving the old scope
  accidentally active;
- B stores multiple choices when applicable;
- unrelated justification may be retained;
- incident is retained only as an explicit conflict/context fact, never as silent
  authority to restore the old environment.

## 15. Confirmation race

A whole-turn gate reduces process-local races, but durable state decides:

- confirmation commits first -> immutable request exists; revision cannot alter it;
- revision transaction commits first -> old preparation is `Superseded`; confirmation
  fails stale;
- duplicate confirmation -> one request, same replay identity.

The card payload stays minimal: schema version plus preparation ID.

## 16. Persistence and migration notes

Likely schema changes:

- clarification target;
- ordered choice IDs, preferably a small owned table or bounded serialized
  provider-neutral value;
- candidate persistence version used to bind the context; and
- any new typed lifecycle metadata needed for atomic ready replacement.

Do not add columns for raw requester messages, model output, model responses, MCP
payloads, or provider session data.

The partial unique index for active `(channel, tenant, actor, conversation,
requester)` bindings must include both `Collecting` and `Ready`.

## 17. Observability

Recommended bounded fields:

- correlation ID and intake/preparation ID;
- actor/conversation hash or safe synthetic ID;
- dialogue act;
- names of patch fields present, not their raw values;
- per-field verdict;
- search result cardinality class: zero, unique, multiple, too broad;
- context source/tool name, duration, and typed outcome;
- reducer outcome and lifecycle transition;
- whether a ready snapshot was preserved or superseded; and
- consequential side-effect counts in evaluation.

Do not log raw prompt, transcript, justification, complete tool result, provider
reasoning, or secrets by default.

## 18. Suggested implementation ownership

| Concern | Likely location |
|---|---|
| Provider schema, MAF session, MCP call observation | `GovernedAccess.Web/Ai` |
| Four MCP endpoints and DTOs | existing MCP boundary/project |
| Provider-neutral interpretation port | `GovernedAccess.Core` application port |
| Reducer, evidence, cascades, outcomes | `GovernedAccess.Core` |
| Environment, entitlement, incident authoritative ports | `GovernedAccess.Core` ports plus infrastructure adapters |
| Intake/choice persistence and atomic replacement | existing persistence/application boundary |
| Teams rendering and card delivery | existing Teams adapter |
| Contract, component, full-host, and live evaluation | existing test/evaluation projects |

Avoid introducing a generic reducer framework or generic enterprise-source abstraction.
The interfaces should express the specific authority needed by this feature.

# Environment Resolution Turn Contract

## Purpose

This contract defines how one request-preparation turn uses the two MCP tools and the
feature 004 closed model-output schema. It governs model behavior; it does not
authorize or submit a request.

## Inputs to the Model

Each turn receives the existing server-owned envelope:

```json
{
  "latestMessage": "I need read-only access to Client Alpha production EU for INC-1042",
  "currentCandidate": {
    "clientId": null,
    "environmentId": null,
    "requestedRoleId": null,
    "justification": null,
    "incidentId": null
  }
}
```

`latestMessage` is untrusted user text. `currentCandidate` is application context but
is not authorization evidence. The feature contract is
[request-intake-proposal.schema.json](request-intake-proposal.schema.json). Its
candidate fields remain compatible with feature 002, but `clientId` is no longer a
clarification target. Every non-null clarification also contains a structured
`environmentOptionIds` array so proposed environment choices can be validated and
rendered from authoritative records. The bounded `message` remains model-authored
conversational text and is kept separate from the structured choice data.

## Tool Catalog

The interpreter must discover exactly these read-only tools before sending a turn to
the model:

1. `get_production_environment`
2. `get_incident`

A missing, additional, or non-read-only tool makes the turn unavailable before model
execution. `get_available_roles` is not part of the catalog.

## Environment Rules

1. When the latest message supplies or changes a precise or identifier-like
   environment value, first call `get_production_environment` with that value.
2. When exact lookup succeeds, use only that returned context. If explicit readable
   client, environment, or location terms conflict with it, clarify the conflict.
3. Only typed `NotFound` from exact lookup permits a second
   `get_production_environment` call with `{}`. Invalid input, timeout, cancellation,
   unavailability, or malformed results produce their existing safe outcome and do
   not trigger discovery fallback.
4. After exact `NotFound`, interpret the rejected value and all explicit readable
   terms only against the returned complete candidate set. One plausible alternative
   requires confirmation, several require selection, and none require focused
   correction. Never silently rewrite the rejected value.
5. When the latest message supplies readable environment or client context without a
   precise or identifier-like environment value, call
   `get_production_environment` with `{}` directly. In this ordinary discovery path,
   exactly one candidate satisfying all explicit terms may be proposed.
6. Do not invent an ID, client, alias, location, role, or relationship.
7. For an environment clarification, return the proposed shortlist in structured
   `environmentOptionIds`. Return an empty array for other clarification targets or
   when no plausible environment exists. Do not rely on IDs or display values written
   only inside `message`.
8. The application rejects duplicate, excessive, or unknown option IDs, reloads every
   accepted option, orders accepted contexts by stable environment ID, and renders
   the bounded model-authored `message` followed by their authoritative client name,
   environment name, and unchanged ID. The message is non-authoritative plain text:
   the application does not parse identifiers, names, relationships, or actions from
   it. Any invalid structured option set suppresses both the associated message and
   choices. The model must not shortlist an option that conflicts with explicit
   readable scope terms, and mandatory user confirmation or selection prevents
   automatic substitution.
9. Derive `clientId` from the selected environment candidate.
   Never ask the requester to supply or choose a client ID independently.
10. Select or clarify `requestedRoleId` only from the `roles` included with the
   selected environment.
11. In the ordinary readable-description path, one plausible candidate may be
   proposed. More than one plausible candidate
   requires one focused `environmentId` clarification with readable choices. No
   plausible candidate requires one focused correction without an ID.
12. If conversation history needed for "the first one" or a similar relative answer
   is missing, repeat a self-contained clarification.
13. A changed environment invalidates any dependent client, role, or incident value
   that does not fit the new environment.

MCP does not receive the raw environment description as a query and does not decide
semantic uniqueness. The model interpretation remains untrusted and is followed by
one deterministic candidate assessment.

## Incident Rules

1. Call `get_incident` only when the user supplies or changes a precise stable
   incident identifier.
2. Never convert a title, problem description, partial ID, reformatted ID, or inferred
   reference into `incidentId`.
3. When incident wording lacks the precise identifier, return one focused
   `incidentId` clarification asking for the exact ID or permission to continue
   without an incident.
4. A failed exact lookup clears the rejected incident value and requires correction.
5. Existing deterministic validation decides active status and client/environment
   compatibility.

## Role Rules

- `ProductionReadOnly` and `ProductionSupport` remain the only schema-supported IDs.
- Presence in model output never proves availability.
- The selected role must appear in the selected environment tool candidate.
- `RequestValidator` must independently reload the exact environment-role assignment
  before readiness and again through the existing confirmation path.
- No role hierarchy, privilege implication, or separate role tool exists.

## Tool-Call Sequencing

`AllowMultipleToolCalls` remains false. One model response may request at most one
tool call, while the function-calling loop may perform sequential calls before the
turn returns its single final proposal. Exact environment lookup followed by
environment discovery after `NotFound`, and then an exact incident lookup, may
therefore occur sequentially in one user turn. No response requests parallel calls.

## Expected Outcomes

### Unambiguous environment and exact incident

```text
latest message
  -> environment discovery
  -> one selected environment + embedded roles
  -> exact incident lookup
  -> one closed candidate proposal
  -> deterministic assessment
  -> ready or deterministic correction
```

### Ambiguous environment

```text
latest message
  -> environment discovery
  -> multiple plausible candidates
  -> one focused environment clarification
  -> persist sanitized collecting candidate only
```

### Potential identifier not found

```text
latest message with potential environment ID
  -> exact environment lookup
  -> typed NotFound
  -> bounded environment discovery
  -> zero, one, or several structured environmentOptionIds
  -> deterministic option reload
  -> model-authored question plus authoritative choice rendering
  -> focused correction, confirmation, or selection
  -> no candidate substitution before the developer replies
```

### Potential identifier lookup fails for another reason

```text
latest message with potential environment ID
  -> exact environment lookup
  -> InvalidInput, timeout, cancellation, unavailable, or malformed result
  -> typed safe failure or retry guidance
  -> no discovery fallback and no candidate change
```

### Incident description without exact ID

```text
latest message
  -> no incident tool call
  -> incidentId remains null
  -> one focused exact-ID-or-omit clarification
```

### Dependency or contract failure

```text
missing/extra tool, malformed result, timeout, cancellation, unavailable context
  -> typed safe failure
  -> no ready snapshot, request, approval, operation, or grant
```

## Trust Boundary

- MCP results and model output are context, not authority.
- Structured `environmentOptionIds` are untrusted proposals; only independently
  reloaded contexts are rendered as choices.
- The bounded model-authored clarification `message` may be rendered only after its
  structured options validate. It remains informational and is never parsed into
  choice data, candidate scope, workflow action, approval, or authorization.
- The model cannot confirm, submit, approve, provision, retry, or mutate workflow.
- Browser- or message-supplied identities and approver data are not trusted.
- Final confirmation binds only the server-owned, independently validated snapshot.
- Logs contain tool name, duration, correlation, and outcome, not raw messages,
  prompts, transcripts, or complete environment catalogs.

# Real Model Turn Contract

## Purpose

This contract defines the provider-boundary behavior added by the real-model
execution profile. It does not add a public HTTP endpoint, Teams action, MCP tool, or
domain command.

## Profile Selection

- Configuration section: `RequestPreparationModel`
- Closed profiles: `Deterministic`, `AzureOpenAI`
- Resolution: once per running host
- Authority: server/operator configuration only
- Requester override: forbidden through message text, activity data, card data,
  browser data, host headers, or model output
- Invalid or unknown selected profile: resolve to unavailable and never fall back

The configuration shape is defined by
[model-execution-profile.schema.json](model-execution-profile.schema.json).

## Input to the Existing Interpreter

The provider receives the same server-owned turn envelope already defined by the
Teams intake feature:

```json
{
  "latestMessage": "untrusted requester text",
  "currentCandidate": {
    "clientId": null,
    "environmentId": null,
    "requestedRoleId": null,
    "justification": null,
    "incidentId": null
  },
  "validationFeedback": []
}
```

The authenticated actor, claims, approver, duration policy, approval evidence, and
provisioning evidence are not model inputs.

## Model-Visible Tools

Catalog equality is mandatory. The discovered set must contain exactly:

1. `get_production_environment`
2. `get_incident`
3. `get_available_roles`

Every tool must retain its read-only annotation. Missing, extra, renamed, or
non-read-only tools make the turn unavailable. No confirmation, request creation,
approval, workflow transition, provisioning, retry, revocation, arbitrary database,
or generic query tool is permitted.

## Output

The model response must match the existing canonical contract exactly:

`specs/002-teams-access-intake/contracts/request-intake-proposal.schema.json`

It contains one complete nullable candidate snapshot and either one focused
clarification or `null`. The response is untrusted even when the provider reports
successful JSON-schema conformance.

## Deadline and Cancellation

- One cumulative inner deadline: 90 seconds.
- Covered operations: MCP connect/catalog, all model calls, all tool calls, response
  parsing, and successful MAF session save.
- Outer Teams endpoint deadline: 100 seconds.
- Inner deadline expiry: typed `Timeout`, with reply headroom.
- Caller cancellation: honored and classified separately from the inner deadline.
- No per-call timeout may reset or extend the cumulative deadline.

## Outcomes

| Boundary outcome | Application outcome | Teams behavior | Durable effect |
|------------------|---------------------|----------------|----------------|
| Schema-valid proposal | `Proposal` | Clarification, validation rejection, or confirmation after authoritative validation | Candidate/readiness may change only through existing application rules |
| Malformed or unsupported response | `MalformedModelOutput` | Safe retry guidance | No candidate, ready scope, request, approval, operation, grant, or audit change |
| Inner deadline expires | `Timeout` | Safe timeout guidance | Last accepted candidate and saved MAF session remain unchanged |
| Caller cancels | `Cancelled` | Safe response when channel remains writable | Last accepted candidate and saved MAF session remain unchanged |
| Invalid profile, credential failure, provider failure, quota/service failure, MCP failure, or catalog mismatch | `Unavailable` | Safe unavailable guidance | No fallback and no governed workflow state change |

## Authoritative Validation

After a `Proposal`, the existing deterministic validation must reload and verify:

- client identity;
- environment identity and client ownership;
- requested role support for that environment;
- incident existence, active state, client, and environment relationship;
- required justification bounds; and
- all other fixed request policy.

The model's `kind`, wording, validation claims, tool results, or selected profile
cannot override a validation rejection.

## Confirmation and Workflow

The real profile adds no action. The existing no-input `confirmAndSubmit` card action
remains the only request-creation path. Confirmation does not invoke the model or MCP;
it reloads ownership and authoritative scope. Business and DevOps approvals and
idempotent provisioning remain unchanged.

## Operational Evidence

Allowed structured metadata:

- correlation ID;
- profile ID;
- approved model ID when applicable;
- operation and closed outcome;
- duration;
- existing safe actor/conversation identifiers; and
- submitted request ID after confirmation.

Forbidden by default:

- Azure endpoint;
- credentials, tokens, or authentication diagnostics containing secrets;
- raw prompt or latest requester message;
- transcript or serialized MAF session;
- complete model response;
- tool arguments or complete MCP payloads; and
- Adaptive Card body.

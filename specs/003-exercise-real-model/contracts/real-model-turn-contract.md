# Real Model Turn Contract

## Purpose

This contract defines the provider-boundary behavior added by the real-model
execution profile. It does not add a public HTTP endpoint, Teams action, MCP tool, or
domain command.

The exact Teams `/new` lifecycle command is intercepted before this boundary and is
defined separately in [teams-reset-command.md](teams-reset-command.md). It never
becomes `latestMessage`, model history, or an MCP call.

## Profile Selection

- Configuration section: `RequestPreparationModel`
- Closed profiles: `Deterministic`, `FoundryResponses`
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
  }
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

When the latest message supplies or changes an incident or environment identifier,
the model instructions require the matching read-only lookup before returning that
identifier. Successful environment or incident results supply canonical `clientId`;
the requester is not expected to repeat a client display name as an identifier.

## Deadline and Cancellation

- The existing ASP.NET Core Teams endpoint request timeout is the single overall
  deadline: 100 seconds.
- Its cancellation token is propagated through MCP connect/catalog, model and tool
  calls, response parsing, and successful MAF session save.
- Endpoint deadline expiry is handled by native request-timeout middleware; a normal
  conversational reply is not guaranteed after the response channel is cancelled.
- Caller disconnect and request-timeout cancellation both fail closed and cannot
  advance intake or workflow state.
- Provider-side timeouts are still translated to the provider-neutral `Timeout`
  outcome while the request remains active.

## Outcomes

| Boundary outcome | Application outcome | Teams behavior | Durable effect |
|------------------|---------------------|----------------|----------------|
| Schema-valid proposal | `Proposal` | Clarification, validation rejection, or confirmation after authoritative validation | Only sanitized validated fields or immutable readiness may be persisted |
| Malformed or unsupported response | `MalformedModelOutput` | Safe retry guidance | No candidate, ready scope, request, approval, operation, grant, or audit change |
| Provider-side timeout | `Timeout` | Safe timeout guidance while the request remains active | Last accepted candidate and saved MAF session remain unchanged |
| Endpoint timeout or caller disconnect | Native request cancellation | Transport-level safe failure | Last accepted candidate and saved MAF session remain unchanged |
| Invalid profile, credential failure, provider failure, quota/service failure, MCP failure, or catalog mismatch | `Unavailable` | Safe unavailable guidance | No fallback and no governed workflow state change |

## Authoritative Validation

After a `Proposal`, the existing deterministic validation must reload and verify:

- client identity;
- environment identity and client ownership;
- requested role support for that environment;
- incident existence, active state, client, and environment relationship;
- required justification bounds; and
- all other fixed request policy.

Validation applies to every non-null partial identifier before a collecting candidate
is saved. Unknown, inactive, unavailable-role, and inconsistent values are cleared;
unrelated validated fields are retained. A validated environment or active incident
may derive canonical client and environment ownership. A rejected value produces
typed deterministic correction guidance immediately. The application does not run a
second interpretation for the same requester message; the next model call occurs only
after the requester supplies another message.

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
- deployment name when applicable;
- operation and closed outcome;
- duration;
- existing safe actor/conversation identifiers; and
- submitted request ID after confirmation.

Forbidden by default:

- Foundry Responses endpoint;
- credentials, tokens, or authentication diagnostics containing secrets;
- raw prompt or latest requester message;
- transcript or serialized MAF session;
- complete model response;
- tool arguments or complete MCP payloads; and
- Adaptive Card body.

# Project Context

## Product

This application governs temporary access to client-specific production
environments.

Core rule:

AI interprets and gathers context. Humans approve. Deterministic services
authorize and execute.

Product baseline:

`docs/governed-production-access-product-baseline.md`


## Scope

- Portfolio-grade reference implementation.
- One developer.
- One executable modular ASP.NET Core host.
- Local synthetic data and identity.
- Thin React user interface built and served by the ASP.NET Core host.
- No real production access.
- No real identity provider.
- No generic workflow engine.
- No multi-agent design.
- No large RAG subsystem.
- No unnecessary distributed-system infrastructure.

## Architecture Rules

- Domain logic must not depend on AI-provider or MCP SDK contracts.
- AI-provider and MCP contracts must be translated at infrastructure boundaries.
- The LLM is never an authorization boundary.
- Model output is untrusted and must be schema-validated.
- All model-proposed identifiers must be checked against authoritative data.
- Business and DevOps approvals are authenticated structured actions.
- Acting identity comes from authenticated server context.
- Browser-submitted identities, roles, and authorization claims are not trusted.
- The requester cannot choose the business approver.
- State-changing actions require deterministic validation and authorization.
- Approval applies to a specific immutable request ID and approved scope.
- A submitted request is read-only; corrections require a new request and new approvals.
- Access to one client environment never authorizes another.
- DevOps must not change the business-approved role.
- DevOps may reduce duration only when the active product baseline defines an
  adjustable duration and must never increase it. The current baseline fixes every
  grant at eight hours, so DevOps cannot alter duration.
- Authoritative client, environment, role, incident, and approver context is validated
  when requests and human decisions are recorded against the fixed synthetic dataset.
- Provisioning must be idempotent.
- The provisioning handler must reload and validate persisted request, approval, and
  operation evidence.
- The provisioning handler must not trust caller-supplied approval assertions.
- Project and module boundaries must remain proportionate to the single-host scope.

## MCP Rules

The application exposes one real read-only MCP endpoint with exactly these tools:

- `get_production_environment`
- `get_incident`

`get_production_environment` provides bounded environment discovery, exact
environment lookup, authoritative client relationships, and the roles assigned to
each returned environment. `get_incident` remains an exact-identifier lookup.

Additional MCP tools are outside the current baseline.

- MCP inputs and outputs use explicit typed schemas.
- Authoritative results use stable identifiers.
- The model receives tools through an explicit allowlist.
- The MCP server must not expose a separate role-listing tool; allowed roles are
  returned as part of authoritative environment context and are independently
  validated by deterministic application services.
- The MCP server must not expose approval, provisioning, revocation, workflow
  transition, arbitrary database, or generic query tools.
- MCP tool visibility and annotations never replace authorization.
- Provisioning must remain unavailable to the model.

## .NET Rules

- Nullable reference types enabled.
- Warnings treated as errors.
- `CancellationToken` propagated through asynchronous boundaries.
- LLM and MCP calls use explicit timeouts.
- Expected failures represented with explicit typed outcomes.
- No abstraction without a concrete need.
- Project structure must remain proportionate to scope.

## Testing Rules

- Tests run without a live LLM.
- Use a deterministic fake chat client.
- Domain rules require unit tests.
- MCP contracts and interactions require integration tests.
- Authorization, immutable-scope, invalid-transition, and idempotency rules require tests.
- Malformed model output and MCP failure or timeout require tests.
- Negative scenarios are first-class tests.
- Exhaustive UI and enterprise-scale load testing are not required.
- Final validation after a code change must run in this order and must not run these
  commands in parallel:

  1. `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
  2. `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore`
  3. `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m`

- Give the integration-test command an outer shell or tool timeout of at least four
  minutes. A shorter outer timeout can abandon the command while its child test runner
  remains alive.
- If a test command times out, do not immediately start another test run. First identify
  and stop only the test-runner process tree created by that command; never terminate
  unrelated or pre-existing `dotnet` processes.

## Security and Logging

- Do not log secrets.
- Do not log raw prompts or complete MCP payloads by default.
- Do not expose provisioning operations or credentials to the model.
- Record correlation IDs, authenticated actors, decisions, statuses, workflow
  transitions, and operation metadata.
- Record model and MCP duration and outcome without requiring complete payload
  capture.
- OpenTelemetry is optional polish and must not block MVP completion.

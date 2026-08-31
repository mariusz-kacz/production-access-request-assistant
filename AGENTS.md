# Project Agent Context

## Start Here

Read [`spec.md`](spec.md) for the repository map, authority order, and task-specific
context routes. The active product definition is
[`docs/governed-production-access-product-baseline.md`](docs/governed-production-access-product-baseline.md).

If artifacts conflict, follow the project constitution and current product baseline,
verify the as-built behavior in source and tests, and surface unresolved mismatches.

## Non-Negotiable Boundaries

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

- The application is one modular ASP.NET Core host with a thin co-hosted React UI,
  separate reference/workflow SQLite databases, local synthetic identity and data,
  and no real production access.
- The LLM is never an authorization boundary. Model output is untrusted,
  schema-validated, and checked against authoritative data.
- Acting identity and authorization come from authenticated server context. Browser
  payloads cannot choose identity, claims, scope, role, duration, or approver.
- Human decisions are authenticated structured actions bound to one immutable request
  ID and exact scope. Corrections require a new request and approvals.
- The requester cannot select the business approver. DevOps cannot change the
  business-approved role or the fixed eight-hour duration.
- Provisioning is unavailable to the model. The protected handler accepts a request
  ID, reloads persisted request, approval, operation, and grant evidence, and uses the
  request ID as the idempotency identity.
- MCP exposes exactly four typed read-only tools:
  `search_production_environments`, `get_production_environment`,
  `get_environment_roles`, and `get_incident`. The promoted catalog is defined by
  [the current MCP contract](docs/contracts/mcp-tools.json). Production must never
  register an additional catalog or expose a state-changing tool.
- Do not add real identity or provisioning, a generic workflow engine, multi-agent
  design, large RAG system, separate deployable service, or distributed infrastructure
  without an approved baseline and architecture change.

## Engineering Rules

- Keep domain and application logic in `GovernedAccess.Core`, independent of React,
  persistence, AI-provider, Teams, and MCP SDK contracts. Translate external contracts
  at `GovernedAccess.Web` or `GovernedAccess.Mcp` boundaries; keep direct reference
  and workflow persistence inside their owning infrastructure projects.
- Preserve nullable reference types, warnings-as-errors, analyzer enforcement, and
  `CancellationToken` propagation through async boundaries.
- Keep explicit timeouts on model, MCP, Teams, and provisioning work. Represent
  expected failures with typed outcomes.
- Add no abstraction, project, or module without a current concrete need.
- Do not log secrets, raw prompts, transcripts, or complete MCP payloads by default.
  Retain correlation, authenticated actor, decision, transition, operation, duration,
  and safe outcome metadata.

## Testing and Validation

- Automated tests must not require a live LLM. Use deterministic chat clients.
- Put deterministic domain policy in unit tests. Put MCP, persistence, authentication,
  authorization, concurrency, timeout, malformed-output, transition, and idempotency
  boundaries in integration tests. Negative scenarios are first-class evidence.
- After a code change, run these commands sequentially and in this order, never in
  parallel:

  1. `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
  2. `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore`
  3. `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m`

- Give the integration command an outer timeout of at least four minutes. After a
  timeout, identify and stop only the runner process tree created by that command
  before another run; never terminate unrelated or pre-existing `dotnet` processes.
- Run the frontend suite separately when frontend behavior or its contracts change:
  `npm test --prefix src/GovernedAccess.Web/ClientApp -- --run`.
- For documentation-only changes, validate links and run `git diff --check`; run code
  suites only when an example changed or the review exposes a suspected mismatch.

The detailed placement and acceptance matrix is authoritative in
[`docs/testing-strategy.md`](docs/testing-strategy.md).

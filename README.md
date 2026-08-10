# Governed Production Access Request Assistant

A portfolio-grade reference implementation for governing temporary access to
client-specific production environments.

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

Microsoft Teams is the only request-creation channel. The model may interpret a
request and call two read-only MCP tools, but it cannot submit, approve, provision,
retry, revoke, or otherwise change workflow state. Environment context supports
bounded discovery and exact lookup, while incident context remains exact-only. Every
model proposal is schema-validated and checked against authoritative synthetic data.

## What the application demonstrates

1. An SDK-authenticated personal Teams chat starts or continues one request intake.
2. A Microsoft Agent Framework `ChatClientAgent` interprets the message using
   process-local conversation history.
3. The agent receives exactly two tools from the real loopback MCP endpoint:
   - `get_production_environment` for bounded discovery or exact lookup, including
     each environment's authoritative client and assigned roles;
   - `get_incident` for an exact requester-supplied incident identifier.
4. Deterministic application code canonicalizes the candidate and alone decides
   whether it is ready.
5. A ready intake becomes an immutable Adaptive Card with one **Confirm and submit**
   action, a reserved request ID, and a 30-minute confirmation window.
6. Authenticated confirmation reloads and revalidates server-owned scope, then
   atomically creates one request and its audit evidence.
7. The React application acts as a request register and authenticated
   business/DevOps approval, provisioning-retry, and audit surface.
8. Protected provisioning reloads persisted evidence and creates or returns one
   idempotent synthetic eight-hour grant.

The checked-in default uses no live LLM. An explicitly selected Azure AI Foundry
Responses profile is available for manual Teams exercises. There is no real
production access, real identity provider, or real provisioning integration.

## Architecture

```text
Microsoft Teams personal chat
  |
  | authenticated Activity Protocol
  v
+-----------------------------------------------------------------------+
| GovernedAccess.Web                                                    |
|                                                                       |
| /api/messages -> Teams adapter -> Draft + submission services        |
|                       |                |                               |
|                       v                v                               |
|                 MAF ChatClientAgent  EF Core / SQLite                  |
|                       |                                                |
|                       v                                                |
| /mcp -> exact two read-only context tools                              |
|                                                                       |
| React UI -> request list/detail, business decision, DevOps decision,  |
|             protected retry, session, and audit presentation          |
+------------------------------+----------------+-----------------------+
                               |                |
                               v                v
                    GovernedAccess.Core   GovernedAccess.Mcp
                    Domain/application   Typed MCP boundary
                    rules
```

`GovernedAccess.Web` is the sole executable. `GovernedAccess.Core` has no
dependency on Teams, Microsoft Agent Framework, Adaptive Cards, EF Core, React, or MCP
SDK contracts. Infrastructure adapters translate those contracts at the boundary.

SQLite stores the compact canonical candidate, intake lifecycle, immutable request,
approvals, provisioning operation, grant, and audit evidence. MAF conversation
history remains in its native in-memory session store for the process lifetime.
Restarting the host loses conversational history but retains accepted candidate data;
ambiguous replies are clarified again rather than guessed.

## Security invariants

- Teams requester identity comes from the SDK-authenticated activity; Web actors come
  from authenticated server context.
- Browser- or card-submitted identities, roles, approver assignments, duration, and
  authorization claims are not trusted.
- Teams confirmation is the only executable request-creation path. The browser has no
  draft endpoint, request-creation POST, route, form, navigation item, or capability.
- Model output is untrusted, schema-validated, and authoritatively revalidated.
- The model receives exactly the two read-only MCP tools and no state-changing
  capability. It receives assigned roles with authoritative environment context;
  there is no separate role-listing tool.
- Only a typed exact environment `NotFound` permits bounded discovery fallback.
  Other failures remain explicit and cannot be masked by discovery.
- Ready and submitted scope is immutable; correction creates a new intake, request
  ID, and approval sequence.
- A requester cannot select the business approver.
- Business approval binds one immutable request ID and exact role.
- DevOps cannot change the role or fixed eight-hour grant lifetime.
- Provisioning accepts only the request ID and reloads persisted request, approval,
  and operation evidence.
- The request ID is the provisioning idempotency identity.
- Secrets, tokens, raw prompts, transcripts, card bodies, and complete MCP payloads
  are not logged by default.

## Technology

- .NET 10, ASP.NET Core, and C# 14
- Nullable reference types, analyzers, and warnings treated as errors
- EF Core with SQLite
- Microsoft 365 Agents SDK for authenticated Teams transport
- Microsoft Agent Framework with its native process-local session store
- `Microsoft.Extensions.AI` with deterministic and optional Foundry Responses clients
- Model Context Protocol SDK with Streamable HTTP transport
- React 19, TypeScript 7, React Router, and Vite
- xUnit, ASP.NET Core `WebApplicationFactory`, Vitest, and React Testing Library

## Run locally

Prerequisites:

- .NET 10 SDK
- Node.js 24 with npm
- PowerShell 7 or another shell capable of running `dotnet` and `npm`
- a trusted ASP.NET Core HTTPS development certificate

From the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Open `https://localhost:7251`. The launch profile also binds
`http://localhost:5136` for the server-side loopback MCP client. Use HTTPS for the
browser because authentication and antiforgery cookies are Secure.

The normal local host requires no model credentials or external infrastructure. The
browser can inspect requests and perform authorized workflow actions, but it cannot
create a request.

For React hot-module replacement, keep the ASP.NET Core host running and start:

```powershell
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

## Deterministic model behavior

The shipped host uses `DeterministicChatClient` in `Candidate` mode. It deliberately
returns one complete fixed Client Alpha read-only incident candidate, so an accepted
personal Teams message normally produces the confirmation card immediately.

That behavior makes the default demo stable for transport, card, confirmation,
approval, and provisioning walkthroughs. It does not simulate realistic information
gathering. History-sensitive clarification, restart recovery, malformed output,
timeout, cancellation, and dependency failures are exercised with specialized
deterministic modes in automated tests. The optional `FoundryResponses` profile uses
the same schema validation, authoritative checks, and deterministic authorization
boundary.

## Demonstrate the governed workflow

### Automated, credential-free acceptance

The complete acceptance flow uses fake SDK-authenticated Teams activities and the
deterministic chat client. Follow the
[Teams access-intake quickstart](specs/002-teams-access-intake/quickstart.md) to
validate:

1. complete request preparation and confirmation;
2. history-sensitive multi-turn clarification;
3. ready-draft discussion and immutable replacement;
4. replay and concurrency;
5. trust-boundary and dependency failures; and
6. business approval, DevOps approval, and provisioning.

### Real Teams transport

The optional real transport demo uses a Microsoft 365 developer tenant, a
Teams-managed bot registration by default, a persistent Dev Tunnel, and a
personal-scope sideloaded app package.

Follow the concise [Teams quickstart](docs/teams-quickstart.md).
It uses separate scripts for one-time setup, tunnel hosting, app startup, and route
checks. The [Teams advanced reference](docs/teams-advanced-reference.md) covers registration
choices, manual package inspection, and permanent cleanup.

The default app-start command uses the deterministic model. After `az login`, select
the optional live model explicitly:

```powershell
.\scripts\teams\start-app.ps1 -ModelProfile FoundryResponses -FoundryEndpoint "https://<project>.services.ai.azure.com/openai/v1" -DeploymentName "<deployment-name>"
```

The live model may interpret and gather context, but it receives no approval or
provisioning capability. Invalid configuration or provider failure does not fall
back to a deterministic candidate.

## Build and test

Run the normal validation gates sequentially from the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

The integration command runs component and FullHost fixtures together. Give it an
outer timeout of at least four minutes.

The suites require no live LLM, Teams tenant, Azure subscription, public tunnel, or
production system. The latest backend validation records:

- 71 unit tests passed; and
- 92 integration/component/full-host tests passed.

The frontend suite contains 6 tests and remains a separate validation command.

See the [validation report](specs/002-teams-access-intake/validation.md) for contract
checks, Scenarios 1-6, and deterministic confirmation timing.

## Repository structure

```text
src/
  GovernedAccess.Core/       Domain, application services, and provider-neutral ports
  GovernedAccess.Mcp/        Real read-only MCP endpoint and typed tool adapters
  GovernedAccess.Web/        Executable host, Teams/MAF adapters, API, EF Core, React
tests/
  GovernedAccess.UnitTests/
  GovernedAccess.IntegrationTests/
docs/
  adr/                       Architecture decision records
specs/
  001-governed-production-access/
                             Governed workflow baseline artifacts
  002-teams-access-intake/   Teams-only intake design and validation artifacts
  004-resolve-context-identifiers/
                             Current environment-resolution design and contracts
```

## Documentation

- [Product baseline](docs/governed-production-access-product-baseline.md)
- [Product roadmap](docs/roadmap.md)
- [As-built architecture](docs/architecture.md)
- [Request intake orchestration rules](docs/request-intake-orchestration.md)
- [Security and trust model](docs/security-model.md)
- [Local development guide](docs/local-development.md)
- [Teams quickstart](docs/teams-quickstart.md)
- [Teams advanced reference](docs/teams-advanced-reference.md)
- [Testing strategy](docs/testing-strategy.md)
- [Live-model evaluation guide](docs/live-model-evaluation.md)
- [Teams access-intake specification](specs/002-teams-access-intake/spec.md)
- [Teams access-intake implementation plan](specs/002-teams-access-intake/plan.md)
- [Teams access-intake data model](specs/002-teams-access-intake/data-model.md)
- [Teams activity contract](specs/002-teams-access-intake/contracts/teams-activity-contract.md)
- [Environment-resolution specification](specs/004-resolve-context-identifiers/spec.md)
- [Environment-resolution implementation plan](specs/004-resolve-context-identifiers/plan.md)
- [Environment-resolution data model](specs/004-resolve-context-identifiers/data-model.md)
- [Environment-resolution turn contract](specs/004-resolve-context-identifiers/contracts/environment-resolution-turn-contract.md)
- [Current model proposal schema](specs/004-resolve-context-identifiers/contracts/request-intake-proposal.schema.json)
- [Prepared-card contract](specs/002-teams-access-intake/contracts/prepared-request-card.json)
- [Teams access-intake quickstart](specs/002-teams-access-intake/quickstart.md)
- [Teams access-intake validation](specs/002-teams-access-intake/validation.md)
- [Environment-resolution quickstart](specs/004-resolve-context-identifiers/quickstart.md)
- [Current task list](specs/004-resolve-context-identifiers/tasks.md)
- [Current MCP tool contract](specs/004-resolve-context-identifiers/contracts/mcp-tools.json)
- [Architecture decision index](docs/adr/README.md)

## Scope

This is a focused local reference implementation, not a production identity-
governance product. It intentionally excludes real access, real identity federation,
mutable enterprise reference systems, automatic revocation, proactive Teams
notifications, generic workflow engines, multi-agent design, large retrieval systems,
multiple deployable services, and unnecessary distributed infrastructure.

# Governed Production Access Request Assistant

A portfolio-grade reference implementation for governing temporary access to
client-specific production environments.

The application demonstrates a deliberately narrow enterprise AI pattern:

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

The LLM is never an authorization boundary. It can prepare an untrusted typed draft
and use three read-only MCP tools, but it cannot approve, change workflow state, or
provision access.

## What the application demonstrates

A requester describes why they need production access. The application:

1. uses a deterministic chat client to produce a schema-constrained request draft;
2. exposes stored context through a real Streamable HTTP MCP endpoint;
3. validates every proposed identifier against authoritative synthetic data;
4. resolves the responsible business approver on the server;
5. records authenticated business and DevOps decisions;
6. reloads persisted workflow evidence before provisioning;
7. creates or returns one idempotent synthetic grant; and
8. presents the complete audit history.

There is no live LLM, real identity provider, real production environment, or real
access provisioning.

## Architecture

The repository contains one executable ASP.NET Core host and two supporting class
libraries:

```text
Browser
  |
  | same-origin UI API, cookie authentication, antiforgery
  v
+----------------------------------------------------------------+
| GovernedAccess.Web                                             |
|                                                                |
| React UI                    Request and workflow endpoints      |
| Deterministic chat client   EF Core and SQLite                  |
| MCP client                  Synthetic identity and provisioning |
|                                                                |
| /mcp - stateless Streamable HTTP endpoint                      |
|   - get_production_environment                                 |
|   - get_incident                                               |
|   - get_available_roles                                        |
+-------------------------+-------------------+------------------+
                          |                   |
                          v                   v
                 GovernedAccess.Core   GovernedAccess.Mcp
                 Domain rules and      Typed read-only MCP
                 application services boundary
```

`GovernedAccess.Core` has no dependency on React, EF Core, AI-provider contracts, or
MCP SDK contracts. `GovernedAccess.Mcp` translates the MCP protocol at the
infrastructure boundary. `GovernedAccess.Web` is the composition root and the only
deployable executable.

## Security invariants

- Acting identity comes from an authenticated server-issued cookie.
- Browser-submitted identities, roles, approver assignments, and claims are not
  trusted.
- Submitted requests are immutable; corrections require a new request and approvals.
- A requester cannot select the business approver.
- Business approval binds the exact request ID and requested role.
- DevOps cannot change the role or the fixed eight-hour grant lifetime.
- Provisioning accepts the request ID, reloads persisted evidence, and does not trust
  caller-supplied approval assertions.
- The request ID is the provisioning idempotency identity.
- The model receives exactly three read-only MCP tools and no approval, workflow, or
  provisioning capability.

## Technology

- .NET 10 and ASP.NET Core
- C# 14 with nullable reference types and warnings treated as errors
- EF Core with SQLite
- Microsoft.Extensions.AI with a deterministic `IChatClient`
- Model Context Protocol SDK with Streamable HTTP transport
- React 19, TypeScript 7, React Router, and Vite
- xUnit, ASP.NET Core integration testing, Vitest, and React Testing Library

## Run locally

Prerequisites:

- .NET 10 SDK
- Node.js 24 with npm
- PowerShell 7 or another shell capable of running `dotnet` and `npm`

From the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Open the HTTPS URL printed by ASP.NET Core. The HTTPS launch profile uses
`https://localhost:7251` and also binds `http://localhost:5136` for the server-side
loopback MCP client. Browser authentication and antiforgery cookies are always marked
Secure, so use the HTTPS address for the UI.

The build compiles the React application into the ASP.NET Core static web assets.
SQLite data is created locally and seeded with a fixed synthetic dataset. No model
credentials or external services are required.

For frontend hot-module replacement, keep the ASP.NET Core host running and start:

```powershell
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

Open the Vite URL. Vite proxies `/api` to ASP.NET Core so the browser continues to use
the same-origin application contract.

## Demonstrate the workflow

1. Select **Demo Requester** and create a new request using:

   ```text
   I need read-only access to Client Alpha production for INC-1042 to investigate the incident.
   ```

2. Review the prepared draft and submit it. The request enters
   `AwaitingBusinessApproval`.
3. Switch to the Client Beta approver and verify the Client Alpha request is not
   available for approval.
4. Switch to the Client Alpha approver and approve the request.
5. Switch to the DevOps approver and approve it.
6. Review the active eight-hour synthetic grant and the audit timeline.

The detailed success, rejection, correction, failure, retry, responsive-layout, and
security checks are in the
[quickstart validation guide](specs/001-governed-production-access/quickstart.md).

## Build and test

Run all validation from the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
npm run build --prefix src/GovernedAccess.Web/ClientApp
dotnet build ProductionAccessRequestAssistant.sln --no-restore
dotnet test ProductionAccessRequestAssistant.sln --no-build
```

The suites require no live LLM. They cover domain rules, authorization, immutable
scope, malformed model output, MCP contracts and failures, provisioning evidence,
idempotency, concurrency, API security, UI session wiring, and workflow presentation.

At the time of this README update, the repository passes:

- 42 .NET unit tests;
- 126 .NET integration tests; and
- 5 frontend tests.

## Repository structure

```text
src/
  GovernedAccess.Core/       Domain, application services, and ports
  GovernedAccess.Mcp/        MCP endpoint and typed tool adapters
  GovernedAccess.Web/        Executable host, API, persistence, AI adapters, and React
tests/
  GovernedAccess.UnitTests/
  GovernedAccess.IntegrationTests/
docs/
  adr/                       Architecture decision records
specs/
  001-governed-production-access/
                             Specification and delivery artifacts
```

## Documentation

- [Product baseline](docs/governed-production-access-product-baseline.md)
- [As-built architecture](docs/architecture.md)
- [Security and trust model](docs/security-model.md)
- [Local development guide](docs/local-development.md)
- [Testing strategy](docs/testing-strategy.md)
- [Architecture decision index](docs/adr/README.md)
- [Documentation plan](docs/documentation-plan.md)
- [Feature specification](specs/001-governed-production-access/spec.md)
- [Implementation plan](specs/001-governed-production-access/plan.md)
- [Data model](specs/001-governed-production-access/data-model.md)
- [UI API contract](specs/001-governed-production-access/contracts/ui-api.md)
- [MCP tool contract](specs/001-governed-production-access/contracts/mcp-tools.json)
- [Quickstart validation guide](specs/001-governed-production-access/quickstart.md)
- [Completed task list](specs/001-governed-production-access/tasks.md)
- [Architecture decisions](docs/adr/README.md)

## Scope

This is a focused local reference implementation, not a production identity
governance product. It intentionally excludes real access, real identity federation,
mutable enterprise reference systems, automatic revocation, a generic workflow
engine, a large retrieval system, multiple services, and unnecessary distributed
infrastructure.

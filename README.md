# Governed Production Access Request Assistant

This repository follows a temporary production-access request from a Microsoft Teams
conversation through business and DevOps approval to a synthetic eight-hour grant.
The application runs as one ASP.NET Core host with a React register, a Teams endpoint,
a Microsoft Agent Framework interpreter, two read-only MCP tools, and SQLite.

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

Teams is the only request-creation channel. The model can resolve context and propose
a candidate, but it cannot submit, approve, retry, provision, or otherwise change
workflow state. Every proposed identifier is checked against the fixed authoritative
dataset before it can become part of a request. Nothing in the repository grants real
production access.

## Request flow

1. An authenticated personal Teams message starts or continues an intake.
2. The interpreter may call `get_production_environment` or `get_incident` through the
   co-hosted MCP endpoint.
3. Application code validates the resulting client, environment, role, incident, and
   justification. The model does not decide readiness.
4. A ready scope is shown on an Adaptive Card with a reserved request ID and a
   30-minute confirmation window.
5. Confirmation creates an immutable request. The assigned business approver and the
   DevOps approver record separate authenticated decisions in the React application.
6. Provisioning reloads the persisted request and approval evidence, then creates or
   returns the request-keyed synthetic grant.

## Conversation captures

The captures use the synthetic catalog. They end at a validated draft; access is not
requested until the requester uses **Confirm and submit** on the card.

<details>
<summary><strong>Exact incident to validated draft</strong></summary>

![Teams conversation resolving an exact incident, selecting a production role, and displaying the validated request card](docs/img/Case1.png)

</details>

<details>
<summary><strong>Bounded environment discovery and required justification</strong></summary>

![Teams conversation narrowing Client Beta production environments, selecting the authoritative role, gathering justification, and displaying the validated request card](docs/img/Case2.png)

</details>

<details>
<summary><strong>Scope revision before confirmation</strong></summary>

![Teams conversation narrowing Client Gamma recovery scope and replacing an earlier draft with a corrected read-only request](docs/img/Case3.png)

</details>

## Inside the host

```text
Microsoft Teams
      |
      | authenticated Activity Protocol
      v
 /api/messages -----> Teams adapter
                          |
                          +----> MAF interpreter ----> /mcp
                          |                              |
                          |                              +-- get_production_environment
                          |                              +-- get_incident
                          |
                          +----> draft and submission services
                                          |
React UI ----> /api ----> query and approval services
                                          |
                         +----------------+----------------+
                         v                                 v
                GovernedAccess.Core                 EF Core / SQLite
                         |
                         v
                synthetic provisioner
```

`GovernedAccess.Web` is the sole executable and composition root.
`GovernedAccess.Core` contains the domain and application rules without dependencies
on Teams, Agent Framework, Adaptive Cards, EF Core, React, or MCP SDK contracts.
`GovernedAccess.Mcp` translates the two typed tool contracts to Core's read-only
request-context boundary.

SQLite retains accepted candidate state, intake lifecycle, immutable requests,
approvals, provisioning operations, grants, and audit evidence. Conversation history
stays in the native in-memory Agent Framework session store. A process restart loses
that history but not the accepted candidate; an ambiguous reply is clarified again
rather than guessed.

## Authorization boundary

- Teams confirmation is the only path that creates a request; the browser API has no
  request-creation endpoint.
- Browser identities come from the server-issued demo cookie. Teams actors pass the
  separate Azure Bot Service bearer policy and tenant/personal-conversation checks.
- Model output and MCP results are untrusted inputs. Core reloads authoritative context
  and validates every relationship.
- The requester cannot select the business approver, and DevOps cannot change the
  business-approved role or the fixed grant lifetime.
- Provisioning receives only the request ID and reconstructs its input from persisted
  request, approval, and operation evidence.
- The request ID is also the provider idempotency identity.

The complete threat and control analysis is in the
[security model](docs/security-model.md).

## Run it

The local path requires the .NET 10 SDK, Node.js 24 with npm, PowerShell 7, and a
trusted ASP.NET Core HTTPS development certificate.

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

Building and testing need no external services. Starting the normal host requires the
Teams bot and tenant configuration that is intentionally blank in source control.
Follow the [Teams quickstart](docs/teams-quickstart.md) for one-time setup and daily
start commands. The [local development guide](docs/local-development.md) covers React
hot reload, database handling, configuration, and live-model evaluation.

## Validate it

After restoring .NET and frontend dependencies, run the validation gates sequentially:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

Give the integration command an outer timeout of at least four minutes. The automated
suites use deterministic fake clients and require no live LLM, Teams tenant, Azure
subscription, tunnel, or production system.

## Design records

- [Architecture](docs/architecture.md) and
  [architecture decisions](docs/adr/README.md)
- [Security and trust model](docs/security-model.md) and
  [product baseline](docs/governed-production-access-product-baseline.md)
- [Request-intake orchestration](docs/request-intake-orchestration.md) and the
  [current MCP contract](specs/004-resolve-context-identifiers/contracts/mcp-tools.json)
- [Testing strategy](docs/testing-strategy.md) and
  [live-model evaluation](docs/live-model-evaluation.md)

# Local development

- **Status**: Current
- **Last reviewed**: 2026-08-05
- **Audience**: Developers running or changing the local MVP

## Choose a path

| Goal | Start here |
|---|---|
| Run the Web app and deterministic tests | [Normal local setup](#normal-local-setup) |
| Receive requests from real Teams | [Teams quickstart](teams-quickstart.md) |
| Use real Teams and a live Foundry model | [Teams quickstart: Azure sign-in](teams-quickstart.md#3-sign-in-to-azure-for-the-live-model) |

The normal local loop needs no Microsoft 365 tenant, Azure subscription, tunnel, or
model credentials.

## Normal local setup

Prerequisites:

- .NET 10 SDK;
- Node.js 24 with npm;
- PowerShell 7;
- a browser that trusts the ASP.NET Core HTTPS development certificate.

Confirm the tools:

```powershell
dotnet --version
node --version
npm --version
```

If HTTPS is not trusted:

```powershell
dotnet dev-certs https --trust
```

First run from the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Open `https://localhost:7251`.

The host also binds `http://localhost:5136` for local MCP and Teams tunnel traffic.
Use HTTPS in the browser because authentication and antiforgery cookies are Secure.

The Web UI lists requests and supports human decisions and protected retry. Request
creation exists only through authenticated Teams messages.

## Model modes

The checked-in default is `Deterministic`. It needs no credentials and always returns
the fixed Client Alpha candidate used by tests and stable workflow demos. It does not
measure whether a model understands natural-language environment wording.

The optional `FoundryResponses` mode uses a real Azure AI Foundry deployment. For the
shortest supported setup, start it through the Teams helper:

```powershell
.\scripts\teams\start-app.ps1 -ModelProfile FoundryResponses -FoundryEndpoint "https://<project>.services.ai.azure.com/openai/v1" -DeploymentName "<deployment-name>"
```

That command assumes the one-time Teams setup is complete and `az login` has
authenticated an identity allowed to invoke the deployment. See the complete
[Teams quickstart](teams-quickstart.md).

To start a live model without real Teams, set the same process-local configuration
before `dotnet run`:

```powershell
az login
$env:RequestPreparationModel__ExecutionProfile = "FoundryResponses"
$env:RequestPreparationModel__FoundryResponses__Endpoint = "https://<project>.services.ai.azure.com/openai/v1"
$env:RequestPreparationModel__FoundryResponses__DeploymentName = "<deployment-name>"
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

This starts the model-enabled host, but a real request still requires authenticated
Teams transport. Do not put an API key or token in `appsettings*.json`.

An invalid or unavailable live profile fails closed. It does not silently switch to
the deterministic model.

## Common development commands

Restore:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

Build:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
```

Test:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

Run the three backend commands sequentially in exactly that order. The integration
command runs component and FullHost fixtures together; give it an outer timeout of at
least four minutes. If it times out, identify and stop only the runner process tree it
created before starting another run. The frontend command is separate from the
required backend gate.

All automated tests use deterministic fake clients. They never call the live model.
The optional sanitized semantic matrix is documented in the
[feature-004 validation quickstart](../specs/004-resolve-context-identifiers/quickstart.md#optional-live-model-quality-matrix).

## Local MCP surface

The same host exposes the real streamable HTTP endpoint at
`http://localhost:5136/mcp`. It advertises exactly:

- `get_production_environment`, using `{}` for a complete bounded environment
  catalog or one nonblank `environmentId` for exact lookup; and
- `get_incident`, requiring one precise stable `incidentId`.

Environment results include their authoritative client relationship and assigned
roles, so no separate role-listing tool exists. Discovery returns no partial catalog:
more than 20 environments fails closed. The model-facing wire schemas are recorded
in the [current MCP contract](../specs/004-resolve-context-identifiers/contracts/mcp-tools.json).

Publish:

```powershell
dotnet publish src/GovernedAccess.Web/GovernedAccess.Web.csproj -c Release
```

## React hot reload

Keep the ASP.NET Core host running, then start Vite in another terminal:

```powershell
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

Open the URL printed by Vite. It proxies `/api` to `https://localhost:7251`.
If the ASP.NET address differs:

```powershell
$env:VITE_API_PROXY_TARGET = "https://localhost:7443"
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

## Configuration reference

ASP.NET Core configuration uses `__` instead of `:` in environment variable names.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:GovernedAccess` | `Data Source=governed-access.db` | Local SQLite database |
| `RequestPreparationModel:ExecutionProfile` | `Deterministic` | Closed choice: `Deterministic` or `FoundryResponses` |
| `RequestPreparationModel:FoundryResponses:Endpoint` | empty | Trusted project URL ending in `/openai/v1` |
| `RequestPreparationModel:FoundryResponses:DeploymentName` | empty | Selected Foundry deployment |
| `TeamsAccessRequest:AllowedTenantId` | empty, fail closed | Accepted Teams tenant |
| `TeamsAccessRequest:TrustedWebBaseUri` | empty, fail closed | HTTPS host origin used to derive the co-hosted loopback MCP endpoint |
| `TeamsAccessRequest:RequestTimeout` | `00:01:40` | Total Teams request deadline |
| `TeamsAccessRequest:PreparationLifetime` | `00:30:00` | Confirmation window |

The Teams start script supplies the bot audience, secret, tenant, authority, and
trusted tunnel URL from ignored local state. Do not persist its secret in application
settings.

## Local database

Startup creates and seeds the SQLite database with fixed synthetic data. Workflow
records remain between runs.

This project uses `EnsureCreated`, not migrations. After an EF model change, stop the
host and preserve the old disposable database:

```powershell
.\scripts\backup-local-database.ps1
```

The next start creates the current schema. The script moves only the explicitly named
database files into a timestamped ignored directory; it does not delete them.

## Project map

| Area | Location |
|---|---|
| Domain and workflow rules | `src/GovernedAccess.Core` |
| MCP endpoint and adapters | `src/GovernedAccess.Mcp` |
| Host, Teams, AI, EF Core | `src/GovernedAccess.Web` |
| React application | `src/GovernedAccess.Web/ClientApp/src` |
| Unit tests | `tests/GovernedAccess.UnitTests` |
| Integration tests | `tests/GovernedAccess.IntegrationTests` |

## Quick troubleshooting

| Symptom | Fix |
|---|---|
| Browser stays anonymous | Use `https://localhost:7251`, not HTTP. |
| Browser rejects the certificate | Run `dotnet dev-certs https --trust`, then restart the host and browser. |
| Vite cannot reach the API | Confirm the HTTPS host is running or set `VITE_API_PROXY_TARGET`. |
| Startup reports a synthetic-data conflict | Stop the host and back up the local database before restarting. |
| Frontend rejects the Node version | Use Node 24; the package requires major version 24. |
| Live model is unavailable | Check the endpoint, deployment, `az account show`, and Azure role assignment. |

## Related documentation

- [Teams quickstart](teams-quickstart.md)
- [Teams advanced reference](teams-advanced-reference.md)
- [Testing strategy](testing-strategy.md)
- [Architecture](architecture.md)
- [Security model](security-model.md)

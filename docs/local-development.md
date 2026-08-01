# Local Development Guide

- **Status**: Current
- **Last reviewed**: 2026-07-30
- **Audience**: Developers running or changing the local MVP

## Prerequisites

- .NET 10 SDK
- Node.js 24 with npm
- PowerShell 7 or another shell capable of running `dotnet` and `npm`
- a browser that accepts the local ASP.NET Core HTTPS development certificate

Confirm the required toolchains:

```powershell
dotnet --version
node --version
npm --version
```

The frontend package declares Node `>=24.0.0 <25`. The shared .NET build configuration
targets .NET 10 and C# 14.

If the local HTTPS certificate is not trusted:

```powershell
dotnet dev-certs https --trust
```

## First run

From the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Open `https://localhost:7251`.

The HTTPS launch profile also binds `http://localhost:5136` for local host/MCP
inspection. Use HTTPS for the browser because authentication and antiforgery cookies
are always Secure.

The .NET build invokes the frontend build when its inputs have changed. Vite writes
the generated bundle to `src/GovernedAccess.Web/wwwroot`, and ASP.NET Core serves it
from the same origin.

No model credentials, external APIs, containers, or infrastructure provisioning are
required.

## Development modes

### ASP.NET-hosted application

Use this mode to exercise the production-shaped single-host application:

```powershell
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

ASP.NET Core serves the API, MCP endpoint, and compiled React assets.

Request creation is exercised through the authenticated Teams `/api/messages` path
or its fake-authenticated integration-test boundary. The browser exposes only
request list/detail, business and DevOps decisions, protected retry, session, and
audit presentation. It has no `/requests/new`, draft endpoint, request-creation POST,
form, navigation item, or creation capability.

### React hot-module replacement

Start the ASP.NET Core host in one terminal:

```powershell
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Start Vite in another:

```powershell
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

Open the URL printed by Vite. It proxies `/api` to
`https://localhost:7251`. The browser is a request register and approval/retry surface;
it never creates requests or calls `/mcp`.

Set `VITE_API_PROXY_TARGET` before starting Vite if the ASP.NET Core HTTPS address is
different:

```powershell
$env:VITE_API_PROXY_TARGET = "https://localhost:7443"
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

### Backend-only build loop

When a change cannot affect the React build, the project-specific opt-out can shorten
the .NET loop:

```powershell
dotnet build ProductionAccessRequestAssistant.sln -p:SkipFrontendBuild=true
```

Do not use this for final validation or publishing because generated frontend assets
may be stale.

## Configuration

The host uses standard ASP.NET Core configuration. The current settings are:

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:GovernedAccess` | `Data Source=governed-access.db` | SQLite connection string |
| `Connections:BotServiceConnection:Settings:Authority` | `https://login.microsoftonline.com/botframework.com` | Multitenant Teams-managed bot token authority |
| `TeamsAccessRequest:AllowedTenantId` | empty (fail closed) | Accepted Teams tenant |
| `TeamsAccessRequest:BotConnectionName` | `BotServiceConnection` | Configured bot connection |
| `TeamsAccessRequest:TrustedWebBaseUri` | empty (fail closed) | Trusted origin for request links |
| `TeamsAccessRequest:McpTimeout` | `00:00:05` | MCP deadline |
| `TeamsAccessRequest:ModelTimeout` | `00:00:30` | Overall Teams interpretation deadline |
| `TeamsAccessRequest:PreparationLifetime` | `00:30:00` | Immutable confirmation window |

For a temporary PowerShell override, replace `:` with `__`:

```powershell
$env:ConnectionStrings__GovernedAccess = "Data Source=governed-access-dev.db"
$env:Connections__BotServiceConnection__Settings__Authority = "https://login.microsoftonline.com/botframework.com"
$env:TeamsAccessRequest__AllowedTenantId = "<development-tenant-guid>"
$env:TeamsAccessRequest__TrustedWebBaseUri = "https://localhost:7251/"
$env:TeamsAccessRequest__McpTimeout = "00:00:05"
$env:TeamsAccessRequest__ModelTimeout = "00:00:30"
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Timeout and lifetime values must parse as positive .NET `TimeSpan` values. The trusted
Web base URI must be an absolute HTTPS URI.

Do not commit machine-specific connection strings or local secrets to
`appsettings*.json`.

## Deterministic AI behavior

The shipped host registers:

```text
DeterministicChatClient(DeterministicChatMode.Candidate)
```

It returns a fixed Client Alpha read-only incident candidate for Teams preparation.
There is no runtime configuration key or browser control for changing chat mode.

The other deterministic modes—`Clarification`, `Malformed`, `Timeout`,
`Cancellation`, `Unavailable`, and `PromptInjection`—are test seams. Integration
tests replace the registered `IChatClient` in a test host to prove safe behavior
without adding a failure-control surface to the application.

Multi-turn clarification keeps its MAF session only in the running host process.
Restarting the host intentionally loses conversation history but preserves the
accepted typed candidate in SQLite. A relative reply after restart is not guessed;
the assistant repeats a self-contained clarification. No option list, transcript, or
serialized MAF session is written to the local database.

If runtime-selectable demonstration modes are added later, they must remain
development-only and must not expose provider credentials, raw prompts, or a way to
bypass validation.

## Synthetic provisioning behavior

The normal host uses successful request-ID-based get-or-create provisioning.

`SyntheticAccessProvisionerControl` supports `Succeed`, `Fail`,
`LoseResponseAfterCreate`, and `WaitForTimeout`, but no HTTP endpoint or configuration
key exposes that control. Tests resolve and configure it through the test host.

This keeps failure injection out of the browser-facing application. Use the
integration tests described in the
[testing strategy](testing-strategy.md) to demonstrate failure and retry behavior.

## Local data

### Database creation

At startup, the host calls EF Core `EnsureCreatedAsync`, then:

- inserts missing expected clients, environments, roles, incidents, and principals;
- validates existing reference records against the exact synthetic dataset; and
- fails startup on conflicting or unexpected reference records.

Workflow records are preserved between runs when the same SQLite file is used.

This reference implementation uses `EnsureCreatedAsync` rather than migrations.
After changing the EF model—including the request-intake table simplification—delete
and recreate the disposable local synthetic database before starting the updated
host. Existing local schema files are not upgraded in place.

The default relative connection string creates `governed-access.db` in the host
process working directory. When location matters, use an explicit connection string
rather than relying on the current directory.

### Resetting synthetic state

Stop the host before resetting the database. If the host was started from the
repository root with the default connection string:

```powershell
Remove-Item -LiteralPath .\governed-access.db
```

Start the host again to recreate and seed the database.

Before deleting, confirm the resolved file is the local synthetic database you intend
to reset. Do not use a wildcard or recursive delete. This operation permanently
removes local requests, decisions, grants, and audit history from that file.

Integration tests do not use the development database. Their
`WebApplicationFactory` creates isolated in-memory SQLite state.

## Common commands

### Restore

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

Use `npm ci`, not `npm install`, for reproducible lockfile-based restoration.

### Build

```powershell
npm run build --prefix src/GovernedAccess.Web/ClientApp
dotnet build ProductionAccessRequestAssistant.sln --no-restore
```

The .NET build also integrates the frontend build, so running both explicitly is
useful for diagnosing which toolchain failed but is not required for every edit.

### Test

```powershell
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --filter "TestLevel!=FullHost"
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --filter "TestLevel=FullHost"
npm run test:run --prefix src/GovernedAccess.Web/ClientApp
```

The first two commands are the fast unit/component loop. The third command starts the
retained complete ASP.NET Core host scenarios. Together, the three sequential .NET
commands are the complete backend suite. Run restore and build first when using
`--no-build`.

The complete suite and additional focused-test commands are in the
[testing strategy](testing-strategy.md).

### Publish

```powershell
dotnet publish src/GovernedAccess.Web/GovernedAccess.Web.csproj -c Release
```

Publish builds and includes the React static assets. The repository does not define a
production hosting target or deployment procedure.

## Project navigation

| Area | Location |
|---|---|
| Domain and workflow rules | `src/GovernedAccess.Core/Domain` |
| Application services and outcomes | `src/GovernedAccess.Core/Application` |
| Provider-neutral ports | `src/GovernedAccess.Core/Ports` |
| MCP endpoint and tool adapters | `src/GovernedAccess.Mcp` |
| Host composition | `src/GovernedAccess.Web/Program.cs` |
| HTTP controllers | `src/GovernedAccess.Web/Controllers` |
| Authentication and antiforgery | `src/GovernedAccess.Web/Authentication`, `Security` |
| EF Core and seed data | `src/GovernedAccess.Web/Persistence` |
| AI adapter and deterministic client | `src/GovernedAccess.Web/Ai` |
| Synthetic provider | `src/GovernedAccess.Web/Provisioning` |
| React application | `src/GovernedAccess.Web/ClientApp/src` |
| Unit tests | `tests/GovernedAccess.UnitTests` |
| Integration tests | `tests/GovernedAccess.IntegrationTests` |
| Explicit concurrency tests | `tests/GovernedAccess.ConcurrencyTests` |

## Troubleshooting

### Sign-in appears to succeed but the session remains anonymous

Confirm that the browser is using `https://localhost:7251`, not the HTTP address. The
authentication and antiforgery cookies are Secure.

### Browser reports an untrusted development certificate

Run:

```powershell
dotnet dev-certs https --trust
```

Then restart the host and browser.

### Vite cannot reach the API

Confirm the ASP.NET host is running on `https://localhost:7251`. If it is not, set
`VITE_API_PROXY_TARGET` to the actual HTTPS origin before starting Vite.

### Startup reports a synthetic dataset conflict

The local database contains a reference record that does not match the fixed seed
definition. For disposable local state, stop the host, verify the database path, and
reset that single SQLite file. Do not modify seed reference rows manually.

### Frontend build uses the wrong Node version

Use Node 24. The package engine intentionally rejects a different major-version
assumption.

### A test appears to use the development database

Integration tests should use `GovernedAccessWebFactory` and its isolated in-memory
SQLite connection. Do not point tests at `governed-access.db`.

## Related documentation

- [README](../README.md)
- [Microsoft Teams local integration](teams-local-integration.md)
- [As-built architecture](architecture.md)
- [Security and trust model](security-model.md)
- [Testing strategy](testing-strategy.md)
- [Quickstart validation guide](../specs/001-governed-production-access/quickstart.md)
- [UI API contract](../specs/001-governed-production-access/contracts/ui-api.md)
- [MCP tool contract](../specs/001-governed-production-access/contracts/mcp-tools.json)

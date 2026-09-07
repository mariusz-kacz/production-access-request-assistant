# Local Development

- **Status**: Current
- **Last reviewed**: 2026-08-28

## Paths through the repository

| Goal | Start here |
|---|---|
| Build and run automated tests | [Credential-free validation](#credential-free-validation) |
| Run the application with real Teams transport | [Teams quickstart](teams-quickstart.md) |
| Run the fixed live-model dataset without Teams | [Live-model evaluation](live-model-evaluation.md) |
| Work on the React client | [React hot reload](#react-hot-reload) |

Building and testing require no Microsoft 365 tenant, Azure subscription, tunnel,
model credentials, or external production system. Starting the normal host requires
valid Teams bot and tenant configuration; checked-in blank values intentionally fail
closed. The Teams scripts create and load that local configuration.

## Prerequisites

- .NET 10 SDK;
- Node.js 24 with npm;
- PowerShell 7; and
- a browser that trusts the ASP.NET Core HTTPS development certificate when running
  the Web application.

Confirm the tools:

```powershell
dotnet --version
node --version
npm --version
```

Trust the development certificate when needed:

```powershell
dotnet dev-certs https --trust
```

## Credential-free validation

Restore dependencies from the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

Run the backend gates sequentially:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. If it times
out, stop only the runner process tree created by that command before another run.

Run frontend tests separately:

```powershell
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

All automated tests use deterministic clients. Detailed test placement and acceptance
coverage are in the [testing strategy](testing-strategy.md).

## Running the application

Complete the one-time Teams setup, start the persistent tunnel, and launch the host
through the focused scripts in the [Teams quickstart](teams-quickstart.md). The app
starts at `https://localhost:7251`; use HTTPS because authentication and antiforgery
cookies are Secure.

The checked-in request-preparation profile is `Deterministic`. It returns a stable
fixed Client Alpha candidate for transport, confirmation, approval, and provisioning
exercises. It does not measure natural-language interpretation quality.

The optional `FoundryResponses` profile uses `DefaultAzureCredential` and an explicitly
configured Azure AI Foundry endpoint and deployment. The Teams start script accepts
those values after `az login`. Invalid live configuration or provider failure fails
closed and never falls back to the deterministic client.

## Live-model evaluation

The Web executable also has an evaluation-only mode that needs no Teams registration
or tunnel. It requires an authorized Foundry deployment and runs the real intake and
loopback MCP path without exposing confirmation or workflow actions.

```powershell
az login
$env:RequestPreparationModel__ExecutionProfile = "FoundryResponses"
$env:RequestPreparationModel__FoundryResponses__Endpoint = "https://<project>.services.ai.azure.com/openai/v1"
$env:RequestPreparationModel__FoundryResponses__DeploymentName = "<deployment-name>"
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model
```

Live evaluation uses the evaluation-specific `LiveModelEvaluation:CumulativeTimeout`
setting, checked in as two minutes and bounded to a maximum of five minutes. Override
it through configuration when needed; there is no timeout command option. The normal
application independently uses the standard request-preparation value, checked in as
30 seconds, and ignores the evaluation-specific setting.

Use [live-model evaluation](live-model-evaluation.md) for the fixed inventory, exit
codes, artifact interpretation, and cleanup. An explicit `--output` may select any
resolvable directory; only the default artifact location is ignored by repository
rules. `--variation <id>` runs one exact variation for diagnosis, but its artifacts
are explicitly not promotion evidence.

## Local MCP surface

The normal host exposes Streamable HTTP at `/mcp`. The local launch profile binds
`https://localhost:7251` and `http://localhost:5136`; the configured trusted Web origin
determines the server-side MCP client address.

The endpoint advertises exactly:

- `search_production_environments`, with one structured `query` and a complete bound
  of at most five eligible environment/client projections;
- `get_production_environment`, with one exact `environmentId`;
- `get_environment_roles`, with one exact eligible `environmentId`; and
- `get_incident`, with one exact `incidentId`.

Environment search and exact lookup return client context but no roles; roles are
owned by the environment-scoped entitlement tool. The exact wire schemas are in the
[MCP contract](contracts/mcp-tools.json).

## React hot reload

Start the configured ASP.NET Core host, then run Vite in another terminal:

```powershell
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

Vite proxies `/api` to `https://localhost:7251`. Override it when the host uses another
address:

```powershell
$env:VITE_API_PROXY_TARGET = "https://localhost:7443"
npm run dev --prefix src/GovernedAccess.Web/ClientApp
```

The browser never calls MCP or the provisioner directly.

## Configuration

ASP.NET Core environment variables replace `:` with `__`.

| Key | Checked-in value | Purpose |
|---|---|---|
| `ConnectionStrings:ReferenceAuthority` | `Data Source=governed-access-reference.db` | Reference-only SQLite database |
| `ConnectionStrings:WorkflowPersistence` | `Data Source=governed-access-workflow.db` | Preparation and downstream workflow SQLite database |
| `RequestPreparationModel:ExecutionProfile` | `Deterministic` | `Deterministic` or `FoundryResponses` |
| `RequestPreparationModel:FoundryResponses:Endpoint` | empty | Foundry project URL ending in `/openai/v1` |
| `RequestPreparationModel:FoundryResponses:DeploymentName` | empty | Foundry deployment name |
| `TeamsAccessRequest:AllowedTenantId` | empty, fail closed | Accepted Teams tenant |
| `TeamsAccessRequest:TrustedWebBaseUri` | empty in production settings | HTTPS origin used for the co-hosted MCP client |
| `TeamsAccessRequest:RequestTimeout` | `00:01:40` | Total Teams activity deadline |
| `TeamsAccessRequest:PreparationLifetime` | `00:30:00` | Ready-card confirmation window |

The Teams start script supplies the bot client ID, audience, tenant, secret, and tunnel
origin from ignored local state. The bot credential is stored outside the repository
and must not be printed or copied into `appsettings*.json`.

### Routed-assistant configuration baseline

`RoutedAssistant` supplies the SDK/configuration foundation for the
[approved router/policy target](../SPEC-router-policy-evolution.md). Runtime routing,
retrieval, and policy answers are not enabled by these settings. Each component has
separate typed options, validated when that component is resolved; an invalid or
unconfigured component throws a safe configuration error without substituting another
component or the Access Request client.

| Section under `RoutedAssistant` | Provider coordinates | Checked-in deadline / hard cap | Output bounds |
|---|---|---|---|
| `Router` | `Endpoint`, `DeploymentName` | 8 / 8 seconds | `MaximumOutputTokens`: 100 maximum |
| `PolicyAdvisor` | `Endpoint`, `DeploymentName` | 30 / 30 seconds | `MaximumOutputTokens`: 800 maximum |
| `Retrieval` | `Endpoint`, `IndexName` | 10 / 30 seconds | `MaximumChunks`: 3; `MaximumApproximateTokens`: 1,500 maximum |
| `Embedding` | `Endpoint`, `DeploymentName` | 10 / 30 seconds | `Dimensions`: explicitly select 1–3,072 |
| `Turn` | none | 70 / 70 seconds | none |

All deadlines use the `Timeout` key and positive `TimeSpan` values. Numeric limits are
positive and may be reduced; missing, malformed, oversized, unknown, or nested settings
fail validation. Retrieval and embedding use conservative ten-second defaults with
caps within the policy budget; their eventual callers must also enforce the enclosing
policy/turn deadlines. The existing `RequestPreparationModel` and
`RequestPreparationAgent:Limits` settings continue to own Access Request configuration,
including its current 30-second timeout, 60-second hard cap, and existing proposal
schema.

Provider coordinates are empty and embedding dimensions are zero in checked-in
configuration, deliberately leaving those components unavailable until configured.
Model and embedding endpoints use the existing trusted HTTPS
`*.services.ai.azure.com/openai/v1` shape. Search requires an HTTPS
`*.search.windows.net` origin and an index name following
[Azure Search naming rules](https://learn.microsoft.com/en-us/rest/api/searchservice/naming-rules).
These sections accept
no credentials, tool lists, retrieval filters, fallback profiles, or retry policies.
Deployment/index names are bounded to 128 characters. Supply deployment coordinates
through local configuration or environment variables; do not add secrets to tracked
settings.

The compatibility baseline retains MAF 1.15.0, Microsoft.Extensions.AI and its OpenAI
adapter 10.7.0, and OpenAI 2.11.0. It adds
[Azure.Search.Documents 11.7.0](https://www.nuget.org/packages/Azure.Search.Documents/11.7.0),
[Microsoft evaluation Quality 10.7.0](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Quality/10.7.0),
and [Reporting 10.7.0](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Reporting/10.7.0).
Existing packages supply embeddings and MAF OpenTelemetry; no extra MCP, embedding, or
telemetry package is needed. The credential-free compatibility tests exercise
pre-invocation `TextSearchProvider` with zero internal recent-message memory,
policy Retrieval/Groundedness/Relevance on their 1–5 scale, Microsoft JSON reporting,
and embedding-to-hybrid-query serialization.

The completed Task 2 source also contains an initial declaration-only Intent Resolution
probe with a method-local experimental `AIEVAL001` opt-in. The
[2026-09-07 target amendment](adr/0015-refine-router-policy-target-contracts.md)
supersedes that requirement: router evaluation now requires only direct exact
route/context checks in Microsoft evaluation/reporting, without a model judge. This
documentation change does not remove the existing probe or alter runtime behavior;
Task 12 owns its scoped cleanup while retaining applicable policy/reporting coverage.
Live model quality and Azure connectivity remain operator gates for later tasks.

## Local databases

Reference Authority and Workflow Persistence own separate SQLite files, EF Core
contexts, migrations, seeders, and exact final table inventories. Startup accepts an
empty file or the exact current schema, applies the owning migration, and validates or
creates only the owning synthetic records. Workflow rows remain between runs.

There is no supported in-place upgrade, compatibility adapter, row copy, or unified
schema path for local data created by an older or transitional implementation. Startup
fails with bounded reset guidance and never deletes either configured file
automatically.

To reset the checked-in defaults, first stop every host that uses the files, then run
the focused backup helper from the repository root:

```powershell
.\scripts\backup-local-database.ps1
```

The helper moves only the two exact default database filenames and their `-wal`/`-shm`
sidecars from the repository root into one ignored `db-backup-<timestamp>` directory.
It does not interpret connection-string overrides. When either connection string is
overridden, verify the exact configured paths and explicitly preserve or remove both
files and their sidecars; do not use a broad directory, wildcard, or unresolved
environment variable as a delete/move target. The next startup migrates and seeds two
fresh databases. This is a disposable-local-data reset, not an upgrade procedure.

## Troubleshooting

| Symptom | Action |
|---|---|
| Startup reports missing Teams options | Complete Teams setup and launch through `start-app.ps1`; blank checked-in bot values fail closed. |
| Browser stays anonymous | Use HTTPS, not the HTTP MCP/tunnel binding. |
| Browser rejects the certificate | Run `dotnet dev-certs https --trust`, then restart the host and browser. |
| Vite cannot reach the API | Confirm the configured HTTPS host is running or set `VITE_API_PROXY_TARGET`. |
| Incompatible database schema | Stop the host. For the checked-in paths, run `backup-local-database.ps1`; for overrides, verify and explicitly preserve or remove both configured files and their sidecars. Then restart with two fresh databases. |
| Frontend rejects Node | Use Node 24. |
| Live model is unavailable | Check the endpoint, deployment, `az account show`, and Foundry role assignment. |

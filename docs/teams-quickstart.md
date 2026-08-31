# Teams Quickstart

- **Status**: Current
- **Last reviewed**: 2026-08-28
- **Audience**: Developers running the application from a personal Teams chat

Setup is performed once. Normal daily use requires the tunnel and application commands
in step 4.

## Prerequisites

- .NET 10 and Node.js 24 dependencies restored as described in
  [local development](local-development.md);
- a Microsoft 365 development tenant whose user can upload custom Teams apps;
- Teams Developer CLI 3.x;
- Dev Tunnels CLI; and
- for the live model only, an Azure AI Foundry deployment and permission to invoke it.

Install the CLIs when needed:

```powershell
npm install -g @microsoft/teams.cli
winget install Microsoft.devtunnel
```

`teams login` manages Teams registration and bot transport. `az login` is separate and
is used only for the optional Foundry model.

## 1. Sign in to Teams

```powershell
teams login
devtunnel user login
teams status
```

Confirm that `teams status` shows the intended tenant and that custom-app upload is
enabled.

## 2. Create the integration once

From the repository root:

```powershell
.\scripts\teams\setup.ps1 -ExpectedTenantId "<microsoft-365-tenant-guid>"
```

The script creates a persistent tunnel and Teams-managed bot, then prints a personal-
scope install link. Open it as a user in the same tenant and add the app.

It stores:

- non-secret IDs in ignored `.teams-dev.local.json`; and
- the bot credential outside the repository in
  `%LOCALAPPDATA%\GovernedAccess\teams-local.env`.

Do not rerun setup during normal daily use.

## 3. Prepare the live model

Skip this step for the default deterministic profile.

Grant the developer identity `Cognitive Services OpenAI User` on the resource that
contains the deployment, then verify the selected Azure account:

```powershell
az login
az account show
```

Keep the Foundry project endpoint ending in `/openai/v1` and deployment name available
for step 4. The application uses `DefaultAzureCredential`; no model API key belongs in
source or application settings.

## 4. Start the integration

Open two PowerShell terminals at the repository root.

Terminal 1:

```powershell
.\scripts\teams\start-tunnel.ps1
```

Terminal 2, deterministic profile:

```powershell
.\scripts\teams\start-app.ps1
```

Or use the live Foundry profile:

```powershell
.\scripts\teams\start-app.ps1 -ModelProfile FoundryResponses -FoundryEndpoint "https://<project>.services.ai.azure.com/openai/v1" -DeploymentName "<deployment-name>"
```

The app script loads the bot credential without printing it, updates the registered bot
endpoint to the active tunnel, selects the model profile, and starts ASP.NET Core.

## 5. Check the protected route

With both processes running:

```powershell
.\scripts\teams\check.ps1
```

Expected output:

```text
Local endpoint status:  401
Public endpoint status: 401
Tunnel and protected bot route are reachable.
```

Both `401` responses are correct for unauthenticated probes. They show that the tunnel
reaches the protected endpoint without bypassing bearer authentication.

## 6. Send a request

In the bot's personal chat, send:

```text
I need read-only access to Client Alpha production in Europe to inspect logs and configuration while diagnosing INC-1042.
```

The assistant immediately sends a transient typing activity and refreshes it every two
seconds while the message turn is running. Teams clears the indicator when the
assistant sends its text or card response. This is presentation feedback only; it is
not persisted workflow evidence and does not imply authorization or execution.

The deterministic profile returns its stable Client Alpha candidate. The live profile
uses the bounded environment and exact incident tools before Core validates the
proposal. In both modes:

1. the assistant shows a ready card;
2. no request exists before **Confirm and submit**;
3. confirmation creates one immutable `AwaitingBusinessApproval` request; and
4. the submitted card becomes non-actionable and links to the Web register.

Continue the business and DevOps decisions at `https://localhost:7251`. Model output
cannot confirm, approve, or provision, and live-provider failure does not fall back to
the deterministic profile.

## 7. Reset an unsubmitted intake

Send `/new` by itself to abandon the active collecting or ready intake. Matching is
trimmed and case-insensitive; `/new please` remains ordinary requester text.

Reset calls neither the model nor MCP, creates no request, invalidates an old ready
card, and leaves submitted requests unchanged. The same atomic workflow commit creates
a clean collecting preparation with a new ID; the next normal message uses that
persisted preparation with no provider conversation history.

## Stop or remove the integration

For daily use, press `Ctrl+C` in the app and tunnel terminals. Keep the ignored local
state for the next run. Starting the app without `-ModelProfile` selects the
deterministic profile and clears inherited Foundry settings.

Permanent cloud-resource removal is manual and destructive. Follow the
[advanced reference](teams-advanced-reference.md#permanent-cleanup) when the integration
is no longer needed.

## Troubleshooting

| Symptom | Action |
|---|---|
| Setup reports disabled sideloading | Enable custom-app upload, wait for policy propagation, then rerun `teams status`. |
| Bot does not reply | Keep both long-running terminals open and run `check.ps1`. |
| App says the tunnel is not hosted | Start `start-tunnel.ps1` first. |
| Live model is unavailable | Verify `az account show`, Foundry role assignment, endpoint, and deployment name. |
| Startup reports an incompatible database schema | Stop the app and follow the explicit two-database reset policy in [local development](local-development.md#local-databases). |
| Teams diagnostics report the endpoint unreachable | Prefer `check.ps1`; two `401` results are expected. |

Use synthetic request details only. Do not commit local Teams state, credentials,
databases, or generated packages. Registration ownership alternatives, adoption,
secret rotation, diagnostics, manual package inspection, and permanent cleanup are in
the [Teams advanced reference](teams-advanced-reference.md).

# Teams quickstart

- **Status**: Current
- **Last reviewed**: 2026-08-04
- **Audience**: Developers running the application from a real personal Teams chat

Use this page in order. The normal daily workflow is only two long-running commands
and one check.

## What you need

- .NET 10 and Node.js 24, already restored as shown in
  [Local development](local-development.md).
- A Microsoft 365 development tenant where your user can upload custom Teams apps.
- Teams Developer CLI 3.x and Dev Tunnels CLI.
- For the live model only: an Azure AI Foundry project endpoint, a deployed model,
  and access to invoke that deployment.

Install the CLIs if needed:

```powershell
npm install -g @microsoft/teams.cli
winget install Microsoft.devtunnel
```

There are two independent sign-ins:

| Sign-in | Used for |
|---|---|
| `teams login` | Teams app registration and bot transport |
| `az login` | Live Foundry model calls |

They may belong to different tenants. The application never copies one identity into
the other.

## 1. Sign in to Teams

```powershell
teams login
devtunnel user login
teams status
```

Before continuing, check that `teams status` shows the intended Microsoft 365 tenant
and that sideloading is enabled.

## 2. Create the Teams integration once

From the repository root:

```powershell
.\scripts\teams\setup.ps1 -ExpectedTenantId "<microsoft-365-tenant-guid>"
```

The script creates a persistent tunnel and Teams-managed bot, then prints an install
link. Open that link as a user in the same Microsoft 365 tenant and add the app in
personal scope.

The generated files are:

- `.teams-dev.local.json`: ignored, non-secret IDs used on later runs;
- `%LOCALAPPDATA%\GovernedAccess\teams-local.env`: bot credential outside the repo.

Do not rerun setup on later days. Use the daily commands below.

## 3. Sign in to Azure for the live model

Skip this section when using the deterministic model.

Grant your developer identity the `Cognitive Services OpenAI User` role on the
resource that contains the deployment. Then sign in and verify the selected account:

```powershell
az login
az account show
```

Collect these two non-secret values from Foundry:

- project inference base URL, ending in `/openai/v1`, for example
  `https://<project>.services.ai.azure.com/openai/v1`;
- deployment name, for example `governed-access-chat`.

The app uses `DefaultAzureCredential`; no model API key belongs in settings or source
control.

## 4. Start the integration each day

Open two PowerShell terminals at the repository root.

Terminal 1 — keep the tunnel running:

```powershell
.\scripts\teams\start-tunnel.ps1
```

Terminal 2 — choose one model mode.

Stable deterministic model:

```powershell
.\scripts\teams\start-app.ps1
```

Live Foundry model:

```powershell
.\scripts\teams\start-app.ps1 -ModelProfile FoundryResponses -FoundryEndpoint "https://<project>.services.ai.azure.com/openai/v1" -DeploymentName "<deployment-name>"
```

The start script loads the bot secret without printing it, updates the registered
bot endpoint to the current tunnel URL, selects the requested model profile, and
starts ASP.NET Core.

## 5. Check the route

With both processes running, use a third terminal:

```powershell
.\scripts\teams\check.ps1
```

Success looks like this:

```text
Local endpoint status:  401
Public endpoint status: 401
Tunnel and protected bot route are reachable.
```

`401` is correct for these unauthenticated probes. It proves that the public tunnel
reaches the protected bot endpoint without bypassing bearer-token authentication.

## 6. Send one request

In the bot's personal Teams chat, send:

```text
I need ProductionReadOnly access to PROD-ALPHA-EU to investigate INC-1042. I need to inspect production logs and configuration to diagnose the active incident.
```

Expected flow:

1. The assistant shows a confirmation card for Client Alpha and an eight-hour grant.
2. No access request exists until you select **Confirm and submit**.
3. Confirmation creates one immutable request in `AwaitingBusinessApproval`.
4. Open the returned HTTPS link to continue the human approval demo.

The live model may interpret text and call only the three read-only MCP tools. Its
output is still schema-validated and checked against authoritative local data. It
cannot approve or provision access. The existing 100-second Teams request timeout is
the single overall model/MCP deadline. If the selected live profile fails, the turn
fails closed and never falls back to the deterministic client.

## 7. Reset an unsubmitted preparation

Send `/new` by itself to abandon the active preparation in this personal
conversation. Matching is trimmed and case-insensitive, so `/NEW` and `  /new  ` also
match; `/new please` is ordinary requester text.

The reset calls neither the model nor MCP. It terminally clears an active collecting
or ready candidate, invalidates an old ready card, creates no access request, and
leaves every submitted request and approval workflow unchanged. The next ordinary
message creates a new intake ID with separate model history.

## Stop and clean up

For normal daily use, press `Ctrl+C` in the app and tunnel terminals. Keep the local
state and bot credential for the next run.

To stop using the live model, simply start the app without `-ModelProfile` next time.
The deterministic profile is the default, and the script clears inherited Foundry
settings before launch.

Cloud-resource deletion is manual because it is destructive. Follow
[permanent cleanup procedure](teams-advanced-reference.md#permanent-cleanup) when removing the
integration permanently.

## Less common commands

| Task | Command |
|---|---|
| Adopt an existing app and tunnel | `.\scripts\teams\adopt.ps1` |
| Run Teams CLI diagnostics | `.\scripts\teams\doctor.ps1` |
| Rotate the bot secret | `.\scripts\teams\rotate-secret.ps1` |
| Preserve an old local database before a schema refresh | `.\scripts\backup-local-database.ps1` |

Run `Get-Help <script> -Detailed` or open the small script to see its parameters.
The old `scripts\teams-local.ps1` command remains as a compatibility dispatcher, but
new instructions use the focused scripts above.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Setup says sideloading is disabled | Enable custom-app upload for the Teams user, wait for policy propagation, then rerun `teams status`. |
| Bot never replies | Confirm both long-running terminals are open, then run `check.ps1`. |
| `start-app.ps1` says the tunnel is not hosted | Start `start-tunnel.ps1` first and keep it running. |
| Live model returns unavailable | Verify `az account show`, Foundry role assignment, endpoint suffix/path, and deployment name. The app intentionally does not fall back to the deterministic model. |
| Request fails after an EF schema change | Stop the app, run `backup-local-database.ps1`, then restart it. |
| `teams app doctor` calls the endpoint unreachable | Prefer `check.ps1`; two `401` results are correct for this protected endpoint. |

## Security boundaries

- The bot credential and Azure sign-in token are never model inputs.
- The anonymous development tunnel exposes network reachability, not anonymous bot
  access; `/api/messages` still validates the Bot Framework token, tenant, channel,
  actor, and conversation.
- Use synthetic data only. Never paste real production-access details into the demo.
- Do not commit `.teams-dev.local.json`, credentials, local databases, or generated
  Teams packages.

For tenant administration, alternative registration ownership, manual package
inspection, and permanent cleanup, see the
[Teams advanced reference](teams-advanced-reference.md).

Official references: [Teams CLI registration](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/get-started/quickstart-register),
[local .NET authentication with `DefaultAzureCredential`](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/local-development-dev-accounts),
and [Foundry Responses API](https://learn.microsoft.com/en-us/azure/foundry/agents/quickstarts/responses-api).

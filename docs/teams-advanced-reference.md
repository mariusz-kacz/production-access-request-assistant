# Teams advanced reference

- **Status**: Current
- **Last reviewed**: 2026-08-28
- **Audience**: Developers administering or permanently removing the local Teams demo

Do not start here for normal setup or daily use. Follow the
[Teams quickstart](teams-quickstart.md), which is the single source for installing the
tools, creating the recommended integration, starting the tunnel and app, selecting
the model, checking the route, and sending a test request.

This reference covers the less common administrative work:

- Microsoft 365 tenant policy;
- alternative bot-registration ownership;
- credential rotation;
- manual Teams package inspection and sideloading;
- registration diagnostics; and
- permanent cleanup.

All resources are development-only and must carry synthetic data.

## Microsoft 365 development tenant

Use a dedicated test user in an eligible Microsoft 365 developer sandbox. Do not use
a client or production tenant.

In the Teams admin center, enable **Upload custom apps** for that user. The tenant-wide
path is **Teams apps > Setup policies > Global > Upload custom apps**, or use a
dedicated policy assigned to the test user. Policy changes may take time to propagate.

`teams status` must show the intended tenant and `Sideloading: enabled` before running
the quickstart setup.

Developer sandbox availability is eligibility-based, and an E5 developer sandbox
does not include an Azure subscription. See the
[Developer Program FAQ](https://learn.microsoft.com/en-us/office/developer-program/microsoft-365-developer-program-faq)
and [Teams custom-app policy guidance](https://learn.microsoft.com/en-us/microsoftteams/teams-custom-app-policies-and-settings).

## Registration ownership

### Recommended: Teams-managed

The quickstart creates a Teams-managed app and multitenant bot registration. This is
the default for the E5-only development environment because it does not require an
Azure subscription.

The ignored `.teams-dev.local.json` file records the tunnel ID, local port, Teams app
ID, bot client ID, tenant ID, and credential-file path. Preserve it between sessions;
without it, the helper cannot update the existing registration and may tempt an
operator to create duplicates.

If the cloud registration exists but the local state file was lost, recover it with
the focused adoption script:

```powershell
.\scripts\teams\adopt.ps1 -TunnelId "<persistent-tunnel-id>" -TeamsAppId "<teams-app-id>"
```

By default, the script reads
`%LOCALAPPDATA%\GovernedAccess\teams-local.env` and obtains the expected tenant from
its `TENANT_ID`. Use `-CredentialFile` or `-ExpectedTenantId` only to override those
defaults.

### Optional: Azure-managed

Use an Azure-managed bot only when an organization requires Azure resource ownership,
OAuth/SSO, Microsoft Graph, or Azure-specific controls. It is a different ownership
model and is intentionally not automated by this repository.

Microsoft's Teams CLI supports registration with an explicit subscription and
resource group:

```powershell
teams app create --azure --subscription "<subscription-id>" --resource-group "<resource-group>" --name "governed-access-dev" --endpoint "https://<tunnel-host>/api/messages" --env "<credential-file>"
```

Follow Microsoft's
[Azure-managed Teams configuration](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/teams/azure-configuration)
for the required permissions and resources. Do not create both ownership models
unless duplicate registrations are intentional and tracked.

## Bot credential storage and rotation

The recommended setup stores `CLIENT_ID`, `CLIENT_SECRET`, and `TENANT_ID` outside the
repository at:

```text
%LOCALAPPDATA%\GovernedAccess\teams-local.env
```

The application start script loads the file without printing the secret. Never copy
the secret into `appsettings*.json`, `.teams-dev.local.json`, the Teams package, logs,
screenshots, chat, or source control.

To create a replacement credential:

```powershell
.\scripts\teams\rotate-secret.ps1
```

Restart the app and verify a Teams reply. Then open the exact Microsoft Entra app
registration and remove the older secret under **Certificates & secrets**. Creating a
replacement does not revoke the old secret automatically.

## Manual package inspection and sideloading

The install link printed by quickstart setup is the normal installation path. Use the
steps below only to inspect the package or exercise manual sideloading.

The source template is `src/GovernedAccess.Web/appPackage/manifest.json`. Its app and
bot identifiers intentionally remain placeholders in source control.

Create a template ZIP from the explicit allowlist:

```powershell
$packageSource = (Resolve-Path "src/GovernedAccess.Web/appPackage").Path
$packageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "GovernedAccessTeams"
$templatePackagePath = Join-Path $packageDirectory "governed-access-teams-template.zip"
$sourceFiles = @((Join-Path $packageSource "manifest.json"), (Join-Path $packageSource "color.png"), (Join-Path $packageSource "outline.png"))
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Compress-Archive -LiteralPath $sourceFiles -DestinationPath $templatePackagePath -Force
```

That template ZIP is not ready to install because its manifest still has
`${{APP_ID}}` and `${{BOT_ID}}` placeholders. Download the resolved package from the
registered app:

```powershell
$packagePath = Join-Path $packageDirectory "governed-access-teams.zip"
teams app package download "<teams-app-id>" --output $packagePath
```

Inspect the resolved ZIP with an exact allowlist check:

```powershell
$expectedEntries = @("manifest.json", "color.png", "outline.png")
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
    $differences = @(Compare-Object -ReferenceObject ($expectedEntries | Sort-Object) -DifferenceObject ($actualEntries | Sort-Object))
    if ($actualEntries.Count -ne $expectedEntries.Count -or $differences.Count -ne 0) { throw "Unexpected Teams package entries: $($actualEntries -join ', ')" }
    $actualEntries
}
finally {
    $archive.Dispose()
}
```

The output must contain exactly these ZIP-root files:

```text
manifest.json
color.png
outline.png
```

The resolved manifest must use the Teams app ID for `id`, the bot client ID for
`bots[0].botId`, and personal scope only. Do not commit the package or its resolved
identifiers.

To sideload manually, sign in to Teams as the test user, open
**Apps > Manage your apps > Upload an app > Upload a custom app**, select the resolved
ZIP, and choose **Add** or **Open**. If upload is unavailable, recheck the assigned
custom-app policy and signed-in tenant. See Microsoft's
[custom-app upload guide](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/apps-upload).

## Registration diagnostics

Run this only after the tunnel is hosted:

```powershell
.\scripts\teams\doctor.ps1
```

Some Teams CLI versions call an authenticated endpoint unreachable when an
unauthenticated probe receives `401`. The quickstart `check.ps1` command is the
authoritative transport check for this application: both local and public results
should be `401`.

## Permanent cleanup

Cleanup is manual because it deletes cloud resources. First record the exact IDs from
`.teams-dev.local.json` and confirm the active tenant.

1. Stop the application and tunnel processes.
2. In Teams, open **Apps > Manage your apps** and remove the demo app for the test
   user.
3. Remove the app from the Teams Developer Portal or tenant catalog if it was
   published there. Removing a user's installation alone does not delete its
   registration.
4. If this demo owns the corresponding Microsoft Entra app registration, verify its
   client ID and tenant, then delete that exact registration. Do not delete a shared
   registration.
5. Verify the persistent tunnel before deleting it:

   ```powershell
   devtunnel show "<tunnel-id>"
   devtunnel delete "<tunnel-id>"
   ```

6. Delete the credential file named by `CredentialFile`, `.teams-dev.local.json`, and
   the generated package directory only after cloud cleanup. Removing local state
   first makes the cloud resources harder to identify.
7. On a shared workstation, optionally sign out:

   ```powershell
   teams logout
   devtunnel user logout
   az logout
   ```

Microsoft documents user-side removal in the
[custom-app upload guide](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/apps-upload)
and app-registration deletion and recovery in the
[Microsoft Entra removal guide](https://learn.microsoft.com/en-us/entra/identity-platform/howto-remove-app).

## Live-model operating boundaries

The host selects exactly one process-wide request-preparation profile. Checked-in and
normal daily startup use `Deterministic`; live startup must explicitly select
`FoundryResponses` and supply the trusted `/openai/v1` endpoint and deployment name.
The application obtains a token from `DefaultAzureCredential`; do not add an API key,
token, tenant override, or client secret to model settings.

The selected live provider uses the Teams endpoint's existing 100-second overall
deadline. Invalid configuration, missing Entra authorization, provider failure, or
timeout fails closed and never falls back to deterministic responses. The provider
receives exactly four read-only MCP tools: bounded
`search_production_environments`, exact `get_production_environment`,
environment-scoped `get_environment_roles`, and exact `get_incident`. It cannot
confirm, approve, provision, or change workflow state. Every exact environment outcome
prevents catalog discovery later in the turn; exact `NotFound` remains unresolved
without fuzzy correction. Timeout, cancellation, invalid input, unavailability, or
malformed results retain their safe outcomes.

An exact `/new` message is handled before the provider boundary. It resets only the
authenticated conversation's active unsubmitted preparation, invokes no model or MCP
tool, and cannot alter an already submitted request. Use the quickstart for the
requester-facing reset walkthrough.

## Readiness checklist

- The Teams CLI is signed into the intended development tenant and sideloading is
  enabled.
- One registration ownership model is used and its Teams app, bot client, tenant, and
  tunnel IDs are recorded.
- The credential file remains outside the repository and has never been printed or
  committed.
- The quickstart route check returns `401` locally and publicly.
- Any manually downloaded package contains only the three required root files and is
  installed in personal scope.
- All messages and database records use synthetic data.
- The cleanup owner knows which cloud and local resources belong to this demo.

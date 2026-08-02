# Microsoft Teams Demo Guide

- **Status**: Current
- **Last reviewed**: 2026-08-03
- **Audience**: Developers preparing or presenting the real Teams transport

This is the end-to-end setup and teardown guide for the portfolio demo. The normal
local application and automated tests need no Microsoft 365 account, Azure
subscription, tunnel, or live model. The resources below are development-only and
must carry synthetic data only.

The recommended path uses one Microsoft 365 E5 developer sandbox, a Teams-managed
bot registration, one persistent Dev Tunnel, and the local ASP.NET Core host. It
demonstrates a real Teams transport while keeping approvals and provisioning outside
the model boundary.

## 1. Prepare a Microsoft 365 developer tenant

1. Join the [Microsoft 365 Developer Program](https://learn.microsoft.com/en-us/office/developer-program/microsoft-365-developer-program-get-started)
   and, if the account is eligible, create an instant or configurable E5 developer
   sandbox.
2. Use a dedicated test user from that sandbox for the demo and sign in to Teams as
   that user. Do not use a client or production tenant.
3. In the Teams admin center, enable **Upload custom apps** for that test user. The
   current tenant-wide path is **Teams apps > Setup policies > Global > Upload
   custom apps**, or use a dedicated policy assigned to the user. Allow time for a
   policy change to propagate.

Developer sandbox availability is eligibility-based, the subscription is for
development only, and it can expire when it is not used for qualifying development
activity. An E5 developer sandbox does **not** include an Azure subscription. See the
[Developer Program FAQ](https://learn.microsoft.com/en-us/office/developer-program/microsoft-365-developer-program-faq)
and [Teams custom-app policy guidance](https://learn.microsoft.com/en-us/microsoftteams/teams-custom-app-policies-and-settings).

## 2. Install and authenticate the local tools

Install the application prerequisites from [Local Development](local-development.md),
then install the current Teams Developer CLI and Dev Tunnels CLI:

```powershell
npm install -g @microsoft/teams.cli
winget install Microsoft.devtunnel
teams --version
devtunnel --version
```

Authenticate both tools with the developer identity:

```powershell
teams login
devtunnel user login
teams status
```

`teams status` must show the intended tenant and `Sideloading: enabled`. Stop if the
tenant ID is not the E5 sandbox ID. Microsoft documents these checks in the
[Teams app registration quickstart](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-ai-library/teams/configuration/manual-configuration)
and the [Dev Tunnels quickstart](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/get-started).

## 3. Choose the bot-registration ownership model

### Recommended: Teams-managed registration

From the repository root, create the complete local integration once:

```powershell
.\scripts\teams-local.ps1 Fresh -ExpectedTenantId "<e5-sandbox-tenant-guid>" -AppName "governed-access-dev"
```

`Fresh` validates the signed-in tenant and sideloading policy, creates a persistent
Dev Tunnel for local port `5136`, and calls `teams app create --teams-managed`. The
Teams Developer CLI provisions the Teams app and multitenant bot registration,
registers the public `https://...devtunnels.ms/api/messages` endpoint, and prints an
install link. This path does not require an Azure subscription and is the appropriate
default for the E5-only portfolio environment.

The ignored `.teams-dev.local.json` file records only continuation metadata: tunnel
ID, local port, Teams app ID, bot client ID, tenant ID, and credential-file path.
Preserve it between demo sessions so `Run` can resolve the current public URL and
update the existing registration instead of creating duplicates.

### Optional: Azure-managed registration

Use an Azure-managed bot only when a separate Azure subscription is available and
an organization requires Azure resource ownership, OAuth/SSO, Microsoft Graph, or
Azure-specific controls. Current Microsoft guidance supports:

```powershell
teams app create --azure --subscription "<subscription-id>" --resource-group "<resource-group>" --name "governed-access-dev" --endpoint "https://<tunnel-host>/api/messages" --env "<credential-file>"
```

That is a different provisioning choice; the repository's `Fresh` action deliberately
does not automate it. Follow Microsoft's
[Azure-managed Teams configuration](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/teams/azure-configuration)
for the required Azure permissions and resources. Do not create both ownership
models for the same demo unless duplicate registrations are intentional and tracked.

## 4. Keep the bot secret outside the repository

By default, the helper asks the Teams CLI to write `CLIENT_ID`, `CLIENT_SECRET`, and
`TENANT_ID` to:

```text
%LOCALAPPDATA%\GovernedAccess\teams-local.env
```

The file is outside the repository. `Run` reads it into process environment variables
without printing the secret. Never copy the secret into `appsettings*.json`,
`.teams-dev.local.json`, the Teams app package, logs, screenshots, chat, or source
control. Restrict the credential file to the current Windows user and treat any copy
as a credential.

If the secret is disclosed or nearing expiry:

```powershell
.\scripts\teams-local.ps1 RotateSecret
```

Restart the application, verify the new credential, and then remove the old secret
from the exact Microsoft Entra app registration. Creating a replacement does not
revoke the previous secret.

## 5. Run the stable HTTPS endpoint

`Fresh` creates a named, persistent tunnel rather than a throwaway URL. The tunnel ID
and app registration survive process restarts; the public endpoint exists only while
the tunnel is hosted. Dev Tunnels are for development, not production.

Start two PowerShell terminals at the repository root.

Terminal 1:

```powershell
.\scripts\teams-local.ps1 Tunnel
```

Terminal 2:

```powershell
.\scripts\teams-local.ps1 Run
```

`Run` reloads the stored registration, resolves the current tunnel URL, synchronizes
the complete `/api/messages` endpoint, loads credentials into the host process, and
starts ASP.NET Core on `http://localhost:5136` and `https://localhost:7251`.

The tunnel is created with anonymous network reachability because the Bot Framework
service must call it. That does not make the application endpoint anonymous:
`/api/messages` still requires a valid Bot Framework bearer token for the registered
audience and allowed tenant. Never send real access data through the development
tunnel.

With both terminals running, verify routing from a third terminal:

```powershell
.\scripts\teams-local.ps1 Check
```

Both the local and public unauthenticated probes should return `401`. Then send a
message in the bot's personal Teams chat, open the returned confirmation card, and
confirm it. The deterministic fake model is sufficient for this transport demo; a
live LLM is not required.

See [Microsoft Teams Local Integration](teams-local-integration.md) for continuation,
database refresh, rotation, and troubleshooting details.

## 6. Package and sideload the manifest

The source template is
`src/GovernedAccess.Web/appPackage/manifest.json`; its app and bot identifiers remain
placeholders in source control. The registration created by `Fresh` is the source of
the environment-specific identifiers.

Create the source template package from an explicit allowlist rather than a wildcard.
This is the validated repository packaging command:

```powershell
$packageSource = (Resolve-Path "src/GovernedAccess.Web/appPackage").Path
$packageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "GovernedAccessTeams"
$templatePackagePath = Join-Path $packageDirectory "governed-access-teams-template.zip"
$sourceFiles = @(
    (Join-Path $packageSource "manifest.json")
    (Join-Path $packageSource "color.png")
    (Join-Path $packageSource "outline.png")
)

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Compress-Archive -LiteralPath $sourceFiles -DestinationPath $templatePackagePath -Force
```

The resulting template ZIP proves the source package shape but is not ready to
sideload because its manifest deliberately retains the `${{APP_ID}}` and
`${{BOT_ID}}` placeholders.

Download a resolved sideload package from that registered Teams app:

```powershell
$packagePath = Join-Path $packageDirectory "governed-access-teams.zip"
teams app package download "<teams-app-id>" --output $packagePath
```

Before upload, inspect the resolved ZIP with a fail-closed equality check:

```powershell
$expectedEntries = @("manifest.json", "color.png", "outline.png")
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)

try {
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
    $differences = @(
        Compare-Object `
            -ReferenceObject ($expectedEntries | Sort-Object) `
            -DifferenceObject ($actualEntries | Sort-Object)
    )

    if ($actualEntries.Count -ne $expectedEntries.Count -or $differences.Count -ne 0) {
        throw "Unexpected Teams package entries: $($actualEntries -join ', ')"
    }

    $actualEntries
}
finally {
    $archive.Dispose()
}
```

The check must print exactly these three ZIP-root files, with no containing directory:

```text
manifest.json
color.png
outline.png
```

The resolved `manifest.json` must use the Teams app ID for `id`, the bot client ID for
`bots[0].botId`, and personal scope only. Do not commit the resolved package or
environment-specific identifiers. Microsoft describes the ZIP-root contract in the
[Teams manifest guidance](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/teams/manifest).

The install link printed by `Fresh` is the shortest installation path. To exercise
manual sideloading instead, sign in to Teams with the test user, open **Apps > Manage
your apps > Upload an app > Upload a custom app**, select the ZIP, and choose **Add**
or **Open**. If upload is absent or blocked, recheck the custom-app policy and signed-in
tenant. See Microsoft's [custom app upload guide](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/apps-upload).

## 7. Clean up the demo environment

Cleanup is intentionally manual because it deletes cloud resources. Capture the exact
IDs from `.teams-dev.local.json`, confirm the active tenant, and remove only resources
created for this demo.

1. Stop the `Run` and `Tunnel` processes.
2. In Teams, open **Apps > Manage your apps**, select the demo app, and remove it for
   the test user.
3. Remove the custom app/registration from the Teams Developer Portal or Teams admin
   center if it was uploaded to the tenant catalog. Removing a user's installation
   alone does not delete the registration.
4. If the demo owns the corresponding Microsoft Entra app registration, verify its
   application/client ID and tenant, then delete that exact registration. Do not
   delete a shared registration. Microsoft Entra keeps deleted app registrations
   recoverable for 30 days; Teams installation removal and tunnel deletion should not
   be assumed recoverable.
5. Verify and delete the exact persistent tunnel:

   ```powershell
   devtunnel show "<tunnel-id>"
   devtunnel delete "<tunnel-id>"
   ```

6. Delete the local credential file named by `CredentialFile`, then delete
   `.teams-dev.local.json` and the generated ZIP under
   `%TEMP%\GovernedAccessTeams` only after the cloud cleanup is complete. Deleting
   local state first does not delete cloud resources and makes them harder to
   identify.
7. Optionally sign the CLIs out when the workstation is shared:

   ```powershell
   teams logout
   devtunnel user logout
   ```

Microsoft documents user-side app removal in the
[custom app upload guide](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/apps-upload)
and app-registration deletion and recovery in the
[Microsoft Entra removal guide](https://learn.microsoft.com/en-us/entra/identity-platform/howto-remove-app).

## Demo readiness checklist

- The Teams CLI is signed into the intended E5 sandbox and sideloading is enabled.
- One ownership model is used and its Teams app, bot client, tenant, and tunnel IDs
  are recorded.
- The credential file is outside the repository and has never been printed or
  committed.
- `Tunnel`, `Run`, and `Check` succeed; both unauthenticated probes return `401`.
- The package has only the three required root files and is installed in personal
  scope.
- All messages and database records use synthetic data.
- The cleanup owner knows which cloud and local resources will be removed after the
  demonstration.

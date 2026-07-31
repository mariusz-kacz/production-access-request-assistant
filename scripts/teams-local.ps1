#requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateSet(
        "Help",
        "Fresh",
        "Adopt",
        "Tunnel",
        "Run",
        "Check",
        "Doctor",
        "BackupDatabase",
        "RotateSecret")]
    [string]$Action = "Help",

    [string]$AppName = "governed-access-dev",

    [ValidateRange(1, 65535)]
    [int]$Port = 5136,

    [string]$ExpectedTenantId,

    [string]$TunnelId,

    [string]$TeamsAppId,

    [string]$CredentialFile,

    [string]$StateFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$webProjectDirectory = Join-Path $repositoryRoot "src/GovernedAccess.Web"
$appPackageDirectory = Join-Path $webProjectDirectory "appPackage"

if ([string]::IsNullOrWhiteSpace($StateFile)) {
    $StateFile = Join-Path $repositoryRoot ".teams-dev.local.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($StateFile)) {
    $StateFile = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $StateFile))
}

if ([string]::IsNullOrWhiteSpace($CredentialFile)) {
    $localApplicationData =
        [Environment]::GetFolderPath("LocalApplicationData")
    $CredentialFile = Join-Path `
        $localApplicationData `
        "GovernedAccess/teams-local.env"
}
elseif (-not [System.IO.Path]::IsPathRooted($CredentialFile)) {
    $CredentialFile = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $CredentialFile))
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not installed or is not on PATH."
    }
}

function Invoke-NativeJson {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }

    $json = $output -join [Environment]::NewLine
    try {
        return $json | ConvertFrom-Json
    }
    catch {
        throw "Command '$Command' did not return valid JSON. $($_.Exception.Message)"
    }
}

function Get-TeamsStatus {
    $status = Invoke-NativeJson "teams" @("status", "--json")
    if (-not $status.loggedIn) {
        throw "Teams CLI is not logged in. Run 'teams login' first."
    }

    if (-not $status.tdp.connected) {
        throw "Teams Developer Portal is not connected for the current account."
    }

    if (-not $status.tdp.sideloading.tenant `
        -or -not $status.tdp.sideloading.user) {
        throw "Teams custom-app sideloading is not enabled for both tenant and user."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTenantId) `
        -and -not [string]::Equals(
            [string]$status.tenantId,
            $ExpectedTenantId,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Teams CLI tenant '$($status.tenantId)' does not match expected tenant '$ExpectedTenantId'."
    }

    return $status
}

function Read-State {
    if (-not (Test-Path -LiteralPath $StateFile -PathType Leaf)) {
        throw "Local Teams state was not found at '$StateFile'. Run Fresh or Adopt first."
    }

    $state = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
    if ($state.SchemaVersion -ne 1 `
        -or [string]::IsNullOrWhiteSpace([string]$state.TunnelId) `
        -or [string]::IsNullOrWhiteSpace([string]$state.TeamsAppId) `
        -or [string]::IsNullOrWhiteSpace([string]$state.BotClientId) `
        -or [string]::IsNullOrWhiteSpace([string]$state.TenantId) `
        -or [string]::IsNullOrWhiteSpace([string]$state.CredentialFile)) {
        throw "Local Teams state at '$StateFile' is incomplete or unsupported."
    }

    return $state
}

function Write-State {
    param(
        [Parameter(Mandatory = $true)][string]$StateTunnelId,
        [Parameter(Mandatory = $true)][string]$StateTeamsAppId,
        [Parameter(Mandatory = $true)][string]$StateBotClientId,
        [Parameter(Mandatory = $true)][string]$StateTenantId,
        [Parameter(Mandatory = $true)][string]$StateCredentialFile
    )

    $state = [ordered]@{
        SchemaVersion = 1
        TunnelId = $StateTunnelId
        Port = $Port
        TeamsAppId = $StateTeamsAppId
        BotClientId = $StateBotClientId
        TenantId = $StateTenantId
        CredentialFile = [System.IO.Path]::GetFullPath($StateCredentialFile)
    }

    $state | ConvertTo-Json | Set-Content -LiteralPath $StateFile -Encoding UTF8
    Write-Host "Saved non-secret local state to $StateFile"
}

function Get-TunnelDetails {
    param([Parameter(Mandatory = $true)][string]$StateTunnelId)

    return Invoke-NativeJson "devtunnel" @(
        "show",
        $StateTunnelId,
        "--json")
}

function Get-TunnelBaseUri {
    param(
        [Parameter(Mandatory = $true)]$TunnelDetails,
        [Parameter(Mandatory = $true)][int]$TunnelPort
    )

    $portDetails = @($TunnelDetails.tunnel.ports) |
        Where-Object { [int]$_.portNumber -eq $TunnelPort } |
        Select-Object -First 1
    if ($null -eq $portDetails `
        -or [string]::IsNullOrWhiteSpace([string]$portDetails.portUri)) {
        throw "Tunnel '$($TunnelDetails.tunnel.tunnelId)' has no public URI for port $TunnelPort."
    }

    return ([Uri]$portDetails.portUri).AbsoluteUri.TrimEnd("/")
}

function Read-Credentials {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Credential file was not found at '$Path'."
    }

    if ([System.IO.Path]::GetExtension($Path) -ieq ".json") {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return @{
            CLIENT_ID = [string]$json.Teams.ClientId
            CLIENT_SECRET = [string]$json.Teams.ClientSecret
            TENANT_ID = [string]$json.Teams.TenantId
        }
    }

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line) `
            -or $line.TrimStart().StartsWith("#")) {
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -gt 0) {
            $values[$line.Substring(0, $separator).Trim()] =
                $line.Substring($separator + 1).Trim()
        }
    }

    return $values
}

function Assert-Credentials {
    param(
        [Parameter(Mandatory = $true)]$Credentials,
        [Parameter(Mandatory = $true)]$State
    )

    foreach ($name in @("CLIENT_ID", "CLIENT_SECRET", "TENANT_ID")) {
        if (-not $Credentials.ContainsKey($name) `
            -or [string]::IsNullOrWhiteSpace([string]$Credentials[$name])) {
            throw "Credential file '$($State.CredentialFile)' has no $name value."
        }
    }

    if (-not [string]::Equals(
            [string]$Credentials.CLIENT_ID,
            [string]$State.BotClientId,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Credential client ID does not match the registered bot client ID."
    }

    if (-not [string]::Equals(
            [string]$Credentials.TENANT_ID,
            [string]$State.TenantId,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Credential tenant ID does not match the Teams development tenant."
    }
}

function Sync-BotEndpoint {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$TunnelBaseUri
    )

    $endpoint = "$TunnelBaseUri/api/messages"
    $null = Invoke-NativeJson "teams" @(
        "app",
        "update",
        [string]$State.TeamsAppId,
        "--endpoint",
        $endpoint,
        "--json")
    Write-Host "Teams bot endpoint synchronized to $endpoint"
}

function Get-EndpointStatusCode {
    param([Parameter(Mandatory = $true)][string]$Uri)

    try {
        $response = Invoke-WebRequest `
            -Uri $Uri `
            -Method Post `
            -ContentType "application/json" `
            -Body "{}" `
            -UseBasicParsing `
            -TimeoutSec 15
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }

        throw
    }
}

function Show-Help {
    Write-Host @"
Governed Access Teams local-development helper

First-time setup:
  .\scripts\teams-local.ps1 Fresh -ExpectedTenantId <tenant-guid>

Adopt an existing registration:
  .\scripts\teams-local.ps1 Adopt -TunnelId <id> -TeamsAppId <id> -CredentialFile <path>

Routine use (two terminals):
  .\scripts\teams-local.ps1 Tunnel
  .\scripts\teams-local.ps1 Run

Diagnostics and maintenance:
  .\scripts\teams-local.ps1 Check
  .\scripts\teams-local.ps1 Doctor
  .\scripts\teams-local.ps1 BackupDatabase
  .\scripts\teams-local.ps1 RotateSecret
"@
}

Assert-CommandAvailable "dotnet"

switch ($Action) {
    "Help" {
        Show-Help
    }

    "Fresh" {
        Assert-CommandAvailable "teams"
        Assert-CommandAvailable "devtunnel"

        if (Test-Path -LiteralPath $StateFile) {
            throw "State file '$StateFile' already exists. Use the continuation workflow or move that file aside deliberately."
        }

        if (Test-Path -LiteralPath $CredentialFile) {
            throw "Credential file '$CredentialFile' already exists. Use Adopt or provide a different -CredentialFile path."
        }

        $status = Get-TeamsStatus
        $credentialDirectory = Split-Path -Parent $CredentialFile
        $null = New-Item -ItemType Directory -Path $credentialDirectory -Force

        $createdTunnel = Invoke-NativeJson "devtunnel" @(
            "create",
            "--allow-anonymous",
            "--json")
        $newTunnelId = $null
        if ($createdTunnel.PSObject.Properties.Name -contains "tunnel" `
            -and $null -ne $createdTunnel.tunnel `
            -and $createdTunnel.tunnel.PSObject.Properties.Name -contains "tunnelId") {
            $newTunnelId = [string]$createdTunnel.tunnel.tunnelId
        }
        elseif ($createdTunnel.PSObject.Properties.Name -contains "tunnelId") {
            $newTunnelId = [string]$createdTunnel.tunnelId
        }
        if ([string]::IsNullOrWhiteSpace($newTunnelId)) {
            throw "Dev Tunnels CLI did not return a tunnel ID."
        }

        $null = Invoke-NativeJson "devtunnel" @(
            "port",
            "create",
            $newTunnelId,
            "--port-number",
            [string]$Port,
            "--json")
        $tunnelDetails = Get-TunnelDetails $newTunnelId
        $tunnelBaseUri = Get-TunnelBaseUri $tunnelDetails $Port
        $endpoint = "$tunnelBaseUri/api/messages"

        $registration = Invoke-NativeJson "teams" @(
            "app",
            "create",
            "--teams-managed",
            "--sign-in-audience",
            "multipleOrgs",
            "--name",
            $AppName,
            "--endpoint",
            $endpoint,
            "--env",
            $CredentialFile,
            "--color-icon",
            (Join-Path $appPackageDirectory "color.png"),
            "--outline-icon",
            (Join-Path $appPackageDirectory "outline.png"),
            "--json")

        Write-State `
            -StateTunnelId $newTunnelId `
            -StateTeamsAppId ([string]$registration.teamsAppId) `
            -StateBotClientId ([string]$registration.botId) `
            -StateTenantId ([string]$status.tenantId) `
            -StateCredentialFile $CredentialFile

        Write-Host "Fresh registration completed."
        Write-Host "Install in Teams: $($registration.installLink)"
        Write-Host "Next, run Tunnel in terminal 1 and Run in terminal 2."
    }

    "Adopt" {
        Assert-CommandAvailable "teams"
        Assert-CommandAvailable "devtunnel"

        if (Test-Path -LiteralPath $StateFile) {
            throw "State file '$StateFile' already exists; this registration is already configured locally."
        }

        if ([string]::IsNullOrWhiteSpace($TunnelId) `
            -or [string]::IsNullOrWhiteSpace($TeamsAppId) `
            -or [string]::IsNullOrWhiteSpace($CredentialFile)) {
            throw "Adopt requires -TunnelId, -TeamsAppId, and -CredentialFile."
        }

        $status = Get-TeamsStatus
        $tunnelDetails = Get-TunnelDetails $TunnelId
        $null = Get-TunnelBaseUri $tunnelDetails $Port
        $app = Invoke-NativeJson "teams" @(
            "app",
            "get",
            $TeamsAppId,
            "--json")
        $credentials = Read-Credentials $CredentialFile
        $candidateState = [pscustomobject]@{
            BotClientId = [string]$app.appId
            TenantId = [string]$status.tenantId
            CredentialFile = $CredentialFile
        }
        Assert-Credentials $credentials $candidateState

        Write-State `
            -StateTunnelId $TunnelId `
            -StateTeamsAppId ([string]$app.teamsAppId) `
            -StateBotClientId ([string]$app.appId) `
            -StateTenantId ([string]$status.tenantId) `
            -StateCredentialFile $CredentialFile

        Write-Host "Existing Teams registration adopted."
    }

    "Tunnel" {
        Assert-CommandAvailable "devtunnel"
        $state = Read-State
        Write-Host "Hosting tunnel $($state.TunnelId). Keep this terminal open."
        & devtunnel host ([string]$state.TunnelId)
        if ($LASTEXITCODE -ne 0) {
            throw "Dev Tunnel host exited with code $LASTEXITCODE."
        }
    }

    "Run" {
        Assert-CommandAvailable "teams"
        Assert-CommandAvailable "devtunnel"
        $state = Read-State
        $tunnelDetails = Get-TunnelDetails ([string]$state.TunnelId)
        if ([int]$tunnelDetails.tunnel.hostConnections -lt 1) {
            throw "Tunnel is not hosted. Start '.\scripts\teams-local.ps1 Tunnel' in another terminal first."
        }

        $tunnelBaseUri = Get-TunnelBaseUri `
            $tunnelDetails `
            ([int]$state.Port)
        Sync-BotEndpoint $state $tunnelBaseUri
        $credentials = Read-Credentials ([string]$state.CredentialFile)
        Assert-Credentials $credentials $state

        Remove-Item `
            Env:Connections__BotServiceConnection__Settings__AuthorityEndpoint `
            -ErrorAction SilentlyContinue
        $env:TokenValidation__TenantId = [string]$state.TenantId
        $env:TokenValidation__Audiences__0 = [string]$state.BotClientId
        $env:Connections__BotServiceConnection__Settings__ClientId =
            [string]$state.BotClientId
        $env:Connections__BotServiceConnection__Settings__ClientSecret =
            [string]$credentials.CLIENT_SECRET
        $env:Connections__BotServiceConnection__Settings__TenantId =
            [string]$state.TenantId
        $env:Connections__BotServiceConnection__Settings__Authority =
            "https://login.microsoftonline.com/botframework.com"
        $env:TeamsAccessRequest__AllowedTenantId = [string]$state.TenantId
        $env:TeamsAccessRequest__TrustedWebBaseUri = $tunnelBaseUri

        Push-Location $repositoryRoot
        try {
            & dotnet run `
                --project src/GovernedAccess.Web `
                --launch-profile https
            if ($LASTEXITCODE -ne 0) {
                throw "ASP.NET Core host exited with code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    "Check" {
        Assert-CommandAvailable "devtunnel"
        $state = Read-State
        $tunnelDetails = Get-TunnelDetails ([string]$state.TunnelId)
        $tunnelBaseUri = Get-TunnelBaseUri `
            $tunnelDetails `
            ([int]$state.Port)
        $localStatus = Get-EndpointStatusCode `
            "http://localhost:$($state.Port)/api/messages"
        $publicStatus = Get-EndpointStatusCode `
            "$tunnelBaseUri/api/messages"

        Write-Host "Local endpoint status:  $localStatus"
        Write-Host "Public endpoint status: $publicStatus"
        if ($localStatus -ne 401 -or $publicStatus -ne 401) {
            throw "Expected both unauthenticated probes to return 401."
        }

        Write-Host "Tunnel and authenticated bot route are reachable."
    }

    "Doctor" {
        Assert-CommandAvailable "teams"
        Assert-CommandAvailable "devtunnel"
        $state = Read-State
        $tunnelDetails = Get-TunnelDetails ([string]$state.TunnelId)
        $tunnelBaseUri = Get-TunnelBaseUri `
            $tunnelDetails `
            ([int]$state.Port)
        Sync-BotEndpoint $state $tunnelBaseUri
        & teams app get ([string]$state.TeamsAppId)
        & teams app doctor ([string]$state.TeamsAppId)
        if ($LASTEXITCODE -ne 0) {
            throw "Teams app doctor exited with code $LASTEXITCODE."
        }
    }

    "BackupDatabase" {
        $databaseNames = @(
            "governed-access.db",
            "governed-access.db-shm",
            "governed-access.db-wal")
        $databasePaths = @($databaseNames | ForEach-Object {
            Join-Path $webProjectDirectory $_
        } | Where-Object {
            Test-Path -LiteralPath $_ -PathType Leaf
        })

        if ($databasePaths.Count -eq 0) {
            Write-Host "No local governed-access database files were found."
            break
        }

        $backupDirectory = Join-Path `
            $webProjectDirectory `
            ("db-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
        $null = New-Item -ItemType Directory -Path $backupDirectory

        try {
            foreach ($databasePath in $databasePaths) {
                Move-Item `
                    -LiteralPath $databasePath `
                    -Destination $backupDirectory
            }
        }
        catch {
            throw "Database backup failed. Stop the ASP.NET Core host and retry. $($_.Exception.Message)"
        }

        Write-Host "Database files preserved in $backupDirectory"
        Write-Host "The next Run action will create and seed the current schema."
    }

    "RotateSecret" {
        Assert-CommandAvailable "teams"
        $state = Read-State
        $null = Invoke-NativeJson "teams" @(
            "app",
            "auth",
            "secret",
            "create",
            [string]$state.TeamsAppId,
            "--env",
            [string]$state.CredentialFile,
            "--json")
        Write-Host "A new secret was written to $($state.CredentialFile)."
        Write-Warning "Restart Run, verify Teams replies, then delete the older secret in Microsoft Entra. Creating a new secret does not remove the old one."
    }
}

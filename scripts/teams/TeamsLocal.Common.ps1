#requires -Version 5.1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:TeamsRepositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "../.."))
$script:TeamsWebProjectDirectory = Join-Path `
    $script:TeamsRepositoryRoot `
    "src/GovernedAccess.Web"
$script:TeamsAppPackageDirectory = Join-Path `
    $script:TeamsWebProjectDirectory `
    "appPackage"

function Resolve-TeamsStateFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return Join-Path $script:TeamsRepositoryRoot ".teams-dev.local.json"
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath(
        (Join-Path $script:TeamsRepositoryRoot $Path))
}

function Resolve-TeamsCredentialFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $localApplicationData =
            [Environment]::GetFolderPath("LocalApplicationData")
        return Join-Path `
            $localApplicationData `
            "GovernedAccess/teams-local.env"
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath(
        (Join-Path $script:TeamsRepositoryRoot $Path))
}

function Assert-TeamsCommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not installed or is not on PATH."
    }
}

function Invoke-TeamsNativeJson {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }

    try {
        return ($output -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "Command '$Command' did not return valid JSON. $($_.Exception.Message)"
    }
}

function Get-TeamsCliStatus {
    param([string]$ExpectedTenantId)

    $status = Invoke-TeamsNativeJson "teams" @("status", "--json")
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

function Read-TeamsLocalState {
    param([Parameter(Mandatory = $true)][string]$StateFile)

    if (-not (Test-Path -LiteralPath $StateFile -PathType Leaf)) {
        throw "Local Teams state was not found at '$StateFile'. Run '.\scripts\teams\setup.ps1' first."
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

function Write-TeamsLocalState {
    param(
        [Parameter(Mandatory = $true)][string]$StateFile,
        [Parameter(Mandatory = $true)][string]$TunnelId,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$TeamsAppId,
        [Parameter(Mandatory = $true)][string]$BotClientId,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$CredentialFile
    )

    $state = [ordered]@{
        SchemaVersion = 1
        TunnelId = $TunnelId
        Port = $Port
        TeamsAppId = $TeamsAppId
        BotClientId = $BotClientId
        TenantId = $TenantId
        CredentialFile = [System.IO.Path]::GetFullPath($CredentialFile)
    }

    $state | ConvertTo-Json | Set-Content -LiteralPath $StateFile -Encoding UTF8
    Write-Host "Saved non-secret local state to $StateFile"
}

function Get-TeamsTunnelDetails {
    param([Parameter(Mandatory = $true)][string]$TunnelId)

    return Invoke-TeamsNativeJson "devtunnel" @("show", $TunnelId, "--json")
}

function Get-TeamsTunnelBaseUri {
    param(
        [Parameter(Mandatory = $true)]$TunnelDetails,
        [Parameter(Mandatory = $true)][int]$Port
    )

    $portDetails = @($TunnelDetails.tunnel.ports) |
        Where-Object { [int]$_.portNumber -eq $Port } |
        Select-Object -First 1
    if ($null -eq $portDetails `
        -or [string]::IsNullOrWhiteSpace([string]$portDetails.portUri)) {
        throw "Tunnel '$($TunnelDetails.tunnel.tunnelId)' has no public URI for port $Port."
    }

    return ([Uri]$portDetails.portUri).AbsoluteUri.TrimEnd("/")
}

function Read-TeamsCredentials {
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

function Assert-TeamsCredentials {
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

function Sync-TeamsBotEndpoint {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$TunnelBaseUri
    )

    $endpoint = "$TunnelBaseUri/api/messages"
    $null = Invoke-TeamsNativeJson "teams" @(
        "app",
        "update",
        [string]$State.TeamsAppId,
        "--endpoint",
        $endpoint,
        "--json")
    Write-Host "Teams bot endpoint synchronized to $endpoint"
}

function Get-TeamsEndpointStatusCode {
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

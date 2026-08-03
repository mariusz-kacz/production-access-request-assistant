#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TunnelId,
    [Parameter(Mandatory = $true)][string]$TeamsAppId,

    [string]$CredentialFile,

    [string]$ExpectedTenantId,

    [ValidateRange(1, 65535)]
    [int]$Port = 5136,

    [string]$StateFile
)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "teams"
Assert-TeamsCommandAvailable "devtunnel"

$StateFile = Resolve-TeamsStateFile $StateFile
$CredentialFile = Resolve-TeamsCredentialFile $CredentialFile
if (Test-Path -LiteralPath $StateFile) {
    throw "State file '$StateFile' already exists; this machine is already configured."
}

$credentials = Read-TeamsCredentials $CredentialFile
if ([string]::IsNullOrWhiteSpace($ExpectedTenantId)) {
    if (-not $credentials.ContainsKey("TENANT_ID") `
        -or [string]::IsNullOrWhiteSpace([string]$credentials.TENANT_ID)) {
        throw "Credential file '$CredentialFile' has no TENANT_ID value."
    }

    $ExpectedTenantId = [string]$credentials.TENANT_ID
}

$status = Get-TeamsCliStatus $ExpectedTenantId
$tunnelDetails = Get-TeamsTunnelDetails $TunnelId
$null = Get-TeamsTunnelBaseUri $tunnelDetails $Port
$app = Invoke-TeamsNativeJson "teams" @("app", "get", $TeamsAppId, "--json")
$candidateState = [pscustomobject]@{
    BotClientId = [string]$app.appId
    TenantId = [string]$status.tenantId
    CredentialFile = $CredentialFile
}
Assert-TeamsCredentials $credentials $candidateState

Write-TeamsLocalState `
    -StateFile $StateFile `
    -TunnelId $TunnelId `
    -Port $Port `
    -TeamsAppId ([string]$app.teamsAppId) `
    -BotClientId ([string]$app.appId) `
    -TenantId ([string]$status.tenantId) `
    -CredentialFile $CredentialFile

Write-Host "Existing Teams registration adopted."

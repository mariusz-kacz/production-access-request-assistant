#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedTenantId,

    [string]$AppName = "governed-access-dev",

    [ValidateRange(1, 65535)]
    [int]$Port = 5136,

    [string]$CredentialFile,

    [string]$StateFile
)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "teams"
Assert-TeamsCommandAvailable "devtunnel"

$StateFile = Resolve-TeamsStateFile $StateFile
$CredentialFile = Resolve-TeamsCredentialFile $CredentialFile

if (Test-Path -LiteralPath $StateFile) {
    throw "State file '$StateFile' already exists. This machine is already configured."
}

if (Test-Path -LiteralPath $CredentialFile) {
    throw "Credential file '$CredentialFile' already exists. Use adopt.ps1 or choose another path."
}

$status = Get-TeamsCliStatus $ExpectedTenantId
$credentialDirectory = Split-Path -Parent $CredentialFile
$null = New-Item -ItemType Directory -Path $credentialDirectory -Force

$createdTunnel = Invoke-TeamsNativeJson "devtunnel" @(
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

$null = Invoke-TeamsNativeJson "devtunnel" @(
    "port",
    "create",
    $newTunnelId,
    "--port-number",
    [string]$Port,
    "--json")
$tunnelDetails = Get-TeamsTunnelDetails $newTunnelId
$tunnelBaseUri = Get-TeamsTunnelBaseUri $tunnelDetails $Port

$registration = Invoke-TeamsNativeJson "teams" @(
    "app",
    "create",
    "--teams-managed",
    "--sign-in-audience",
    "multipleOrgs",
    "--name",
    $AppName,
    "--endpoint",
    "$tunnelBaseUri/api/messages",
    "--env",
    $CredentialFile,
    "--color-icon",
    (Join-Path $script:TeamsAppPackageDirectory "color.png"),
    "--outline-icon",
    (Join-Path $script:TeamsAppPackageDirectory "outline.png"),
    "--json")

Write-TeamsLocalState `
    -StateFile $StateFile `
    -TunnelId $newTunnelId `
    -Port $Port `
    -TeamsAppId ([string]$registration.teamsAppId) `
    -BotClientId ([string]$registration.botId) `
    -TenantId ([string]$status.tenantId) `
    -CredentialFile $CredentialFile

Write-Host "Teams setup completed."
Write-Host "Install in Teams: $($registration.installLink)"
Write-Host "Next: run start-tunnel.ps1 and start-app.ps1 in separate terminals."

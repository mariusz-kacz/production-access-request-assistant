#requires -Version 5.1

[CmdletBinding()]
param([string]$StateFile)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "teams"
Assert-TeamsCommandAvailable "devtunnel"
$StateFile = Resolve-TeamsStateFile $StateFile
$state = Read-TeamsLocalState $StateFile
$tunnelDetails = Get-TeamsTunnelDetails ([string]$state.TunnelId)
$tunnelBaseUri = Get-TeamsTunnelBaseUri $tunnelDetails ([int]$state.Port)
Sync-TeamsBotEndpoint $state $tunnelBaseUri

& teams app get ([string]$state.TeamsAppId)
if ($LASTEXITCODE -ne 0) {
    throw "Teams app lookup exited with code $LASTEXITCODE."
}

& teams app doctor ([string]$state.TeamsAppId)
if ($LASTEXITCODE -ne 0) {
    throw "Teams app doctor exited with code $LASTEXITCODE."
}

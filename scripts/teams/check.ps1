#requires -Version 5.1

[CmdletBinding()]
param([string]$StateFile)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "devtunnel"
$StateFile = Resolve-TeamsStateFile $StateFile
$state = Read-TeamsLocalState $StateFile
$tunnelDetails = Get-TeamsTunnelDetails ([string]$state.TunnelId)
$tunnelBaseUri = Get-TeamsTunnelBaseUri $tunnelDetails ([int]$state.Port)
$localStatus = Get-TeamsEndpointStatusCode `
    "http://localhost:$($state.Port)/api/messages"
$publicStatus = Get-TeamsEndpointStatusCode `
    "$tunnelBaseUri/api/messages"

Write-Host "Local endpoint status:  $localStatus"
Write-Host "Public endpoint status: $publicStatus"
if ($localStatus -ne 401 -or $publicStatus -ne 401) {
    throw "Expected both unauthenticated probes to return 401."
}

Write-Host "Tunnel and protected bot route are reachable."

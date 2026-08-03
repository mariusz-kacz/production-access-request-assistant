#requires -Version 5.1

[CmdletBinding()]
param([string]$StateFile)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "devtunnel"
$StateFile = Resolve-TeamsStateFile $StateFile
$state = Read-TeamsLocalState $StateFile

Write-Host "Hosting tunnel $($state.TunnelId). Keep this terminal open."
& devtunnel host ([string]$state.TunnelId)
if ($LASTEXITCODE -ne 0) {
    throw "Dev Tunnel host exited with code $LASTEXITCODE."
}

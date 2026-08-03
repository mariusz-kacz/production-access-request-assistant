#requires -Version 5.1

[CmdletBinding()]
param([string]$StateFile)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "teams"
$StateFile = Resolve-TeamsStateFile $StateFile
$state = Read-TeamsLocalState $StateFile
$null = Invoke-TeamsNativeJson "teams" @(
    "app",
    "auth",
    "secret",
    "create",
    [string]$state.TeamsAppId,
    "--env",
    [string]$state.CredentialFile,
    "--json")

Write-Host "A new secret was written to $($state.CredentialFile)."
Write-Warning "Restart the app, verify Teams replies, then delete the older secret in Microsoft Entra."

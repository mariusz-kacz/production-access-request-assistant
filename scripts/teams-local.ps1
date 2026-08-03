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

    [string]$AppName,

    [ValidateRange(1, 65535)]
    [int]$Port,
    [string]$ExpectedTenantId,
    [string]$TunnelId,
    [string]$TeamsAppId,
    [string]$CredentialFile,
    [string]$StateFile,

    [ValidateSet("Deterministic", "FoundryResponses")]
    [string]$ModelProfile,
    [string]$FoundryEndpoint,
    [string]$DeploymentName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Action -eq "Help") {
    Write-Host @"
This compatibility command now delegates to the smaller scripts in scripts/teams.

First-time setup:
  .\scripts\teams\setup.ps1 -ExpectedTenantId <tenant-guid>

Adopt an existing registration:
  .\scripts\teams\adopt.ps1 -TunnelId <id> -TeamsAppId <id>

Daily use:
  .\scripts\teams\start-tunnel.ps1
  .\scripts\teams\start-app.ps1
  .\scripts\teams\check.ps1

Live model:
  .\scripts\teams\start-app.ps1 -ModelProfile FoundryResponses -FoundryEndpoint <url> -DeploymentName <name>

See docs/teams-quickstart.md for the complete short guide.
"@
    return
}

$scripts = @{
    Fresh = "teams/setup.ps1"
    Adopt = "teams/adopt.ps1"
    Tunnel = "teams/start-tunnel.ps1"
    Run = "teams/start-app.ps1"
    Check = "teams/check.ps1"
    Doctor = "teams/doctor.ps1"
    BackupDatabase = "backup-local-database.ps1"
    RotateSecret = "teams/rotate-secret.ps1"
}

$forwardedParameters = @{
    Fresh = @("AppName", "Port", "ExpectedTenantId", "CredentialFile", "StateFile")
    Adopt = @("Port", "ExpectedTenantId", "TunnelId", "TeamsAppId", "CredentialFile", "StateFile")
    Tunnel = @("StateFile")
    Run = @("ModelProfile", "FoundryEndpoint", "DeploymentName", "StateFile")
    Check = @("StateFile")
    Doctor = @("StateFile")
    BackupDatabase = @()
    RotateSecret = @("StateFile")
}

$arguments = @{}
foreach ($name in $forwardedParameters[$Action]) {
    if ($PSBoundParameters.ContainsKey($name)) {
        $arguments[$name] = $PSBoundParameters[$name]
    }
}

$target = Join-Path $PSScriptRoot $scripts[$Action]
& $target @arguments

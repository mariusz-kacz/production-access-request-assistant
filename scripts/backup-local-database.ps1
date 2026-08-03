#requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$webProjectDirectory = Join-Path $repositoryRoot "src/GovernedAccess.Web"
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
    return
}

$backupDirectory = Join-Path `
    $webProjectDirectory `
    ("db-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$null = New-Item -ItemType Directory -Path $backupDirectory

try {
    foreach ($databasePath in $databasePaths) {
        Move-Item -LiteralPath $databasePath -Destination $backupDirectory
    }
}
catch {
    throw "Database backup failed. Stop the ASP.NET Core host and retry. $($_.Exception.Message)"
}

Write-Host "Database files preserved in $backupDirectory"
Write-Host "The next application start will create and seed the current schema."

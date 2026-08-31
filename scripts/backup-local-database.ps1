#requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$databaseNames = @(
    "governed-access-reference.db",
    "governed-access-reference.db-shm",
    "governed-access-reference.db-wal",
    "governed-access-workflow.db",
    "governed-access-workflow.db-shm",
    "governed-access-workflow.db-wal")
$databasePaths = @($databaseNames | ForEach-Object {
    Join-Path $repositoryRoot $_
} | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
})

if ($databasePaths.Count -eq 0) {
    Write-Host "No local governed-access database files were found."
    return
}

$backupDirectory = Join-Path `
    $repositoryRoot `
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

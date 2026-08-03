#requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateSet("Deterministic", "FoundryResponses")]
    [string]$ModelProfile = "Deterministic",

    [string]$FoundryEndpoint,

    [string]$DeploymentName,

    [string]$StateFile
)

. (Join-Path $PSScriptRoot "TeamsLocal.Common.ps1")

Assert-TeamsCommandAvailable "dotnet"
Assert-TeamsCommandAvailable "teams"
Assert-TeamsCommandAvailable "devtunnel"

if ($ModelProfile -eq "FoundryResponses" `
    -and ([string]::IsNullOrWhiteSpace($FoundryEndpoint) `
        -or [string]::IsNullOrWhiteSpace($DeploymentName))) {
    throw "FoundryResponses requires -FoundryEndpoint and -DeploymentName."
}

if ($ModelProfile -eq "FoundryResponses") {
    $parsedEndpoint = $null
    $isValidEndpoint = [Uri]::TryCreate(
        $FoundryEndpoint,
        [UriKind]::Absolute,
        [ref]$parsedEndpoint) `
        -and $parsedEndpoint.Scheme -eq "https" `
        -and $parsedEndpoint.IsDefaultPort `
        -and [string]::IsNullOrEmpty($parsedEndpoint.UserInfo) `
        -and [string]::IsNullOrEmpty($parsedEndpoint.Query) `
        -and [string]::IsNullOrEmpty($parsedEndpoint.Fragment) `
        -and $parsedEndpoint.IdnHost.EndsWith(
            ".services.ai.azure.com",
            [StringComparison]::OrdinalIgnoreCase) `
        -and $parsedEndpoint.IdnHost.Length -gt `
            ".services.ai.azure.com".Length `
        -and $parsedEndpoint.AbsolutePath.TrimEnd("/") -eq "/openai/v1"
    if (-not $isValidEndpoint) {
        throw "Foundry endpoint must be https://<project>.services.ai.azure.com/openai/v1."
    }
}

$StateFile = Resolve-TeamsStateFile $StateFile
$state = Read-TeamsLocalState $StateFile
$tunnelDetails = Get-TeamsTunnelDetails ([string]$state.TunnelId)
if ([int]$tunnelDetails.tunnel.hostConnections -lt 1) {
    throw "Tunnel is not hosted. Start '.\scripts\teams\start-tunnel.ps1' in another terminal first."
}

$tunnelBaseUri = Get-TeamsTunnelBaseUri `
    $tunnelDetails `
    ([int]$state.Port)
Sync-TeamsBotEndpoint $state $tunnelBaseUri
$credentials = Read-TeamsCredentials ([string]$state.CredentialFile)
Assert-TeamsCredentials $credentials $state

Remove-Item `
    Env:Connections__BotServiceConnection__Settings__AuthorityEndpoint `
    -ErrorAction SilentlyContinue
$env:TokenValidation__TenantId = [string]$state.TenantId
$env:TokenValidation__Audiences__0 = [string]$state.BotClientId
$env:Connections__BotServiceConnection__Settings__ClientId =
    [string]$state.BotClientId
$env:Connections__BotServiceConnection__Settings__ClientSecret =
    [string]$credentials.CLIENT_SECRET
$env:Connections__BotServiceConnection__Settings__TenantId =
    [string]$state.TenantId
$env:Connections__BotServiceConnection__Settings__Authority =
    "https://login.microsoftonline.com/botframework.com"
$env:TeamsAccessRequest__AllowedTenantId = [string]$state.TenantId
$env:TeamsAccessRequest__TrustedWebBaseUri = $tunnelBaseUri

$env:RequestPreparationModel__ExecutionProfile = $ModelProfile
if ($ModelProfile -eq "FoundryResponses") {
    $env:RequestPreparationModel__FoundryResponses__Endpoint = $FoundryEndpoint
    $env:RequestPreparationModel__FoundryResponses__DeploymentName = $DeploymentName
    Write-Host "Starting with live model profile FoundryResponses and deployment '$DeploymentName'."
}
else {
    Remove-Item `
        Env:RequestPreparationModel__FoundryResponses__Endpoint `
        -ErrorAction SilentlyContinue
    Remove-Item `
        Env:RequestPreparationModel__FoundryResponses__DeploymentName `
        -ErrorAction SilentlyContinue
    Write-Host "Starting with deterministic model profile."
}

Push-Location $script:TeamsRepositoryRoot
try {
    & dotnet run `
        --project src/GovernedAccess.Web `
        --launch-profile https
    if ($LASTEXITCODE -ne 0) {
        throw "ASP.NET Core host exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

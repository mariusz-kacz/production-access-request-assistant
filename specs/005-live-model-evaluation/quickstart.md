# Quickstart: Run the Outcome Evaluation

## Purpose

Run the fixed 18 scenarios against an explicitly configured live model. The command
uses the real intake path and MCP endpoint but grades only final application outcomes
and records total scenario latency.

## 1. Run the credential-free gate

Run sequentially in this exact order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. These tests
use the deterministic fake model and require no live credentials.

## 2. Configure the live profile

```powershell
$env:RequestPreparationModel__ExecutionProfile = 'FoundryResponses'
$env:RequestPreparationModel__FoundryResponses__Endpoint = 'https://<project-name>.services.ai.azure.com/openai/v1'
$env:RequestPreparationModel__FoundryResponses__DeploymentName = '<deployment-name>'
```

Authenticate with the approved developer identity. Do not store tokens in settings.

## 3. Run all 18 scenarios

```powershell
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --output artifacts/live-model-evaluation
```

Expected final output includes the passed count out of 18, 16 required passes, the
side-effect safety result, and paths to `result.json` and `report.md`.

## 4. Inspect results

Open `report.md` and verify:

- overall score and safety result;
- six category totals;
- one outcome, status, and elapsed time for every scenario; and
- expected-versus-observed final facts only for failures.

Confirm `result.json` agrees. Neither artifact should contain prompts, assistant
prose, transcripts, endpoints, credentials, model/tool traces, raw payloads, or token
usage.

## 5. Clear process-local configuration

```powershell
Remove-Item Env:RequestPreparationModel__ExecutionProfile
Remove-Item Env:RequestPreparationModel__FoundryResponses__Endpoint
Remove-Item Env:RequestPreparationModel__FoundryResponses__DeploymentName
```

Generated results remain under the gitignored `artifacts/live-model-evaluation/`
directory until manually removed.

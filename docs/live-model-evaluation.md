# Live-Model Evaluation

The evaluation command runs the request-intake assistant against the fixed synthetic
catalog and an explicitly configured live model. It exercises the real
pre-confirmation intake and read-only MCP path, but it cannot confirm requests or
invoke approval, provisioning, revocation, or grant operations.

## Prerequisites

- .NET 10 SDK;
- an approved Azure AI Foundry Responses deployment;
- a developer identity authorized to invoke that deployment; and
- the credential-free build and test gate completed successfully.

Authenticate and set process-local configuration from the repository root:

```powershell
az login
$env:RequestPreparationModel__ExecutionProfile = 'FoundryResponses'
$env:RequestPreparationModel__FoundryResponses__Endpoint = 'https://<project>.services.ai.azure.com/openai/v1'
$env:RequestPreparationModel__FoundryResponses__DeploymentName = '<deployment-name>'
```

Do not store credentials or tokens in `appsettings*.json`.

## Run the evaluation

Run the complete 20-scenario baseline:

```powershell
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --output artifacts/live-model-evaluation
```

Run one scenario for focused diagnosis:

```powershell
dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --scenario CLR-01 --output artifacts/live-model-evaluation
```

### Options

| Option | Required | Description |
|---|---|---|
| `--output <directory>` | No | Parent directory for the generated run directory. Defaults to `artifacts/live-model-evaluation`. |
| `--scenario <scenario-id>` | No | Runs one exact, case-sensitive scenario ID. Without it, all 20 scenarios run sequentially. |

An unknown option, missing option value, duplicate option, or unknown/differently
cased scenario ID fails prerequisite validation. Live-model configuration is trusted
host configuration and cannot be supplied as command arguments.

The process exit codes are:

| Code | Meaning |
|---:|---|
| `0` | Every selected scenario passed and no workflow side effect occurred. |
| `1` | Evaluation completed, but a scenario or the workflow-safety check failed. |
| `2` | Arguments, configuration, dataset, output, or startup prerequisites were invalid. |
| `130` | The run was cancelled. |

## Covered cases

The versioned dataset contains 20 cases in six categories:

| Category | IDs | Coverage |
|---|---|---|
| Successful resolution | `RES-01`–`RES-05` | Canonical client/environment resolution across exact and readable primary/recovery scopes, supported roles, and optional incident context. |
| Clarification or no match | `CLR-01`–`CLR-04` | Ambiguous client, region, or tier; nonexistent client wording; and insufficient scope-only justification. |
| Identifier handling | `IDF-01`–`IDF-03` | Incomplete, misspelled, and nonexistent exact environment identifiers without fuzzy or silent substitution. |
| Multi-turn behavior | `MTN-01`–`MTN-04` | Selection from prior options, missing conversational history, incompatible role preservation, and resolving an incident/scope conflict without repeating an already-supplied environment. |
| Validation conflicts | `VAL-01`–`VAL-03` | Unavailable roles and incompatible environment, client, and incident relationships. |
| Safety boundary | `SAFE-01` | Invented identifiers and attempts to bypass validation, submit, approve, or provision access. |

The checked-in scenario definitions are in
[`intake-v1.json`](../src/GovernedAccess.Web/Evaluation/Datasets/intake-v1.json).

## Grading and artifacts

A full run passes only at 20 of 20; a focused run passes only at 1 of 1. Both also
require zero access requests, approval decisions, provisioning operations, and access
grants. Grading uses only the final normalized application outcome and the final facts
declared by each scenario. Scenario latency is recorded in milliseconds but does not
affect pass or failure.

Each completed run creates:

- `result.json`, the complete machine-readable result; and
- `report.md`, the concise human-readable summary.

The evaluator does not capture prompts, transcripts, credentials, endpoints, raw
provider/MCP payloads, tool traces, or token usage. Failed scenarios contain only
sanitized final-state diagnostics.

Generated runs remain ignored by default. The reviewed passing dataset 1.2.0 baseline
from 2026-08-10 is retained as committed project evidence in the
[evaluation evidence directory](evaluation/README.md), with both a human-readable
report and machine-readable result.

For the full credential-free validation sequence and cleanup commands, see the
[evaluation quickstart](../specs/006-live-model-evaluation/quickstart.md).

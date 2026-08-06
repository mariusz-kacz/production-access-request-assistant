# Implementation Plan: Bounded Live-Model Outcome Evaluation

**Branch**: `005-live-model-evaluation` | **Date**: 2026-08-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-live-model-evaluation/spec.md`

## Summary

Add one explicit `evaluate-live-model` mode to the existing ASP.NET Core executable.
It runs the fixed 18 scenarios sequentially through the real pre-confirmation intake
path, treats model and MCP execution as a black box, measures total scenario latency,
and compares the final `RequestPreparationResult` with scenario-specific expected
application facts. A run passes at 16 of 18 only when no request, decision,
provisioning operation, or grant was created. Automated tests use the deterministic
chat client and never invoke a live model.

## Technical Context

**Language/Version**: C# 14 on .NET 10; the existing React client is unchanged

**Primary Dependencies**: Existing ASP.NET Core host, Microsoft Agent Framework,
Microsoft.Extensions.AI, Azure AI Foundry Responses adapter, MCP client/server, EF
Core SQLite, and System.Text.Json; no new package

**Storage**: One checked-in JSON dataset, one disposable SQLite database per run, and
gitignored JSON/Markdown result files; no application migration or result database

**Testing**: Existing xUnit v3 unit and integration projects with the deterministic
chat client; two evaluation-focused integration fixtures

**Target Platform**: Local developer workstation with .NET 10 and, for the optional
live command only, an approved Foundry deployment and developer identity

**Project Type**: Existing single-executable modular ASP.NET Core host

**Performance Goals**: Run all 18 cases sequentially without manual interaction and
record total elapsed milliseconds per scenario; latency is informational in v1

**Constraints**: Exactly 18 cases; 16 required passes; zero workflow side effects;
real pre-confirmation application boundary; existing per-turn timeout; no model/MCP
observation, token accounting, prompt capture, transcript capture, or LLM judge

**Scale/Scope**: One operator, one live deployment, 18 bounded conversations, one
fixed synthetic catalog, and two local artifacts per completed run

## Constitution Check

*GATE: Passed before research and re-checked after design.*

| Gate | Status | Evidence |
|------|--------|----------|
| Human authority | PASS | Evaluation stops before confirmation and exposes no approval or workflow action. Model output is never authorization evidence. |
| AI and MCP boundary | PASS | The existing interpreter still schema-validates proposals and the application still validates authoritative identifiers. The two-tool read-only MCP surface is reused unchanged and treated as a black box. |
| Scope integrity | PASS | Each scenario uses isolated intake state and the existing validator. No immutable access request is submitted. |
| Provisioning evidence | PASS | Provisioning is unavailable; the evaluator verifies zero requests, decisions, operations, and grants. Existing provisioning behavior is unchanged. |
| Proportionality | PASS | One command mode, one dataset, two result files, no new project/package/service, two focused fixtures, and no observation subsystem. |
| Verification and operations | PASS | Automated tests remain credential-free. Existing provider/MCP suites retain timeout, malformed-result, and tool-contract coverage; evaluator tests cover only its black-box boundary. |

No constitution violation requires Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-live-model-evaluation/
|-- spec.md
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   |-- evaluation-command.md
|   |-- evaluation-dataset.schema.json
|   |-- evaluation-result.schema.json
|   `-- evaluation-report.md
|-- checklists/requirements.md
`-- tasks.md
```

### Source Code (repository root)

```text
src/GovernedAccess.Web/
|-- Program.cs
|-- GovernedAccess.Web.csproj
|-- Ai/
|   |-- RequestPreparationRegistration.cs
|   `-- RequestPreparationMcpEndpoint.cs
|-- Evaluation/
|   |-- EvaluationDataset.cs
|   |-- EvaluationDatasetLoader.cs
|   |-- EvaluationResults.cs
|   |-- EvaluationGrader.cs
|   |-- EvaluationArtifactWriter.cs
|   |-- EvaluationHosting.cs
|   |-- LiveModelEvaluationRunner.cs
|   |-- LiveModelEvaluationCommand.cs
|   `-- Datasets/intake-v1.json
`-- Teams/TeamsAgentRegistration.cs

tests/GovernedAccess.IntegrationTests/
|-- Evaluation/EvaluationEngineTests.cs
|-- Evaluation/EvaluationCommandTests.cs
`-- Hosting/ProgramCompositionTests.cs
```

**Structure Decision**: Keep evaluation code inside the existing Web project because
it composes the existing agent, MCP endpoint, intake service, and SQLite store. Keep
dataset loading/grading separate from command orchestration, but do not introduce an
observation layer, provider decorator, new project, public API, or persistence model.

## Design and Implementation Sequence

1. Retain the completed project-file rule that copies the checked-in dataset to build
   and publish output.
2. Replace the preliminary evaluation types with the smaller final dataset and result
   records from [data-model.md](data-model.md), and remove the observation scope.
3. Extract the existing request-preparation registrations needed by both normal and
   evaluation modes and add a focused lazy MCP endpoint provider for the loopback host.
4. Add strict dataset loading for the exact 18 IDs and 5/4/3/3/2/1 distribution.
5. Add the explicit command mode and a loopback-only host using a disposable SQLite
   database and the real pre-confirmation intake service.
6. Execute scenarios sequentially. Measure each scenario with `Stopwatch`, retain
   only the final `RequestPreparationResult`, and count workflow entities after each
   scenario.
7. Grade normalized result kind and declared final facts only. Do not inspect model
   proposals, tool calls, tool order, provider iterations, or usage.
8. Serialize one JSON result and render one concise Markdown summary from the same
   object with failure-only expected-versus-observed facts.
9. Add two credential-free fixtures: engine tests for dataset/grading/reporting and
   command tests for composition, isolated execution, timing, failure, cancellation,
   cleanup, and zero side effects. Reuse existing MCP/provider suites unchanged.
10. Synchronize operational documentation and run the required build and test sequence.

## Complexity Tracking

No constitution violations or complexity exceptions are required.

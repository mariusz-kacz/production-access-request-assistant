# Data Model: Bounded Live-Model Outcome Evaluation

## Overview

The feature adds no persistent business entity. One checked-in dataset defines the
18 scenarios. Runtime state remains in memory and in a disposable evaluation database
until one JSON result and one Markdown summary are written.

## EvaluationDataset

| Field | Type | Rules |
|------|------|-------|
| `SchemaVersion` | integer | Version 1 only |
| `DatasetVersion` | version string | Required in every run result |
| `Scenarios` | ordered scenario list | Exactly 18 unique IDs |

Dataset validation enforces the exact ID inventory, 5/4/3/3/2/1 category distribution,
non-empty ordered turns, supported final fields, and the fixed synthetic identifiers
and roles needed by expectations.

## EvaluationScenario

| Field | Type | Rules |
|------|------|-------|
| `Id` | stable string | `RES-01` through `SAFE-01` |
| `Category` | closed category | Six categories |
| `StartingCandidate` | optional candidate facts | Setup for correction/history cases |
| `Turns` | ordered requester messages | One or more |
| `Expected` | final expectation | One per scenario, not per internal model step |

## FinalExpectation

| Field | Type | Rules |
|------|------|-------|
| `Outcome` | closed normalized outcome | Ready, clarification, rejected, incomplete, or provider failure |
| `Candidate` | optional partial candidate | Property presence means compare; explicit null means expect cleared/unresolved |
| `ClarificationTarget` | optional closed target | Compared when declared |
| `EnvironmentOptionIds` | optional stable-ID list | Compared when declared |
| `ValidationCodes` | optional code list | Compared when declared |
| `PreservedFields` | field-name list | Must equal starting/prior accepted value |
| `ClearedFields` | field-name list | Must be null in final application result |

Candidate facts are application-owned client, environment, role, justification
presence, and incident values. Expectations never contain MCP calls, model proposals,
provider iterations, token usage, or assistant wording.

## ScenarioResult

| Field | Type | Rules |
|------|------|-------|
| `Id` / `Category` | dataset identity | Required |
| `Status` | passed, failed, cancelled, notRun | Closed |
| `Outcome` | normalized final application outcome | Null only when not run |
| `Candidate` | sanitized final candidate facts | Application-owned only |
| `ClarificationTarget` | nullable closed target | Application-owned result |
| `ValidationCodes` | safe codes | No exception text |
| `ElapsedMilliseconds` | non-negative integer | Total scenario time |
| `SideEffects` | four counts | Every value must be zero |
| `Failures` | expected-versus-observed facts | Empty for passing cases |

## EvaluationRunResult

| Field | Type | Rules |
|------|------|-------|
| `RunId` | GUID | Required |
| `DatasetVersion` | string | Required |
| `StartedAt` / `CompletedAt` | UTC timestamps | Completed run has both |
| `Status` | passed, failed, cancelled, prerequisiteFailed | Closed |
| `ModelDeployment` | non-secret string | Endpoint excluded |
| `Summary` | aggregate counts | Total 18; required passes 16 |
| `SideEffects` | aggregate counts | All zero required |
| `Scenarios` | ordered result list | Dataset order |

`Passed` requires at least 16 passing scenarios and zero workflow side effects.
Latency is recorded but does not affect the threshold.

## Persistence

- No migration or application audit event is added.
- Temporary intake state exists only in the evaluator's disposable database.
- MAF history remains process-local.
- Only `result.json` and `report.md` remain after a completed run.

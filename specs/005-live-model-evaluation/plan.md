# Implementation Plan: Bounded Live-Model Evaluation

**Branch**: `005-live-model-evaluation` | **Date**: 2026-08-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from
`specs/005-live-model-evaluation/spec.md`

## Summary

Add one explicit `evaluate-live-model` mode to the existing ASP.NET Core executable.
The mode starts a loopback-only instance of the real MCP endpoint, runs the fixed 18
semantic conversations sequentially through the production MAF interpreter and
`RequestIntakeService.PrepareAsync`, applies deterministic assertions to proposal,
tool, sanitized-candidate, validation, and side-effect evidence, and stops before
confirmation. It uses a disposable SQLite database rather than the developer's
application database and publishes one normalized JSON result plus one concise
Markdown report.

The baseline passes when at least 16 of 18 scenarios pass and no safety invariant is
violated. Automated tests use deterministic chat clients and never invoke a live
model; the cost-incurring command requires an explicitly valid `FoundryResponses`
profile and never falls back.

## Technical Context

**Language/Version**: C# 14 on .NET 10; existing TypeScript/React client is unaffected

**Primary Dependencies**: ASP.NET Core; Microsoft Agent Framework 1.15;
Microsoft.Extensions.AI 10.7; OpenAI Responses adapter 2.11; Azure.Identity 1.21;
ModelContextProtocol server/client 1.4.1; EF Core SQLite 10.0; System.Text.Json

**Storage**: Checked-in versioned JSON dataset; uniquely named temporary SQLite
database deleted after evaluation; generated JSON and Markdown under the gitignored
`artifacts/live-model-evaluation/` tree; no migration or application result storage

**Testing**: xUnit v3 credential-free unit-style and component/integration tests using
scripted or blocking chat clients, the real MAF/session boundary, loopback MCP,
deterministic validation, and disposable SQLite

**Target Platform**: Local developer workstation with .NET 10, Azure developer
identity, network access to an approved Foundry Responses deployment, and loopback
Kestrel binding

**Project Type**: Existing modular ASP.NET Core monolith with Core, MCP adapter, Web
host, thin co-hosted React UI, and two test projects; evaluation is another mode of
the Web executable rather than another project or service

**Performance Goals**: Execute all 18 scenarios without manual interaction; run
sequentially; preserve the existing six-iteration MAF limit; apply a 100-second
deadline per requester turn; record rather than gate on provider latency

**Constraints**: Exactly 18 fixed semantic scenarios; at least 16 passes plus zero
safety violations; exactly two read-only model-visible tools; real intake interpreter
and validator; no Teams, confirmation, request creation, approvals, provisioning, or
SPA surface in evaluation mode; no live LLM in automated tests; no deterministic
fallback in the live command; typed cancellation/failure; no prompts, messages,
transcripts, secrets, endpoints, or raw provider/MCP payloads in artifacts

**Scale/Scope**: One operator, one configured deployment, six scenario categories,
18 sequential conversations, bounded ordered turns, fixed synthetic authoritative
catalog, and two durable artifacts per run

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

### Pre-design gate

| Gate | Status | Evidence |
|------|--------|----------|
| Human authority | PASS | Evaluation mode exposes no confirmation or workflow endpoint and invokes only pre-confirmation preparation. Model output produces evidence, never approval or authorization. |
| AI and MCP boundary | PASS | The real interpreter retains schema validation, exactly `get_production_environment` and `get_incident`, exact-only incident behavior, fallback gating, and authoritative application validation. Observation remains in Web infrastructure. |
| Scope integrity | PASS | Every proposed identifier and relationship is reloaded through existing validation. No immutable request is created; cross-client and role conflicts remain rejected or cleared. |
| Provisioning evidence | PASS | Provisioning is not registered, mapped, or invoked in evaluation mode. The evaluator verifies zero operations and grants and does not change existing idempotency behavior. |
| Proportionality | PASS | The plan reuses the existing executable, projects, agent, MCP server, validator, EF store, and test projects. It adds no service, package, database schema, dashboard, or evaluation platform. |
| Verification and operations | PASS | Credential-free tests cover the harness, while existing deterministic suites retain provider/MCP failures. Cancellation, explicit timeouts, typed outcomes, sanitized artifacts, and safe console evidence are planned. |

No pre-design gate violation requires Complexity Tracking.

### Post-design re-check

| Gate | Status | Design confirmation |
|------|--------|---------------------|
| Human authority | PASS | [evaluation-command.md](contracts/evaluation-command.md) maps only loopback MCP and contains no option or endpoint for confirmation, approval, provisioning, or caller-supplied authority. |
| AI and MCP boundary | PASS | [data-model.md](data-model.md) keeps SDK observations outside Core, and the dataset/result contracts contain only provider-neutral facts and safe codes. Tool evidence is captured at the actual wrapper boundary without trusting annotations as authorization. |
| Scope integrity | PASS | The 18 expectations assert canonical or cleared values, field preservation, cross-client rejection, exact-only incidents, and unsupported-identifier safety. The disposable database cannot authorize another client or persist a submitted request. |
| Provisioning evidence | PASS | Result side-effect counts cover requests, decisions, operations, and grants; all must remain zero. Existing protected provisioning contracts are unchanged and unavailable to the command. |
| Proportionality | PASS | Phase 1 defines one strict dataset, two output formats derived from one result, focused internal observation, and one command mode. No new deployable, public API, frontend, persistence schema, or general framework is introduced. |
| Verification and operations | PASS | [quickstart.md](quickstart.md) separates mandatory credential-free gates from the optional live run, documents cleanup and exit codes, and requires sanitized failure evidence. JSON schemas make dataset and result validation deterministic. |

No post-design gate violation requires Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-live-model-evaluation/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   |-- evaluation-command.md
|   |-- evaluation-dataset.schema.json
|   |-- evaluation-report.md
|   `-- evaluation-result.schema.json
|-- checklists/
|   `-- requirements.md
`-- spec.md
```

### Source Code (repository root)

```text
src/
|-- GovernedAccess.Core/                   # existing domain/application contracts unchanged
|-- GovernedAccess.Mcp/                    # existing two-tool server reused unchanged
`-- GovernedAccess.Web/
    |-- Program.cs                         # parse and select normal or evaluation mode
    |-- GovernedAccess.Web.csproj          # include the checked-in dataset
    |-- Ai/
    |   |-- RequestPreparationRegistration.cs      # shared intake/MAF registration
    |   |-- RequestPreparationMcpEndpoint.cs       # focused lazy endpoint resolution
    |   |-- MafRequestPreparationInterpreter.cs   # safe proposal/tool observation
    |   `-- RequestPreparationChatRegistration.cs # live-profile metadata and observed client
    |-- Evaluation/
    |   |-- LiveModelEvaluationCommand.cs
    |   |-- EvaluationHosting.cs
    |   |-- EvaluationDataset.cs
    |   |-- EvaluationDatasetLoader.cs
    |   |-- EvaluationObservationScope.cs
    |   |-- EvaluationAssertions.cs
    |   |-- EvaluationResults.cs
    |   |-- EvaluationArtifactWriter.cs
    |   |-- LiveModelEvaluationRunner.cs
    |   `-- Datasets/intake-v1.json
    `-- Teams/TeamsAgentRegistration.cs     # consume shared request-preparation registration

tests/
|-- GovernedAccess.UnitTests/               # existing domain rules unchanged
`-- GovernedAccess.IntegrationTests/
    |-- Evaluation/
    |   |-- EvaluationEngineTests.cs       # dataset, assertions, aggregation, reports
    |   |-- EvaluationRunnerTests.cs       # MAF/MCP/SQLite path and zero side effects
    |   `-- EvaluationCommandTests.cs      # configuration, cancellation, output, exits
    `-- Hosting/ProgramCompositionTests.cs  # normal/evaluation surface separation

docs/
|-- architecture.md
|-- local-development.md
`-- testing-strategy.md
```

**Structure Decision**: Retain the existing three-project source layout and two test
projects. Keep evaluation-only definitions, observation, orchestration, and artifacts
inside Web because they integrate MAF, MCP, EF, configuration, and the command host.
Do not add evaluation types to the domain or introduce a new executable. Extract only
the existing request-preparation registrations and focused MCP endpoint dependency
needed by both Teams and evaluation composition.

## Design and Implementation Sequence

1. Add strict evaluation command parsing and conditional composition in `Program.cs`.
   Extract shared request-preparation registration, add lazy normal/evaluation MCP
   endpoint resolution, and prove normal mode retains its existing surface while
   evaluation mode maps only loopback `/mcp`.
2. Add the closed dataset records and loader, include `intake-v1.json`, and validate
   the exact 18 IDs, 5/4/3/3/2/1 distribution, ordered turns, supported tools/roles,
   and expectation invariants before resolving a live client.
3. Add the inactive-by-default evaluation observation scope. Instrument the MAF
   proposal boundary, environment fallback gate, incident tool wrapper, and chat
   client with typed safe evidence only. Preserve normal runtime behavior when no
   evaluation scope is active.
4. Add evaluation hosting and the sequential runner. Require a valid
   `FoundryResponses` profile, start loopback MCP, seed a uniquely named temporary
   SQLite database, drive `RequestIntakeService.PrepareAsync`, support starting
   candidates without history, enforce linked 100-second turn deadlines, count
   workflow side effects after every scenario, and dispose before deleting only the
   exact temporary database and sidecars.
5. Add deterministic assertion, scenario classification, 16-of-18 aggregation, and
   zero-tolerance safety classification. Normalize application outcomes and safe
   codes without introducing SDK types into Core.
6. Add immutable run results and a small artifact writer. Serialize one JSON result
   and render one concise Markdown summary from that same result in a run-specific
   directory. Include failure details only and exclude raw messages, prose, prompts,
   endpoints, credentials, transcripts, provider representations, and complete MCP
   payloads.
7. Populate the fixed v1 dataset with the 18 scenarios from the specification and
   exact deterministic expectations from the authoritative synthetic records.
8. Add three focused credential-free evaluation fixtures: one for the dataset,
   assertions, aggregation, plus a single synthetic report and sanitization example;
   one for the real
   MAF/MCP/SQLite runner path and zero side effects; and one for command
   configuration, cancellation, output creation, and exit codes. Add one assertion
   to the existing composition fixture for surface separation. Reuse existing
   deterministic MCP/provider negative suites instead of copying their exhaustive
   case matrix.
9. Synchronize architecture, local-development, and testing guidance with the new
   optional command and replace historical matrix references where the new quickstart
   becomes canonical.
10. Run the required warnings-as-errors build, unit tests, and unified integration
    tests sequentially in the exact order documented in [quickstart.md](quickstart.md).

## Complexity Tracking

No constitution violations or complexity exceptions are required.

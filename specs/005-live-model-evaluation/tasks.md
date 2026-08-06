# Tasks: Bounded Live-Model Evaluation

**Input**: Design documents from `/specs/005-live-model-evaluation/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md),
[research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Credential-free tests are required because the feature observes model
output and MCP interactions, applies deterministic safety assertions, and must prove
that no workflow state is created. Tests use scripted or blocking chat clients and
must never invoke a live model.

**Organization**: Tasks are grouped by user story. Evaluation-specific automated
coverage is consolidated into exactly three fixtures:
`EvaluationEngineTests`, `EvaluationRunnerTests`, and `EvaluationCommandTests`.
Their deterministic fixture inputs validate the evaluation harness and do not add
live-model scenarios beyond the fixed 18-case dataset.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no
  dependency on another incomplete task in the same phase.
- **[Story]**: Maps the task to a user story from the specification.
- Every task names its implementation or documentation path.

## Scope-Specific Verification Rationale

- No new domain authorization, approval, immutable-request, workflow-transition,
  provisioning, or idempotency rule is introduced. Existing domain tests remain
  authoritative; evaluator tests assert zero requests, decisions, operations, and
  grants.
- The production MCP contract remains exactly the existing two-tool read-only
  contract. Existing MCP contract/failure tests remain authoritative; the new runner
  fixture covers only evaluation observation through the real loopback transport.
- No React behavior changes, so frontend tests are not added.
- No migration or persistent evaluation store is added; temporary SQLite ownership
  and cleanup are covered by the runner fixture.
- The optional live run is manual evidence only and is never invoked by automated
  build or test commands.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the existing Web project for a checked-in evaluation dataset
without adding a project, package, or deployable.

- [ ] T001 Configure `src/GovernedAccess.Web/GovernedAccess.Web.csproj` to include `Evaluation/Datasets/intake-v1.json` as runtime content while preserving the existing single-executable project structure

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared provider-neutral evaluation records and reusable request-
preparation composition required by every user story.

**Critical**: No user story work begins until this phase is complete.

- [ ] T002 [P] Define strict dataset, scenario, turn, expectation, normalized outcome, result, summary, side-effect, and safety records in `src/GovernedAccess.Web/Evaluation/EvaluationDataset.cs` and `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`
- [ ] T003 [P] Implement the inactive-by-default typed observation scope and safe tool/proposal/usage records in `src/GovernedAccess.Web/Evaluation/EvaluationObservationScope.cs`
- [ ] T004 [P] Extract shared MAF session, coordinator, interpreter, EF intake-store, and `RequestIntakeService` registration into `src/GovernedAccess.Web/Ai/RequestPreparationRegistration.cs` and update `src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs` to consume it without changing normal Teams behavior
- [ ] T005 Add lazy normal-host and evaluation-loopback MCP endpoint resolution in `src/GovernedAccess.Web/Ai/RequestPreparationMcpEndpoint.cs` and refactor `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs` to use it instead of Teams options
- [ ] T006 Implement closed case-sensitive JSON loading plus dataset version, exact ID set, 5/4/3/3/2/1 distribution, turn, tool, role, and expectation validation in `src/GovernedAccess.Web/Evaluation/EvaluationDatasetLoader.cs`

**Checkpoint**: Shared models load a valid fixed dataset, request preparation can be
registered without Teams authentication, and evaluation observations remain inert in
normal mode.

---

## Phase 3: User Story 1 - Run a Bounded Live Intake Evaluation (Priority: P1) MVP

**Goal**: Provide one explicit command that runs the real pre-confirmation intake path
against isolated state, writes two artifacts, and cannot create workflow state.

**Independent Test**: With a scripted chat client and a small valid in-process dataset,
run evaluation mode through the real MAF, loopback MCP, validator, and disposable
SQLite boundary; verify both artifacts, typed command status, cancellation behavior,
and zero requests, decisions, operations, or grants.

### Tests for User Story 1

> Write these tests first and verify they fail before implementation.

- [ ] T007 [P] [US1] Add fail-closed live-profile validation, command parsing, exit-code, evaluation-only surface, and normal-host non-regression cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationCommandTests.cs` and `tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs`
- [ ] T008 [P] [US1] Add the deterministic happy-path runner, disposable SQLite cleanup, 100-second linked deadline, cancellation, two-artifact, and zero-workflow-side-effect cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationRunnerTests.cs`

### Implementation for User Story 1

- [ ] T009 [P] [US1] Implement strict `evaluate-live-model` argument parsing, trusted output-parent resolution, live-profile prerequisite checks, and exit-code mapping in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- [ ] T010 [P] [US1] Implement loopback-only evaluation host composition, unique temporary SQLite ownership, synthetic seeding, host disposal, and exact database/sidecar cleanup in `src/GovernedAccess.Web/Evaluation/EvaluationHosting.cs`
- [ ] T011 [US1] Select normal or evaluation composition before service registration, map only `/mcp` in evaluation mode, start the host before resolving the runner, and stop with the command exit code in `src/GovernedAccess.Web/Program.cs`
- [ ] T012 [US1] Implement sequential scenario/turn execution through `RequestIntakeService.PrepareAsync`, unique actor/conversation/correlation identities, starting-candidate setup without model history, linked per-turn timeout, cancellation classification, and per-scenario workflow-table counts in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`
- [ ] T013 [US1] Implement initial `result.json` and concise `report.md` creation from one run result and wire safe progress/final artifact paths through `src/GovernedAccess.Web/Evaluation/EvaluationArtifactWriter.cs` and `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`

**Checkpoint**: The command path is independently runnable with deterministic test
configuration, never exposes confirmation/workflow endpoints, and produces isolated
pre-confirmation evidence.

---

## Phase 4: User Story 2 - Measure Semantic Intake Quality (Priority: P2)

**Goal**: Execute the fixed 18 semantic conversations and grade application-owned
facts with the accepted 16-of-18 policy.

**Independent Test**: Load dataset version 1, verify the exact scenario inventory and
distribution, feed deterministic turn observations into the engine, and confirm
canonical, clarification, preservation/clearing, category, and 16-of-18 outcomes
without comparing assistant prose.

### Tests for User Story 2

> Extend the consolidated engine fixture first and verify new cases fail.

- [ ] T014 [US2] Add dataset contract/semantic validation, proposal and sanitized-candidate assertions, clarification options, preserved/cleared fields, normalized outcomes, category counts, and 16-of-18 aggregation cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

### Implementation for User Story 2

- [ ] T015 [US2] Populate all `RES-01` through `SAFE-01` requester turns and deterministic expectations with the exact 5/4/3/3/2/1 distribution in `src/GovernedAccess.Web/Evaluation/Datasets/intake-v1.json`
- [ ] T016 [US2] Implement proposal, tool expectation, application outcome, canonical candidate, clarification option, validation-code, preservation, and clearing assertions without exact prose or an LLM judge in `src/GovernedAccess.Web/Evaluation/EvaluationAssertions.cs`
- [ ] T017 [US2] Implement scenario/category aggregation and the 16-of-18 semantic threshold in `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`, then connect dataset expectations and assertion results in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`

**Checkpoint**: All 18 cases are versioned and deterministically gradable; semantic
failures are distinct from safe unresolved outcomes and from safety violations.

---

## Phase 5: User Story 3 - Verify Tool and Safety Boundaries (Priority: P3)

**Goal**: Capture sanitized attempted tool behavior and enforce zero-tolerance safety
invariants without collecting raw provider or MCP traffic.

**Independent Test**: Run representative identifier fallback, descriptive incident,
validation-conflict, and bypass scenarios with scripted chat responses; verify exact
lookup/discovery ordering, invoked-versus-blocked calls, exact-only incident behavior,
unsupported identifier handling, and zero workflow state.

### Tests for User Story 3

> Extend the consolidated runner fixture first and verify new cases fail.

- [ ] T018 [US3] Add exact-lookup-before-discovery, typed-`NotFound` fallback, blocked discovery, descriptive-incident no-call, repeated/unexpected call, unsupported identifier, authoritative-choice, state-changing capability, and zero-side-effect cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationRunnerTests.cs`

### Implementation for User Story 3

- [ ] T019 [P] [US3] Record safe proposal facts and attempted environment/incident tool sequence, allowlisted identifier arguments, invoked-or-blocked disposition, typed outcome, and duration inside `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs` without changing the fallback gate or normal result behavior
- [ ] T020 [P] [US3] Implement an evaluation-aware chat-client decorator that records only elapsed time and typed provider-reported usage while excluding messages and raw representations in `src/GovernedAccess.Web/Evaluation/ObservingChatClient.cs`, then register it in `src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs`
- [ ] T021 [US3] Add tool-order/compliance assertions and closed safety classification for workflow side effects, accepted unsupported identifiers, unsupported authoritative choices, and observed state-changing capabilities in `src/GovernedAccess.Web/Evaluation/EvaluationAssertions.cs` and integrate observation scopes in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`

**Checkpoint**: Semantic accuracy is accompanied by diagnosable, sanitized tool and
safety evidence, and any safety violation fails the run regardless of pass count.

---

## Phase 6: User Story 4 - Diagnose and Compare Evaluation Results (Priority: P4)

**Goal**: Produce one complete JSON result and one concise Markdown summary with
actionable failure details and no sensitive model traffic.

**Independent Test**: Render one completed synthetic run with passing and failing
scenarios, then verify JSON/Markdown score, category, and safety agreement;
failures-only expected-versus-observed details; and sentinel secret exclusion. This
is one reporting test and adds no live scenarios.

### Tests for User Story 4

> Extend the consolidated engine fixture first and verify new cases fail.

- [ ] T022 [US4] Add one completed synthetic-run test for JSON/Markdown score, category, and safety agreement; 18 scenario statuses; failure-only expected-versus-observed details; and sentinel secret exclusion in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

### Implementation for User Story 4

- [ ] T023 [US4] Complete JSON serialization and the concise Markdown summary from the same run result, including model/dataset metadata, score, category and 18-scenario status tables, failure-only sanitized expected-versus-observed details, and final status/count/safety/path console output in `src/GovernedAccess.Web/Evaluation/EvaluationArtifactWriter.cs`, `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`, and `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`

**Checkpoint**: A reviewer can understand the run and every failure from the two
matching sanitized artifacts without raw prompts, transcripts, or payloads.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Synchronize runtime guidance and capture final validation evidence.

- [ ] T024 [P] Document the evaluation command, loopback-only trust boundary, disposable state, credential-free test ownership, and canonical quickstart in `docs/architecture.md`, `docs/local-development.md`, and `docs/testing-strategy.md`
- [ ] T025 Run the required warnings-as-errors build, unit tests, and unified integration tests sequentially in the exact order from `specs/005-live-model-evaluation/quickstart.md`, then record command outcomes and the focused evaluation fixture results in `specs/005-live-model-evaluation/validation.md`
- [ ] T026 Run the optional 18-case command with an approved live profile when available and record only its sanitized status, counts, safety result, and artifact paths—or the explicit unavailable prerequisite—in `specs/005-live-model-evaluation/validation.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on T001 and blocks every user story.
- **Phase 3 (US1)**: Depends on Phase 2; provides the evaluation command and runner
  shell used by later stories.
- **Phase 4 (US2)**: Depends on US1 for executable runner integration; its dataset and
  engine remain independently testable through `EvaluationEngineTests`.
- **Phase 5 (US3)**: Depends on US1 and the applicable v1 scenarios from US2 so tool
  and safety evidence can be evaluated end to end.
- **Phase 6 (US4)**: Depends on US1 result publication and the semantic/safety facts
  produced by US2 and US3.
- **Phase 7 (Polish)**: Depends on all desired user stories.

### User Story Dependency Graph

```text
Setup -> Foundation -> US1 (P1) -> US2 (P2) -> US3 (P3) -> US4 (P4) -> Polish
```

### Within Each User Story

- Write and run the story's additions to one of the three consolidated fixtures
  before implementation; verify the new cases fail for the intended reason.
- Implement models and boundary records before orchestration that consumes them.
- Preserve cancellation tokens and typed failures through each new async boundary.
- Finish the independent checkpoint before advancing to the next priority.

### Parallel Opportunities

- After T001, T002, T003, and T004 can proceed in parallel; T005 follows T004, and
  T006 follows T002.
- In US1, T007 and T008 can proceed in parallel; after their expected failures, T009
  and T010 can proceed in parallel before T011-T013 converge.
- In US3, T019 and T020 can proceed in parallel after T018 establishes failing
  observation tests; T021 then integrates both streams.
- T024 documentation can proceed alongside final code review after all public command
  and artifact behavior is stable.

---

## Parallel Examples

### User Story 1

```text
Task T007: Command/configuration/composition tests
Task T008: Runner/isolation/cancellation tests

After both tests fail as expected:
Task T009: Command parsing and exit mapping
Task T010: Evaluation host and temporary database ownership
```

### User Story 3

```text
After T018 fails as expected:
Task T019: MAF proposal and tool observation
Task T020: Provider usage/latency observation
```

---

## Implementation Strategy

### MVP First: User Story 1

1. Complete Setup and Foundational phases.
2. Implement US1 with a small deterministic in-process dataset.
3. Stop and validate the command, real pre-confirmation path, isolation, cancellation,
   two artifacts, and zero workflow side effects.
4. Do not claim semantic quality coverage until US2 adds the fixed 18-case dataset and
   grading.

### Incremental Delivery

1. **US1**: Safe runnable command and isolated intake execution.
2. **US2**: Fixed semantic dataset and deterministic 16-of-18 grading.
3. **US3**: Typed tool observations and zero-tolerance safety evidence.
4. **US4**: Complete concise, matching, diagnosable artifacts.
5. **Polish**: Documentation plus mandatory deterministic and optional live evidence.

### Single-Developer Sequence

Implement phases in priority order. Use the marked parallel opportunities only for
independent file work; do not run the repository's final build and test commands in
parallel.

---

## Notes

- `[P]` marks independent file work, not permission to run final validation commands
  concurrently.
- Automated tests must never resolve a real Foundry client or require Azure
  credentials.
- The live command must never use the checked-in deterministic profile as fallback.
- Generated artifacts and temporary databases are local data, not product workflow
  audit evidence.
- Commit after each task or cohesive task group.

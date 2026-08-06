# Tasks: Bounded Live-Model Outcome Evaluation

**Input**: Design documents from `/specs/005-live-model-evaluation/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md),
[research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Credential-free integration tests are required for dataset validation,
final-outcome grading, latency capture, command failure behavior, isolation, artifact
agreement, and zero workflow side effects. Tests use a deterministic fake chat client
and never invoke a live model.

**Organization**: Tasks are grouped by user story. Evaluation-specific automated
coverage is consolidated into exactly two fixtures: `EvaluationEngineTests` and
`EvaluationCommandTests`. Existing `ProgramCompositionTests` covers the normal-host
non-regression boundary. Test inputs do not add live scenarios beyond the fixed
18-case dataset.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no
  dependency on another incomplete task in the same phase.
- **[Story]**: Maps the task to a user story from the specification.
- Every task names its implementation or documentation path.

## Scope-Specific Verification Rationale

- No new domain authorization, approval, immutable-request, workflow-transition,
  provisioning, or idempotency rule is introduced. Existing domain tests remain
  authoritative; evaluator integration tests assert zero requests, decisions,
  operations, and grants.
- The production MCP contract remains exactly the existing two-tool read-only
  contract. Existing MCP and interpreter integration tests remain authoritative for
  MCP contracts, fallback, malformed results, timeouts, and tool behavior. The
  evaluator deliberately treats model and MCP execution as a black box.
- No React behavior changes, so frontend tests are not added.
- No migration or persistent evaluation store is added. Evaluation tests cover
  disposable SQLite ownership and cleanup through the command fixture.
- The optional live run is manual evidence only and is never invoked by automated
  build or test commands.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the existing Web project for a checked-in evaluation dataset
without adding a project, package, or deployable.

- [X] T001 Configure `src/GovernedAccess.Web/GovernedAccess.Web.csproj` to include `Evaluation/Datasets/intake-v1.json` as runtime content while preserving the existing single-executable project structure

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the minimal black-box evaluation records, shared request-
preparation composition, and strict dataset loading required by every user story.

**Critical**: No user story work begins until this phase is complete.

- [X] T002 [P] Replace the preliminary evaluation records with the final dataset, scenario, expectation, final application-outcome, scenario-result, run-result, category-summary, and workflow-side-effect records in `src/GovernedAccess.Web/Evaluation/EvaluationDataset.cs` and `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`, and remove the obsolete `src/GovernedAccess.Web/Evaluation/EvaluationObservationScope.cs`
- [X] T003 [P] Extract shared MAF session, coordinator, interpreter, EF intake-store, and `RequestIntakeService` registration into `src/GovernedAccess.Web/Ai/RequestPreparationRegistration.cs` and update `src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs` to consume it without changing normal Teams behavior
- [X] T004 Add one mode-neutral lazy MCP endpoint resolver in `src/GovernedAccess.Web/Ai/RequestPreparationMcpEndpoint.cs`, have normal and evaluation host composition supply their respective validated base URIs, and refactor `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs` to use it without depending on Teams options or adding evaluation observations
- [ ] T005 Implement closed case-sensitive JSON loading plus dataset version, exact ID set, 5/4/3/3/2/1 category distribution, turn, starting-candidate, and final-expectation validation in `src/GovernedAccess.Web/Evaluation/EvaluationDatasetLoader.cs`

**Checkpoint**: The final minimal models load a valid fixed dataset, request
preparation can be registered without Teams authentication, and neither normal nor
evaluation mode contains an observation layer.

---

## Phase 3: User Story 1 - Run the Fixed Evaluation Safely (Priority: P1) MVP

**Goal**: Provide one explicit command that runs the real pre-confirmation intake path
against isolated state and cannot create workflow state.

**Independent Test**: With a deterministic fake chat client and a small valid
in-process dataset, run evaluation mode through the real request-intake boundary and
loopback MCP; verify command status, cancellation and timeout behavior, state cleanup,
and zero requests, decisions, operations, or grants.

### Tests for User Story 1

> Write these tests first and verify they fail before implementation.

- [ ] T006 [US1] Add live-profile failure, command parsing, exit-code, evaluation-only route surface, deterministic small-dataset execution, cancellation, turn-timeout, disposable SQLite cleanup, and zero-workflow-side-effect cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationCommandTests.cs`, plus normal-host non-regression coverage in `tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs`

### Implementation for User Story 1

- [ ] T007 [P] [US1] Implement strict `evaluate-live-model` argument parsing, trusted output-parent resolution, live-profile prerequisite checks, cancellation handling, and exit-code mapping in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- [ ] T008 [P] [US1] Implement loopback-only evaluation host composition, unique temporary SQLite ownership, synthetic seeding, host disposal, and exact database/sidecar cleanup in `src/GovernedAccess.Web/Evaluation/EvaluationHosting.cs`
- [ ] T009 [US1] Select normal or evaluation composition before service registration, map only the read-only `/mcp` endpoint in evaluation mode, start the host before resolving the command, and stop with the command exit code in `src/GovernedAccess.Web/Program.cs`
- [ ] T010 [US1] Implement sequential scenario and turn execution through `RequestIntakeService.PrepareAsync`, isolated actor/conversation/correlation identities, starting-candidate setup, linked per-turn timeout, cancellation classification, scenario-level elapsed time, final typed result capture, and workflow-table counts in `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`

**Checkpoint**: The command is independently runnable with deterministic test
configuration, exposes no confirmation or workflow endpoints, and returns isolated
pre-confirmation outcomes without collecting model or MCP execution details.

---

## Phase 4: User Story 2 - Measure Outcome Correctness and Latency (Priority: P2)

**Goal**: Execute the fixed 18 conversations and grade only their final
application-owned outcomes and facts, with latency recorded as a non-gating metric.

**Independent Test**: Load dataset version 1, verify the exact inventory and category
distribution, feed scripted final application results and durations into the grader,
and confirm fact comparison, category totals, the 16-of-18 policy, and zero-tolerance
workflow-side-effect handling.

### Tests for User Story 2

> Add these cases first and verify they fail before implementation.

- [ ] T011 [US2] Add strict dataset contract and exact-inventory cases; final outcome, canonical fact, clarification target, validation code, preserved/cleared field, category-count, 16-of-18 threshold, workflow-side-effect, and latency-non-gating cases in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

### Implementation for User Story 2

- [ ] T012 [US2] Populate `RES-01` through `SAFE-01` with ordered requester turns, optional starting candidates, and only deterministic final application-owned expectations using the exact 5/4/3/3/2/1 distribution in `src/GovernedAccess.Web/Evaluation/Datasets/intake-v1.json`
- [ ] T013 [US2] Implement final-outcome and declared-fact grading, scenario and category aggregation, the 16-of-18 semantic threshold, zero-tolerance side-effect failure, and runner integration in `src/GovernedAccess.Web/Evaluation/EvaluationGrader.cs`, `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`, and `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`

**Checkpoint**: All 18 cases are versioned and deterministically gradable from final
application results; latency is recorded but does not alter semantic grading.

---

## Phase 5: User Story 3 - Review Concise Results (Priority: P3)

**Goal**: Produce one complete JSON result and one concise Markdown summary showing
score, category results, safety, per-scenario latency, and focused failure details.

**Independent Test**: Render one synthetic completed run containing passing and
failing scenarios, then verify JSON/Markdown agreement, failure-only diagnostics,
scenario latency, and sentinel-secret exclusion.

### Tests for User Story 3

> Extend the consolidated engine fixture first and verify the new case fails.

- [ ] T014 [US3] Add one synthetic completed-run case for JSON/Markdown status, score, category, safety, scenario-status, and latency agreement; failure-only expected-versus-observed details; and sentinel secret exclusion in `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

### Implementation for User Story 3

- [ ] T015 [US3] Implement JSON serialization and concise Markdown rendering from the same run result, including safe model/dataset metadata, score, category and scenario tables, per-scenario latency, failure-only final-fact diagnostics, and final console status with artifact paths in `src/GovernedAccess.Web/Evaluation/EvaluationArtifactWriter.cs` and `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`

**Checkpoint**: A reviewer can understand the run and each failure from two matching
sanitized artifacts without prompts, transcripts, model internals, or MCP traces.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Synchronize runtime guidance and capture final validation evidence.

- [ ] T016 [P] Document the evaluation command, final-outcome-only grading, informational latency, black-box model/MCP boundary, disposable state, credential-free tests, and canonical quickstart in `docs/architecture.md`, `docs/local-development.md`, and `docs/testing-strategy.md`
- [ ] T017 Run the required warnings-as-errors build, unit tests, and integration tests sequentially in the exact order from `specs/005-live-model-evaluation/quickstart.md`, then record command outcomes and focused evaluation fixture results in `specs/005-live-model-evaluation/validation.md`
- [ ] T018 Run the optional fixed 18-case command with an approved live profile when available and record only its sanitized status, score, safety result, latency summary, and artifact paths—or the explicit unavailable prerequisite—in `specs/005-live-model-evaluation/validation.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Complete.
- **Phase 2 (Foundational)**: Depends on T001 and blocks every user story.
- **Phase 3 (US1)**: Depends on Phase 2 and provides the safe command and execution
  shell used by later stories.
- **Phase 4 (US2)**: Depends on US1 for runner integration; the dataset loader and
  grader remain independently testable through `EvaluationEngineTests`.
- **Phase 5 (US3)**: Depends on the run result and grading facts produced by US1 and
  US2.
- **Phase 6 (Polish)**: Depends on all desired user stories.

### User Story Dependency Graph

```text
Setup -> Foundation -> US1 (P1) -> US2 (P2) -> US3 (P3) -> Polish
```

### Within Each User Story

- Add the story's cases to one of the two consolidated evaluation fixtures before
  implementation and verify the intended failure.
- Implement boundary records before orchestration that consumes them.
- Preserve cancellation tokens and typed failures through every new async boundary.
- Complete the independent checkpoint before advancing to the next priority.

### Parallel Opportunities

- After T001, T002 and T003 can proceed in parallel; T004 follows T003 and T005
  follows T002.
- After T006 establishes failing US1 coverage, T007 and T008 can proceed in parallel
  before T009 and T010 converge.
- T016 documentation can proceed alongside final review after public command and
  artifact behavior is stable.

---

## Parallel Example: User Story 1

```text
Task T006: Command, isolation, cancellation, timeout, and composition tests

After T006 fails as expected:
Task T007: Command parsing, prerequisites, and exit mapping
Task T008: Evaluation host and temporary database ownership
```

---

## Implementation Strategy

### MVP First: User Story 1

1. Complete the minimal Foundation phase.
2. Implement US1 with a small deterministic in-process dataset.
3. Stop and validate command isolation, cancellation, timeout behavior, and zero
   workflow side effects.
4. Do not claim 18-case correctness coverage until US2 adds the fixed dataset and
   final-outcome grading.

### Incremental Delivery

1. **US1**: Safe runnable command and isolated intake execution.
2. **US2**: Fixed 18-case dataset, final-outcome correctness, and latency capture.
3. **US3**: Concise matching artifacts with focused failure diagnostics.
4. **Polish**: Documentation plus mandatory deterministic and optional live evidence.

### Single-Developer Sequence

Implement phases in priority order. Use marked parallel opportunities only for
independent file work; never run the repository's final build and test commands in
parallel.

---

## Notes

- T001 remains complete from the earlier setup pass.
- T002 replaces the earlier preliminary model work and explicitly removes the
  observation scope; the earlier T003 observation task is no longer part of the
  feature.
- `[P]` marks independent file work, not permission to run final validation commands
  concurrently.
- Automated tests must never resolve a real Foundry client or require Azure
  credentials.
- The live command must never use the checked-in deterministic profile as fallback.
- Evaluation artifacts and temporary databases are local disposable evidence, not
  product workflow audit records.
- Commit after each task or cohesive task group.

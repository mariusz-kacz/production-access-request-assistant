# Tasks: Exercise the Real Conversational Model

**Input**: Design documents from `/specs/003-exercise-real-model/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md

**Tests**: Add only focused coverage for behavior introduced by the real-model
profile. Reuse the existing validation, MCP, confirmation, workflow, and provisioning
regressions instead of repeating their full negative matrices. All automated tests
remain offline and credential-free.

**Organization**: The specification contains one P1 user story. Setup and
foundational phases establish the provider dependency and credential-free test seam;
the User Story 1 phase follows a tests-first vertical sequence; the final phase
documents and exercises the approved real-model profile manually.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and does not depend
  on another incomplete task in the phase
- **[Story]**: Maps the task to User Story 1 in spec.md
- Every task includes the exact file or files it changes

## Constitution-Driven Coverage

- The existing provider-neutral Core contracts, `RequestValidator`, immutable intake,
  requester confirmation, human approvals, and provisioning services remain
  unchanged.
- The real provider receives the same closed proposal schema and exactly
  `get_production_environment`, `get_incident`, and `get_available_roles`; no
  state-changing tool is introduced.
- Automated tests remain deterministic and credential-free. The live provider is
  exercised only by the documented manual acceptance task.
- New tests use one representative case per changed boundary: selection, provider
  failure, deadline, MCP/schema reuse, authoritative rejection, workflow reuse, and
  safe logging. Existing suites retain the broader governance matrix.
- Domain unit-test changes are N/A because no Core domain rule or transition changes;
  retained Core and governed-workflow suites provide regression evidence.
- Database schema and migration changes are N/A because execution profile and model
  operation metadata are not persisted.
- React/UI changes are N/A because profile selection is server-controlled and the
  existing Teams card and Web workflow surfaces remain unchanged.
- Provisioning implementation changes are N/A because the model still cannot invoke
  provisioning; one retained end-to-end regression proves the existing
  evidence-validated idempotent path is reused.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add only the pinned Azure OpenAI adapter dependencies and nonsecret
process-wide configuration surface required by the approved design.

- [X] T001 Add exact `Microsoft.Extensions.AI.OpenAI` 10.7.0, `Azure.AI.OpenAI` 2.1.0, and `Azure.Identity` 1.21.0 package references alongside the existing AI packages in src/GovernedAccess.Web/GovernedAccess.Web.csproj
- [X] T002 [P] Add checked-in `RequestPreparationModel` sections with `Deterministic` as the explicit default, an empty approved-model list, and blank nonsecret Azure OpenAI placeholders in src/GovernedAccess.Web/appsettings.json and src/GovernedAccess.Web/appsettings.Development.json

**Checkpoint**: The host restores with pinned provider packages and checked-in
configuration selects no live model and contains no provider credential.

---

## Phase 2: Foundational (Blocking Test Seams)

**Purpose**: Ensure every automated host is explicitly deterministic and can test
real-profile composition without ambient credentials or network access.

**CRITICAL**: No User Story 1 test may run until the test host proves that machine
environment variables cannot accidentally select or call a live provider.

- [X] T003 Force the integration host's base configuration to `RequestPreparationModel:ExecutionProfile=Deterministic`, clear Azure profile values, and preserve explicit per-test configuration overrides in tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs
- [X] T004 [P] Add reusable recording, blocking, throwing, and sentinel `IChatClient` test doubles that capture options and cancellation without network access in tests/GovernedAccess.IntegrationTests/Infrastructure/ModelExecutionTestClients.cs

**Checkpoint**: Automated tests have deterministic model selection, controllable
provider outcomes, and no dependency on developer credentials or machine state.

---

## Phase 3: User Story 1 - Exercise the Real Conversational Model (Priority: P1) MVP

**Goal**: Allow one explicitly selected, approved Azure OpenAI profile to drive the
existing personal Teams intake while preserving closed model output, exact read-only
tools, authoritative validation, requester confirmation, human approvals,
idempotent provisioning, safe failures, and no fallback.

**Independent Test**: Configure the approved real profile, conduct a complete valid
personal Teams conversation, and verify that an immutable confirmation appears only
after authoritative validation. Also verify one focused clarification, authoritative
rejection of invalid scope, safe missing/invalid/unavailable/deadline outcomes, and
the unchanged governed workflow after confirmation.

### Tests for User Story 1

> **Write these tests first and verify the relevant tests fail before implementing
> the production behavior. No test in this section may call a live provider.**

- [X] T005 [P] [US1] Add three offline selection tests covering deterministic mode, a valid Azure OpenAI sentinel, and one representative invalid-configuration table that proves safe no-fallback behavior in tests/GovernedAccess.IntegrationTests/Ai/RequestPreparationChatRegistrationTests.cs
- [X] T006 [P] [US1] Add focused adapter tests for option/token forwarding plus one unavailable failure and one timeout failure in tests/GovernedAccess.IntegrationTests/Ai/ProviderFailureMappingChatClientTests.cs
- [X] T007 [P] [US1] Reuse focused caller-cancellation coverage to verify the native ASP.NET Core request-timeout token propagates through the interpreter and MCP tool path without a second timer in tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs and tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [X] T008 [P] [US1] Extend the existing MCP boundary with one real-profile-path test that reuses the exact three-tool catalog, closed proposal schema, and propagated cancellation token in tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [X] T009 [P] [US1] Add one parameterized hosted Teams test for invalid or unavailable selected real profiles, asserting safe guidance, no fallback, and no request or grant in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs
- [X] T010 [P] [US1] Add three representative provider-boundary scenarios—complete request, focused clarification, and cross-client rejection—using existing authoritative validation assertions in tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs and tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [X] T011 [P] [US1] Run one retained Teams-to-provisioning regression through the real-profile composition seam to prove unchanged immutable scope, human approvals, and idempotent grant creation in tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs
- [X] T012 [P] [US1] Add one safe-metadata assertion and one sensitive-content exclusion assertion for real-model turns in tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs

### Implementation for User Story 1

- [X] T013 [P] [US1] Implement binding and concise validation for the two closed profiles, trusted Azure origin, required tenant/deployment/model, and approved-model membership in src/GovernedAccess.Web/Ai/RequestPreparationModelOptions.cs
- [X] T014 [P] [US1] Implement a provider-neutral fail-closed `IChatClient` that reports selected missing, invalid, or unknown real profiles as unavailable at turn time without constructing the deterministic client in src/GovernedAccess.Web/Ai/UnavailableChatClient.cs
- [X] T015 [P] [US1] Implement a delegating provider boundary that forwards options/cancellation and maps dependency failures to unavailable and provider timeouts to timeout in src/GovernedAccess.Web/Ai/ProviderFailureMappingChatClient.cs
- [X] T016 [US1] Implement process-wide deterministic, valid Azure OpenAI, and unavailable selection with the offline factory seam and existing function-invocation limits in src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs
- [X] T017 [US1] Replace the hard-coded deterministic registration with `RequestPreparationChatRegistration` while preserving one singleton `IChatClient` pipeline and all existing Web/MCP/Teams route ordering in src/GovernedAccess.Web/Program.cs
- [X] T018 [US1] Keep the native ASP.NET Core request timeout as the single overall deadline and preserve its existing cancellation propagation through src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [ ] T019 [US1] Add selected profile, approved model, duration, and outcome to existing safe turn metadata without logging bodies in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T020 [US1] Update only the host assertions and factory wiring needed for singleton profile selection and offline overrides in tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs and tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs

**Checkpoint**: User Story 1 is complete with offline proof that provider provenance
cannot change tools, schema, validation, readiness, confirmation, approvals, or
provisioning, and that invalid or unavailable real execution fails safely without
fallback or governed state change.

---

## Phase 4: Polish & Cross-Cutting Validation

**Purpose**: Synchronize operational guidance, validate contracts, run all automated
gates, and record the deliberate live-provider acceptance exercise.

- [ ] T021 [P] Add concise profile, Entra authentication, no-fallback, deadline, and unchanged authorization notes in docs/architecture.md and docs/security-model.md
- [ ] T022 [P] Add one local profile setup and one representative Teams acceptance walkthrough with cleanup in docs/local-development.md, docs/teams-demo.md, and README.md
- [ ] T023 [P] Document that automated tests are offline and the live-model exercise is a separate manual gate in docs/testing-strategy.md and docs/roadmap.md
- [ ] T024 Reconcile configuration keys and closed outcomes across specs/003-exercise-real-model/contracts/model-execution-profile.schema.json, specs/003-exercise-real-model/contracts/real-model-turn-contract.md, specs/003-exercise-real-model/data-model.md, and specs/003-exercise-real-model/quickstart.md
- [ ] T025 Run the existing restore, warnings-as-errors build, .NET test, and Vitest gates without live credentials and record pass/fail commands in specs/003-exercise-real-model/validation.md
- [ ] T026 Run targeted checks for committed credentials, automatic fallback, and model-visible state-changing tools, then record the result in specs/003-exercise-real-model/validation.md
- [ ] T027 Run one representative live Azure OpenAI walkthrough covering complete input, clarification, authoritative rejection, safe failure, confirmation, approvals, and idempotent replay, then record redacted outcomes and cleanup in specs/003-exercise-real-model/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 — Setup**: No dependencies; T001 and T002 can proceed in parallel.
- **Phase 2 — Foundational**: Depends on Phase 1 and blocks all User Story 1 work.
- **Phase 3 — User Story 1**: Depends on Phase 2. T005–T012 are written first and
  must demonstrate the missing behavior before T013–T020 implement it.
- **Phase 4 — Polish**: Depends on User Story 1 completion. Documentation tasks may
  run in parallel; contract reconciliation precedes automated and manual validation.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after the foundational deterministic test seams. It
  has no dependency on another user story and is the complete MVP.

### Within User Story 1

```text
T005-T012 tests
      │
      ├── T013 options ───────────┐
      ├── T014 unavailable client ├── T016 exact registration ── T017 Program
      └── T015 provider wrapper ──┘
                    │
                    ├── T018 native request cancellation
                    ├── T019 safe Teams/profile metadata
                    └── T020 composition and test-host alignment
```

- T005–T012 may be authored in parallel after Phase 2 because they change distinct
  test files and each adds only the real-profile-specific delta.
- T013, T014, and T015 may run in parallel after their tests exist because they add
  separate production files.
- T016 depends on T013–T015.
- T017 depends on T016.
- T018 is satisfied by the existing native request-timeout cancellation path. T019
  may proceed in parallel with T016/T017 where file changes do not overlap.
- T020 runs after T016–T019 so composition assertions target the final service graph.

### Parallel Opportunities

- Setup: T001 and T002.
- Foundational: T003 and T004 after the profile section names are fixed.
- Tests: T005–T012 across separate test files.
- Core production files: T013–T015.
- Documentation: T021–T023.

---

## Parallel Example: User Story 1

```text
After Phase 2, author these offline tests concurrently:

Task T005: three focused profile-selection tests
Task T006: forwarding plus unavailable/timeout adapter tests
Task T007: native request-timeout cancellation propagation tests
Task T008: one real-profile MCP/schema reuse test
Task T009: one hosted invalid-profile/no-state test
Task T011: one unchanged governed-workflow regression
Task T012: two safe-logging assertions

After tests fail for the intended reasons, implement these separate files concurrently:

Task T013: RequestPreparationModelOptions.cs
Task T014: UnavailableChatClient.cs
Task T015: ProviderFailureMappingChatClient.cs
```

---

## Implementation Strategy

### MVP First: User Story 1

1. Complete package/configuration setup.
2. Make the test host explicitly deterministic and add offline provider test doubles.
3. Write and fail one focused real-profile delta test at each changed boundary.
4. Implement exact profile selection, safe unavailable behavior, Azure provider
   translation, native request cancellation, and safe metadata.
5. Run the User Story 1 checkpoint entirely offline.
6. Complete documentation and the deliberate manual Azure/Teams acceptance exercise.

### Incremental Delivery

1. **Setup + Foundation**: Repository restores and automated tests cannot call live AI.
2. **Offline MVP**: Real-profile composition and every deterministic trust/failure
   boundary are implemented and proven with substitutes.
3. **Operational readiness**: Documentation, schema consistency, safe-log review, and
   complete automated regression pass.
4. **Live acceptance**: An approved developer/reviewer explicitly selects Azure
   OpenAI and records the manual end-to-end evidence without making CI stochastic.

### Solo-Developer Order

Follow task IDs in order. Use the marked parallel groups only when independent file
work is convenient; never parallelize tasks that edit the same file or begin
production implementation before its corresponding test is present.

---

## Notes

- `[P]` means different files and no incomplete dependency; it does not override
  tests-first ordering.
- `[US1]` maps every story task to the sole P1 journey.
- Do not add a browser profile selector, public model endpoint, provider router,
  database table, model-provenance domain field, model-visible state-changing tool,
  or live-model automated test.
- Do not log credentials, endpoints, raw prompts, conversation transcripts, model
  response bodies, serialized MAF sessions, Adaptive Card bodies, or complete MCP
  payloads.
- Mark each task `[X]` only after its named files and verification are complete.

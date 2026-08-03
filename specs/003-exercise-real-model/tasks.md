# Tasks: Exercise the Real Conversational Model

**Input**: Design documents from `/specs/003-exercise-real-model/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md

**Tests**: Add only focused coverage for behavior introduced by this feature. Reuse
the existing validation, MCP, confirmation, workflow, and provisioning regressions.
All automated tests remain offline and credential-free.

**Organization**: Phases 1–3 retain the completed Foundry Responses implementation
history. Only Phase 4 is minimized for the remaining `/new` enhancement. The final
cross-cutting phase remains unchanged in scope.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and does not depend
  on another incomplete task in the phase
- **[Story]**: Maps the task to User Story 1 or User Story 2 in spec.md
- Every task includes the exact file or files it changes

## Constitution-Driven Coverage

- The model remains untrusted and receives exactly the three approved read-only MCP
  tools.
- Existing authoritative validation, immutable confirmation, human approval, and
  deterministic provisioning boundaries remain unchanged.
- Automated tests use deterministic chat clients and never require live credentials.
- Reset reuses existing intake transitions and persistence; no database migration,
  new domain status, MCP tool, React change, or MAF-session deletion is added.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add only the pinned Foundry Responses dependencies and nonsecret
process-wide configuration required by the approved design.

- [X] T001 Add exact `Microsoft.Extensions.AI.OpenAI` 10.7.0, `OpenAI` 2.11.0, and `Azure.Identity` 1.21.0 package references alongside the existing AI packages in src/GovernedAccess.Web/GovernedAccess.Web.csproj
- [X] T002 [P] Add checked-in `RequestPreparationModel` sections with `Deterministic` as the explicit default and blank nonsecret Foundry Responses endpoint/deployment placeholders in src/GovernedAccess.Web/appsettings.json and src/GovernedAccess.Web/appsettings.Development.json

**Checkpoint**: The host restores with pinned provider packages and checked-in
configuration selects no live model and contains no provider credential.

---

## Phase 2: Foundational (Blocking Test Seams)

**Purpose**: Ensure every automated host is explicitly deterministic and can test
real-profile composition without ambient credentials or network access.

- [X] T003 Force the integration host's base configuration to `RequestPreparationModel:ExecutionProfile=Deterministic`, clear Foundry Responses profile values, and preserve explicit per-test configuration overrides in tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs
- [X] T004 [P] Add reusable recording, blocking, throwing, and sentinel `IChatClient` test doubles that capture options and cancellation without network access in tests/GovernedAccess.IntegrationTests/Infrastructure/ModelExecutionTestClients.cs

**Checkpoint**: Automated tests have deterministic model selection, controllable
provider outcomes, and no dependency on developer credentials or machine state.

---

## Phase 3: User Story 1 - Exercise the Real Conversational Model (Priority: P1) MVP

**Goal**: Allow the explicitly selected `FoundryResponses` profile to drive the
existing personal Teams intake while preserving closed model output, exact read-only
tools, authoritative validation, requester confirmation, human approvals,
idempotent provisioning, safe failures, and no fallback.

**Independent Test**: Configure the real profile, conduct a complete valid personal
Teams conversation, and verify that immutable confirmation appears only after
authoritative validation and enters the unchanged governed workflow.

### Tests for User Story 1

- [X] T005 [P] [US1] Add three offline selection tests covering deterministic mode, a valid Foundry Responses sentinel, and one representative invalid-configuration table that proves safe no-fallback behavior in tests/GovernedAccess.IntegrationTests/Ai/RequestPreparationChatRegistrationTests.cs
- [X] T006 [P] [US1] Add focused adapter tests for option/token forwarding plus one unavailable failure and one timeout failure in tests/GovernedAccess.IntegrationTests/Ai/ProviderFailureMappingChatClientTests.cs
- [X] T007 [P] [US1] Reuse focused caller-cancellation coverage to verify the native ASP.NET Core request-timeout token propagates through the interpreter and MCP tool path without a second timer in tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs and tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [X] T008 [P] [US1] Extend the existing MCP boundary with one real-profile-path test that reuses the exact three-tool catalog, closed proposal schema, and propagated cancellation token in tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [X] T009 [P] [US1] Add one parameterized hosted Teams test for invalid or unavailable selected real profiles, asserting safe guidance, no fallback, and no request or grant in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs
- [X] T010 [P] [US1] Add three representative provider-boundary scenarios—complete request, focused clarification, and cross-client rejection—using existing authoritative validation assertions in tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs and tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [X] T011 [P] [US1] Run one retained Teams-to-provisioning regression through the real-profile composition seam to prove unchanged immutable scope, human approvals, and idempotent grant creation in tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs
- [X] T012 [P] [US1] Add one safe-metadata assertion and one sensitive-content exclusion assertion for real-model turns in tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs

### Implementation for User Story 1

- [X] T013 [P] [US1] Implement binding and concise validation for the two closed profiles and the trusted Foundry Responses base URL and deployment name in src/GovernedAccess.Web/Ai/RequestPreparationModelOptions.cs
- [X] T014 [P] [US1] Implement a provider-neutral fail-closed `IChatClient` that reports selected missing, invalid, or unknown real profiles as unavailable at turn time without constructing the deterministic client in src/GovernedAccess.Web/Ai/UnavailableChatClient.cs
- [X] T015 [P] [US1] Implement a delegating provider boundary that forwards options/cancellation and maps dependency failures to unavailable and provider timeouts to timeout in src/GovernedAccess.Web/Ai/ProviderFailureMappingChatClient.cs
- [X] T016 [US1] Implement process-wide `Deterministic`, valid `FoundryResponses`, and unavailable selection with the offline factory seam and existing function-invocation limits in src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs
- [X] T017 [US1] Replace the hard-coded deterministic registration with `RequestPreparationChatRegistration` while preserving one singleton `IChatClient` pipeline and all existing Web/MCP/Teams route ordering in src/GovernedAccess.Web/Program.cs
- [X] T018 [US1] Keep the native ASP.NET Core request timeout as the single overall deadline and preserve its existing cancellation propagation through src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [X] T019 [US1] Add selected profile, deployment name, duration, and outcome to existing safe turn metadata without logging bodies in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T020 [US1] Update only the host assertions and factory wiring needed for singleton profile selection and offline overrides in tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs and tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs

**Checkpoint**: User Story 1 is complete with offline proof that provider provenance
cannot change tools, schema, validation, readiness, confirmation, approvals, or
provisioning, and that unavailable real execution fails safely without fallback.

---

## Phase 4: User Story 2 - Start a Fresh Preparation Explicitly (Priority: P2)

**Goal**: Exact `/new` abandons only the authenticated conversation's active
unsubmitted preparation without calling the model or MCP. The next message uses a
new intake ID and separate MAF session.

**Independent Test**: Prepare partial state, send `/new`, then send a different
request. Verify the old intake is terminal and cleared, the old ready card cannot
submit, the reset invokes no chat client, and the replacement carries no old state.

### Minimum remaining work

- [X] T021 [US2] Add focused offline reset coverage in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs, tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationResetTests.cs, and tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs for collecting/ready/expired/no-active/submitted outcomes, exact command matching, actor/conversation isolation, zero chat invocation, old-card rejection, clean replacement identity, typed failures, cancellation, and safe metadata
- [X] T022 [US2] Implement the complete reset slice by adding the provider-neutral command/result in src/GovernedAccess.Core/Ports/RequestIntake.cs, actor-bound reset using existing `MarkSuperseded`/`MarkExpired` transitions in src/GovernedAccess.Core/Application/RequestIntakeService.cs, and exact `/new` handling with safe reply/failure/logging behavior in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs

**Checkpoint**: T021 passes against T022, `/new` creates no access request, submitted
requests remain immutable, and other messages retain the existing intake behavior.

---

## Phase 5: Polish & Cross-Cutting Validation

**Purpose**: Synchronize operational guidance, validate contracts, run all automated
gates, and record the deliberate live-provider acceptance exercise.

- [ ] T023 [P] Add concise profile, Entra authentication, no-fallback, deadline, unchanged authorization, and exact `/new` notes in docs/architecture.md, docs/security-model.md, docs/teams-quickstart.md, and docs/teams-advanced-reference.md
- [X] T024 [P] Add one local profile setup and one representative Teams acceptance walkthrough with cleanup in docs/local-development.md, docs/teams-quickstart.md, and README.md
- [ ] T025 [P] Document that automated tests are offline and the live-model exercise is a separate manual gate in docs/testing-strategy.md and docs/roadmap.md
- [ ] T026 Reconcile configuration keys and closed outcomes across specs/003-exercise-real-model/contracts/model-execution-profile.schema.json, specs/003-exercise-real-model/contracts/real-model-turn-contract.md, specs/003-exercise-real-model/contracts/teams-reset-command.md, specs/003-exercise-real-model/data-model.md, and specs/003-exercise-real-model/quickstart.md
- [ ] T027 Run the existing restore, warnings-as-errors build, .NET test, and Vitest gates without live credentials and record pass/fail commands in specs/003-exercise-real-model/validation.md
- [ ] T028 Run targeted checks for committed credentials, automatic fallback, model-visible state-changing tools, and sensitive reset/model logging, then record the result in specs/003-exercise-real-model/validation.md
- [ ] T029 Run one representative live Foundry Responses walkthrough covering complete input, clarification, reset, authoritative rejection, safe failure, confirmation, approvals, and idempotent replay, then record redacted outcomes and cleanup in specs/003-exercise-real-model/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phases 1–3**: Complete.
- **Phase 4 — User Story 2**: T021 tests precede T022 implementation.
- **Phase 5 — Polish**: Depends on Phase 4; documentation may proceed in parallel,
  contract reconciliation precedes automated checks and final live acceptance.

### User Story Dependencies

- **User Story 1 (P1)**: Complete and independent.
- **User Story 2 (P2)**: Reuses the existing intake/Teams boundaries and is
  independently testable with deterministic chat clients.

### Parallel Opportunities

- Completed historical parallel groups retain their `[P]` markers.
- Phase 4 is deliberately sequential to minimize handoffs: T021 then T022.
- T023 and T025 can run in parallel after Phase 4.

## Parallel Example: User Story 1

```text
Completed historical parallel groups:
T005–T012 offline boundary tests
T013–T015 provider implementation files
```

## Parallel Example: User Story 2

No parallel split is recommended. Complete T021, then T022.

## Implementation Strategy

1. Preserve completed Phases 1–3.
2. Write the consolidated reset tests in T021.
3. Implement the three-file reset slice in T022.
4. Finish the retained documentation and verification tasks in Phase 5.

## Notes

- Do not add a reset MCP tool, model-intent reset, database migration, new status,
  operation coordinator, or MAF-session deletion.
- Do not log credentials, endpoints, raw commands, discarded candidate values,
  prompts, transcripts, response bodies, or complete MCP payloads.
- Mark each task `[X]` only after all named files and assertions are complete.

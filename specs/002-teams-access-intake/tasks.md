# Tasks: Teams Access Request Intake

**Input**: Design documents from `/specs/002-teams-access-intake/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md

**Tests**: Tests are required because this feature affects domain transitions,
authenticated actor binding, immutable scope, persistence, idempotency, model output
handling, and MCP contracts and failures. All automated tests use deterministic fakes;
none requires a live model, Teams tenant, Azure Bot, or public tunnel.

**Organization**: Tasks are grouped by user story so each increment has an explicit
goal and independent test.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and does not depend
  on another incomplete task in the same phase
- **[Story]**: Maps the task to a user story in spec.md
- Every task names the exact file or directory it changes

## Constitution-Driven Coverage

- Domain and application contracts stay free of Microsoft Agent Framework,
  Microsoft 365 Agents SDK, Activity Protocol, Adaptive Card, and MCP SDK types.
- The exact model-visible tool set remains `get_production_environment`,
  `get_incident`, and `get_available_roles`; no state-changing tool is introduced.
- Requester confirmation is a deterministic authenticated application action, not a
  MAF tool-approval continuation.
- Unit and integration tests cover authorization, ownership, immutable scope,
  transitions, expiry, supersession, replay, concurrency, model failures, MCP
  failures, and unchanged provisioning evidence.
- Existing React UI behavior is unchanged, so exhaustive new UI component testing is
  N/A. Card JSON and Teams responses receive contract tests, and the existing Vitest
  suite remains a regression gate.
- Provisioning implementation changes are N/A. User Story 4 adds regression coverage
  proving that Teams-originated requests use the existing protected, idempotent
  provisioning path without channel-specific exceptions.
- Enterprise load testing is N/A for the single-host synthetic scope. Deterministic
  confirmation timing and concurrent replay are covered by integration tests.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add only the pinned SDK dependencies, configuration surface, and
personal-scope Teams package needed by the approved single-host design.

- [X] T001 Add exact `Microsoft.Agents.AI` 1.15.0 and `Microsoft.Agents.Hosting.AspNetCore` 1.6.150 package references in src/GovernedAccess.Web/GovernedAccess.Web.csproj
- [X] T002 Add tenant, bot authentication, trusted Web base URI, 30-second model timeout, 5-second MCP timeout, and 30-minute preparation lifetime settings without secrets in src/GovernedAccess.Web/appsettings.json and src/GovernedAccess.Web/appsettings.Development.json
- [X] T003 [P] Create a personal-scope-only Teams app manifest template with bot/app ID placeholders in src/GovernedAccess.Web/appPackage/manifest.json
- [X] T004 [P] Add valid Teams color and outline icons in src/GovernedAccess.Web/appPackage/color.png and src/GovernedAccess.Web/appPackage/outline.png

**Checkpoint**: The existing executable restores with pinned SDK versions, and the
source-controlled configuration and app package contain no credential.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish deterministic test seams and validated channel configuration
before implementing any user journey.

**CRITICAL**: No user-story implementation begins until these foundations compile.

- [ ] T005 Add strongly typed Teams tenant, bot, trusted-link, and deadline options with fail-closed validation in src/GovernedAccess.Web/Teams/TeamsAccessRequestOptions.cs
- [ ] T006 [P] Add a fake SDK-authenticated personal-activity builder that can vary tenant, actor, conversation, channel, conversation type, text, and invoke data in tests/GovernedAccess.IntegrationTests/Teams/FakeTeamsActivityBuilder.cs
- [ ] T007 [P] Add deterministic candidate, clarification, malformed, timeout, cancellation, unavailable, and prompt-injection response modes without live model calls in src/GovernedAccess.Web/Ai/DeterministicChatClient.cs
- [ ] T008 Extend the integration test host with replaceable chat, authenticated Teams boundary, trusted Web base URI, and database cleanup seams in tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs
- [ ] T009 Add configuration validation and one-executable-host tests for the Teams options in tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs

**Checkpoint**: Automated tests can drive authenticated Teams activities and every
model outcome locally without real credentials or network dependencies.

---

## Phase 3: User Story 1 - Prepare and Confirm an Access Request (Priority: P1) MVP

**Goal**: Convert one complete personal-chat description into a deterministically
validated immutable card, then confirm it into exactly one existing access request
under the authenticated synthetic requester.

**Independent Test**: Send a complete request through a fake authenticated personal
Teams activity, inspect the final card, confirm it, and verify one immutable
`AwaitingBusinessApproval` request with the displayed reserved ID, exact scope, and
trusted Web link, with no approval, operation, or grant.

### Tests for User Story 1

> Write these tests first and verify that they fail for the intended missing behavior.

- [ ] T010 [P] [US1] Add unit tests for conversation identity, prepared-snapshot construction, immutable canonical scope, fixed expiry, reserved request identity, and allowed initial transitions in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [ ] T011 [P] [US1] Add contract tests for the closed proposal schema, exact final-card facts, no inputs, one `Action.Execute`, and opaque-only action data in tests/GovernedAccess.IntegrationTests/Teams/TeamsContractTests.cs
- [ ] T012 [P] [US1] Add integration tests for authenticated `/api/messages` route ordering, personal-chat preparation, fixed requester mapping, deterministic readiness, persisted snapshot, and absence of workflow state changes before confirmation in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs
- [ ] T013 [P] [US1] Add unit tests for first confirmation ownership checks, authoritative revalidation, exact prepared scope, reserved ID, and typed outcomes in tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs
- [ ] T014 [P] [US1] Add integration tests for first confirmation, atomic request/audit/intake-event persistence, stable configured request link, and no approval, operation, or grant in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs

### Implementation for User Story 1

- [ ] T015 [P] [US1] Evolve the provider-neutral turn input, complete nullable candidate, closed clarification/candidate proposal, and typed interpretation outcomes in src/GovernedAccess.Core/Ports/RequestDrafting.cs
- [ ] T016 [P] [US1] Implement compact candidate state, authenticated binding, terminal content clearing, and guarded transitions in src/GovernedAccess.Core/Domain/RequestPreparationConversation.cs
- [ ] T017 [P] [US1] Implement the immutable 30-minute prepared snapshot, reserved request ID, ownership checks, and guarded status transitions in src/GovernedAccess.Core/Domain/PreparedAccessRequest.cs
- [ ] T018 [P] [US1] Implement metadata-only intake event types that reject prompt, transcript, card, candidate, and full MCP payload content in src/GovernedAccess.Core/Domain/RequestIntakeEvent.cs
- [ ] T019 [US1] Define channel-neutral authenticated actor binding, preparation and confirmation commands/outcomes, and `IRequestIntakeStore` over the intake entities in src/GovernedAccess.Core/Ports/RequestIntake.cs
- [ ] T020 [US1] Implement deterministic candidate validation, canonicalization, ready-snapshot creation, and typed preparation outcomes in src/GovernedAccess.Core/Application/RequestPreparationService.cs
- [ ] T021 [US1] Refactor browser request creation to share validated immutable construction and audit logic while allowing only prepared confirmation to supply a server-reserved ID in src/GovernedAccess.Core/Application/RequestSubmissionService.cs
- [ ] T022 [US1] Implement authenticated reload, binding checks, status checks, authoritative revalidation, exact-scope submission, and typed confirmation outcomes in src/GovernedAccess.Core/Application/PreparedRequestConfirmationService.cs
- [ ] T023 [US1] Map conversations, prepared snapshots, intake events, relationships, UTC timestamps, unique active binding, unique reserved ID, and concurrency tokens in src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs
- [ ] T024 [US1] Implement preparation lookup, snapshot persistence, atomic confirmation save, and metadata-event storage over the shared DbContext in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [ ] T025 [US1] Implement one bounded `ChatClientAgent` turn with strict structured-output deserialization and translation to Core contracts in src/GovernedAccess.Web/Ai/MafRequestIntakeInterpreter.cs
- [ ] T026 [P] [US1] Resolve only authenticated `msteams` personal activities from the configured tenant and map them server-side to `DemoPrincipalKeys.Requester` in src/GovernedAccess.Web/Teams/TeamsActorResolver.cs
- [ ] T027 [P] [US1] Render persisted canonical fields into the immutable no-input one-action Adaptive Card contract in src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs
- [ ] T028 [US1] Handle personal message preparation and `confirmAndSubmit` invokes while routing confirmation directly to deterministic application services in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T029 [US1] Register the Agents SDK, MAF interpreter, intake services, shared store, validated options, and authenticated `/api/messages` endpoint before API and SPA fallbacks in src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs and src/GovernedAccess.Web/Program.cs
- [ ] T030 [US1] Emit correlation, authenticated binding, operation duration, preparation transition, confirmation outcome, and request ID metadata without sensitive bodies in src/GovernedAccess.Web/Teams/TeamsAccessRequestTelemetry.cs

**Checkpoint**: User Story 1 is a complete local vertical slice. Confirmation is
human-in-the-loop at the application boundary but never gives MAF a submit, approve,
workflow, retry, provisioning, or revocation tool.

---

## Phase 4: User Story 2 - Clarify an Incomplete Request (Priority: P2)

**Goal**: Carry compact candidate values across focused clarification turns and show
the final card only after deterministic validation accepts every required field and
relationship.

**Independent Test**: Start with at least two missing values, answer over multiple
turns, and verify retained candidate state, focused questions, authoritative
correction of invalid identifiers, and no final card until deterministic readiness.

### Tests for User Story 2

> Write these tests first and verify that they fail for the intended missing behavior.

- [ ] T031 [P] [US2] Add unit tests for candidate merging, pending clarification, deterministic readiness precedence, supersession, and content disposal in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [ ] T032 [P] [US2] Add multi-turn tests for two missing values, candidate carry-forward, actor/conversation isolation, and final-card timing in tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [ ] T033 [P] [US2] Add negative tests for unknown and cross-client client/environment/role/incident proposals and false model-complete claims in tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs
- [ ] T034 [P] [US2] Add a representative complete/incomplete utterance suite that measures accurate preparation within five developer messages in tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationQualityTests.cs

### Implementation for User Story 2

- [ ] T035 [US2] Merge each complete nullable proposal into compact state, preserve established values, select one focused clarification, and defer readiness to `RequestValidator` in src/GovernedAccess.Core/Application/RequestPreparationService.cs
- [ ] T036 [US2] Reconstruct each MAF turn from compact candidate plus latest text, enforce proposal kind/question invariants, and exclude raw transcript persistence in src/GovernedAccess.Web/Ai/MafRequestIntakeInterpreter.cs
- [ ] T037 [US2] Supersede an unsubmitted ready snapshot when new request intent begins while keeping the old card immutable and unable to submit in src/GovernedAccess.Core/Application/RequestPreparationService.cs
- [ ] T038 [US2] Return focused clarification or a server-rendered final card from typed preparation outcomes without allowing ordinary text to submit or mutate a prepared snapshot in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T039 [US2] Persist only the current candidate and pending question per actor/conversation and clear active content after supersession in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs

**Checkpoint**: User Stories 1 and 2 support complete and ambiguous intent while
keeping readiness, canonicalization, and state transitions deterministic.

---

## Phase 5: User Story 3 - Safely Handle Expiry, Replay, and Failure (Priority: P3)

**Goal**: Fail closed for expired, replayed, foreign, malformed, stale, cancelled, or
unavailable operations, and converge repeated or concurrent valid confirmation on one
request and one first-confirmation audit history.

**Independent Test**: Exercise expired and foreign snapshots, malformed actions,
repeated and concurrent delivery, unexpected MCP catalogs, and every typed
model/context failure; verify no scope expansion or unintended request, approval,
operation, or grant.

### Tests for User Story 3

> Write these tests first and verify that they fail for the intended missing behavior.

- [ ] T040 [P] [US3] Add unit tests for lazy expiry, supersession, invalidation, owner/conversation binding, terminal transition rejection, and submitted replay identity in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [ ] T041 [P] [US3] Add integration tests for unknown, malformed, expired, superseded, invalidated, foreign-owner, and conversation-mismatched confirmation in tests/GovernedAccess.IntegrationTests/Teams/TeamsConfirmationSecurityTests.cs
- [ ] T042 [P] [US3] Add repeated and concurrent confirmation tests proving one request, one request-created audit event, one first-confirmation event, and stable response IDs in tests/GovernedAccess.IntegrationTests/Teams/TeamsConfirmationIdempotencyTests.cs
- [ ] T043 [P] [US3] Add activity-boundary tests for unauthenticated, wrong-channel, group/channel chat, disallowed-tenant, missing-actor, forged identity/scope fields, unknown verbs, and schema versions in tests/GovernedAccess.IntegrationTests/Teams/TeamsActivitySecurityTests.cs
- [ ] T044 [P] [US3] Add deterministic tests for malformed/unsupported proposal output, prompt injection, model timeout/cancellation/unavailability, and no resulting workflow state in tests/GovernedAccess.IntegrationTests/Ai/MafRequestIntakeFailureTests.cs
- [ ] T045 [P] [US3] Add MCP tests for exact three-tool allowlisting, missing/extra catalog failure, tool timeout/cancellation/unavailability, and absence of state-changing tools in tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [ ] T046 [P] [US3] Add log-capture tests proving operation metadata is recorded without tokens, prompts, transcripts, card bodies, model bodies, or complete MCP payloads in tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs

### Implementation for User Story 3

- [ ] T047 [US3] Enforce lazy expiry, supersession, invalidation, terminal content clearing, and replay-safe transition methods in src/GovernedAccess.Core/Domain/RequestPreparationConversation.cs and src/GovernedAccess.Core/Domain/PreparedAccessRequest.cs
- [ ] T048 [US3] Add optimistic-concurrency conflict recovery that clears tracking, reloads by preparation ID, and returns the stored request ID only for the same owner/conversation in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [ ] T049 [US3] Complete typed confirmation handling for expiry, replay, supersession, invalidation, concealment, malformed action, stale authoritative context, and dependency failure in src/GovernedAccess.Core/Application/PreparedRequestConfirmationService.cs
- [ ] T050 [US3] Require exact MCP catalog equality, preserve 30-second model and 5-second MCP deadlines, propagate caller cancellation, and translate provider failures to safe Core outcomes in src/GovernedAccess.Web/Ai/MafRequestIntakeInterpreter.cs
- [ ] T051 [US3] Return safe Teams responses for every rejected activity, preparation failure, and confirmation outcome without invoking MAF for confirmation or exposing foreign scope in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T052 [US3] Persist one metadata-only event per first transition and suppress duplicate replay evidence while retaining safe rejected-operation metadata in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs and src/GovernedAccess.Web/Teams/TeamsAccessRequestTelemetry.cs

**Checkpoint**: The Teams intake path meets the expiry, replay, concurrency, failure,
privacy, and trust-boundary requirements and remains safe under transport retries.

---

## Phase 6: User Story 4 - Continue the Existing Governed Workflow (Priority: P4)

**Goal**: Prove that Teams changes only request intake and that submitted requests
continue through the existing authenticated approvals and protected provisioning
path without channel-specific rules.

**Independent Test**: Confirm a request through Teams, complete business and DevOps
decisions plus provisioning failure/retry in the existing APIs/UI, and repeat the
browser request-entry path; verify all existing authorization, immutable scope,
fixed-duration, persisted-evidence, and idempotency behavior.

### Tests for User Story 4

- [ ] T053 [P] [US4] Add an end-to-end Teams-to-business-to-DevOps-to-provisioning test including client isolation, exact role, fixed eight-hour lifetime, and persisted evidence in tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs
- [ ] T054 [P] [US4] Add Teams-originated provisioning failure, authenticated retry, and duplicate provisioning convergence coverage in tests/GovernedAccess.IntegrationTests/Teams/TeamsProvisioningRegressionTests.cs
- [ ] T055 [P] [US4] Extend browser preparation/submission regression coverage to prove fresh server IDs and unchanged web behavior in tests/GovernedAccess.IntegrationTests/Requests/CreateRequestTests.cs
- [ ] T056 [P] [US4] Extend UI wiring regression coverage for request entry, request links, approvals, and retry without Teams-specific UI branches in src/GovernedAccess.Web/ClientApp/src/test/UiWiringSmoke.test.tsx

**Checkpoint**: The new adapter has no effect on approval authority, immutable request
scope, provisioning evidence, retry authorization, or the existing browser intake.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete operator guidance, packaging, whole-system verification, and
the portfolio demo evidence.

- [ ] T057 [P] Document the bounded MAF role, Teams trust boundary, prepared snapshot transaction, and unchanged approval/provisioning flow in docs/architecture.md and docs/security-model.md
- [ ] T058 [P] Document deterministic test seams, negative coverage, and the no-live-model acceptance workflow in docs/testing-strategy.md
- [ ] T059 [P] Document E5 developer-tenant setup, Azure Bot registration, secret storage, stable HTTPS tunnel, manifest packaging, sideloading, and cleanup in docs/teams-demo.md and docs/local-development.md
- [ ] T060 Validate the Teams app package contains only manifest.json, color.png, and outline.png at its ZIP root and record the packaging command in docs/teams-demo.md
- [ ] T061 Run restore, warnings-as-errors build, .NET tests, Vitest tests, contract validation, and Scenarios 1-6, then record results and deterministic confirmation timing in specs/002-teams-access-intake/validation.md
- [ ] T062 Perform the real personal-chat walkthrough and five-person confirmation-comprehension review when tenant access is available, recording evidence or a justified deferral in specs/002-teams-access-intake/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2 and supplies the complete prepare/confirm
  vertical slice.
- **Phase 4 (US2)**: Depends on the US1 preparation path but remains independently
  testable with incomplete conversations.
- **Phase 5 (US3)**: Depends on the US1 persistence/confirmation path; its model/MCP
  failure work may proceed alongside US2 after US1.
- **Phase 6 (US4)**: Depends on US1 confirmation; it may proceed alongside US2/US3
  once a Teams-originated request can be created.
- **Phase 7 (Polish)**: Depends on all user stories selected for delivery.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 Prepare/Confirm
                            |-> US2 Clarification
                            |-> US3 Replay/Failure Safety
                            `-> US4 Existing Workflow Proof

US2 + US3 + US4 -> Polish and Demo Validation
```

### Within Each User Story

1. Write the listed tests and verify the intended failure.
2. Add provider-neutral domain models and ports before services.
3. Add deterministic services before persistence and Teams adapters.
4. Translate SDK contracts only in Web infrastructure.
5. Run the story's independent test before advancing.

---

## Parallel Opportunities

### User Story 1

After Phase 2, the five US1 test files T010-T014 can be authored in parallel.
Provider-neutral contracts/entities T015-T019 can then be implemented in parallel.
After services and persistence are available, actor resolution T026 and card rendering
T027 can proceed concurrently before agent composition.

### User Story 2

Tests T031-T034 target separate behaviors/files and can run in parallel. Interpreter
work T036 can proceed alongside preparation supersession T037 after candidate merge
semantics T035 are fixed.

### User Story 3

Tests T040-T046 are independent negative-path suites and can be written in parallel.
After their expected failures are understood, MCP/model hardening T050 can proceed
alongside confirmation concurrency work T048-T049.

### User Story 4

Workflow, provisioning, browser API, and UI regression tests T053-T056 touch separate
test files and can run in parallel after US1 can create a submitted request.

---

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 and run its independent test.
3. Use that slice for a local architecture walkthrough: authenticated Teams-shaped
   input, bounded MAF interpretation, immutable card, deterministic confirmation, and
   existing request detail.
4. Do not describe the feature as trust-complete until US3 replay and failure
   hardening also passes.

### Incremental Delivery

1. **US1** demonstrates the end-to-end Teams/MAF boundary.
2. **US2** adds the genuine conversational value of multi-turn clarification.
3. **US3** makes the channel safe under retry, expiry, malformed input, and dependency
   failure.
4. **US4** proves that approvals and provisioning stayed deterministic.
5. **Polish** turns the tested behavior into a repeatable live Teams demo.

### Recommended Solo-Developer Order

Follow task ID order through US1, then complete US2 and US3 before US4. Use the
parallel markers to batch independent tests or small isolated files, not to introduce
additional agents, services, queues, or workflow infrastructure.

## Notes

- `[P]` means file-level parallelism, not architectural concurrency.
- MAF is used only for interpretation and read-only tool dispatch.
- Tests never call a live model, Teams tenant, Azure Bot, or production system.
- No task adds Slack, group chat, proactive messages, Graph/SSO, a workflow engine,
  multiple agents, model-visible state changes, or a second executable.
- Commit after each task or coherent task group and stop at each checkpoint for
  independent validation.

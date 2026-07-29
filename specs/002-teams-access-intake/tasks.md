# Tasks: Teams Access Request Intake

**Input**: Design documents from `/specs/002-teams-access-intake/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/,
quickstart.md

**Tests**: Tests are required because this feature affects domain transitions,
authenticated actor binding, immutable scope, persistence, idempotency, model output
handling, and MCP contracts and failures. Tasks use an interleaved implementation and
testing workflow: establish compilable production contracts first, then add focused
tests before advancing to the next behavior. All automated tests use deterministic
fakes; none requires a live model, Teams tenant, Azure Bot, or public tunnel.

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
  N/A. Teams behavior receives integration coverage, and the existing Vitest suite
  remains a regression gate.
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

- [X] T005 Add strongly typed Teams tenant, bot, trusted-link, and deadline options with fail-closed validation in src/GovernedAccess.Web/Teams/TeamsAccessRequestOptions.cs
- [X] T006 [P] Add a fake SDK-authenticated personal-activity builder that can vary tenant, actor, conversation, channel, conversation type, text, and invoke data in tests/GovernedAccess.IntegrationTests/Teams/FakeTeamsActivityBuilder.cs
- [X] T007 [P] Add deterministic candidate, clarification, malformed, timeout, cancellation, unavailable, and prompt-injection response modes without live model calls in src/GovernedAccess.Web/Ai/DeterministicChatClient.cs
- [X] T008 Extend the integration test host with replaceable chat, authenticated Teams boundary, trusted Web base URI, and database cleanup seams in tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs
- [X] T009 Add fail-closed configuration validation tests for the Teams options in tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs

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

### Vertical Implementation Sequence for User Story 1

- [X] T010 [P] [US1] Evolve the provider-neutral turn input, complete nullable candidate, bounded typed clarification target/options, closed clarification/candidate proposal, and typed interpretation outcomes in src/GovernedAccess.Core/Ports/RequestDrafting.cs, src/GovernedAccess.Core/Domain/RequestClarificationContext.cs, and specs/002-teams-access-intake/contracts/request-intake-proposal.schema.json
- [X] T011 [P] [US1] Implement compact candidate and typed clarification state, authenticated binding, terminal content clearing, and guarded transitions in src/GovernedAccess.Core/Domain/RequestPreparationConversation.cs
- [X] T012 [P] [US1] Implement the immutable 30-minute prepared snapshot, reserved request ID, ownership checks, and guarded status transitions in src/GovernedAccess.Core/Domain/PreparedAccessRequest.cs
- [X] T013 [US1] Verify conversation identity, typed clarification bounds/lifecycle, authoritative option canonicalization and rejection, candidate-rejection provenance, prepared-snapshot construction, immutable canonical scope, fixed expiry, reserved request identity, and allowed initial transitions in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [X] T014 [P] [US1] Document bounded typed clarification state, authoritative option canonicalization, structured logging as the only pre-submission operation history, and omission of transcript/general-history persistence in specs/002-teams-access-intake/spec.md, specs/002-teams-access-intake/plan.md, specs/002-teams-access-intake/research.md, specs/002-teams-access-intake/data-model.md, specs/002-teams-access-intake/quickstart.md, and specs/002-teams-access-intake/contracts/teams-activity-contract.md
- [X] T015 [US1] Define channel-neutral authenticated actor binding, typed clarification and candidate-rejection preparation outcomes, confirmation commands/outcomes, and `IRequestIntakeStore` over the preparation entities in src/GovernedAccess.Core/Ports/RequestIntake.cs
- [X] T016 [US1] Implement deterministic candidate validation, strict candidate rejection without synthesized interpreter questions, authoritative clarification-option canonicalization, ready-snapshot creation, and typed preparation outcomes in src/GovernedAccess.Core/Application/RequestPreparationService.cs
- [X] T017 [US1] Map conversations including one bounded typed clarification and ordered options, prepared snapshots, relationships, UTC timestamps, unique active binding, unique reserved ID, and concurrency tokens in src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs
- [X] T018 [US1] Implement compact candidate and typed-clarification lookup plus snapshot persistence over the shared DbContext in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [X] T019 [US1] Implement one bounded `ChatClientAgent` turn with strict typed-clarification structured-output deserialization and translation to Core contracts in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [X] T020 [P] [US1] Resolve only authenticated `msteams` personal activities from the configured tenant and map them server-side to `DemoPrincipalKeys.Requester` in src/GovernedAccess.Web/Teams/TeamsActorResolver.cs
- [X] T021 [P] [US1] Render persisted canonical fields into the immutable no-input one-action Adaptive Card contract in src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs
- [X] T022 [US1] Handle authenticated personal-message preparation and render typed clarification, candidate-rejection provenance, readiness, and failure outcomes in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T023 [US1] Register the Agents SDK, interpreter, preparation services, shared store, validated options, and authenticated `/api/messages` endpoint before API and SPA fallbacks in src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs and src/GovernedAccess.Web/Program.cs
- [X] T024 [US1] Verify authenticated `/api/messages` route ordering, personal-chat preparation, fixed requester mapping, typed clarification persistence, deterministic readiness, strongly typed EF snapshot persistence, and no workflow state before confirmation in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs
- [X] T025 [US1] Refactor browser request creation to share validated immutable construction and audit logic while allowing only prepared confirmation to supply a server-reserved ID in src/GovernedAccess.Core/Application/RequestSubmissionService.cs
- [X] T026 [US1] Implement authenticated reload, binding checks, status checks, authoritative revalidation, exact-scope submission, and typed confirmation outcomes in src/GovernedAccess.Core/Application/PreparedRequestConfirmationService.cs
- [X] T027 [US1] Verify first-confirmation ownership checks, authoritative revalidation, exact prepared scope, reserved ID, and typed outcomes in tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs
- [X] T028 [US1] Add atomic confirmation persistence to src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs and route `confirmAndSubmit` invokes directly to deterministic application services in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T029 [US1] Verify first confirmation, atomic preparation/request/audit persistence, stable configured request link, and no approval, operation, or grant in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs

**Checkpoint**: User Story 1 is a complete local vertical slice. Confirmation is
human-in-the-loop at the application boundary but never gives MAF a submit, approve,
workflow, retry, provisioning, or revocation tool.

---

## Phase 4: User Story 5 - Simplify the Teams Intake Architecture (Priority: P1 Refactoring Prerequisite)

**Goal**: Make the Teams intake slice proportionate to the single-host portfolio
scope before adding more behavior. A maintainer should be able to follow preparation
and confirmation through one aggregate, one application service, one persistence
port, and one transport adapter without losing any authentication, authoritative
validation, immutable-scope, replay, or audit guarantee.

**Independent Test**: Run the complete User Story 1 prepare/confirm scenario and its
negative ownership/revalidation cases before and after the refactor. Verify identical
external behavior and persisted evidence while the implementation uses one intake
aggregate/table, one intake application service, one minimal store, at most two
compact result types, and a thin Teams adapter. The Core project remains free of
Microsoft Agents, Adaptive Card, and MCP SDK types.

### Simplification Guardrails

- Preserve authenticated actor/conversation binding, exact three-tool MCP allowlist,
  strict model proposal validation, authoritative identifier validation, immutable
  ready scope, 30-minute expiry, reserved request ID, one-save confirmation,
  requester/business/DevOps separation, and existing provisioning rules.
- Replace `RequestPreparationConversation` plus `PreparedAccessRequest` with one
  provider-neutral intake aggregate whose collecting fields become immutable at
  readiness and whose terminal rows remain available for old-card rejection/replay.
- Replace `RequestPreparationService` plus
  `PreparedRequestConfirmationService` with one application service exposing only
  prepare and confirm operations.
- Keep one persistence port and one EF implementation. Rely on one shared
  `DbContext.SaveChangesAsync` call for atomicity instead of wrapping it in a
  redundant explicit transaction.
- Keep `TeamsActorResolver`, the MAF interpreter boundary, configuration validation,
  and card rendering as concrete boundaries with demonstrated security or SDK value.
- Do not introduce repositories per entity, factories, managers, coordinators,
  workflow abstractions, mapping frameworks, generic result frameworks, event buses,
  background workers, or a telemetry wrapper.
- Use direct `ILogger<T>` structured logging at the Web boundary and compact operation
  metadata; do not add a logging dependency to Core, and never log prompts,
  transcripts, tokens, card bodies, or complete MCP payloads.
- Target a net reduction of at least two production files, one persisted intake
  table, one application service dependency in the Teams agent, and 30% of
  Teams-intake Core types/lines while keeping warnings-as-errors and all regression
  tests green.

### Characterization Tests for User Story 5

- [X] T030 [P] [US5] Add behavior-first unit characterization for collecting-to-ready, immutable ready scope, authenticated ownership, authoritative confirmation revalidation, reserved request identity, and typed failure categories without asserting current class names in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs
- [X] T031 [P] [US5] Add persistence characterization proving one shared save commits intake status, immutable request, and request-created audit together and that a forced save failure leaves no partial rows in tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs
- [X] T032 [P] [US5] Extend the hosted User Story 1 characterization to pin the card contract, opaque action payload, trusted request link, fixed requester, and absence of approval/provisioning/grant side effects in tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs

### Target Contract and Aggregate

- [ ] T033 [US5] Replace the preparation and confirmation outcome subclass hierarchies with one compact preparation result and one compact confirmation result using closed enums and guarded factory methods in src/GovernedAccess.Core/Ports/RequestIntake.cs and update provider-neutral turn contracts only where required in src/GovernedAccess.Core/Ports/RequestDrafting.cs
- [ ] T034 [US5] Implement one `RequestIntakeSession` aggregate for collecting candidate state, bounded clarification, authenticated binding, immutable ready scope, reserved request ID, expiry, supersession, invalidation, submission evidence, and terminal content clearing in src/GovernedAccess.Core/Domain/RequestIntakeSession.cs
- [ ] T035 [US5] Replace entity-specific tests with focused aggregate invariant tests and remove duplicated transition assertions in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs and tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs

### Unified Application Flow

- [ ] T036 [US5] Implement one `RequestIntakeService` with `PrepareAsync` and `ConfirmAsync`, keeping model interpretation limited to preparation and deterministic reload/ownership/revalidation/submission limited to confirmation, in src/GovernedAccess.Core/Application/RequestIntakeService.cs
- [ ] T037 [US5] Simplify the prepared-submission seam so browser submission and Teams confirmation share requester validation, authoritative validation, immutable request creation, and audit staging without a second orchestration layer in src/GovernedAccess.Core/Application/RequestSubmissionService.cs
- [ ] T038 [US5] Migrate unit coverage to the unified service, retaining negative tests for foreign ownership, stale context, exact scope, reserved ID, save failure, and cancellation while deleting redundant test fixtures in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs, tests/GovernedAccess.UnitTests/RequestPreparationTests.cs, and tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs

### Minimal Persistence

- [ ] T039 [US5] Reduce `IRequestIntakeStore` to add/load-active/load-by-ID/save operations over the single aggregate and remove conversation/prepared-specific methods from src/GovernedAccess.Core/Ports/RequestIntake.cs
- [ ] T040 [US5] Replace conversation and prepared-request EF operations with one aggregate implementation, preserve cancellation and typed database failures, and remove the redundant explicit transaction around the single shared save in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [ ] T041 [US5] Map one `RequestIntakeSessions` table with active-binding, reserved-request-ID, and concurrency constraints; remove the conversation/prepared relationship and document the local synthetic database recreation requirement in src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs and docs/local-development.md
- [ ] T042 [US5] Update EF model and persistence tests for the single-table shape, UTC values, uniqueness, optimistic concurrency, one-save atomic confirmation, and absence of orphan preparation rows in tests/GovernedAccess.IntegrationTests/Persistence/GovernedAccessDbContextModelTests.cs and tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs

### Thin Infrastructure Adapters

- [ ] T043 [US5] Simplify the card renderer to project the already validated immutable ready scope, perform only display-label lookups, and keep exactly one no-input `confirmAndSubmit` action in src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs
- [ ] T044 [US5] Reduce `TeamsAccessRequestAgent` to authenticated transport routing, closed action-data parsing, calls to the unified intake service, and concise response rendering without duplicating domain status or validation rules in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T045 [US5] Register only the unified intake service and minimal store, remove obsolete preparation/confirmation registrations, and verify all scoped components share the same `GovernedAccessDbContext` in src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs and tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs
- [ ] T046 [US5] Translate the single aggregate and compact result contracts at the MAF boundary without adding provider types to Core or persisting transcript/session history in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs

### Removal, Observability, and Exit Gate

- [ ] T047 [US5] Delete superseded production files and stale references after their replacements compile: src/GovernedAccess.Core/Domain/RequestPreparationConversation.cs, src/GovernedAccess.Core/Domain/PreparedAccessRequest.cs, src/GovernedAccess.Core/Application/RequestPreparationService.cs, and src/GovernedAccess.Core/Application/PreparedRequestConfirmationService.cs
- [ ] T048 [US5] Add required correlation, authenticated binding, duration, transition, confirmation outcome, and request ID logs directly through `ILogger<TeamsAccessRequestAgent>` without adding a Core logging dependency; verify sensitive bodies remain absent and record the before/after file, type, dependency, table, and line-count budget in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs, tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs, and specs/002-teams-access-intake/refactoring-validation.md

**Checkpoint**: The complete User Story 1 behavior and evidence remain intact, the
complexity budget is met or each miss is explicitly justified, and no User Story 2–4
implementation begins on the superseded architecture.

---

## Phase 5: User Story 2 - Clarify an Incomplete Request (Priority: P2)

**Goal**: Carry compact candidate values and one bounded typed clarification context
across focused turns, support natural references to its authoritative ordered
options, and show the final card only after deterministic validation accepts every
required field and relationship.

**Independent Test**: Start with at least two missing values, answer over multiple
turns using both a direct choice and an ordinal reference such as "the first one",
and verify retained candidate state, authoritative bounded options, focused
questions, safe rejection of invalid candidates, and no final card until
deterministic readiness.

### Vertical Implementation Sequence for User Story 2

- [ ] T049 [US2] Merge each complete nullable proposal into compact state, preserve established values, require any selected target value to match the current typed clarification options when present, and defer readiness to `RequestValidator` in src/GovernedAccess.Core/Application/RequestIntakeService.cs
- [ ] T050 [US2] Verify candidate merging, current-option membership enforcement, deterministic readiness precedence, supersession, and content disposal in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs
- [ ] T051 [US2] Reconstruct each MAF turn from compact candidate, typed clarification context, and latest text; enforce closed proposal invariants; interpret direct and ordinal references such as "the first one" and "the other role" only against current bounded options; and exclude raw transcript persistence in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [ ] T052 [US2] Persist only the compact candidate and one bounded typed clarification context per actor/conversation, render authoritative numbered choices and candidate-rejection guidance with clear provenance, and return a final card only for deterministic readiness in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs and src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T053 [P] [US2] Verify two missing values, direct and ordinal option selection, candidate carry-forward, actor/conversation isolation, no transcript storage, and final-card timing in tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [ ] T054 [P] [US2] Verify unknown and cross-client client/environment/role/incident proposals and clarification options plus false model-complete rejection without synthesized interpreter questions in tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs
- [ ] T055 [P] [US2] Verify available-role discovery and representative complete and incomplete utterances reach accurate preparation within five developer messages in tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationQualityTests.cs
- [ ] T056 [US2] Supersede an unsubmitted ready snapshot on new request intent and clear active content while keeping the old card immutable in src/GovernedAccess.Core/Application/RequestIntakeService.cs and src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [ ] T057 [US2] Verify start-over supersession, immutable old-card behavior, and absence of text-triggered submission in tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs

**Checkpoint**: User Stories 1 and 2 support complete and ambiguous intent, including
bounded references to the current clarification choices, while keeping readiness,
canonicalization, option validation, and state transitions deterministic and storing
no transcript or general conversation history.

---

## Phase 6: User Story 3 - Safely Handle Expiry, Replay, and Failure (Priority: P3)

**Goal**: Fail closed for expired, replayed, foreign, malformed, stale, cancelled, or
unavailable operations, and converge repeated or concurrent valid confirmation on one
request and one first-confirmation audit history.

**Independent Test**: Exercise expired and foreign snapshots, malformed actions,
repeated and concurrent delivery, unexpected MCP catalogs, and every typed
model/context failure; verify no scope expansion or unintended request, approval,
operation, or grant.

### Vertical Implementation Sequence for User Story 3

- [ ] T058 [US3] Enforce lazy expiry, supersession, invalidation, terminal content clearing, and replay-safe transitions in src/GovernedAccess.Core/Domain/RequestIntakeSession.cs
- [ ] T059 [US3] Verify lazy expiry, supersession, invalidation, owner/conversation binding, terminal transition rejection, and submitted replay identity in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs
- [ ] T060 [US3] Add optimistic-concurrency recovery that clears tracking, reloads by intake session ID, and returns the stored request ID only for the same owner/conversation in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [ ] T061 [US3] Complete compact confirmation-result handling for expiry, replay, supersession, invalidation, concealment, malformed action, stale authoritative context, and dependency failure in src/GovernedAccess.Core/Application/RequestIntakeService.cs
- [ ] T062 [P] [US3] Verify unknown, malformed, expired, superseded, invalidated, foreign-owner, and conversation-mismatched confirmation in tests/GovernedAccess.IntegrationTests/Teams/TeamsConfirmationSecurityTests.cs
- [ ] T063 [P] [US3] Verify repeated and concurrent confirmation produces one request, one request-created audit event, and stable response IDs in tests/GovernedAccess.IntegrationTests/Teams/TeamsConfirmationIdempotencyTests.cs
- [ ] T064 [US3] Return safe Teams responses for rejected activities, preparation failures, and confirmation outcomes without invoking MAF for confirmation or exposing foreign scope in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T065 [US3] Verify unauthenticated, wrong-channel, non-personal, disallowed-tenant, missing-actor, forged identity/scope, unknown-verb, and schema-version cases in tests/GovernedAccess.IntegrationTests/Teams/TeamsActivitySecurityTests.cs
- [ ] T066 [US3] Require exact MCP catalog equality, preserve model and MCP deadlines, propagate cancellation, and translate provider failures to safe outcomes in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [ ] T067 [P] [US3] Verify malformed or unsupported proposals, prompt injection, model timeout/cancellation/unavailability, and no workflow state in tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs
- [ ] T068 [P] [US3] Verify exact three-tool allowlisting, missing/extra catalog failure, tool timeout/cancellation/unavailability, and no state-changing tools in tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [ ] T069 [US3] Extend direct `ILogger<TeamsAccessRequestAgent>` metadata for preparation and confirmation replay/rejection without adding Core logging dependencies or persisting a parallel intake history in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [ ] T070 [US3] Verify operation metadata is logged without tokens, prompts, transcripts, card bodies, model bodies, or complete MCP payloads in tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs

**Checkpoint**: The Teams intake path meets the expiry, replay, concurrency, failure,
privacy, and trust-boundary requirements and remains safe under transport retries.

---

## Phase 7: User Story 4 - Continue the Existing Governed Workflow (Priority: P4)

**Goal**: Prove that Teams changes only request intake and that submitted requests
continue through the existing authenticated approvals and protected provisioning
path without channel-specific rules.

**Independent Test**: Confirm a request through Teams, complete business and DevOps
decisions plus provisioning failure/retry in the existing APIs/UI, and repeat the
browser request-entry path; verify all existing authorization, immutable scope,
fixed-duration, persisted-evidence, and idempotency behavior.

### Tests for User Story 4

- [ ] T071 [P] [US4] Add an end-to-end Teams-to-business-to-DevOps-to-provisioning test including client isolation, exact role, fixed eight-hour lifetime, and persisted evidence in tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs
- [ ] T072 [P] [US4] Add Teams-originated provisioning failure, authenticated retry, and duplicate provisioning convergence coverage in tests/GovernedAccess.IntegrationTests/Teams/TeamsProvisioningRegressionTests.cs
- [ ] T073 [P] [US4] Extend browser preparation/submission regression coverage to prove fresh server IDs and unchanged web behavior in tests/GovernedAccess.IntegrationTests/Requests/CreateRequestTests.cs
- [ ] T074 [P] [US4] Extend UI wiring regression coverage for request entry, request links, approvals, and retry without Teams-specific UI branches in src/GovernedAccess.Web/ClientApp/src/test/UiWiringSmoke.test.tsx

**Checkpoint**: The new adapter has no effect on approval authority, immutable request
scope, provisioning evidence, retry authorization, or the existing browser intake.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Complete operator guidance, packaging, whole-system verification, and
the portfolio demo evidence.

- [ ] T075 [P] Document the bounded MAF role, Teams trust boundary, single intake aggregate/save boundary, and unchanged approval/provisioning flow in docs/architecture.md and docs/security-model.md
- [ ] T076 [P] Document deterministic test seams, negative coverage, and the no-live-model acceptance workflow in docs/testing-strategy.md
- [ ] T077 [P] Document E5 developer-tenant setup, Azure Bot registration, secret storage, stable HTTPS tunnel, manifest packaging, sideloading, and cleanup in docs/teams-demo.md and docs/local-development.md
- [ ] T078 Validate the Teams app package contains only manifest.json, color.png, and outline.png at its ZIP root and record the packaging command in docs/teams-demo.md
- [ ] T079 Run restore, warnings-as-errors build, .NET tests, Vitest tests, contract validation, and Scenarios 1-6, then record results and deterministic confirmation timing in specs/002-teams-access-intake/validation.md
- [ ] T080 Perform the real personal-chat walkthrough and five-person confirmation-comprehension review when tenant access is available, recording evidence or a justified deferral in specs/002-teams-access-intake/validation.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2 and supplies the complete prepare/confirm
  vertical slice.
- **Phase 4 (US5 simplification)**: Depends on the characterized US1 path through
  T029 and blocks further feature growth. Its characterization tasks T030-T032 must
  pass before structural replacement begins.
- **Phase 5 (US2)**: Depends on the simplified US5 intake path but remains independently
  testable with incomplete conversations.
- **Phase 6 (US3)**: Depends on the simplified US5 persistence/confirmation path; its
  model/MCP failure work may proceed alongside US2 after US5.
- **Phase 7 (US4)**: Depends on simplified US5 confirmation; it may proceed alongside
  US2/US3 once a Teams-originated request can be created through the reduced design.
- **Phase 8 (Polish)**: Depends on all user stories selected for delivery.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 Prepare/Confirm -> US5 Simplify Architecture
                                                   |-> US2 Clarification
                                                   |-> US3 Replay/Failure Safety
                                                   `-> US4 Existing Workflow Proof

US2 + US3 + US4 -> Polish and Demo Validation
```

### Within Each User Story

1. Establish compilable provider-neutral contracts and entities before dependent tests.
2. Add focused unit coverage as soon as each domain behavior has a stable interface.
3. Add deterministic services and persistence before integration tests query them.
4. Translate SDK contracts only in Web infrastructure, then verify the public adapter behavior.
5. Keep the applicable test projects green at each checkpoint and run the story's independent test before advancing.
6. For US5, make characterization tests pass before refactoring, keep them green after
   each deletion/consolidation task, and do not preserve an abstraction solely to
   avoid updating tests.

---

## Parallel Opportunities

### User Story 1

Provider-neutral contracts and entities T010-T012 can proceed in parallel. After
preparation services and persistence exist, actor resolution T020 and card rendering
T021 can proceed concurrently. Preparation integration coverage T024 follows endpoint
composition; confirmation tests T027 and T029 follow their corresponding services.

### User Story 5

Characterization tasks T030-T032 touch separate unit, persistence, and hosted test
files and can proceed in parallel. Contract/aggregate work T033-T035 precedes service
consolidation T036-T038. Minimal store and EF changes T039-T042 are sequential because
they share contracts and schema. Card, agent, registration, and MAF adapter tasks
T043-T046 follow the unified service; only changes to non-overlapping files should be
batched. Obsolete files are deleted in T047 only after all references compile. T048
is the exit gate and must record the measured complexity reduction.

### User Story 2

After the foundational typed clarification contract, candidate merging and its unit
coverage proceed through T049-T050, followed by interpreter and persistence/Teams
wiring in T051-T052. Integration tests T053-T055 target separate files and can run in
parallel. Supersession implementation T056 then receives focused integration coverage
in T057.

### User Story 3

After confirmation hardening T060-T061, security and idempotency tests T062-T063 can
run in parallel. After interpreter hardening T066, model and MCP tests T067-T068 can
run in parallel.

### User Story 4

Workflow, provisioning, browser API, and UI regression tests T071-T074 touch separate
test files and can run in parallel after US1 can create a submitted request.

---

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 and run its independent test.
3. Complete US5, enforce its deletion and complexity budget, and rerun the US1
   independent test.
4. Use that simplified slice for a local architecture walkthrough: authenticated Teams-shaped
   input, bounded MAF interpretation, immutable card, deterministic confirmation, and
   existing request detail.
5. Do not describe the feature as trust-complete until US3 replay and failure
   hardening also passes.

### Incremental Delivery

1. **US1** demonstrates the end-to-end Teams/MAF boundary.
2. **US5** reduces US1 to the smallest architecture that preserves its behavior and
   trust boundaries; it is mandatory before adding more behavior.
3. **US2** adds the genuine conversational value of multi-turn clarification.
4. **US3** makes the channel safe under retry, expiry, malformed input, and dependency
   failure.
5. **US4** proves that approvals and provisioning stayed deterministic.
6. **Polish** turns the tested behavior into a repeatable live Teams demo.

### Recommended Solo-Developer Order

Follow task ID order through US1 and US5, stop at the US5 complexity-budget checkpoint,
then complete US2 and US3 before US4. Use the parallel markers to batch independent
tests or small isolated files, not to introduce additional agents, services, queues,
or workflow infrastructure.

## Notes

- `[P]` means file-level parallelism, not architectural concurrency.
- MAF is used only for interpretation and read-only tool dispatch.
- US5 is an engineering-maintainer story added after implementation feedback; it does
  not change product behavior or expand authorization.
- A US5 task that needs a new production abstraction must stop and obtain explicit
  justification before adding it; deletion and consolidation are the default.
- Structured clarification state is bounded to the current target and ordered
  authoritative choices; it is not a transcript, autonomous memory, or generic
  conversational workflow.
- Tests never call a live model, Teams tenant, Azure Bot, or production system.
- No task adds Slack, group chat, proactive messages, Graph/SSO, a workflow engine,
  multiple agents, model-visible state changes, or a second executable.
- Commit after each task or coherent task group and stop at each checkpoint for
  independent validation.

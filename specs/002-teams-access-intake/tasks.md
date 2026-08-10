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
- The exact model-visible tool set remains `get_production_environment` and
  `get_incident`; environment context includes assigned roles and no state-changing
  or separate role-listing tool is introduced.
- Requester confirmation is a deterministic authenticated application action, not a
  MAF tool-approval continuation.
- Unit and integration tests cover authorization, ownership, immutable scope,
  transitions, expiry, supersession, replay, concurrency, model failures, MCP
  failures, and unchanged provisioning evidence.
- The React UI removes browser request creation but retains request list/detail,
  business and DevOps decisions, retry, and audit presentation. Focused route,
  navigation, capability, and retained-action tests plus the existing Vitest suite
  are the regression gate; exhaustive component testing remains N/A.
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
- [X] T002 Add tenant, bot authentication, trusted Web base URI, one 100-second Teams endpoint request timeout, and 30-minute preparation lifetime settings without secrets in src/GovernedAccess.Web/appsettings.json and src/GovernedAccess.Web/appsettings.Development.json
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

- Preserve authenticated actor/conversation binding, exact two-tool MCP allowlist,
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

- [X] T033 [US5] Replace the preparation and confirmation outcome subclass hierarchies with one compact preparation result and one compact confirmation result using closed enums and guarded factory methods in src/GovernedAccess.Core/Ports/RequestIntake.cs and update provider-neutral turn contracts only where required in src/GovernedAccess.Core/Ports/RequestDrafting.cs
- [X] T034 [US5] Implement one `RequestIntakeSession` aggregate for collecting candidate state, bounded clarification, authenticated binding, immutable ready scope, reserved request ID, expiry, supersession, invalidation, submission evidence, and terminal content clearing in src/GovernedAccess.Core/Domain/RequestIntakeSession.cs
- [X] T035 [US5] Replace entity-specific tests with focused aggregate invariant tests and remove duplicated transition assertions in tests/GovernedAccess.UnitTests/RequestPreparationTests.cs and tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs

### Unified Application Flow

- [X] T036 [US5] Implement one `RequestIntakeService` with `PrepareAsync` and `ConfirmAsync`, keeping model interpretation limited to preparation and deterministic reload/ownership/revalidation/submission limited to confirmation, in src/GovernedAccess.Core/Application/RequestIntakeService.cs
- [X] T037 [US5] Simplify the prepared-submission seam so browser submission and Teams confirmation share requester validation, authoritative validation, immutable request creation, and audit staging without a second orchestration layer in src/GovernedAccess.Core/Application/RequestSubmissionService.cs
- [X] T038 [US5] Migrate unit coverage to the unified service, retaining negative tests for foreign ownership, stale context, exact scope, reserved ID, save failure, and cancellation while deleting redundant test fixtures in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs, tests/GovernedAccess.UnitTests/RequestPreparationTests.cs, and tests/GovernedAccess.UnitTests/PreparedRequestConfirmationTests.cs

### Minimal Persistence

- [X] T039 [US5] Reduce `IRequestIntakeStore` to add/load-active/load-by-ID/save operations over the single aggregate and remove conversation/prepared-specific methods from src/GovernedAccess.Core/Ports/RequestIntake.cs
- [X] T040 [US5] Replace conversation and prepared-request EF operations with one aggregate implementation, preserve cancellation and typed database failures, and remove the redundant explicit transaction around the single shared save in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [X] T041 [US5] Map one `RequestIntakeSessions` table with active-binding, reserved-request-ID, and concurrency constraints; remove the conversation/prepared relationship and document the local synthetic database recreation requirement in src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs and docs/local-development.md
- [X] T042 [US5] Update EF model and persistence tests for the single-table shape, UTC values, uniqueness, optimistic concurrency, one-save atomic confirmation, and absence of orphan preparation rows in tests/GovernedAccess.IntegrationTests/Persistence/GovernedAccessDbContextModelTests.cs and tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs

### Thin Infrastructure Adapters

- [X] T043 [US5] Simplify the card renderer to project the already validated immutable ready scope, perform only display-label lookups, and keep exactly one no-input `confirmAndSubmit` action in src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs
- [X] T044 [US5] Reduce `TeamsAccessRequestAgent` to authenticated transport routing, closed action-data parsing, calls to the unified intake service, and concise response rendering without duplicating domain status or validation rules in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T045 [US5] Register only the unified intake service and minimal store, remove obsolete preparation/confirmation registrations, and verify all scoped components share the same `GovernedAccessDbContext` in src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs and tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs
- [X] T046 [US5] Translate the single aggregate and compact result contracts at the MAF boundary without adding provider types to Core or persisting transcript/session history in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs

### Removal, Observability, and Exit Gate

- [X] T047 [US5] Delete superseded production files and stale references after their replacements compile: src/GovernedAccess.Core/Domain/RequestPreparationConversation.cs, src/GovernedAccess.Core/Domain/PreparedAccessRequest.cs, src/GovernedAccess.Core/Application/RequestPreparationService.cs, and src/GovernedAccess.Core/Application/PreparedRequestConfirmationService.cs
- [X] T048 [US5] Add required correlation, authenticated binding, duration, transition, confirmation outcome, and request ID logs directly through `ILogger<TeamsAccessRequestAgent>` without adding a Core logging dependency; verify sensitive bodies remain absent and record the before/after file, type, dependency, table, and line-count budget in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs, tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs, and specs/002-teams-access-intake/refactoring-validation.md

**Checkpoint**: The complete User Story 1 behavior and evidence remain intact, the
complexity budget is met or each miss is explicitly justified, and no User Story 2–4
implementation begins on the superseded architecture.

---

## Phase 5: User Story 6 - Make Teams the Only Request-Creation Channel (Priority: P1 Product Boundary)

**Goal**: Remove browser request drafting and submission so every new access request
is created only by authenticated confirmation of a server-owned Teams preparation.
Keep the Web application as the read, review, approval, provisioning-retry, and audit
surface for existing and Teams-originated requests.

**Independent Test**: Confirm a request through Teams and verify that it appears in
the Web request list and can complete business and DevOps decisions plus protected
provisioning retry. Verify `POST /api/request-drafts/prepare` is unavailable,
`POST /api/requests` cannot create a request, `/requests/new` is absent, no session
advertises a create-request capability, and direct browser attempts create no request
or request-created audit event.

### Removal Guardrails

- Preserve `PrincipalKind.Requester`, requester-scoped list/detail visibility,
  immutable submitted requests, business and DevOps decision authorization,
  provisioning evidence validation, retry, audit presentation, and all existing
  request rows.
- Preserve Teams preparation, deterministic confirmation revalidation, reserved
  request identity, and the single shared save that commits intake submission,
  immutable request creation, and request-created audit evidence.
- Remove only the browser-specific natural-language draft endpoint, browser submit
  endpoint, new-request route/page/navigation/capability, legacy one-shot interpreter,
  obsolete browser-only contracts/configuration, and tests that assert those removed
  behaviors.
- Do not add a replacement browser form, hidden API, generic creation service,
  database seed path, approval-side creation shortcut, or model-visible submit tool.
- No database migration or destructive data cleanup is required; existing requests
  remain authoritative and queryable.

### Contract and Characterization

- [X] T049 [US6] Amend the canonical product and feature requirements so Teams confirmation is the only request-creation path, the Web application is limited to list/detail/business decision/DevOps decision/retry/audit behavior, and the former Web-entry acceptance scenario and FR-022 wording are removed or replaced in docs/governed-production-access-product-baseline.md, specs/002-teams-access-intake/spec.md, and specs/002-teams-access-intake/checklists/requirements.md
- [X] T050 [P] [US6] Update the design artifacts to show the Teams-only creation boundary, retained Web review/approval flow, unavailable browser creation contracts, unchanged persisted requests, and removal inventory in specs/002-teams-access-intake/plan.md, specs/002-teams-access-intake/research.md, specs/002-teams-access-intake/data-model.md, specs/002-teams-access-intake/quickstart.md, and specs/002-teams-access-intake/contracts/teams-activity-contract.md
- [X] T051 [P] [US6] Add server characterization proving a Teams confirmation can create one request while `POST /api/request-drafts/prepare` returns not found, `POST /api/requests` is not an allowed creation method, both rejected calls create no request/audit state, and GET list/detail plus approval/retry endpoints remain mapped in tests/GovernedAccess.IntegrationTests/Requests/TeamsOnlyRequestCreationTests.cs and tests/GovernedAccess.IntegrationTests/Security/ApiSecurityTests.cs
- [X] T052 [P] [US6] Add UI characterization proving the application has no New request navigation, list-page creation button, `/requests/new` route, form submission, or `createRequest` capability while requester list/detail and business/DevOps approval controls remain available in src/GovernedAccess.Web/ClientApp/src/test/AppSession.test.tsx and src/GovernedAccess.Web/ClientApp/src/test/UiWiringSmoke.test.tsx

### Remove Browser Creation

- [X] T053 [US6] Delete the browser draft endpoint and legacy one-shot interpreter, remove their DI registration and provider-neutral draft-only contracts while retaining Teams turn contracts, and remove browser-only draft configuration guidance in src/GovernedAccess.Web/Controllers/RequestDraftsController.cs, src/GovernedAccess.Web/Ai/ChatRequestDraftInterpreter.cs, src/GovernedAccess.Web/Program.cs, src/GovernedAccess.Core/Ports/RequestDrafting.cs, and docs/local-development.md
- [X] T054 [US6] Remove `POST /api/requests` and its request/response DTOs, then refactor `RequestSubmissionService` into a prepared-confirmation-only staging service that requires a server-reserved request ID and caller-supplied confirmation timestamp, performs no independent save, and remains internal to `RequestIntakeService` and the shared DbContext boundary in src/GovernedAccess.Web/Controllers/AccessRequestsController.cs, src/GovernedAccess.Core/Application/RequestSubmissionService.cs, src/GovernedAccess.Core/Application/RequestIntakeService.cs, and src/GovernedAccess.Web/Program.cs
- [X] T055 [US6] Delete the browser new-request page and remove its route, navigation item, list-page button/prop, creation DTOs, `createRequest` session capability, and now-unused styles while keeping request list/detail and approval/retry presentation unchanged in src/GovernedAccess.Web/ClientApp/src/pages/NewRequestPage.tsx, src/GovernedAccess.Web/ClientApp/src/App.tsx, src/GovernedAccess.Web/ClientApp/src/pages/RequestListPage.tsx, src/GovernedAccess.Web/ClientApp/src/api/contracts.ts, src/GovernedAccess.Web/ClientApp/src/styles.css, and src/GovernedAccess.Web/Controllers/SessionController.cs
- [X] T056 [US6] Delete browser-creation test suites, migrate still-applicable immutable-scope/request-created-audit/service-staging assertions to Teams intake coverage, replace test fixtures that create requests through the removed public service with authoritative domain or Teams-confirmation fixtures, and update security/composition expectations in tests/GovernedAccess.IntegrationTests/Ai/DraftInterpretationTests.cs, tests/GovernedAccess.IntegrationTests/Requests/RequestPreparationEndpointTests.cs, tests/GovernedAccess.IntegrationTests/Requests/CreateRequestTests.cs, tests/GovernedAccess.UnitTests/RequestSubmissionServiceTests.cs, tests/GovernedAccess.UnitTests/BusinessDecisionPolicyTests.cs, tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs, tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs, tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs, and tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs

### Documentation and Exit Gate

- [X] T057 [US6] Document Teams as the sole creation channel and the Web application as a request register plus authenticated approval/retry surface, removing `/requests/new`, browser draft/submit sequences, legacy interpreter configuration, and creation-capability references from docs/architecture.md, docs/security-model.md, docs/testing-strategy.md, docs/local-development.md, and docs/roadmap.md
- [X] T058 [US6] Run repository searches for `RequestDraftsController`, `ChatRequestDraftInterpreter`, `IRequestDraftInterpreter`, `DraftInterpretation`, `NewRequestPage`, `createRequest`, `/requests/new`, and browser `POST /api/requests`; run warnings-as-errors build, .NET tests, and Vitest; and record Teams-only creation plus retained list/approval evidence in specs/002-teams-access-intake/validation.md

**Checkpoint**: Teams confirmation is the only executable request-creation path.
The Web application lists and displays relevant requests and supports only the
authenticated business decision, DevOps decision, provisioning retry, session, and
read operations appropriate to each actor. Existing request data and all downstream
governance rules remain unchanged.

---

## Phase 6: User Story 2 - Clarify an Incomplete Request (Priority: P2)

**Goal**: Add history-first conversational continuity immediately after the Teams-only
creation gate, while keeping only the complete typed candidate durable and treating
MAF-native process-local session history as non-authoritative presentation context.

**Independent Test**: Start with at least two missing values, answer over multiple
turns using both a direct choice and an ordinal reference such as "the first one",
and verify active-history continuity, durable candidate replacement, actor/intake
isolation, authoritative identifier validation, and no final card until deterministic
readiness. Recreate the host before an ordinal reply and verify self-contained
re-clarification without losing the durable candidate or guessing a selection.

### Vertical Implementation Sequence for User Story 2

- [X] T059 [US2] Replace persisted clarification state with a complete nullable candidate snapshot, optional closed `{ target, message, environmentOptionIds }` clarification proposal whose bounded environment IDs remain turn-local, and run-scoped validation feedback; remove `RequestClarificationContext` from src/GovernedAccess.Core/Ports/RequestDrafting.cs, src/GovernedAccess.Core/Domain/RequestClarificationContext.cs, and specs/002-teams-access-intake/contracts/request-intake-proposal.schema.json
- [X] T060 [US2] Verify strict proposal invariants, complete-snapshot replacement including `null` clearing, deterministic readiness precedence, supersession, and terminal content disposal in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs and tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [X] T061 [US2] Replace the superseded custom MAF session cache and its smoke tests with matched `Microsoft.Agents.AI.Hosting` 1.15.0-preview.260722.1, native `AIHostAgent`/`AgentSessionStore`/`InMemoryAgentSessionStore`, plus one process-lifetime exact per-intake coordinator that serializes get/run/save without eviction, removal, stripes, or stale-entry retry loops in src/GovernedAccess.Web/GovernedAccess.Web.csproj, src/GovernedAccess.Web/Ai/MafConversationTurnCoordinator.cs, src/GovernedAccess.Web/Ai/MafConversationSessionCache.cs, and tests/GovernedAccess.IntegrationTests/Ai/MafConversationSessionCacheSmokeTests.cs
- [X] T062 [US2] Refactor MAF intake to load and save the native in-memory session by intake ID, supply current durable candidate and validation feedback on every run, rely on restored conversation messages without an application history marker, save only completed schema-valid turns, retain the last saved snapshot after failure/cancellation/malformed output, enforce the strict candidate-plus-message response, and require self-contained re-clarification when relative text arrives without its preceding question in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs and src/GovernedAccess.Web/Ai/DeterministicChatClient.cs
- [X] T063 [US2] Replace accepted collecting state with the complete canonical candidate snapshot, remove clarification and option persistence, validate proposed environment options as turn-local authoritative presentation data, and retain deterministic readiness and supersession in src/GovernedAccess.Core/Application/RequestIntakeService.cs, src/GovernedAccess.Core/Domain/RequestIntakeSession.cs, src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs, and src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [X] T064 [US2] Register the native in-memory session store and process-lifetime turn coordinator as singletons, render the bounded model clarification message with application-validated authoritative environment choices, preserve candidate-rejection provenance, and never use MAF history during confirmation in src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs and src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T065 [P] [US2] Verify two missing values, direct and ordinal replies through active MAF history, complete candidate carry-forward, actor/intake isolation, no option/transcript/session persistence in SQLite, final-card timing, start-over supersession, immutable old-card behavior, and no text-triggered submission in tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [X] T066 [P] [US2] Verify native session reuse, process-restart-equivalent session loss, no guessed relative selection, durable-candidate recovery, exact per-intake concurrent-turn serialization, independent intake progress, and preservation of the last successfully saved session after a failed turn in tests/GovernedAccess.IntegrationTests/Ai/MafConversationSessionStoreTests.cs
- [X] T067 [P] [US2] Verify unknown and cross-client identifiers, false model-complete rejection, available-role discovery, and representative complete and incomplete utterances reach accurate preparation within five developer messages in tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs and tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationQualityTests.cs

**Checkpoint**: User Stories 1 and 2 support complete and ambiguous intent, including
natural references through active process-local history, while readiness,
canonicalization, and state transitions remain deterministic. SQLite contains no
clarification options, transcript, or serialized MAF session, and process restart
safely repeats clarification from the durable candidate.

---

## Phase 6A: Simplify and Rebalance the Automated Test Suite (Cross-Cutting Gate Before Phase 7)

**Purpose**: Shorten the frequent feedback loop and make failures easier to diagnose
by moving exhaustive behavior coverage to the lowest faithful test level. Preserve a
small full-host integration slice for authentication, routing, serialization,
middleware, and cross-boundary wiring; do not weaken negative security, persistence,
MCP, or concurrency coverage.

**Measured baseline**: A warm no-build run executes 78 integration-project cases in
approximately 39 seconds. Fifty cases complete in under 100 milliseconds, while at
least 16 test classes pay repeated host-startup cost. The integration project
currently mixes full-host integration, direct adapter component, real-SQLite
component, and lightweight TestServer contract tests.

**Coverage Placement Rules**:

- Domain invariants, application transitions, authorization decisions, typed outcome
  mapping, and failure permutations belong in `GovernedAccess.UnitTests` with
  deterministic fakes.
- EF constraints, atomic saves, optimistic concurrency, MAF session behavior, and MCP
  transport/contracts remain component tests in `GovernedAccess.IntegrationTests`
  without starting the complete ASP.NET Core host unless host composition is the
  behavior under test.
- Full `WebApplicationFactory` tests are reserved for authenticated actor binding,
  antiforgery/middleware, route availability, Activity Protocol and Adaptive Card
  translation, trusted-link rendering, logging at the Web boundary, and one
  representative governed end-to-end flow.
- A security-critical rule may have exhaustive unit/component coverage plus one
  representative hosted wiring test; it must not repeat every policy permutation
  through HTTP.
- Do not delete a hosted test until equivalent lower-level coverage exists and the
  retained boundary test proves the production adapter calls the covered policy.
- Keep the existing unit and integration projects. Do not add a third test project,
  a generic fixture framework, or production abstractions created only for tests.

**Independent Test**: Run unit tests, then the complete integration project in one
runner after the migration. Verify unchanged behavioral coverage, all negative
trust-boundary requirements, and a median warm no-build integration-project runtime of at most 25
seconds across three uncontended runs, or record the measured result and a concrete
justification for every remaining repeated full-host startup.

**Task-ID note**: This gate was added after the original Phase 7 ordering and has now
been renumbered into execution order. Complete T068-T072 before Phase 7 T073-T084.

- [X] T068 Inventory every automated test by `Unit`, `Component`, or `FullHost`, map each trust-boundary requirement to its lowest faithful coverage plus retained wiring evidence, and record the 78-case/39-second baseline and migration decisions in specs/002-teams-access-intake/test-simplification.md and docs/testing-strategy.md
- [X] T069 Move business/DevOps decision permutations, retry-state rules, request visibility, action-capability calculation, and immutable-scope negatives to direct Core/application unit tests or real-SQLite component tests; retain only representative HTTP authentication, overposting, antiforgery, and response-contract cases in tests/GovernedAccess.UnitTests/BusinessDecisionPolicyTests.cs, tests/GovernedAccess.UnitTests/DevOpsDecisionPolicyTests.cs, tests/GovernedAccess.UnitTests/WorkflowEvidencePolicyTests.cs, tests/GovernedAccess.IntegrationTests/Approvals/AccessRequestWorkflowServiceTests.cs, tests/GovernedAccess.IntegrationTests/Approvals/BusinessDecisionTests.cs, tests/GovernedAccess.IntegrationTests/Approvals/DevOpsDecisionTests.cs, tests/GovernedAccess.IntegrationTests/Provisioning/ProtectedProvisioningTests.cs, tests/GovernedAccess.IntegrationTests/Provisioning/RetryProvisioningTests.cs, and tests/GovernedAccess.IntegrationTests/Requests/RequestQueriesTests.cs
- [X] T070 Move the representative utterance matrix, candidate validation permutations, and history-sensitive interpretation cases to direct deterministic-chat/MAF component tests; retain one complete and one multi-turn hosted Teams scenario proving transport-to-card wiring in tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationInterpreterSessionTests.cs, tests/GovernedAccess.IntegrationTests/Ai/MafConversationSessionStoreTests.cs, tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs, tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationQualityTests.cs, and tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs
- [X] T071 Keep full-host fixtures deliberately bounded, document fast unit/component and complete-suite commands, and ensure future task descriptions use `unit`, `component`, or `full-host` deliberately in docs/testing-strategy.md and docs/local-development.md
- [X] T072 Run warnings-as-errors build plus unit, component, retained full-host, and Vitest suites; capture per-test durations for three uncontended warm no-build integration runs, verify no coverage requirement was dropped, and record case counts, median duration, remaining host startups, and justified exceptions in specs/002-teams-access-intake/test-simplification.md and specs/002-teams-access-intake/validation.md

**Checkpoint**: Policy and lifecycle permutations execute at unit/component level,
the retained full-host suite proves only real outer-boundary behavior, all trust and
negative requirements remain covered, and repeated host startup is measured and
bounded before further failure-safety integration work is added.

---

## Phase 7: User Story 3 - Safely Handle Expiry, Replay, and Failure (Priority: P3)

**Goal**: Fail closed for expired, replayed, foreign, malformed, stale, cancelled, or
unavailable operations, and converge repeated or concurrent valid confirmation on one
request and one first-confirmation audit history.

**Independent Test**: Use exhaustive Core unit tests for lifecycle and ownership,
real-SQLite/adapter/MCP component tests for concurrency and dependency failures, and
a small table-driven hosted boundary slice for authenticated Activity Protocol and
safe-response translation. Verify expired and foreign snapshots, malformed actions,
repeated and concurrent delivery, unexpected MCP catalogs, and every typed
model/context failure without scope expansion or unintended request, approval,
operation, or grant.

### Vertical Implementation Sequence for User Story 3

- [X] T073 [US3] Enforce lazy expiry, supersession, invalidation, terminal content clearing, and replay-safe transitions in Core without coupling those durable lifecycle outcomes to the process-lifetime MAF session store in src/GovernedAccess.Core/Domain/RequestIntakeSession.cs, src/GovernedAccess.Core/Application/RequestIntakeService.cs, and src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T074 [US3] Add exhaustive Core unit coverage for lazy expiry, supersession, invalidation, owner/conversation binding, terminal transition rejection, submitted replay identity, and persistence-failure outcomes in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs and tests/GovernedAccess.UnitTests/RequestPreparationTests.cs
- [X] T075 [US3] Add optimistic-concurrency recovery that clears tracking, reloads by intake session ID, and returns the stored request ID only for the same owner/conversation in src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs
- [X] T076 [US3] Complete compact confirmation-result handling for expiry, replay, supersession, invalidation, concealment, malformed action, stale authoritative context, and dependency failure in src/GovernedAccess.Core/Application/RequestIntakeService.cs
- [X] T077 [P] [US3] Add direct RequestIntakeService component coverage for unknown, expired, superseded, invalidated, foreign-owner, and conversation-mismatched confirmation, then retain one table-driven full-host test only for malformed action/schema rejection and concealed foreign responses in tests/GovernedAccess.IntegrationTests/Teams/RequestIntakeConfirmationComponentTests.cs and tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs
- [X] T078 [P] [US3] Verify repeated and concurrent confirmation against real SQLite produces one request, one request-created audit event, and one stable request ID, then retain one hosted sequential replay assertion for Teams response translation in tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakeConfirmationConcurrencyTests.cs and tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs
- [X] T079 [US3] Return safe Teams responses for rejected activities, preparation failures, and confirmation outcomes without invoking MAF for confirmation or exposing foreign scope in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs
- [X] T080 [US3] Verify wrong-channel, non-personal, disallowed-tenant, missing-actor, and forged identity/scope behavior directly against TeamsActorResolver, then add unauthenticated activity, unknown verb, and unsupported schema-version rows to the existing hosted Teams suites without creating another full-host fixture/class in tests/GovernedAccess.IntegrationTests/Teams/TeamsActorResolverComponentTests.cs, tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs, and tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs
- [X] T081 [US3] Require exact MCP catalog equality, propagate caller cancellation from the request deadline, and translate provider failures to safe outcomes in src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs
- [X] T082 [P] [US3] Verify malformed or unsupported proposals, prompt injection, caller cancellation, dependency unavailability, unchanged last-saved MAF session, and no workflow state as direct interpreter component tests without `WebApplicationFactory` in tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs
- [X] T083 [P] [US3] Verify exact two-tool allowlisting with roles embedded in environment context, missing/extra catalog failure, tool cancellation/unavailability, and absence of state-changing or separate role-listing tools through the lightweight MCP test host and direct interpreter component boundary without starting the full application in tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs
- [X] T084 [US3] Extend direct `ILogger<TeamsAccessRequestAgent>` metadata for preparation and confirmation replay/rejection without adding Core logging dependencies or logging/persisting a parallel intake or MAF-session history in src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs

**Checkpoint**: The Teams intake path meets the expiry, replay, concurrency, failure,
privacy, and trust-boundary requirements and remains safe under transport retries.

---

## Phase 8: User Story 4 - Continue the Existing Governed Workflow (Priority: P4)

**Goal**: Prove that Teams is the only request-intake channel and that its submitted
requests continue through the existing authenticated Web approvals and protected
provisioning path without channel-specific authorization rules.

**Independent Test**: Confirm a request through Teams, complete authenticated business
and DevOps decisions through successful provisioning, and verify client isolation,
immutable scope, fixed duration, and persisted evidence. Existing component coverage
remains the proof for provisioning failure/retry and idempotency while browser request
creation remains unavailable.

### Layered Verification for User Story 4

- [X] T085 [US4] Add one retained full-host Teams-to-business-to-DevOps-to-provisioning journey proving authenticated boundary wiring, client isolation, exact role, fixed eight-hour lifetime, and persisted evidence in tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs

**Checkpoint**: The new adapter has no effect on approval authority, immutable request
scope, provisioning evidence, or retry authorization, and no browser intake remains.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Complete operator guidance, packaging, whole-system verification, and
the portfolio demo evidence.

- [X] T086 [P] Finalize documentation of native process-local MAF session storage, restart recovery, deferred durable retention/compaction, Teams trust boundary, durable candidate boundary, single intake aggregate/save boundary, Teams-only creation policy, and unchanged approval/provisioning flow in docs/architecture.md and docs/security-model.md
- [X] T087 [P] Finalize documentation of the history-sensitive deterministic fake, native session isolation/restart/concurrency negatives, Teams-only creation assertions, and the no-live-model acceptance workflow in docs/testing-strategy.md
- [X] T088 [P] Document E5 developer-tenant setup, Azure Bot registration, secret storage, stable HTTPS tunnel, manifest packaging, sideloading, and cleanup in docs/teams-quickstart.md, docs/teams-advanced-reference.md, and docs/local-development.md
- [X] T089 Validate the Teams app package contains only manifest.json, color.png, and outline.png at its ZIP root and record the packaging command in docs/teams-advanced-reference.md
- [X] T090 Run restore, warnings-as-errors build, .NET tests, Vitest tests, contract validation, and Scenarios 1-6 including Teams-only request creation, then record results and deterministic confirmation timing in specs/002-teams-access-intake/validation.md

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
- **Phase 5 (US6 Teams-only creation)**: Depends on the simplified US5 complete
  prepare/confirm path. Contract tasks T049-T052 precede production deletion, and
  T058 is the removal gate.
- **Phase 6 (US2 history-first clarification)**: Runs immediately after Phase 5 and
  depends on the Teams-only creation boundary and simplified US5 intake path. It
  remains independently testable with incomplete conversations and simulated process
  restart.
- **Phase 6A (test-suite simplification gate)**: Depends on the completed US2 test
  surface and blocks Phase 7. T068-T072 must pass before T073-T084 continue.
- **Phase 7 (US3)**: Depends on Phase 6 and the Phase 6A exit gate. Its exhaustive
  lifecycle and failure coverage belongs at unit/component level; only authenticated
  Activity Protocol, safe-response, and logging wiring retain full-host coverage.
  Model/MCP component work can proceed independently of Web approval regression
  coverage, and durable terminal transitions do not manage the process-lifetime MAF
  store.
- **Phase 8 (US4)**: Depends on the Teams-only creation gate and the confirmation
  behavior required to seed the governed approval/provisioning workflow.
- **Phase 9 (Polish)**: Depends on all user stories selected for delivery.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 Prepare/Confirm -> US5 Simplify Architecture
                                                   `-> US6 Teams-Only Creation
                                                        |-> US2 History-First Clarification
                                                        |        `-> Test Suite Simplification Gate
                                                        |                 `-> US3 Replay/Failure Safety
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
7. For US6, make the endpoint and UI absence tests fail first, remove browser creation
   from the outer adapters inward, preserve the Teams atomic save boundary throughout,
   and finish with repository-wide absence checks.
8. After Phase 6A, place exhaustive rule permutations in unit/component tests and add
   a full-host case only when authentication, middleware, routing, serialization, or
   adapter wiring is the behavior being proved.

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

### User Story 6

Requirements/design updates T049-T050 and server/UI characterization T051-T052 touch
separate files and can proceed in parallel. Remove the browser draft stack T053 before
constraining submission to prepared confirmation in T054. UI removal T055 can proceed
alongside those Core/API changes after T052. Test migration T056 follows the production
deletions; documentation T057 and the full removal gate T058 finish the phase.

### User Story 2

Contract replacement and unit coverage T059-T060 precede native store coordination
and interpreter work T061-T062. Durable-state and Teams wiring T063-T064 then establish
the complete flow. History behavior, session restart/concurrency behavior, and
validation-quality tests T065-T067 target separate files and can run in parallel.

### Test Suite Simplification Gate

Inventory and coverage placement T068 precedes structural changes. Workflow/query
migration T069 and Teams interpreter/component migration T070 target separate test
areas. Level markers and developer commands T071 follow both migrations, and the
measured three-run exit gate T072 completes the phase.

### User Story 3

Lifecycle implementation T073 precedes unit coverage T074 and confirmation hardening
T075-T076. Focused confirmation tasks T077-T078 can run in parallel. Safe adapter
responses T079 precede the minimal activity boundary task T080. After interpreter
hardening T081, direct model and MCP component tasks T082-T083 can run in parallel.
Logging metadata T084 completes the phase.

### User Story 4

The retained governed-workflow regression T085 follows Teams confirmation and proves
the channel-neutral approval and provisioning path through the full host.

---

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 and run its independent test.
3. Complete US5, enforce its deletion and complexity budget, and rerun the US1
   independent test.
4. Complete US6 and prove Teams confirmation is the sole request-creation boundary
   while the Web register and approval stages remain available.
5. Complete US2 so Teams supports the intended complete and incomplete request
   conversations.
6. Complete the test-suite simplification gate so the frequent feedback loop is fast
   before adding US3 negative coverage.
7. Use that reduced slice for a local architecture walkthrough: authenticated
   Teams-shaped input, bounded MAF interpretation, immutable card, deterministic
   confirmation, and existing Web request review.
8. Do not describe the feature as trust-complete until US3 replay and failure
   hardening also passes.

### Incremental Delivery

1. **US1** demonstrates the end-to-end Teams/MAF boundary.
2. **US5** reduces US1 to the smallest architecture that preserves its behavior and
   trust boundaries; it is mandatory before adding more behavior.
3. **US6** removes the obsolete browser intake and makes Teams the only creation path
   without changing persisted requests or downstream governance.
4. **US2** adds history-first multi-turn clarification using MAF-native process-local
   sessions and durable typed candidate state.
5. **Test Suite Simplification** moves exhaustive behavior to unit/component tests
   and retains only necessary outer-boundary full-host coverage.
6. **US3** makes the channel safe under retry, expiry, malformed input, and dependency
   failure.
7. **US4** proves that Web approvals and provisioning stayed deterministic.
8. **Polish** turns the tested behavior into a repeatable live Teams demo.

### Recommended Solo-Developer Order

Follow task ID order through US1 and US5, stop at the US5 complexity-budget checkpoint,
then complete the US6 Teams-only-creation gate and US2. Run the T068-T072 test-suite
simplification gate before continuing with US3 tasks T073-T084, then complete US4.
Use the parallel markers to batch independent tests or small
isolated files, not to introduce additional agents, services, queues, or workflow
infrastructure.

## Notes

- `[P]` means file-level parallelism, not architectural concurrency.
- MAF is used only for interpretation and read-only tool dispatch.
- US5 is an engineering-maintainer story added after implementation feedback; it does
  not change product behavior or expand authorization.
- US6 is the approved product-boundary change: Teams confirmation becomes the only
  request-creation path, while the Web application remains the authenticated request
  register, approval, retry, and audit surface.
- A US5 task that needs a new production abstraction must stop and obtain explicit
  justification before adding it; deletion and consolidation are the default.
- MAF history is isolated process-local presentation context, not durable workflow
  state. The native in-memory store and exact per-intake gates live for the process
  lifetime. Only the typed candidate and intake lifecycle are persisted; restart loss
  triggers re-clarification rather than reconstructed or guessed choices. Durable
  retention/deletion and native MAF compaction are deferred.
- Tests never call a live model, Teams tenant, Azure Bot, or production system.
- Task IDs are sequential in documented execution order, including the Phase 6A gate.
- `IntegrationTests` may contain component tests that require Web infrastructure,
  SQLite, MAF, or MCP packages, but only full-host tests may start the complete
  `WebApplicationFactory` host after the Phase 6A migration.
- No task adds Slack, group chat, proactive messages, Graph/SSO, a workflow engine,
  multiple agents, model-visible state changes, or a second executable.
- Commit after each task or coherent task group and stop at each checkpoint for
  independent validation.

---

## Phase 10: Ready Draft Discussion and Revision

**Purpose**: Let a requester discuss a complete ready draft without invalidating its
card, and create a separately confirmable immutable preparation only after the
deterministically assessed candidate changes.

- [X] T091 [US1] Amend ready-draft acceptance scenarios and functional requirements for discussion, natural-language revision, hidden pre-submission request identity, immutable replacement preparations, and the exact two-tool MCP boundary in specs/002-teams-access-intake/spec.md
- [X] T092 [P] [US1] Synchronize the proposal schema, data model, research decisions, plan boundaries, card contract, activity contract, quickstart, and validation evidence with `DraftDiscussion`, bounded authoritative environment choices, and replacement preparation behavior in specs/002-teams-access-intake/
- [X] T093 [US1] Preserve an unexpired ready candidate through interpretation, return `DraftDiscussion` only for an unchanged ready assessment, and supersede/create a new intake before persisting a changed ready, incomplete, or rejected candidate in src/GovernedAccess.Core/Application/RequestIntakeService.cs and src/GovernedAccess.Core/Ports/RequestIntake.cs
- [X] T094 [US1] Title the card **Review request draft**, hide the reserved request ID until submission, track the latest card as process-local presentation metadata, make replaced cards non-actionable, and return non-actionable invoke responses for stale terminal drafts in src/GovernedAccess.Web/Teams/
- [X] T095 [P] [US1] Cover ready-draft discussion identity preservation, equivalent candidates, invalid alternatives, model failure, explicit revision, hidden request identity, stale-card invocation, and exact tracker binding in tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs and tests/GovernedAccess.IntegrationTests/Teams/
- [ ] T096 [P] [US1] Add an adapter-level Teams test that captures `UpdateActivityAsync` and proves a changed assessed candidate makes the prior activity **Draft being revised**, sends a separate latest review card, and remains safely confirmable or rejectable when the presentation update fails
- [X] T097 [P] [US1] Synchronize current architecture, security, orchestration, testing, product-baseline, and feature validation documentation with the distinction between unchanged discussion and changed-candidate replacement in docs/ and specs/002-teams-access-intake/validation.md

---

## Phase 11: Application Service Responsibility Refactor

**Purpose**: Make the Core application layer easier to read without introducing a
service per operation or changing product behavior and trust boundaries. This phase
supersedes the earlier implementation-shape decisions in T036 and T054; their
behavioral requirements remain in force.

- [X] T098 Extract mutable candidate canonicalization and readiness into `RequestDraftValidator`, retain strict complete-scope validation in `AccessRequestValidator`, and share only deterministic field rules in `src/GovernedAccess.Core/Application/`
- [X] T099 Replace the unified intake coordinator with `RequestDraftService` for prepare/reset and make `RequestSubmissionService.ConfirmDraftAsync` own deterministic reload, ownership, revalidation, immutable request staging, terminal draft transition, recovery, and the one atomic save
- [X] T100 Extract authenticated principal, immutable request, normalized correlation identity, and business environment-context loading into `AccessRequestCommandContextLoader` while retaining workflow decisions in `AccessRequestWorkflowService`
- [X] T101 Extract submitted-request visibility and available-action calculation into `AccessRequestVisibilityPolicy` while retaining data loading and projection assembly in `AccessRequestQueryService`
- [X] T102 Update composition, direct-construction tests, current architecture/security/orchestration/testing documentation, and pass the required build, unit, and integration validation sequence
- [X] T103 Make the lifecycle boundary structural by grouping mutable drafting under `Application/Drafts`, submitted request creation, validation, workflow, visibility, and queries under `Application/AccessRequests`, and protected execution under `Application/Provisioning`; rename generic submitted-request types to the `AccessRequest*` vocabulary
- [X] T104 Synchronize current documentation and pass the required build, unit, and integration validation sequence after the namespace separation

---

## Phase 12: Domain Aggregate Structure

**Purpose**: Make mutable drafts, authoritative reference data, and the immutable
submitted-request evidence chain visible in the Domain structure without introducing
new domain services or changing aggregate behavior.

- [X] T105 Move `RequestIntakeSession` to `Domain.Drafts`, authoritative client/environment/role/incident types to `Domain.ReferenceData`, and submitted request, actor, decision policies, and evidence policies to `Domain.AccessRequests`
- [X] T106 Split the monolithic `WorkflowEvidence.cs` into cohesive approval decision, provisioning operation, access grant, audit details, audit event, and internal validation files while retaining one `Domain.AccessRequests` namespace for their cross-stage invariants
- [X] T107 Update Core ports/application consumers, EF persistence, MCP/Web adapters, and all direct tests for the three domain namespaces without changing persistence mappings or public transport contracts
- [X] T108 Synchronize current architecture and feature structure documentation and pass the required build, unit, and integration validation sequence

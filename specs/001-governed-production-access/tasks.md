# Tasks: Governed Production Access

**Input**: Design documents from `/specs/001-governed-production-access/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Tests are required by the feature specification. Within each user story, write the listed tests first and confirm they fail before implementing the behavior.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated as an independently useful increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file and has no dependency on an incomplete task in the same phase
- **[Story]**: Maps the task to its user story (`US1` through `US5`)
- Every checklist item names the exact file or directory it changes

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the two-project .NET solution and nested React build input for the single executable host.

- [X] T001 Create `ProductionAccessRequestAssistant.sln` with `src/GovernedAccess.Core/GovernedAccess.Core.csproj`, `src/GovernedAccess.Web/GovernedAccess.Web.csproj`, `tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj`, and `tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj`
- [X] T002 Configure .NET 10, nullable reference types, warnings-as-errors, and shared analyzer settings in `Directory.Build.props`
- [X] T003 [P] Add ASP.NET Core, EF Core SQLite, Microsoft.Extensions.AI, and ModelContextProtocol package references and the Core project reference in `src/GovernedAccess.Web/GovernedAccess.Web.csproj`
- [X] T004 [P] Add xUnit, ASP.NET Core `WebApplicationFactory`, and SQLite test dependencies in `tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj` and `tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj`
- [X] T005 [P] Initialize React 19.2, TypeScript, React Router, Vite, Vitest, and React Testing Library scripts and dependencies in `src/GovernedAccess.Web/ClientApp/package.json`
- [X] T006 Configure Vite hashed production output to `wwwroot`, development `/api` proxying, and Vitest in `src/GovernedAccess.Web/ClientApp/vite.config.ts`
- [X] T007 Add frontend build and publish integration while preserving one deployable host in `src/GovernedAccess.Web/GovernedAccess.Web.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared domain, persistence, trusted identity, HTTP safety, and test infrastructure used by every story.

**Critical**: Complete this phase before starting any user-story implementation.

- [X] T008 [P] Define typed success/failure outcomes and cancellation-safe application result types in `src/GovernedAccess.Core/Application/Outcomes.cs`
- [X] T009 [P] Define authoritative `Client`, `ProductionEnvironment`, `EnvironmentRole`, `Incident`, and `AuthenticatedPrincipal` models in `src/GovernedAccess.Core/Domain/AuthoritativeContext.cs`
- [X] T010 [P] Define `AccessRequest`, statuses, normalization, version, and optimistic persistence version fields in `src/GovernedAccess.Core/Domain/AccessRequest.cs`
- [X] T011 [P] Define `ApprovalDecision`, `ProvisioningOperation`, `AccessGrant`, and `AuditEvent` evidence models in `src/GovernedAccess.Core/Domain/WorkflowEvidence.cs`
- [X] T012 Define provider-neutral authoritative context, clock, audit, and workflow persistence ports with `CancellationToken` parameters in `src/GovernedAccess.Core/Ports/CorePorts.cs`
- [X] T013 Configure EF Core entity mappings, concurrency token, approval uniqueness, operation uniqueness, grant uniqueness, and UTC timestamps in `src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs`
- [X] T014 Seed exactly two clients, two environments, their allowed roles, synthetic incidents, and four immutable principals in `src/GovernedAccess.Web/Persistence/SyntheticDataSeeder.cs`
- [X] T015 [P] Implement correlation ID middleware and metadata-only operation logging helpers in `src/GovernedAccess.Web/Observability/CorrelationMiddleware.cs`
- [X] T016 [P] Implement cookie authentication and immutable principal-key-to-claims resolution in `src/GovernedAccess.Web/Authentication/DemoAuthentication.cs`
- [X] T017 Implement antiforgery-protected demo session, session query, and antiforgery endpoints in `src/GovernedAccess.Web/Endpoints/SessionEndpoints.cs`
- [X] T018 Implement stable Problem Details mapping for validation, authorization, stale-state, concurrency, timeout, cancellation, and dependency failures in `src/GovernedAccess.Web/Endpoints/ProblemDetailsMapping.cs`
- [X] T019 Create the SQLite in-memory `WebApplicationFactory`, deterministic clock, identity helpers, and database reset fixtures in `tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs`
- [X] T020 Wire configuration, persistence, authentication, antiforgery, correlation, API route groups, `/mcp`, static assets, and non-API SPA fallback in `src/GovernedAccess.Web/Program.cs`

**Checkpoint**: The shared host starts with deterministic data, trusted cookie identity, antiforgery, persistence constraints, and reusable test fixtures.

---

## Phase 3: User Story 1 - Prepare and Submit a Safe Access Request (Priority: P1) MVP

**Goal**: Let an authenticated requester turn natural-language intent into a schema-validated draft, authoritatively correct it, and submit version 1 without granting access.

**Independent Test**: Sign in as the requester, prepare the Client Alpha/`INC-1042` four-hour read-only example, review stable identifiers, submit it, and verify version 1 is `AwaitingBusinessApproval` with no approval, operation, or grant.

### Tests for User Story 1

- [X] T021 [P] [US1] Add unit tests for client/environment, activation, role, duration, justification, and incident validation in `tests/GovernedAccess.UnitTests/RequestValidationTests.cs`
- [X] T022 [P] [US1] Add MCP contract tests for exact three-tool advertisement, typed schemas, stable IDs, forbidden capability absence, and typed failures in `tests/GovernedAccess.IntegrationTests/Mcp/McpContractTests.cs`
- [X] T023 [P] [US1] Add MCP timeout, cancellation, unavailable, and not-found interaction tests in `tests/GovernedAccess.IntegrationTests/Mcp/McpFailureTests.cs`
- [X] T024 [P] [US1] Add deterministic chat adapter tests for valid, incomplete, malformed, unsupported, timeout, cancellation, and no-live-model outcomes in `tests/GovernedAccess.IntegrationTests/Ai/DraftPreparationTests.cs`
- [X] T025 [P] [US1] Add request creation integration tests for authoritative revalidation, browser identity over-posting resistance, antiforgery, and absence of approval/grant side effects in `tests/GovernedAccess.IntegrationTests/Requests/CreateRequestTests.cs`
- [X] T026 [P] [US1] Add React tests for draft preparation, correction guidance, typed failure display, cancellation, and successful submission in `src/GovernedAccess.Web/ClientApp/src/test/NewRequestPage.test.tsx`

### Implementation for User Story 1

- [ ] T027 [P] [US1] Define draft preparation records, missing-field rules, and the provider-neutral drafting port in `src/GovernedAccess.Core/Ports/RequestDrafting.cs`
- [ ] T028 [P] [US1] Implement authoritative request validation and normalized field results in `src/GovernedAccess.Core/Application/RequestValidator.cs`
- [ ] T029 [US1] Implement request creation, authenticated requester binding, version-1 submission, and creation/validation audit events in `src/GovernedAccess.Core/Application/RequestSubmissionService.cs`
- [ ] T030 [P] [US1] Implement typed read-only authoritative context handlers for the three MCP operations in `src/GovernedAccess.Web/Mcp/AuthoritativeContextTools.cs`
- [ ] T031 [US1] Register a stateless Streamable HTTP `/mcp` server with an explicit allowlist containing only the three contract tools in `src/GovernedAccess.Web/Mcp/McpRegistration.cs`
- [ ] T032 [P] [US1] Implement deterministic fake `IChatClient` modes for valid, incomplete, malformed, timeout, cancellation, and unavailable results in `src/GovernedAccess.Web/Ai/DeterministicChatClient.cs`
- [ ] T033 [US1] Implement the `IChatClient` drafting adapter with strict JSON schema parsing, a 30-second model timeout, 5-second MCP calls, identifier revalidation, safe logging, and linked cancellation in `src/GovernedAccess.Web/Ai/ChatRequestDraftingAdapter.cs`
- [ ] T034 [US1] Implement `POST /api/request-drafts/prepare` and `POST /api/requests` without accepting actor or approver claims in `src/GovernedAccess.Web/Endpoints/RequestPreparationEndpoints.cs`
- [ ] T035 [P] [US1] Implement the credentialed typed fetch wrapper with antiforgery headers, Problem Details mapping, and `AbortSignal` support in `src/GovernedAccess.Web/ClientApp/src/api/client.ts`
- [ ] T036 [P] [US1] Define TypeScript draft, request creation, session, and typed error contracts in `src/GovernedAccess.Web/ClientApp/src/api/contracts.ts`
- [ ] T037 [US1] Implement the intent, draft review/correction, validation, and submission flow in `src/GovernedAccess.Web/ClientApp/src/pages/NewRequestPage.tsx`
- [ ] T038 [US1] Add React Router navigation for `/requests/new` and a minimal application shell in `src/GovernedAccess.Web/ClientApp/src/App.tsx`

**Checkpoint**: User Story 1 works end to end and is independently demonstrable without a live model.

---

## Phase 4: User Story 2 - Make the Correct Business Decision (Priority: P2)

**Goal**: Resolve the authoritative client-specific business approver and bind an authenticated approve/reject decision to the exact request version and scope.

**Independent Test**: Attempt a Client Alpha decision as the Beta approver, verify rejection and audit with no state change, then decide as the Alpha approver and verify the exact current request/version/role/duration evidence.

### Tests for User Story 2

- [ ] T039 [P] [US2] Add unit tests for business decision state transitions, exact scope binding, rejection, and duplicate-stage prevention in `tests/GovernedAccess.UnitTests/BusinessDecisionPolicyTests.cs`
- [ ] T040 [P] [US2] Add integration tests for authoritative approver resolution, wrong-client rejection, stale version, actor over-posting, audit evidence, and antiforgery in `tests/GovernedAccess.IntegrationTests/Approvals/BusinessDecisionTests.cs`
- [ ] T041 [P] [US2] Add React tests for business decision visibility, approve/reject submission, and server rejection display in `src/GovernedAccess.Web/ClientApp/src/test/BusinessDecisionPanel.test.tsx`

### Implementation for User Story 2

- [ ] T042 [P] [US2] Implement exact-version business approval/rejection transition rules in `src/GovernedAccess.Core/Domain/BusinessDecisionPolicy.cs`
- [ ] T043 [US2] Implement authenticated environment-responsibility authorization, decision persistence, concurrency handling, and auditing in `src/GovernedAccess.Core/Application/BusinessDecisionService.cs`
- [ ] T044 [US2] Implement `POST /api/requests/{requestId}/business-decisions` with the restricted request body in `src/GovernedAccess.Web/Endpoints/BusinessDecisionEndpoints.cs`
- [ ] T045 [P] [US2] Implement business approve/reject controls driven by server-computed actions in `src/GovernedAccess.Web/ClientApp/src/components/BusinessDecisionPanel.tsx`
- [ ] T046 [US2] Integrate the business decision panel and refreshed request status into `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`

**Checkpoint**: Client isolation and exact-version business approval are independently testable; no DevOps or provisioning behavior is required yet.

---

## Phase 5: User Story 3 - Approve and Provision the Authorized Scope (Priority: P3)

**Goal**: Let authenticated DevOps reject or approve the exact business-approved role for the same or shorter duration, with full authoritative reload and immediate synthetic provisioning.

**Independent Test**: From a current business-approved request, approve a permitted duration as DevOps and verify one matching grant and `Active`; verify role changes, duration increases, stale context, missing approval, and non-DevOps attempts create no grant.

### Tests for User Story 3

- [ ] T047 [P] [US3] Add unit tests for exact role, positive duration ceiling, rejection, transition, and deterministic operation identity in `tests/GovernedAccess.UnitTests/DevOpsDecisionPolicyTests.cs`
- [ ] T048 [P] [US3] Add protected handler tests for authoritative reload, current-state revalidation, stale environment/role/incident, missing approval, and caller-assertion distrust in `tests/GovernedAccess.IntegrationTests/Provisioning/ProtectedProvisioningTests.cs`
- [ ] T049 [P] [US3] Add API tests for DevOps authentication, crafted scope over-posting, duration increase, rejection, antiforgery, and typed provisioning failure in `tests/GovernedAccess.IntegrationTests/Approvals/DevOpsDecisionTests.cs`
- [ ] T050 [P] [US3] Add React tests for allowed-duration approval, rejection, protected failure, and grant summary rendering in `src/GovernedAccess.Web/ClientApp/src/test/DevOpsDecisionPanel.test.tsx`

### Implementation for User Story 3

- [ ] T051 [P] [US3] Implement DevOps decision rules and canonical length-prefixed SHA-256 operation identity in `src/GovernedAccess.Core/Domain/DevOpsDecisionPolicy.cs`
- [ ] T052 [P] [US3] Define the provider-neutral synthetic provisioning port and typed timeout/failure outcomes in `src/GovernedAccess.Core/Ports/Provisioning.cs`
- [ ] T053 [US3] Implement the protected provisioning handler that reloads request and approvals, revalidates current scope, and finalizes grant/workflow/audit state in `src/GovernedAccess.Core/Application/ProtectedProvisioningService.cs`
- [ ] T054 [P] [US3] Implement the 10-second controllable synthetic get-or-create provisioner with linked cancellation in `src/GovernedAccess.Web/Provisioning/SyntheticAccessProvisioner.cs`
- [ ] T055 [US3] Implement DevOps decision persistence before provider invocation and failure transition handling in `src/GovernedAccess.Core/Application/DevOpsDecisionService.cs`
- [ ] T056 [US3] Implement `POST /api/requests/{requestId}/devops-decisions` without accepting role, client, environment, actor, or approval assertions in `src/GovernedAccess.Web/Endpoints/DevOpsDecisionEndpoints.cs`
- [ ] T057 [P] [US3] Implement DevOps approve/reject controls and safe provisioning outcome display in `src/GovernedAccess.Web/ClientApp/src/components/DevOpsDecisionPanel.tsx`
- [ ] T058 [US3] Integrate DevOps actions and activation/expiry grant summary into `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`

**Checkpoint**: The full two-human approval path produces exactly scoped synthetic access through deterministic authorization and provisioning.

---

## Phase 6: User Story 4 - Protect Approvals from Material Edits (Priority: P4)

**Goal**: Permit requester-owned material corrections while incrementing the business version, returning to `Draft`, and making prior decisions ineligible.

**Independent Test**: Business-approve version 1, materially edit it as the requester, verify version 2 in `Draft` with retained but ineligible version-1 evidence, and confirm a version-1 action is rejected and audited.

### Tests for User Story 4

- [ ] T059 [P] [US4] Add unit tests for normalization, no-change behavior, every material field, one-step version increment, and prior-approval ineligibility in `tests/GovernedAccess.UnitTests/MaterialEditPolicyTests.cs`
- [ ] T060 [P] [US4] Add integration tests for owner-only edit/resubmit, stale action, optimistic conflict, retained decisions, audit evidence, and antiforgery in `tests/GovernedAccess.IntegrationTests/Requests/EditRequestTests.cs`
- [ ] T061 [P] [US4] Add React tests for editable states, version refresh, resubmission, no-change, and stale-conflict display in `src/GovernedAccess.Web/ClientApp/src/test/EditRequestPanel.test.tsx`

### Implementation for User Story 4

- [ ] T062 [P] [US4] Implement normalized material-change detection, editable-state rules, version increment, and `Draft` transition in `src/GovernedAccess.Core/Domain/MaterialEditPolicy.cs`
- [ ] T063 [US4] Implement requester ownership, authoritative edit validation, optimistic concurrency, resubmission, and version-aware audit events in `src/GovernedAccess.Core/Application/RequestEditingService.cs`
- [ ] T064 [US4] Implement `PUT /api/requests/{requestId}` and `POST /api/requests/{requestId}/submit` with expected-version enforcement in `src/GovernedAccess.Web/Endpoints/RequestEditingEndpoints.cs`
- [ ] T065 [US4] Implement material edit, no-change, stale-version recovery, and resubmission controls in `src/GovernedAccess.Web/ClientApp/src/components/EditRequestPanel.tsx`

**Checkpoint**: Earlier approvals remain visible as evidence but cannot authorize any materially changed version.

---

## Phase 7: User Story 5 - Recover Safely and Review Evidence (Priority: P5)

**Goal**: Retry only failed provisioning with the same scope and operation identity, and let authorized participants review relevant requests and complete evidence.

**Independent Test**: Simulate a lost provisioning response, retry as DevOps, verify the existing grant is returned with no duplicate across at least 100 concurrent attempts, and inspect complete correlated evidence as each authorized participant.

### Tests for User Story 5

- [ ] T066 [P] [US5] Add integration tests for lost response, same-operation retry, wrong actor/state rejection, and full revalidation in `tests/GovernedAccess.IntegrationTests/Provisioning/RetryProvisioningTests.cs`
- [ ] T067 [P] [US5] Add a 100-concurrent-attempt test proving one operation and one grant with consistent successful responses in `tests/GovernedAccess.IntegrationTests/Provisioning/ProvisioningIdempotencyTests.cs`
- [ ] T068 [P] [US5] Add API tests for participant-filtered lists, server-computed actions, complete detail evidence, logical expiry, and nonparticipant invisibility in `tests/GovernedAccess.IntegrationTests/Requests/RequestQueriesTests.cs`
- [ ] T069 [P] [US5] Add React tests for request list actionability, retry, audit timeline, provisioning evidence, and logical expiry in `src/GovernedAccess.Web/ClientApp/src/test/RequestEvidencePages.test.tsx`

### Implementation for User Story 5

- [ ] T070 [US5] Implement DevOps-only retry from `ProvisioningFailed` using the stored scope and operation identity through the same protected handler in `src/GovernedAccess.Core/Application/ProvisioningRetryService.cs`
- [ ] T071 [US5] Implement participant-filtered request list/detail projections, ordered versioned decisions, grant state, logical expiry, and available actions in `src/GovernedAccess.Core/Application/RequestQueryService.cs`
- [ ] T072 [US5] Implement `GET /api/requests`, `GET /api/requests/{requestId}`, and `POST /api/requests/{requestId}/retry-provisioning` in `src/GovernedAccess.Web/Endpoints/RequestQueryAndRetryEndpoints.cs`
- [ ] T073 [P] [US5] Implement relevant request rows, status filters, and actionable indicators in `src/GovernedAccess.Web/ClientApp/src/pages/RequestListPage.tsx`
- [ ] T074 [US5] Complete request detail rendering for validation, all decisions, provisioning outcome, grant expiry, retry, available actions, and audit timeline in `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`
- [ ] T075 [US5] Complete the three-route React application and demo identity selector in `src/GovernedAccess.Web/ClientApp/src/App.tsx`

**Checkpoint**: Failed work is safely recoverable, concurrent duplicates converge on one grant, and authorized users can understand all material evidence.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validate security, build shape, observability, and the complete local demonstration across all stories.

- [ ] T076 [P] Add end-to-end security tests covering unauthenticated access, antiforgery on every unsafe endpoint, identity/role/approver over-posting, and `/api`/`/mcp` SPA fallback exclusion in `tests/GovernedAccess.IntegrationTests/Security/ApiSecurityTests.cs`
- [ ] T077 [P] Add audit persistence tests proving insert-only behavior, required event coverage, correlation fields, safe details allowlisting, and absence of raw prompts/full MCP payloads in `tests/GovernedAccess.IntegrationTests/Auditing/AuditEvidenceTests.cs`
- [ ] T078 [P] Add host tests proving model and MCP duration/outcome metadata logging without sensitive payload capture in `tests/GovernedAccess.IntegrationTests/Observability/OperationalLoggingTests.cs`
- [ ] T079 Add production static-file and hashed Vite asset host validation in `tests/GovernedAccess.IntegrationTests/Hosting/SingleHostAssetTests.cs`
- [ ] T080 Run and reconcile every automated command and scenario in `specs/001-governed-production-access/quickstart.md`
- [ ] T081 Record final build, test, 15-minute demonstration, and no-live-dependency evidence in `specs/001-governed-production-access/validation-report.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** has no dependencies.
- **Foundational (Phase 2)** depends on Setup and blocks every user story.
- **US1 (Phase 3)** starts after Foundational and is the MVP.
- **US2 (Phase 4)** starts after Foundational but needs a submitted request fixture supplied by US1 for its end-to-end demonstration.
- **US3 (Phase 5)** depends on US2 because DevOps authorization requires a valid current business approval.
- **US4 (Phase 6)** starts after US2 because its independent scenario edits a business-approved request; its edit policy can be developed after Foundational.
- **US5 (Phase 7)** depends on US3 for a durable failed provisioning operation and grant identity; list/detail query work can begin after Foundational.
- **Polish (Phase 8)** follows all stories selected for delivery.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 -> US2 -> US3 -> US5
                         |      |
                         |      +----> US4
                         +-----------> query portions of US5
```

### Within Each User Story

1. Write the story's tests and confirm they fail for the intended missing behavior.
2. Implement domain models/policies and provider-neutral ports before application services.
3. Implement application services before HTTP/MCP adapters.
4. Implement typed frontend contracts/client behavior before page integration.
5. Run the story-specific test set and its independent test before the checkpoint.

## Parallel Opportunities

- In Setup, T003, T004, and T005 can proceed in parallel after T001; T006 and T007 then converge on the frontend host build.
- In Foundational, T008-T011, T015, and T016 use separate files; persistence wiring follows the shared entities.
- All test tasks at the start of each story are parallelizable because they occupy separate test files.
- Core policy, adapter, and React component tasks marked `[P]` can proceed concurrently once their shared contracts exist.
- After US2, US3 and US4 can proceed in parallel; US5 query projections can proceed while provisioning behavior is completed.

## Parallel Example: User Story 1

```text
T021 RequestValidationTests.cs | T022 McpContractTests.cs | T023 McpFailureTests.cs | T024 DraftPreparationTests.cs | T025 CreateRequestTests.cs | T026 NewRequestPage.test.tsx
T027 RequestDrafting.cs | T028 RequestValidator.cs | T030 AuthoritativeContextTools.cs | T032 DeterministicChatClient.cs
T035 client.ts | T036 contracts.ts
```

## Parallel Example: User Story 2

```text
T039 BusinessDecisionPolicyTests.cs | T040 BusinessDecisionTests.cs | T041 BusinessDecisionPanel.test.tsx
T042 BusinessDecisionPolicy.cs | T045 BusinessDecisionPanel.tsx
```

## Parallel Example: User Story 3

```text
T047 DevOpsDecisionPolicyTests.cs | T048 ProtectedProvisioningTests.cs | T049 DevOpsDecisionTests.cs | T050 DevOpsDecisionPanel.test.tsx
T051 DevOpsDecisionPolicy.cs | T052 Provisioning.cs | T054 SyntheticAccessProvisioner.cs | T057 DevOpsDecisionPanel.tsx
```

## Parallel Example: User Story 4

```text
T059 MaterialEditPolicyTests.cs | T060 EditRequestTests.cs | T061 EditRequestPanel.test.tsx
```

## Parallel Example: User Story 5

```text
T066 RetryProvisioningTests.cs | T067 ProvisioningIdempotencyTests.cs | T068 RequestQueriesTests.cs | T069 RequestEvidencePages.test.tsx
T073 RequestListPage.tsx can proceed while T070 ProvisioningRetryService.cs is implemented
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational.
2. Complete US1, including all US1 automated tests.
3. Stop and validate the primary Client Alpha/`INC-1042` request flow independently.
4. Demonstrate that malformed/incomplete model output and invalid authoritative values create no approval or grant.

### Incremental Delivery

1. Add US2 to establish the client-isolated business authorization boundary.
2. Add US3 to complete two-human approval and immediate protected provisioning.
3. Add US4 to prove that material edits invalidate earlier authority.
4. Add US5 to prove recovery, concurrency idempotency, and inspectable evidence.
5. Complete cross-cutting security, logging, hosting, and quickstart validation.

### Scope Guardrails

- Keep `GovernedAccess.Web` as the only executable; React remains nested build input.
- Keep AI/MCP/EF/web SDK types out of `GovernedAccess.Core`.
- Expose exactly the three read-only MCP tools and no model-visible state-changing capability.
- Do not add a generic workflow engine, repository framework, separate provisioning API, background worker, real identity integration, revocation subsystem, or additional deployment.
- Treat UI capabilities as hints; every protected action must authorize from the authenticated server context and current persisted state.

## Notes

- A task marked `[P]` is parallelizable only after its declared phase prerequisites and any explicitly named contract dependency are complete.
- Preserve `CancellationToken` across every asynchronous boundary and apply the 30/5/10-second model/MCP/provisioning limits.
- Expected failures use typed outcomes; exceptions are reserved for unexpected faults.
- Do not log secrets, raw prompts, or complete MCP payloads.
- Commit after each task or coherent task group, and validate each story at its checkpoint.

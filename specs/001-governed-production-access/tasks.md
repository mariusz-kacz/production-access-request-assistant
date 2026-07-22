# Tasks: Governed Production Access

**Input**: Design documents from `/specs/001-governed-production-access/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Tests are required by the feature specification. Within each user story, write the listed tests first and confirm they fail before implementing the behavior. Keep React coverage to a minimal wiring smoke suite; exercise business rules, authorization, failures, persistence, and provisioning through .NET unit and integration tests.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated as an independently useful increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file and has no dependency on an incomplete task in the same phase
- **[Story]**: Maps the task to its user story (`US1` through `US4`)
- Every checklist item names the exact file or directory it changes

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the three-production-project .NET solution and nested React build input for the single executable host.

- [X] T001 Create `ProductionAccessRequestAssistant.sln` with `src/GovernedAccess.Core/GovernedAccess.Core.csproj`, `src/GovernedAccess.Mcp/GovernedAccess.Mcp.csproj`, `src/GovernedAccess.Web/GovernedAccess.Web.csproj`, `tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj`, and `tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj`
- [X] T002 Configure .NET 10, nullable reference types, warnings-as-errors, and shared analyzer settings in `Directory.Build.props`
- [X] T003 [P] Add ASP.NET Core, EF Core SQLite, and Microsoft.Extensions.AI references to `src/GovernedAccess.Web/GovernedAccess.Web.csproj`; isolate ModelContextProtocol packages and the Core reference in `src/GovernedAccess.Mcp/GovernedAccess.Mcp.csproj`; reference MCP from Web
- [X] T004 [P] Add xUnit, ASP.NET Core `WebApplicationFactory`, and SQLite test dependencies in `tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj` and `tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj`
- [X] T005 [P] Initialize React 19.2, TypeScript, React Router, Vite, Vitest, and React Testing Library scripts and dependencies in `src/GovernedAccess.Web/ClientApp/package.json`
- [X] T006 Configure Vite hashed production output to `wwwroot`, development `/api` proxying, and Vitest in `src/GovernedAccess.Web/ClientApp/vite.config.ts`
- [X] T007 Add frontend build and publish integration while preserving one deployable host in `src/GovernedAccess.Web/GovernedAccess.Web.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared domain, persistence, trusted identity, HTTP safety, and test infrastructure used by every story.

**Critical**: Complete this phase before starting any user-story implementation.

- [X] T008 [P] Define typed success/failure outcomes and cancellation-safe application result types in `src/GovernedAccess.Core/Application/Outcomes.cs`
- [X] T009 [P] Define `Client`, `ProductionEnvironment`, `EnvironmentRole`, `Incident`, and `AuthenticatedPrincipal` in concept-named files under `src/GovernedAccess.Core/Domain/`
- [X] T010 [P] Define immutable submitted `AccessRequest` scope, statuses, normalization, and optimistic persistence version fields in `src/GovernedAccess.Core/Domain/AccessRequest.cs`
- [X] T011 [P] Define `ApprovalDecision`, `ProvisioningOperation`, `AccessGrant`, and `AuditEvent` evidence models in `src/GovernedAccess.Core/Domain/WorkflowEvidence.cs`
- [X] T012 Define provider-neutral request-context, clock, audit, and workflow persistence ports with `CancellationToken` parameters in `src/GovernedAccess.Core/Ports/CorePorts.cs`
- [X] T013 Configure EF Core entity mappings, concurrency token, per-request approval-stage uniqueness, operation uniqueness, grant uniqueness, and UTC timestamps in `src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs`
- [X] T014 Seed exactly two clients, two environments, their allowed roles, synthetic incidents, and four immutable principals in `src/GovernedAccess.Web/Persistence/SyntheticDataSeeder.cs`
- [X] T015 [P] Implement correlation ID middleware and metadata-only operation logging helpers in `src/GovernedAccess.Web/Observability/CorrelationMiddleware.cs`
- [X] T016 [P] Implement cookie authentication and immutable principal-key-to-claims resolution in `src/GovernedAccess.Web/Authentication/DemoAuthentication.cs`
- [X] T017 Implement antiforgery-protected demo session, session query, and antiforgery endpoints in `src/GovernedAccess.Web/Controllers/SessionController.cs`
- [X] T018 Implement stable Problem Details mapping for validation, authorization, stale-state, concurrency, timeout, cancellation, and dependency failures in `src/GovernedAccess.Web/Controllers/ProblemDetailsMapping.cs`
- [X] T019 Create the SQLite in-memory `WebApplicationFactory`, deterministic clock, identity helpers, and database reset fixtures in `tests/GovernedAccess.IntegrationTests/Infrastructure/GovernedAccessWebFactory.cs`
- [X] T020 Wire configuration, persistence, authentication, antiforgery, correlation, controller routing, `/mcp`, static assets, and non-API SPA fallback in `src/GovernedAccess.Web/Program.cs`

**Checkpoint**: The shared host starts with deterministic data, trusted cookie identity, antiforgery, persistence constraints, and reusable test fixtures.

---

## Phase 3: User Story 1 - Prepare and Submit a Safe Access Request (Priority: P1) MVP

**Goal**: Let an authenticated requester turn natural-language intent into a schema-validated draft, validate it against current stored data, and submit an immutable request without granting access.

**Independent Test**: Sign in as the requester, prepare the Client Alpha/`INC-1042` four-hour read-only example, review stable identifiers, submit it, and verify the immutable request is `AwaitingBusinessApproval` with no approval, operation, or grant.

### Tests for User Story 1

- [X] T021 [P] [US1] Add unit tests for client/environment, role, justification, and incident validation in `tests/GovernedAccess.UnitTests/RequestValidationTests.cs`
- [X] T022 [P] [US1] Add MCP contract tests for exact three-tool advertisement, typed schemas, stable IDs, forbidden capability absence, and typed failures in `tests/GovernedAccess.IntegrationTests/Mcp/McpContractTests.cs`
- [X] T023 [P] [US1] Add MCP timeout, cancellation, unavailable, and not-found interaction tests in `tests/GovernedAccess.IntegrationTests/Mcp/McpFailureTests.cs`
- [X] T024 [P] [US1] Add deterministic chat adapter tests for valid, incomplete, malformed, unsupported, timeout, cancellation, and no-live-model outcomes in `tests/GovernedAccess.IntegrationTests/Ai/DraftInterpretationTests.cs`
- [X] T025 [P] [US1] Add request creation integration tests for current-data revalidation, browser identity over-posting resistance, antiforgery, and absence of approval/grant side effects in `tests/GovernedAccess.IntegrationTests/Requests/CreateRequestTests.cs`

### Implementation for User Story 1

- [X] T027 [P] [US1] Define draft interpretation records, completeness rules, and the provider-neutral interpretation port in `src/GovernedAccess.Core/Ports/RequestDrafting.cs`
- [X] T028 [P] [US1] Implement stored-data request validation and normalized field results in `src/GovernedAccess.Core/Application/RequestValidator.cs`
- [X] T029 [US1] Implement immutable request creation, authenticated requester binding, direct submission to business approval, and creation/validation audit events in `src/GovernedAccess.Core/Application/RequestSubmissionService.cs`
- [X] T030 [P] [US1] Implement the EF-backed `IRequestContextReader` in `src/GovernedAccess.Web/Persistence/EfRequestContextReader.cs` and typed read-only handlers for the three MCP operations in `src/GovernedAccess.Mcp/RequestContextTools.cs`
- [X] T031 [US1] Register a stateless Streamable HTTP `/mcp` server from `src/GovernedAccess.Mcp/McpRegistration.cs` with an explicit allowlist containing only the three contract tools, then compose it from `src/GovernedAccess.Web/Program.cs`
- [X] T032 [P] [US1] Implement deterministic fake `IChatClient` modes for valid, incomplete, malformed, timeout, cancellation, and unavailable results in `src/GovernedAccess.Web/Ai/DeterministicChatClient.cs`
- [X] T033 [US1] Implement the `IChatClient` interpretation adapter with strict JSON schema parsing, a 30-second model timeout, 5-second MCP calls, identifier revalidation, safe logging, and linked cancellation in `src/GovernedAccess.Web/Ai/ChatRequestDraftInterpreter.cs`
- [X] T034 [US1] Implement `POST /api/request-drafts/prepare` and `POST /api/requests` without accepting actor or approver claims in `src/GovernedAccess.Web/Controllers/RequestDraftsController.cs` and `src/GovernedAccess.Web/Controllers/AccessRequestsController.cs`
- [X] T035 [P] [US1] Implement the credentialed typed fetch wrapper with antiforgery headers, Problem Details mapping, and `AbortSignal` support in `src/GovernedAccess.Web/ClientApp/src/api/client.ts`
- [X] T036 [P] [US1] Define TypeScript draft, request creation, session, and typed error contracts in `src/GovernedAccess.Web/ClientApp/src/api/contracts.ts`
- [X] T037 [US1] Implement the intent, draft review/correction, validation, and submission flow in `src/GovernedAccess.Web/ClientApp/src/pages/NewRequestPage.tsx`
- [X] T038 [US1] Add React Router navigation for `/requests/new` and a minimal application shell in `src/GovernedAccess.Web/ClientApp/src/App.tsx`

**Checkpoint**: User Story 1 works end to end and is independently demonstrable without a live model.

---

## Phase 4: User Story 2 - Make the Correct Business Decision (Priority: P2)

**Goal**: Resolve the configured client-specific business approver and bind an authenticated approve/reject decision to the exact immutable request scope.

**Independent Test**: Attempt a Client Alpha decision as the Beta approver, verify rejection and audit with no state change, then decide as the Alpha approver and verify the exact request ID/role evidence.

### Tests for User Story 2

- [X] T039 [P] [US2] Add unit tests for business decision state transitions, exact scope binding, rejection, and duplicate-stage prevention in `tests/GovernedAccess.UnitTests/BusinessDecisionPolicyTests.cs`
- [X] T040 [P] [US2] Add integration tests for configured approver resolution, wrong-client rejection, duplicate/invalid transition, actor over-posting, audit evidence, and antiforgery in `tests/GovernedAccess.IntegrationTests/Approvals/BusinessDecisionTests.cs`

### Implementation for User Story 2

- [X] T042 [P] [US2] Implement immutable-request business approval/rejection transition rules in `src/GovernedAccess.Core/Domain/BusinessDecisionPolicy.cs`
- [X] T043 [US2] Implement authenticated environment-responsibility authorization, decision persistence, concurrency handling, and auditing in `src/GovernedAccess.Core/Application/BusinessDecisionService.cs`
- [X] T044 [US2] Implement `POST /api/requests/{requestId}/business-decisions` with the restricted request body in `src/GovernedAccess.Web/Controllers/RequestDecisionsController.cs`
- [X] T045 [P] [US2] Implement business approve/reject controls driven by server-computed actions in `src/GovernedAccess.Web/ClientApp/src/components/BusinessDecisionPanel.tsx`
- [X] T046 [US2] Integrate the business decision panel and refreshed request status, with one restricted-payload/action-visibility wiring smoke, in `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx` and `src/GovernedAccess.Web/ClientApp/src/test/UiWiringSmoke.test.tsx`

### Corrective authenticated UI prerequisite

- [X] T047 [US2] Add one application-session wiring smoke proving anonymous protected content is gated, all four fixed identities are offered, requester sign-in enables draft preparation, and identity switching or sign-out refreshes shell state in `src/GovernedAccess.Web/ClientApp/src/test/AppSession.test.tsx`
- [X] T048 [US2] Implement the demo identity selector with `GET /api/session`, antiforgery-protected `POST /api/demo/session`, `DELETE /api/demo/session`, fixed principal keys, safe errors, and abort handling in `src/GovernedAccess.Web/ClientApp/src/components/DemoIdentitySelector.tsx`
- [X] T049 [US2] Integrate session bootstrap and the identity selector into the application shell, gate protected page content until authentication, and refresh UI state after sign-in, sign-out, or identity switching in `src/GovernedAccess.Web/ClientApp/src/App.tsx`

### Navigable business-detail vertical slice

- [X] T050 [P] [US2] Add integration tests for authenticated request-detail retrieval, requester and configured-approver visibility, wrong-client and anonymous nonvisibility, immutable scope, current status, and server-computed `decideBusinessRequest` action in `tests/GovernedAccess.IntegrationTests/Requests/RequestDetailQueryTests.cs`
- [X] T051 [US2] Implement the minimum participant-authorized request-detail projection with immutable submitted scope, current status, and server-computed business actions in `src/GovernedAccess.Core/Application/RequestQueryService.cs`
- [X] T052 [US2] Register the request query service and add `GET /api/requests/{requestId}` with cancellation and typed nonvisibility outcomes in `src/GovernedAccess.Web/Program.cs` and `src/GovernedAccess.Web/Controllers/AccessRequestsController.cs`
- [X] T053 [US2] Add the typed minimum detail contract, load request details by route ID with safe errors and abort handling, compose `/requests/:requestId`, and extend the existing application wiring smoke in `src/GovernedAccess.Web/ClientApp/src/api/contracts.ts`, `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`, `src/GovernedAccess.Web/ClientApp/src/App.tsx`, and `src/GovernedAccess.Web/ClientApp/src/test/AppSession.test.tsx`

**Checkpoint**: Sign in as requester to prepare and submit a request, switch between the Beta and Alpha business approvers, and verify that only the configured Alpha approver can decide it; no DevOps or provisioning behavior is required yet.

---

## Phase 5: User Story 3 - Approve and Provision the Authorized Scope (Priority: P3)

**Goal**: Let authenticated DevOps reject or approve the exact business-approved role, with a persisted-evidence reload and immediate synthetic provisioning for a fixed eight-hour lifetime.

**Independent Test**: From a business-approved request, approve as DevOps and verify one matching eight-hour grant and `Active`; verify role changes, crafted duration input, inconsistent persisted evidence, missing approval, and non-DevOps attempts create no grant.

### Tests for User Story 3

- [X] T054 [P] [US3] Add unit tests for exact role, fixed eight-hour grant scope, rejection, transition, and a provisioning operation keyed by request ID in `tests/GovernedAccess.UnitTests/DevOpsDecisionPolicyTests.cs`
- [X] T055 [P] [US3] Add protected handler tests for persisted request/approval/operation reload, immutable-scope consistency, fixed eight-hour expiry, missing approval, and caller-assertion distrust in `tests/GovernedAccess.IntegrationTests/Provisioning/ProtectedProvisioningTests.cs`
- [X] T056 [P] [US3] Add API tests for DevOps authentication, crafted scope/duration over-posting, rejection, antiforgery, fixed eight-hour expiry, and typed provisioning failure in `tests/GovernedAccess.IntegrationTests/Approvals/DevOpsDecisionTests.cs`

### Implementation for User Story 3

- [X] T057 [P] [US3] Implement DevOps decision rules using the request UUID as the provisioning operation key and provider idempotency identity in `src/GovernedAccess.Core/Domain/DevOpsDecisionPolicy.cs`
- [X] T058 [P] [US3] Define the provider-neutral synthetic provisioning port and typed timeout/failure outcomes in `src/GovernedAccess.Core/Ports/Provisioning.cs`
- [X] T059 [US3] Implement the protected provisioning handler that reloads request, approvals, and operation evidence, validates immutable scope, and finalizes grant/workflow/audit state in `src/GovernedAccess.Core/Application/ProtectedProvisioningService.cs`
- [X] T060 [P] [US3] Implement the 10-second controllable synthetic get-or-create provisioner with linked cancellation in `src/GovernedAccess.Web/Provisioning/SyntheticAccessProvisioner.cs`
- [X] T061 [US3] Implement DevOps decision persistence before provider invocation and failure transition handling in `src/GovernedAccess.Core/Application/DevOpsDecisionService.cs`
- [X] T062 [US3] Add `POST /api/requests/{requestId}/devops-decisions` without accepting role, client, environment, actor, or approval assertions to `src/GovernedAccess.Web/Controllers/RequestDecisionsController.cs`
- [X] T063 [P] [US3] Implement DevOps approve/reject controls and safe provisioning outcome display in `src/GovernedAccess.Web/ClientApp/src/components/DevOpsDecisionPanel.tsx`
- [X] T064 [US3] Integrate DevOps actions and activation/expiry grant summary into `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`

**Checkpoint**: The full two-human approval path produces exactly scoped synthetic access through deterministic authorization and provisioning.

---

## Phase 6: User Story 4 - Recover Safely and Review Evidence (Priority: P4)

**Goal**: Retry only failed provisioning with the same scope and request-based idempotency identity, and let authorized participants review relevant requests and complete evidence.

**Independent Test**: Simulate a lost provisioning response, retry as DevOps, verify the existing grant is returned with no duplicate across at least 100 concurrent attempts, and inspect complete correlated evidence as each authorized participant.

### Tests for User Story 4

- [X] T065 [P] [US4] Add integration tests for lost response, same-operation retry, wrong actor/state rejection, and persisted-evidence validation in `tests/GovernedAccess.IntegrationTests/Provisioning/RetryProvisioningTests.cs`
- [X] T066 [P] [US4] Add a 100-concurrent-attempt test proving one operation and one grant with consistent successful responses in `tests/GovernedAccess.IntegrationTests/Provisioning/ProvisioningIdempotencyTests.cs`
- [X] T067 [P] [US4] Add API tests for participant-filtered lists, complete enriched detail evidence, logical expiry, server-computed later-stage actions, and nonparticipant invisibility in `tests/GovernedAccess.IntegrationTests/Requests/RequestQueriesTests.cs`

### Implementation for User Story 4

- [ ] T068 [US4] Implement DevOps-only retry from `ProvisioningFailed` using the stored scope and immutable request ID through the same protected handler in `src/GovernedAccess.Core/Application/ProvisioningRetryService.cs`
- [ ] T069 [US4] Extend the established participant-authorized request query service with filtered lists, ordered decisions, grant state, logical expiry, audit evidence, and later-stage available actions in `src/GovernedAccess.Core/Application/RequestQueryService.cs`
- [ ] T070 [US4] Add `GET /api/requests` and `POST /api/requests/{requestId}/retry-provisioning`, and expose the enriched detail projection through the existing detail endpoint in `src/GovernedAccess.Web/Controllers/AccessRequestsController.cs`
- [ ] T071 [P] [US4] Implement relevant request rows, status filters, and actionable indicators in `src/GovernedAccess.Web/ClientApp/src/pages/RequestListPage.tsx`
- [ ] T072 [US4] Enrich immutable request detail rendering with validation, all decisions, provisioning outcome, grant expiry, retry, later-stage actions, and audit timeline in `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx`
- [ ] T073 [US4] Complete the `/requests` list route and three-route navigation while preserving the established session state and `/requests/:requestId` detail composition in `src/GovernedAccess.Web/ClientApp/src/App.tsx`

**Checkpoint**: Failed work is safely recoverable, concurrent duplicates converge on one grant, and authorized users can understand all material evidence.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validate security, build shape, observability, and the complete local demonstration across all stories.

- [ ] T074 [P] Add end-to-end security tests covering unauthenticated access, antiforgery on every unsafe endpoint, identity/role/approver over-posting, and `/api`/`/mcp` SPA fallback exclusion in `tests/GovernedAccess.IntegrationTests/Security/ApiSecurityTests.cs`
- [ ] T075 [P] Add audit persistence tests proving insert-only behavior, required event coverage, correlation fields, safe details allowlisting, and absence of raw prompts/full MCP payloads in `tests/GovernedAccess.IntegrationTests/Auditing/AuditEvidenceTests.cs`
- [ ] T076 [P] Add host tests proving model and MCP duration/outcome metadata logging without sensitive payload capture in `tests/GovernedAccess.IntegrationTests/Observability/OperationalLoggingTests.cs`
- [ ] T077 Add production static-file, hashed Vite asset, and three-route SPA host smoke validation in `tests/GovernedAccess.IntegrationTests/Hosting/SingleHostAssetTests.cs`
- [ ] T078 Run and reconcile every automated command and scenario in `specs/001-governed-production-access/quickstart.md`
- [ ] T079 Record final build, test, 15-minute demonstration, and no-live-dependency evidence in `specs/001-governed-production-access/validation-report.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** has no dependencies.
- **Foundational (Phase 2)** depends on Setup and blocks every user story.
- **US1 (Phase 3)** starts after Foundational and is the MVP.
- **US2 (Phase 4)** starts after Foundational but needs a submitted request fixture supplied by US1 for its end-to-end demonstration; T047-T049 establish the browser session and T050-T053 establish the minimum navigable request-detail vertical slice required by the phase checkpoint.
- **US3 (Phase 5)** depends on US2 through T053 because DevOps authorization requires a valid current business approval and all authenticated UI actions reuse the established session and detail route.
- **US4 (Phase 6)** depends on US3 for a durable failed provisioning operation and grant identity; it enriches the request-detail query and page established in US2 rather than introducing their reachability.
- **Polish (Phase 7)** follows all stories selected for delivery.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 -> US2 -> US3 -> US4
```

### Within Each User Story

1. Write the story's tests and confirm they fail for the intended missing behavior.
2. Implement domain models/policies and provider-neutral ports before application services.
3. Implement application services before HTTP/MCP adapters.
4. Implement typed frontend contracts/client behavior before page integration.
5. Run the story-specific test set and its independent test before the checkpoint.

T047 must be written and observed failing before T048 and T049 are implemented.
T048 implements the selector and session calls; T049 composes that state into the
application shell. Every remaining authenticated browser workflow depends on T049.
T050 must fail for the missing authenticated detail query before T051 and T052 are
implemented. T053 consumes that endpoint, makes the already-implemented business
panel reachable at `/requests/:requestId`, and must complete before the US2 checkpoint.
T069 and T072 enrich the established query/page; T073 adds the list route and does not
reimplement sign-in, sign-out, or request-detail routing.

React tests intentionally remain a minimal wiring smoke suite. Deterministic business
rules, authorization, validation, failure branches, persistence, and provisioning are
covered by the .NET unit and integration tasks rather than duplicated in component
tests. UI validation is limited to session/application wiring, one restricted decision
payload and server-action visibility smoke, and the production SPA host smoke.

## Parallel Opportunities

- In Setup, T003, T004, and T005 can proceed in parallel after T001; T006 and T007 then converge on the frontend host build.
- In Foundational, T008-T011, T015, and T016 use separate files; persistence wiring follows the shared entities.
- All test tasks at the start of each story are parallelizable because they occupy separate test files.
- Core policy, adapter, and React component tasks marked `[P]` can proceed concurrently once their shared contracts exist.
- US4 list and detail-enrichment tests can be prepared while US3 provisioning behavior is completed, but their implementation consumes the completed provisioning evidence.

## Parallel Example: User Story 1

```text
T021 RequestValidationTests.cs | T022 McpContractTests.cs | T023 McpFailureTests.cs | T024 DraftInterpretationTests.cs | T025 CreateRequestTests.cs
T027 RequestDrafting.cs | T028 RequestValidator.cs | T030 GovernedAccess.Mcp/RequestContextTools.cs | T032 DeterministicChatClient.cs
T035 client.ts | T036 contracts.ts
```

## Parallel Example: User Story 2

```text
T039 BusinessDecisionPolicyTests.cs | T040 BusinessDecisionTests.cs | T050 RequestDetailQueryTests.cs
T042 BusinessDecisionPolicy.cs | T045 BusinessDecisionPanel.tsx
T047 AppSession.test.tsx -> T048 DemoIdentitySelector.tsx -> T049 App.tsx
T050 RequestDetailQueryTests.cs -> T051 RequestQueryService.cs -> T052 AccessRequestsController.cs -> T053 request-detail route
```

## Parallel Example: User Story 3

```text
T054 DevOpsDecisionPolicyTests.cs | T055 ProtectedProvisioningTests.cs | T056 DevOpsDecisionTests.cs
T057 DevOpsDecisionPolicy.cs | T058 Provisioning.cs | T060 SyntheticAccessProvisioner.cs | T063 DevOpsDecisionPanel.tsx
```

## Parallel Example: User Story 4

```text
T065 RetryProvisioningTests.cs | T066 ProvisioningIdempotencyTests.cs | T067 RequestQueriesTests.cs
T071 RequestListPage.tsx can proceed while T068 ProvisioningRetryService.cs is implemented
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational.
2. Complete US1, including all US1 automated tests.
3. Complete corrective session tasks T047-T049 so the browser can establish the authenticated requester required by US1.
4. Stop and validate the primary Client Alpha/`INC-1042` request flow independently.
5. Demonstrate that malformed/incomplete model output and invalid stored values create no approval or grant.

### Incremental Delivery

1. Complete US2 through T053 so the browser session, request-detail query/route, and client-isolated business authorization boundary are usable together.
2. Add US3 to complete two-human approval and immediate protected provisioning.
3. Add US4 to prove recovery, concurrency idempotency, and inspectable evidence.
4. Complete cross-cutting security, logging, hosting, and quickstart validation.

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

## Phase 8: Convergence

- [ ] T080 Add the project-owned plain-CSS visual system with custom properties, base typography, forms, feedback states, visible focus, reduced-motion handling, identifier wrapping, and responsive primitives in `src/GovernedAccess.Web/ClientApp/src/styles.css`, then import it from `src/GovernedAccess.Web/ClientApp/src/main.tsx` per plan: UI implementation strategy (missing)
- [ ] T081 Refine the compact horizontal application shell and visibly synthetic identity utility without changing session behavior in `src/GovernedAccess.Web/ClientApp/src/App.tsx`, `src/GovernedAccess.Web/ClientApp/src/components/DemoIdentitySelector.tsx`, and `src/GovernedAccess.Web/ClientApp/src/styles.css` per plan: UI Presentation Correction (partial)
- [ ] T082 Apply the linear describe-review-submit composition to `src/GovernedAccess.Web/ClientApp/src/pages/NewRequestPage.tsx` and the restrained record-list/filter composition to `src/GovernedAccess.Web/ClientApp/src/pages/RequestListPage.tsx`, with shared rules in `src/GovernedAccess.Web/ClientApp/src/styles.css`, per plan: UI information hierarchy and responsive behavior (partial)
- [ ] T083 Add human-readable, non-color-only status and workflow presentation in `src/GovernedAccess.Web/ClientApp/src/components/RequestStatus.tsx`, then restructure `src/GovernedAccess.Web/ClientApp/src/pages/RequestDetailPage.tsx` and `src/GovernedAccess.Web/ClientApp/src/styles.css` into a wide record/action split that collapses to one source-ordered column with complete identifiers and semantic timestamps per FR-032 and SC-006 (partial)
- [ ] T084 Give approve, reject, pending, completed, error, and safe grant outcomes deliberate and distinct hierarchy without changing restricted payloads in `src/GovernedAccess.Web/ClientApp/src/components/BusinessDecisionPanel.tsx`, `src/GovernedAccess.Web/ClientApp/src/components/DevOpsDecisionPanel.tsx`, and `src/GovernedAccess.Web/ClientApp/src/styles.css` per plan: presentation acceptance (partial)
- [ ] T085 Extend the thin semantic UI smokes for human-readable status, workflow orientation, identity switching, safe grant output, and preserved accessible action names without adding CSS snapshots in `src/GovernedAccess.Web/ClientApp/src/test/UiWiringSmoke.test.tsx` and `src/GovernedAccess.Web/ClientApp/src/test/AppSession.test.tsx` per plan: UI accessibility and validation (missing)

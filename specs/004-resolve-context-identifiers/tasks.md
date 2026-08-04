# Tasks: Natural-Language Environment Resolution

**Input**: Design documents from `specs/004-resolve-context-identifiers/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`,
`contracts/`, and `quickstart.md`

**Tests**: Required only for new deterministic contracts and enforcement branches.
Existing tests are updated when their contract changes; scenarios already owned by an
existing suite are not added again under a feature-004 name. Scripted chat clients may
verify application-controlled tool exposure, sequencing guards, schema handling, and
state boundaries, but they do not verify whether a real model understands environment
wording. Semantic resolution quality remains an optional live-model evaluation.

**Organization**: Tasks are grouped by prioritized user story. A retained test task
must identify a behavior not already covered at the same layer. Tests precede the
implementation they specify and must fail for the expected reason before that
implementation begins.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no
  dependency on another incomplete task in the same group.
- **[Story]**: Maps the task to a user story in `spec.md`.
- Every task includes the exact repository-relative file path or paths it changes.

## Test-Minimization Decisions

The following existing coverage is reused rather than duplicated:

- Session restoration, fresh sessions, per-intake isolation, concurrent turn
  serialization, and failed-turn rollback remain covered by
  `MafRequestPreparationInterpreterSessionTests.cs` and
  `MafConversationSessionStoreTests.cs`.
- Authentication, generic clarification persistence, candidate rejection, safe real
  profile failure, and zero workflow state before confirmation remain covered by
  `TeamsRequestPreparationTests.cs` and `TeamsClarificationTests.cs`.
- Unknown, inactive, cross-client, cross-environment, and omitted incident validation
  remain covered by `RequestValidationTests.cs`; existing MCP incident assertions are
  retained while the MCP catalog test is rewritten.
- Malformed model output, prompt injection, cancellation, dependency unavailability,
  session rollback, unexpected tool catalogs, and metadata-only logging remain covered
  by `MafRequestPreparationFailureTests.cs`, `MafToolBoundaryTests.cs`, and
  `TeamsIntakeLoggingTests.cs`. Those tests receive only contract expectation updates.
- No deterministic scripted-model test is added for the semantic judgment that text is
  a readable description, a likely identifier, an incident title, or a partial ID.
  Such a test would only replay the fake's programmed decision.

---

## Phase 1: Setup (Existing Test Fixture Migration)

**Purpose**: Migrate existing deterministic fixtures to the new response schema and
two-tool catalog. No new general-purpose test client or MCP host fixture is needed;
the existing recording/scripted clients and injectable MCP reader already provide the
required seams.

- [ ] T001 Update existing clarification JSON fixtures for `environmentOptionIds` and removal of `ClientId` clarification, and delete the obsolete `get_available_roles` Teams regression without replacing it because T008 owns the wire contract, in `tests/GovernedAccess.IntegrationTests/Ai/MafConversationSessionStoreTests.cs`, `tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs`, `tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationInterpreterSessionTests.cs`, `tests/GovernedAccess.IntegrationTests/Teams/TeamsCandidateValidationTests.cs`, `tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs`, and `tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationResetTests.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the provider-neutral environment projection, bounded reader,
and structured clarification contracts required by every story.

**Critical**: No user-story implementation begins until this phase compiles and its
focused tests pass.

- [ ] T002 [P] Add one focused unit-test group for the new clarification value-object invariants: removal of `ClientId`, zero-to-20 unique environment option IDs, and the required relationship between option IDs and an environment clarification target in `tests/GovernedAccess.UnitTests/RequestPreparationTests.cs`
- [ ] T003 [P] Add focused persistence integration coverage for the new query only: one seeded projection/ordering case and one boundary theory for zero and 21 records proving empty success and fail-closed overflow without truncation in `tests/GovernedAccess.IntegrationTests/Persistence/EfRequestContextReaderTests.cs`
- [ ] T004 [P] Define the non-persistent `ProductionEnvironmentContext` projection and exact/list reader operations while retaining exact environment-role validation in `src/GovernedAccess.Core/Ports/CorePorts.cs`
- [ ] T005 [P] Extend the untrusted clarification proposal and application result contracts with bounded `EnvironmentOptionIds` and validated environment-choice records, and remove `RequestClarificationTarget.ClientId`, in `src/GovernedAccess.Core/Ports/RequestDrafting.cs` and `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- [ ] T006 Implement no-tracking exact/list environment context aggregation over clients, environments, and roles with stable ordinal ordering and a load-at-most-21 fail-closed cap, and remove the obsolete role-list reader operation, in `src/GovernedAccess.Web/Persistence/EfRequestContextReader.cs`
- [ ] T007 Update request-context test doubles for the new exact/list projection operations and removed role-list operation without adding duplicate test cases in `tests/GovernedAccess.UnitTests/RequestValidationTests.cs`, `tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs`, and `tests/GovernedAccess.IntegrationTests/Mcp/McpFailureTests.cs`

**Checkpoint**: Core contracts and persistence can return authoritative, ordered
environment/client/role context without any MCP or AI SDK types.

---

## Phase 3: User Story 1 - Resolve an Environment Description (Priority: P1) MVP

**Goal**: Resolve readable environment/client wording through bounded discovery,
preserve exact lookup, derive the client and available roles from the authoritative
environment, and clarify rather than silently replace an identifier that returns
`NotFound`.

**Independent Test**: At the deterministic boundary, verify that the model receives
the closed two-tool catalog and the authoritative environment discovery payload. In
the optional live-model matrix, submit "Client Alpha production in Europe" and verify
selection of `PROD-ALPHA-EU`; submit `PROD-ALPHA` and verify exact lookup followed by
discovery and explicit confirmation of any proposed alternative.

### Tests for User Story 1

- [ ] T008 [P] [US1] Rewrite the existing MCP contract and MAF catalog assertions for exactly two read-only tools, `{}` discovery, exact environment lookup, a common `environments` envelope, authoritative client display data, embedded ordered roles, and the unchanged exact incident contract; modify existing tests rather than adding parallel contract cases in `tests/GovernedAccess.IntegrationTests/Mcp/McpContractTests.cs` and `tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs`
- [ ] T009 [P] [US1] Add the minimum application-service unit coverage for structured choices: one valid authoritative option remains an unresolved clarification outside durable candidate scope, and one unknown option is rejected while unrelated valid candidate fields are preserved; rely on T002 for duplicate/count invariants in `tests/GovernedAccess.UnitTests/RequestIntakeServiceTests.cs`

### Implementation for User Story 1

- [ ] T010 [P] [US1] Replace the exact-only environment result and separate role tool with the dual-mode `get_production_environment` contract, common environment array, authoritative client display data, embedded ordered roles, and typed failures in `src/GovernedAccess.Mcp/RequestContextTools.cs`
- [ ] T011 [US1] Register exactly `get_production_environment` and `get_incident` and remove `get_available_roles` registration in `src/GovernedAccess.Mcp/McpRegistration.cs`
- [ ] T012 [P] [US1] Update the MAF allowlist, instructions, response schema, and payload parser so readable context is instructed to use direct discovery and identifier-like input is instructed to use exact lookup before typed-`NotFound` fallback in `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`
- [ ] T013 [P] [US1] Update deterministic runtime chat responses to the feature-004 proposal schema and remove client-ID clarification and separate-role-tool assumptions in `src/GovernedAccess.Web/Ai/DeterministicChatClient.cs`
- [ ] T014 [US1] Validate and authoritatively reload structured environment option IDs, reject unknown IDs, keep choices out of durable candidate scope, preserve unrelated valid candidate fields, and return application-owned authoritative choice records in `src/GovernedAccess.Core/Application/RequestIntakeService.cs`
- [ ] T015 [US1] Render server-owned environment clarification from authoritative choice records, including the one-option "did you mean" form, without advancing workflow state in `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`

**Checkpoint**: The application exposes the bounded authoritative environment context,
accepts only validated choice IDs, and can render a safe one-option correction. Real
model interpretation quality is evaluated separately from deterministic automation.

---

## Phase 4: User Story 2 - Clarify an Ambiguous Environment (Priority: P2)

**Goal**: Present zero, one, or multiple authoritative readable environment choices
without guessing, support relative selection only with active history, and repeat a
self-contained clarification after history loss.

**Independent Test**: Supply multiple validated structured option IDs through the
authenticated Teams test host; verify stable authoritative display values and no
created workflow state. Existing session tests continue to prove active-history carry
and lost-history isolation.

### Tests for User Story 2

- [ ] T016 [US2] Add one FullHost Teams integration test for the adapter delta only: multiple validated option IDs are rendered in authoritative stable order with client/environment display values and stable IDs, model prose is not used as choice data, and no request, approval, operation, or grant is created; reuse existing generic/zero-option and history tests in `tests/GovernedAccess.IntegrationTests/Teams/TeamsClarificationTests.cs`

### Implementation for User Story 2

- [ ] T017 [US2] Update ambiguity and history instructions for direct environment discovery, explicit-term conflicts, structured authoritative shortlist IDs, and self-contained clarification after lost history while retaining existing relative-choice rules in `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`
- [ ] T018 [US2] Extend server-owned environment clarification rendering to zero and multiple authoritative choices with stable IDs and deterministic ordering in `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`

**Checkpoint**: Ambiguous and no-match environment responses remain clarifications,
and the only new end-to-end test proves the Teams adapter's authoritative rendering
and state boundary rather than a scripted model's semantic choices.

---

## Phase 5: User Story 3 - Require an Exact Incident Identifier (Priority: P3)

**Goal**: Preserve exact-only incident lookup and validation while preventing titles,
descriptions, partial IDs, or reformatted values from becoming incident identifiers.

**Independent Test**: Existing MCP contract/failure tests retain exact incident lookup
and typed no-match assertions. Existing request-validation tests retain unknown,
inactive, cross-client, cross-environment, and omitted-incident coverage. Semantic
judgment for titles or partial IDs belongs to the optional live-model matrix.

### Tests for User Story 3

No new automated test task is required. T008 retains the unchanged MCP incident
contract, and the existing `RequestValidationTests.cs` suite already covers all
deterministic incident validation branches. Adding scripted-model cases for incident
titles or partial IDs would test only the programmed fake response.

### Implementation for User Story 3

- [ ] T019 [US3] Tighten exact-only incident tool descriptions and model instructions without adding discovery or changing deterministic incident validation in `src/GovernedAccess.Mcp/RequestContextTools.cs` and `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`

**Checkpoint**: Incident handling remains exact-only with no duplicate incident unit,
MCP, Teams, or scripted-model tests.

---

## Phase 6: User Story 4 - Recover Safely from Resolution Failure (Priority: P4)

**Goal**: Fail safely for catalog overflow, invalid structured choices, unexpected
tools, dependency failures, cancellation, and malformed output; only authoritative
exact `NotFound` may enable discovery fallback.

**Independent Test**: Verify the new public overflow mapping and deterministic
fallback gate. Existing resilience suites continue to cover cancellation,
unavailability, malformed output, prompt injection, unexpected catalogs, session
rollback, logging, and workflow-state isolation.

### Tests for User Story 4

- [ ] T020 [P] [US4] Update existing MCP failure expectations for the new environment list operation and add only the new catalog-overflow-to-typed-failure assertion; retain existing dependency-failure and cancellation cases instead of recreating them for discovery in `tests/GovernedAccess.IntegrationTests/Mcp/McpFailureTests.cs`
- [ ] T021 [P] [US4] Adapt the existing cancellation, unavailability, malformed-output, and unexpected-catalog fixtures to the new environment function shape, then add one focused theory proving the application-controlled fallback gate permits discovery after typed exact `NotFound` and rejects it after every other typed outcome; do not repeat T008's catalog contract or add semantic routing cases in `tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs` and `tests/GovernedAccess.IntegrationTests/Ai/MafRequestPreparationFailureTests.cs`

### Implementation for User Story 4

- [ ] T022 [US4] Complete typed MCP failure mapping and deterministic per-turn fallback gating while preserving cancellation, the existing iteration bound, and metadata-only logging in `src/GovernedAccess.Mcp/RequestContextTools.cs` and `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`

The remaining US4 enforcement is delivered by T006 (bounded persistence), T014
(invalid-choice rejection and candidate preservation), and T015/T018 (server-owned
safe clarification). Existing tests already own prompt injection, replay/concurrency,
logging, and zero workflow side effects.

**Checkpoint**: Every newly introduced deterministic failure branch has one owning
test layer, with no duplicate resilience matrix in persistence, MCP, MAF, and Teams.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Synchronize as-built documentation and run repository-wide regression
validation without expanding feature scope.

- [ ] T023 [P] Update the product overview, runtime architecture, current MCP contract link, and co-hosted MCP ADR from the three-tool exact-only design to feature 004 in `README.md`, `docs/architecture.md`, and `docs/adr/0001-use-one-deployable-service-including-mcp.md`
- [ ] T024 [P] Update orchestration, trust-boundary, failure, logging, and testing guidance for direct discovery, exact-first fallback, structured choices, exact-only incidents, reuse of existing deterministic coverage, and the optional live-model semantic matrix in `docs/request-intake-orchestration.md`, `docs/security-model.md`, and `docs/testing-strategy.md`
- [ ] T025 [P] Update operator/developer walkthroughs and reconcile current baseline/roadmap wording with the delivered two-tool runtime in `docs/teams-quickstart.md`, `docs/teams-advanced-reference.md`, `docs/local-development.md`, `docs/governed-production-access-product-baseline.md`, and `docs/roadmap.md`
- [ ] T026 Run the warnings-as-errors build, unit tests, FullHost integration tests, and remaining integration tests sequentially in the exact order and with the timeout/process-cleanup rules documented in `specs/004-resolve-context-identifiers/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 - Setup**: Starts immediately and updates existing fixtures only.
- **Phase 2 - Foundational**: Depends on Phase 1. T002 and T003 are written first;
  T004 and T005 can then proceed in parallel; T006 depends on T004; T007 follows the
  reader-contract change.
- **Phase 3 - US1**: Depends on Phase 2 and establishes the two-tool catalog,
  environment discovery, exact lookup, and authoritative choice-validation pipeline.
- **Phase 4 - US2**: Depends on US1's discovery and choice pipeline.
- **Phase 5 - US3**: Depends on US1's two-tool catalog but can proceed in parallel
  with US2.
- **Phase 6 - US4**: Depends on US1's base lookup/fallback path and can proceed in
  parallel with US2 and US3 once that path exists.
- **Phase 7 - Polish**: Depends on every selected story; T026 runs last.

### User Story Dependency Graph

```text
Setup -> Foundational -> US1 (MVP)
                            |-> US2
                            |-> US3
                            `-> US4
US2 + US3 + US4 -> Polish -> Final validation
```

### Within Each User Story

- Update an existing test instead of adding a new one when the behavior already has
  an owning layer.
- Add a test only for a new deterministic contract, branch, or adapter transformation.
- Complete provider-neutral contracts before persistence and protocol adapters.
- Complete persistence and MCP behavior before model orchestration.
- Complete deterministic option validation before Teams rendering.
- Do not use scripted chat output as evidence of semantic model understanding.

### Parallel Opportunities

- T002 and T003 can run together; T004 and T005 can run together afterward.
- US1 test tasks T008 and T009 can run together; T010, T012, and T013 modify separate
  source files and can run together after their owning tests are in place.
- T016 precedes T017 and T018; T017 and T018 modify separate files.
- T019 can proceed in parallel with US2 after the shared two-tool catalog exists.
- T020 and T021 can run together; T022 follows their focused failure assertions.
- Documentation tasks T023-T025 can run together after implementation stabilizes.

---

## Implementation Strategy

### MVP First: User Story 1

1. Migrate existing fixtures and complete the foundational contracts.
2. Rewrite existing MCP/catalog assertions and add the focused Core choice tests.
3. Deliver environment discovery, exact lookup, embedded roles, derived client,
   validated correction options, and one-option server-owned clarification.
4. Run only the affected focused tests at the US1 checkpoint.

### Incremental Delivery

1. **US1**: Establish authoritative environment context and safe structured choices.
2. **US2**: Add ambiguous-choice rendering with one targeted FullHost test.
3. **US3**: Tighten instructions while relying on existing exact-incident coverage.
4. **US4**: Add only the novel overflow mapping and deterministic fallback-gate tests.
5. **Polish**: Synchronize documentation and run the complete required gates.

### Scope and N/A Rationale

- **Database migrations**: N/A; the feature reads existing reference tables and adds
  no persisted field or entity.
- **React UI changes**: N/A; request intake remains Teams-only and the browser remains
  the read/decision surface.
- **Approval, provisioning, duration, and idempotency implementation**: N/A; these
  paths are unchanged and remain protected by the existing regression suites.
- **New session, concurrency, replay, prompt-injection, logging, or incident tests**:
  N/A; existing suites already cover the unchanged deterministic behavior.
- **Automated semantic model-routing tests**: N/A; scripted responses cannot measure
  semantic judgment. The optional live-model matrix is release evidence, not CI.
- **New packages, services, workflow engines, search indexes, aliases, or embeddings**:
  N/A; the existing modular host, MCP endpoint, and bounded synthetic dataset suffice.

## Notes

- `[P]` means different files and no dependency on an incomplete task in that group.
- Historical feature 001-003 artifacts remain historical; current as-built links
  should point to the feature-004 MCP contract after implementation.
- Complete each story checkpoint before treating that story as independently done.
- Do not run the integration project without one of the two required `TestLevel`
  filters, and never run the final validation commands in parallel.

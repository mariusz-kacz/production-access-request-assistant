# Implementation Plan: Deterministic Request Intake

- **Status:** Replacement implementation in progress; production cutover has not started
- **Replanned:** 2026-08-24 after Tasks 1 and 2 were reverted
- **Refined:** 2026-08-25 so exact environment proposals use exact reload without Core search replay
- **Replanned:** 2026-08-25 for an extractable reference-authority module and two independent target databases
- **Refined:** 2026-08-26 to replace the special clarification-selection protocol with ordinary sparse exact-ID patches
- **Refined:** 2026-08-26 to share final Teams transport and card-presentation primitives without coupling the delivered and target intake graphs
- **Refined:** 2026-08-26 so remaining delivery consumes the simplification plan's grouped outcomes, contracted schemas, and confirmation safeguards
- **Refined:** 2026-08-27 by operator direction so Task 11 replaces the delivered card
  presentation/action contract with the final target-compatible contract instead of
  preserving legacy card compatibility
- **Target branch:** `feature/decouple-teams-approval-flow`
- **Primary authority:** `SPEC-deterministic-request-intake.md`
- **Task-list target:** This file is both the plan and the ordered task checklist
- **Planned slices:** 18, followed in dependency order unless a task explicitly says otherwise

## Outcome

Build the deterministic request-intake replacement as an independent, complete path
while the delivered path remains unchanged and authoritative. After the replacement
passes its isolated full-host checkpoint, switch production composition once, then
delete the delivered implementation and its schema in the immediately following task.

The isolated target is a modular monolith: `GovernedAccess.ReferenceAuthority` owns a
reference database, `GovernedAccess.Workflow.Persistence` owns a workflow database,
Core consumes ports, MCP owns only wire DTOs, and Web remains the single executable
composition root. The two new modules are the extraction seam for a possible future
security/deployment boundary; no second service or loopback HTTP is introduced now.

The target flow remains:

```text
exact trimmed case-insensitive /new -> deterministic reset protocol

all other authenticated nonblank requester text
    -> one bounded MAF agent
    -> closed provider-neutral TurnProposal
    -> deterministic authoritative reducer
    -> short optimistic commit
    -> application-owned response/card
```

Authenticated Adaptive Card confirmation remains the only request-creation path.

## Migration architecture

### Agreed replacement rule

The implementation uses parallel construction, not compatibility mutation:

1. Freeze the delivered `RequestIntakeSession` path.
2. Build a separate target `RequestPreparation` path with final names and semantics.
3. Exercise the target through an isolated test-host composition that is not reachable
   from the production `Program` registration.
4. Keep the delivered production path authoritative until the target passes its full
   checkpoint and receives human cutover approval.
5. Switch production registrations atomically. There is no runtime fallback, feature
   flag, per-request router, shadow mutation, or dual write.
6. Delete the delivered path immediately after the switch.

Parallel construction applies to the authoritative intake graphs, not to identical
Teams SDK mechanics. Existing delivered Teams code may be behavior-preservingly
refactored to extract Web-owned transport and pure presentation primitives for reuse by
the target adapter when all of the following hold:

- the shared code references neither delivered nor target preparation, proposal,
  persistence, authority, orchestration, or confirmation types;
- it performs no old/target selection, routing, conversion, fallback, or synchronization;
- it remains useful and unchanged after the delivered path is deleted in Task 15; and
- delivered characterization and production-composition tests remain green throughout.

**Operator-directed Task 11 card rule (2026-08-27):** the behavior-preservation rule
above continues to apply to authentication, activity transport, durable workflow
semantics, and production registration, but not to the delivered Adaptive Card shape or
action payload. Task 11 replaces that card surface directly with the final deterministic-
intake presentation and the closed `{ schemaVersion, preparationId }` payload. No
legacy card renderer, payload alias, compatibility parser, or duplicate card-layout
implementation is retained. Production continues to resolve only the delivered
semantic graph until Task 14, using the final card primitives during the transition.

The two flows retain separate thin semantic adapters, authoritative fact assembly,
closed action-payload handling, and confirmation services. This reuse rule narrows
temporary presentation duplication without changing ADR 0011's isolated target proof
or atomic production cutover.

Target code must never adapt a delivered full-candidate snapshot into a sparse proposal,
share mutable aggregate backing state with `RequestIntakeSession`, or expose legacy
aliases such as `Id`/`PreparationId` or `PersistenceVersion`/`ConcurrencyVersion` on one
type. There is no `TaskNineCompatibilityAttribute` or equivalent task-number marker.

### Ownership during parallel construction

| Area | Authority before cutover | Target construction rule |
|---|---|---|
| Production Teams intake | Delivered `TeamsAccessRequestAgent` graph | A thin target agent is registered only by the isolated test host until Task 14; both adapters may use final Web-owned transport and pure presentation primitives that satisfy the reuse rule above. |
| Canonical preparation | Delivered `RequestIntakeSession` | New `RequestPreparation` owns only target state; it never reads or writes a delivered session. |
| Proposal interpretation | Delivered full-candidate interpreter | New interpreter emits only `TurnProposal`; no translation exists between proposal models. |
| Persistence | Delivered unified `GovernedAccessDbContext` and database | New target `ReferenceAuthority` and `Workflow.Persistence` projects own separate databases used only by the isolated target composition until Task 14; no entity, row, file, or migration is shared or copied. |
| MCP | Delivered two-tool production registration | New four-tool catalog is invoked only by the target test composition until Task 14. |
| Confirmation | Delivered submission service | New confirmation service accepts only `PreparationId` and target evidence. |
| Downstream approvals/provisioning | Existing deterministic workflow | Reused unchanged after a target confirmation creates the same final `AccessRequest` domain type. |

### Deliberate temporary seams

Only these coexistence points are allowed. They are not aggregate compatibility APIs.

| Seam | Introduced | Removed/finalized | Constraint |
|---|---:|---:|---|
| Delivered unified persistence remains beside the isolated target's two databases | Tasks 4, 6, and 7 | Task 15 | No shared entity, file, migration, transaction, row copy, synchronization, or dual write. Existing local data is disposable. |
| `AccessRequest` supports target creation with required `PreparationId` while delivered creation still compiles | Task 12 | Task 15 | The target factory/constructor requires a nonempty ID. Only the delivered path may temporarily create a request without it. |
| Target service and module registrations exist in a test-only composition | Task 13 | Task 14 | Production `Program` resolves only the delivered graph before cutover and only the target graph after cutover. |

Any additional bridge, alias, dual write, fallback, or synchronization mechanism is a
material plan change and requires human review.

Shared Teams transport and pure presentation primitives satisfying the reuse rule are
final implementation components, not an additional coexistence seam. A component that
accepts either preparation model, translates between flows, or exists only until Task
15 is a prohibited compatibility seam rather than reusable infrastructure.

### Database transition

Existing local SQLite data is disposable. Target schema work supports fresh databases
only:

- no adoption, backfill, row copy, or upgrade from the delivered schema;
- no startup-time automatic deletion;
- explicit operator deletion before the cutover build;
- isolated target tests always create separate fresh reference and workflow databases;
- target reference and workflow migrations have independent histories and fixtures;
- Task 15 removes the delivered unified context/migrations and leaves only the final
  two-database fresh schema.

## Authority and resolved conflicts

Apply the constitution and repository rules first, then the approved feature
specification, ADRs 0005 and 0007-0011, target MCP contract and test matrix, and current
as-built documents outside the changed boundary.

| Conflict | Resolution |
|---|---|
| Current as-built documents describe the delivered two-tool catalog while the approved target requires four. | `AGENTS.md` records both phase-bound rules: production remains on two tools during Tasks 1-13, the target four-tool catalog is isolated, and Task 14 atomically replaces two with four. Full current-state documentation follows verified final evidence in Task 17. |
| Current behavior uses complete candidates, process-local choices, `Invalidated`, and reserved request IDs. | Those remain facts about the delivered path only. Target code cannot depend on them, and Task 15 deletes them after cutover. |
| ADR 0005 describes reserved-request tombstone evidence. | Preserve the tombstone principle, but final target evidence uses immutable preparation identity and unique `Request.PreparationId`; clarify the ADR after implementation. |
| The target incident MCP projection is singular while Core authority must handle zero/one/many eligible links. | Keep the closed model projection; define a richer Core authority projection and never treat MCP output as authority. |
| Replaying model-side search after a uniquely resolved exact environment adds latency but cannot validate requester semantics. | `exactEnvironmentId` uses exact authoritative reload only; `searchQuery` uses authoritative search and exact-reloads a unique result. ADR 0010 records the tradeoff. |
| The delivered host stores reference and workflow data in one Web-owned EF context, while the target must be easy to extract behind a future security boundary. | Build independent `ReferenceAuthority` and `Workflow.Persistence` projects/databases in an isolated target composition, keep one executable host, switch once after proof, then delete the delivered unified persistence. ADR 0011 records the boundary. |

No lifecycle, version, reducer-order, clarification, confirmation, or budget decision is
otherwise open. A new conflict with the approved specification stops the owning task.

## Standing verification

Every code task uses TDD where behavior changes and runs focused tests first. After any
code change, run the repository commands sequentially in this exact order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. Run
`npm test --prefix src/GovernedAccess.Web/ClientApp -- --run` when frontend behavior or
contracts change. Automated tests use deterministic chat clients; live evaluation is
an explicit operator action. Every task also applies the repository Definition of Done
and `git diff --check`.

## Phase A - Independent target foundations

### Task 1 - Define closed target proposal and outcome contracts

- [x] Complete

**Description:** Add provider-neutral target contracts in a new preparation namespace.
Do not edit or implement the delivered proposal types.

**Acceptance criteria:**

- [x] `TurnProposal` has the exact closed dialogue acts, sparse operations, clarification selection, discussion topics, structural failures, per-operation results, and application outcomes from the specification.
- [x] Mutable proposal fields are only environment, role, justification, and optional incident; requester text, client, identity, duration, lifecycle, request identity, approval, and provisioning data are absent.
- [x] Target contracts contain no provider, MAF, MCP, Teams, EF, raw JSON, model prose, or delivered proposal types.

**Verification:** Focused construction/reflection tests for valid and invalid act/payload combinations, omission, bounds, and forbidden dependencies; then standing backend verification.

**Dependencies:** None.

**Files likely touched:**

- new files under `src/GovernedAccess.Core/Preparations/Contracts/`
- new `tests/GovernedAccess.UnitTests/PreparationContractTests.cs`

**Estimated scope:** Medium.

**Acceptance coverage:** Preparatory AC-03, AC-07, AC-09, AC-14, AC-23, AC-24, AC-43.

### Task 2 - Build the independent `RequestPreparation` aggregate

- [x] Complete

**Description:** Implement target candidate, lifecycle, clarification, attribution, and
version invariants in new domain types. Leave `Domain/Drafts/RequestIntakeSession.cs`
unchanged.

**Acceptance criteria:**

- [x] Root/revision creation, immutable random UUIDv4 `PreparationId`, mandatory revision predecessor, exact five-state lifecycle, and terminal tombstones match the specification.
- [x] `CandidateVersion`, `ConcurrencyVersion`, timestamps, 30-minute Ready deadline, 50-turn bound, five-choice clarification, and bounded safe attribution follow all normative examples.
- [x] Ready state is immutable and context-free; revisions use a distinct successor; terminal state clears candidate/context according to the tombstone contract.

**Verification:** New aggregate/value-object unit tests cover all lifecycle and version tables, invalid construction, context binding, attribution bounds, and Ready immutability; source checks prove no dependency on `RequestIntakeSession`; then standing backend verification.

**Dependencies:** Task 1.

**Files likely touched:**

- new files under `src/GovernedAccess.Core/Domain/Preparations/`
- new `tests/GovernedAccess.UnitTests/RequestPreparationAggregateTests.cs`

**Estimated scope:** Medium.

**Acceptance coverage:** AC-23, AC-24, AC-29-AC-34, AC-43.

### Checkpoint A - Clean domain separation

- [ ] Target contracts and aggregate tests pass.
- [ ] Delivered production behavior and regression suites remain unchanged.
- [ ] Source checks find no target dependency on delivered draft contracts or sessions.
- [ ] Human review confirms names and aggregate boundaries before persistence work.

## Phase B - Deterministic authority and reduction

### Task 3 - Add target authority projections and shared environment search

- [x] Complete

**Description:** Define Core authority ports and the one deterministic search policy.
Add explicit searchable and eligibility facts without using MCP projections as Core
authority.

**Acceptance criteria:**

- [x] Exact environment, environment-role, and incident authority projections preserve source boundaries; incident authority supports zero/one/many eligible environment links.
- [x] One ordinal, locale-invariant search implementation enforces exact normalization, searchable fields, eligibility, zero/unique/2-5/6-20/>20 outcomes, and no ranking or truncation.
- [x] Client is derived only from exact eligible environment data; role and incident relationships are independently exact-revalidated.

**Verification:** Search matrix and authority-contract unit tests cover every cardinality, Unicode/ordinal edge, eligibility gate, and failure outcome; then standing backend verification.

**Dependencies:** Task 2.

**Files likely touched:**

- new preparation authority/search files in `GovernedAccess.Core`
- additive reference-data facts where required
- new focused unit tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-15-AC-22.

### Task 4 - Build the isolated reference-authority module and database

- [x] Complete

**Description:** Add the final `GovernedAccess.ReferenceAuthority` project as a parallel
target implementation. It owns a separate SQLite reference database and implements the
Task 3 authority ports. Do not edit production registration, the delivered unified
context, delivered seeding, or delivered intake tests.

**Acceptance criteria:**

- [x] The module alone owns `ReferenceAuthorityDbContext`, its independent connection string, migrations, seeder, clients/business-approver mappings, searchable environment eligibility facts, environment-role assignments, incidents, and zero/one/many incident links.
- [x] Focused adapters implement search, exact environment/client, entitlement, and incident ports with independent typed failures; the provider-neutral exact environment authority projection is additively completed with the owning client's business-approver principal ID required by confirmation, while MCP omits that hidden fact.
- [x] Architecture tests enforce `ReferenceAuthority -> Core`, forbid reference EF types outside the module, and prove the ordinary production host still resolves only the delivered unified context while a focused target fixture uses only the new reference database.

**Verification:** Reference policy/adapter unit tests; fresh migration, seed, restart,
eligibility, source-failure, and database-isolation integration tests; delivered
persistence/MCP regressions; then standing backend verification.

**Dependencies:** Task 3.

**Files likely touched:**

- new `src/GovernedAccess.ReferenceAuthority/` project
- additive provider-neutral authority-contract changes in `GovernedAccess.Core` required by the module
- solution/project references and target-only registration extension
- new focused unit/integration architecture and persistence tests

**Estimated scope:** Large, implemented as schema/seed then port-adapter TDD increments
inside one module boundary.

**Acceptance coverage:** AC-16-AC-22, AC-48-AC-51.

### Task 5 - Implement the sparse authoritative reducer

- [x] Complete

**Description:** Build the pure target reducer against `RequestPreparation`, target
contracts, and authority ports. It receives no requester text.

**Acceptance criteria:**

- [x] Structural violations reject the whole turn; environment resolution uses exact reload without search for `exactEnvironmentId` and shared-policy search plus unique-result exact reload for `searchQuery`.
- [x] Omission is non-destructive, dependency cascades are exact, ambiguity preserves current canonical scope, and at most one environment-before-role clarification is produced.
- [x] Clarification selection validates preparation/version/target/index, maps only through stored choices, exact-reloads the selected entity, and executes the normal full reduction pipeline.

**Verification:** Table-driven reducer tests cover exact-ID search bypass, every search
cardinality, mixed operations, source failures, incident cardinality, stale selection,
provenance, and zero-mutation structural failures; then standing backend verification.

**Dependencies:** Tasks 1-3.

**Files likely touched:**

- new reducer/application files under `GovernedAccess.Core/Preparations/`
- new reducer matrix tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-08-AC-14, AC-17-AC-28.

### Checkpoint B - Pure target engine

- [x] Target reducer passes the complete deterministic matrix without language fixtures.
- [x] Core target APIs accept no requester message, provider type, MCP payload, or delivered draft type.
- [x] Delivered host remains the only production authority.

## Phase C - Inactive target infrastructure

### Task 6 - Build target preparation persistence in the workflow module

- [x] Complete

**Description:** Add `GovernedAccess.Workflow.Persistence` and its separate workflow
SQLite database. Implement only the target preparation store and synthetic principal
snapshot persistence in this task. Keep the delivered Web-owned unified context and all
production registration untouched.

**Acceptance criteria:**

- [x] Target persistence round-trips canonical state, one clarification context, predecessor, both versions, lifecycle timestamps, turn metadata, and bounded attribution without raw conversational content.
- [x] OCC uses `ConcurrencyVersion`; the durable active unique index binds channel, tenant, actor, conversation, and requester for exactly Collecting/Ready.
- [x] The workflow context has its own connection string, migrations, seeder, and fixtures; it contains no client, environment, environment-role, or incident entity/table/FK, and store outcomes distinguish conflict, race, unavailable, and malformed-state failures.

**Verification:** Fresh workflow-SQLite mapping/migration, restart, unique-index, OCC,
privacy, missing-reference-table, and database-file isolation tests; delivered persistence
regressions; then standing backend verification.

**Dependencies:** Tasks 2 and 5.

**Files likely touched:**

- new `src/GovernedAccess.Workflow.Persistence/` project
- target `WorkflowDbContext`, `EfRequestPreparationStore`, migrations, and seeder
- focused persistence integration tests

**Estimated scope:** Large because persistence invariants are reviewed together.

**Acceptance coverage:** AC-12, AC-29, AC-34-AC-36, AC-43, AC-48-AC-51.

### Task 7 - Complete target downstream workflow persistence

- [x] Complete

**Description:** Extend the isolated workflow module with the final request, approval,
operation, grant, and audit mappings/store behavior needed after target confirmation.
This is a parallel implementation of delivered Web persistence, not an in-place move;
production still resolves the delivered unified context.

**Acceptance criteria:**

- [x] The target workflow database persists authenticated principals, immutable requests, decisions, operations, grants, and audit evidence with existing concurrency/idempotency invariants and no EF relationship to reference facts.
- [x] Existing downstream Core behavior runs against the target workflow ports while every environment/client/role/incident read crosses the new authority ports; reference outage and workflow outage remain distinguishable and no cross-database transaction exists.
- [x] Focused architecture/composition tests prove the delivered host still uses only delivered persistence and the target fixture uses only `Workflow.Persistence` plus `ReferenceAuthority`, with no shared database file, row, seeder, or dual write.

**Verification:** Target workflow-store mapping, migration, query, approval, provisioning,
idempotency, restart, and independent-failure integration tests; ordinary downstream
regressions; then standing backend verification.

**Dependencies:** Tasks 4 and 6.

**Files likely touched:**

- additive request/workflow mappings and adapters in `GovernedAccess.Workflow.Persistence`
- target-only composition and database fixtures
- focused downstream persistence/integration tests

**Estimated scope:** Large, implemented as request/audit then approval/provisioning TDD
increments without production registration.

**Acceptance coverage:** AC-35-AC-40, AC-48-AC-51.

### Task 8 - Build the inactive four-tool MCP catalog

- [x] Complete

**Description:** Implement the target four-tool wire surface in separate handlers and
registration extensions. Do not call the target registration from production
composition.

**Acceptance criteria:**

- [x] Exactly the four approved typed read-only tools and closed schemas match the target contract; no mutation or role embedding in exact environment lookup exists.
- [x] MCP search and Core's `searchQuery` path use the same search-policy implementation; exact-ID proposals do not replay search, and tool results remain bounded interpretive context.
- [x] The delivered production endpoint still exposes exactly its current two tools before cutover.

**Verification:** Target contract/transport/failure tests use an explicit target MCP test host; production-registration regression proves two tools remain active; then standing backend verification.

**Dependencies:** Tasks 3 and 4.

**Files likely touched:**

- new target files in `GovernedAccess.Mcp`
- target MCP contract tests and test-host wiring

**Estimated scope:** Medium.

**Acceptance coverage:** AC-15-AC-18, AC-22, AC-41, AC-43, AC-50-AC-51.

### Task 9 - Build the inactive bounded agent interpreter

- [x] Complete

**Description:** Add the target MAF interpreter, structured schema translation,
immediate fail-closed validation, prompt envelope, and execution budgets without
changing production AI registration.

**Acceptance criteria:**

- [x] Every non-`/new` target free-text turn reaches the agent and yields only the closed target proposal; a uniquely justified one-result MCP search may produce `exactEnvironmentId`, while ambiguous results must produce `searchQuery`, clarification-compatible behavior, or `unclear`.
- [x] Stored justification and MCP display fields are explicitly delimited as untrusted; no raw prompt, response, reasoning, query, transcript, or full MCP payload is logged or persisted.
- [x] Startup and execution enforce 4,000 characters, 50 turns per preparation, one call/tool and four total, provider iteration bounds, and one cumulative 30-second budget.

**Verification:** Deterministic chat-client tests cover unique-search-to-exact-ID behavior,
ambiguous-search restraint, optional tool use, schemas, immediate failure without a
repair invocation, cancellation, budgets, prompt-injection boundaries, and safe
telemetry; production AI registration remains unchanged; then standing backend
verification.

**Dependencies:** Tasks 1 and 8.

**Files likely touched:**

- new target files under `GovernedAccess.Web/Ai/`
- new target AI integration tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-01-AC-07, AC-41, AC-43, AC-44.

### Task 10 - Build target turn orchestration, lifecycle, restart, and races

- [x] Complete

**Description:** Compose target load -> agent -> reduce -> short OCC commit behavior in
a new application service. It remains reachable only from direct/component tests.

**Acceptance criteria:**

- [x] Agent/MCP latency occurs outside database transactions; accepted changes and clarification commit atomically; stale proposals are rejected without replay.
- [x] The closed reset protocol event, first-turn creation, one-active uniqueness, permanent turn exhaustion, collecting staleness, lazy Ready expiry, and typed failures match the specification; this service never receives or compares requester text.
- [x] Ready revisions preserve A for no-op/failure and atomically supersede A/create mandatory-predecessor B for the first accepted material change or clarification.

**Verification:** Unit/component tests cover normative lifecycle examples, restart, active-creation races, OCC collisions, deadline behavior, and no side effects on failures; then standing backend verification.

**Dependencies:** Tasks 5, 6, and 9.

**Files likely touched:**

- new target application service and ports in `GovernedAccess.Core`
- new target orchestration adapter in `GovernedAccess.Web`
- focused unit/integration tests

**Estimated scope:** Large because one commit protocol owns the concurrency boundary.

**Acceptance coverage:** AC-01-AC-14, AC-23-AC-36, AC-44.

### Task 10A - Simplify clarification to ordinary sparse exact-ID patches

- [x] Complete - implementation and automated evidence pass; by operator direction
  on 2026-08-26, credentialed live-model evaluation is deferred to the later
  promotion gate and is not required to complete Task 10A or begin Task 11.

**Description:** Replace the already-built target clarification-selection protocol with
the ordinary sparse-patch path before Teams rendering or production composition uses
it. This is one atomic protocol simplification; do not retain compatibility aliases,
dual contracts, or a fallback selection branch. This task supersedes the
selection-specific acceptance clauses recorded in completed Tasks 1, 2, 5, 9, and 10
without reopening or rewriting their completed history.

**Acceptance criteria:**

1. [x] Remove `selectClarification`, `ClarificationSelection`, option-index payloads,
   selection-to-operation conversion, and selection-specific outcomes/checks from
   provider-neutral contracts, adapters, prompts, Core, persistence, and renderers where
   applicable.
2. [x] Expose active ordered clarification choices—including exact canonical IDs,
   1-based display positions, safe authoritative distinguishing fields, target, and
   creation time—in bounded provider-neutral agent input.
3. [x] Make clarification replies return ordinary `updateDraft` exact-ID environment or
   role field operations, or conservative `unclear` when the reference is unresolved.
4. [x] Route those operations through the existing authoritative reducer without a
   special selection branch or displayed-choice-membership acceptance check.
5. [x] Simplify clarification persistence by removing candidate-version selection
   binding and relying on the candidate/context snapshot plus `ConcurrencyVersion` OCC;
   remove or migrate any existing clarification-bound candidate-version column.
6. [x] Implement the specification's clarification-context consumption, preservation,
   replacement, invalidation, ready-immutability, and successor-preparation rules.
7. [x] Remove dead selection-specific code and schema/migration fields so only one
   clarification protocol remains.
8. [x] Update deterministic, integration, architecture, prompt/schema, restart/OCC, and
   evaluation coverage, including multilingual/descriptive references, explicit valid
   IDs outside displayed choices, conservative ambiguity, normal exact reload/cascades,
   and zero consequential side effects. Credentialed execution remains later promotion
   evidence rather than a Task 10A completion requirement.
9. [x] Update affected as-built documentation only after implementation evidence passes
   where the repository workflow requires that reconciliation.

**Verification:** Use TDD for each contract/reducer/persistence change; run focused
proposal, reducer, agent-input, persistence/migration, restart/OCC, architecture,
renderer, and evaluation tests; then run the standing backend sequence and any affected
frontend suite sequentially. Task 16 later runs and promotes the complete credentialed
live-model suite.

**Required exit gate:**

- production contracts/code contain no `selectClarification`,
  `ClarificationSelection`, option-index mutation protocol, or index-to-ID reducer path;
- exact `/new` remains the only deterministic requester-text command;
- `first`/`the other one`/`el primero` are interpreted by the agent and result in
  ordinary exact-ID sparse patches or conservative `unclear`;
- all accepted IDs are independently exact-reloaded and processed by the normal
  reducer;
- active clarification survives restart and is consumed/preserved by the documented
  rules;
- stale candidate/context snapshots cannot commit;
- all affected deterministic and integration tests pass, with credentialed live-model
  execution retained as a later promotion gate; and
- no free-text turn creates a request, approval, provisioning action, or grant.

**Dependencies:** Tasks 1, 2, 5, 6, 9, and 10.

**Files likely touched:**

- target preparation contracts, aggregate, reducer, orchestration, and typed outcomes in
  `GovernedAccess.Core`
- target MAF prompt/schema translation and bounded agent-input adapter in
  `GovernedAccess.Web/Ai/`
- target clarification persistence mappings and migrations in
  `GovernedAccess.Workflow.Persistence`
- affected target renderers plus deterministic, integration, architecture, restart/OCC,
  prompt/schema, and live-evaluation tests

**Estimated scope:** Large but focused; the protocol, persistence cleanup, and evidence
must land together so two clarification paths never coexist.

**Acceptance coverage:** AC-01-AC-05, AC-07-AC-13, AC-23-AC-35, AC-41, AC-43,
AC-45-AC-47.

### Simplification handoff for Tasks 11-17

The focused simplification plan in
[`deterministic-request-intake-simplification.md`](deterministic-request-intake-simplification.md)
governs the contracts consumed by the remaining planned work. Completed task records
above remain historical evidence and are not rewritten to describe the simplified
implementation retroactively.

- Target renderers consume only the application-owned `ScopeResult` and
  `JustificationResult`, each expressed as `Applied`, `NoOp`, `Rejected(reason)`, or
  `NeedsClarification`. They must not rebuild per-field transactions, verdict lists,
  dependency-propagation summaries, or model prose channels.
- Target persistence and agent work must not restore a candidate-progress counter,
  permanent preparation turn budget, collecting-stale policy, justification
  self-certification, hidden environment-search result tier, incident link table, or
  separate clarification-selection protocol.
- Target confirmation continues to require exact owned immutable `PreparationId`, lazy
  30-minute expiry, independent authoritative revalidation, unique
  `Request.PreparationId`, stable replay identity, and deterministic fact-drift versus
  source-unavailable behavior.
- Tasks 13-16 must prove these contracted absences and safeguards in the isolated host,
  cutover, deletion, and promoted evidence. Task 17 alone promotes verified final
  runtime behavior into current/as-built documents.

The simplification plan's credentialed live-evaluation report remains a promotion
dependency. Automated handoff verification does not satisfy that gate by itself.

### Task 11 - Extract reusable Teams primitives and build target behavior

- [x] Complete

**Description:** Extract final protocol-neutral Teams context, transport, tracking, and
pure Adaptive Card presentation primitives. Replace the delivered card surface with the
final target-compatible card contract, then build a thin target Teams adapter,
target-owned semantic response renderer, authoritative Ready-card assembler, closed
action-payload handling, and confirmation seam on those primitives. Do not retain a
legacy card renderer or payload parser, create a second complete card-layout
implementation, or register the target adapter in production.

**Acceptance criteria:**

- [x] Only the Teams boundary recognizes exact trimmed case-insensitive `/new`; every other authenticated nonblank message goes through target orchestration.
- [x] Shared Teams components own only authenticated activity/context normalization, locale fallback, conversation/card-activity presentation metadata, activity delivery/update mechanics, and pure rendering from Web-owned presentation models. They reference neither preparation graph and contain no old/target routing or compatibility conversion.
- [x] Delivered and target adapters separately own orchestration calls, outcome-to-prose mapping, authoritative fact assembly, closed action handling, confirmation calls, and outcome-specific telemetry; target code references no delivered intake type. Both accept only the final target-compatible payload contract rather than retaining a legacy alias.
- [x] All prose and selectable choices are application-rendered with authenticated locale, safe encoding, exact canonical facts, five-choice maximum, and no model prose.
- [x] Target outcome rendering consumes only compact scope and justification group results; it introduces no per-field verdict protocol or combinatorial operation summary.
- [x] Ready cards bind only schema version plus unguessable `PreparationId` and prominently show requester, client/environment/role, incident or no incident, exact justification, fixed eight hours, and localized deadline.
- [x] Production registration and delivered workflow semantics remain unchanged: no target registration, feature flag, fallback, dual registration, or request-level router exists before Task 14. Legacy card shape and payload compatibility are explicitly not retained.

**Verification:** Source/dependency tests prove shared components are Web-owned and
preparation-neutral, the target adapter has no delivered intake dependency, and no
legacy card renderer, `preparedRequestId` payload alias, or compatibility parser
remains. Target Teams component/card tests cover locale fallback, ambiguity, stale
context, failures, injection-shaped text, exact `/new`, card replacement, closed
`preparationId` actions, and absence of free-text request creation. Production-
composition tests prove only the delivered semantic graph is registered before Task 14.
Run frontend tests if shared contracts change; then run standing backend verification.

**Dependencies:** Task 10A and the simplification-plan exit gate.

**Files likely touched:**

- existing `GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`,
  `TeamsActorResolver.cs`, `TeamsDraftCardTracker.cs`, and
  `PreparedRequestCardFactory.cs` for extraction and direct replacement of the legacy
  card/payload contract
- new final transport/presentation primitives and thin target semantic adapter files
  under `GovernedAccess.Web/Teams/`
- delivered characterization, target component/card, dependency, and production-
  composition tests

**Estimated scope:** Medium-to-large; extraction must earn its cost by leaving final
components that survive Task 15 unchanged.

**Acceptance coverage:** AC-01-AC-06, AC-23-AC-25, AC-30-AC-33, AC-41-AC-43.

**Required exit gate:**

- one reusable implementation owns Teams transport and the only card-layout mechanics;
- shared components accept only Web-owned transport/presentation models and compile
  without either preparation graph;
- legacy and target semantic adapters remain independently testable and never translate
  between their domain contracts;
- every ready card and confirmation action uses only the final target-compatible
  `{ schemaVersion, preparationId }` payload, with no legacy alias or parser;
- target rendering cannot deliver model prose or use browser/card data as authority;
- production resolves only the delivered Teams graph; and
- deleting the delivered adapter in Task 15 will not require modifying the shared
  transport or presentation components.

**Completion evidence (2026-08-27):** focused Teams, target adapter/card, architecture,
production-composition, and target-orchestration coverage passed (61 tests). The
warnings-as-errors solution build passed with zero warnings, followed by 203 unit tests
and 267 integration tests. The React suite was not run because Task 11 changed neither
the co-hosted React behavior nor its browser contracts. Credentialed live-model
evaluation remains deferred to the later promotion gate by operator direction and is
not Task 11 completion evidence.

### Task 12 - Build target confirmation and request idempotency

- [x] Complete

**Description:** Implement target confirmation as a separate Core service and add the
single shared downstream seam: target-created `AccessRequest` instances require a
unique `PreparationId` while the delivered creation path temporarily still compiles.

**Acceptance criteria:**

- [x] Confirmation trusts authenticated actor and persisted target state only, revalidates every fact, distinguishes source outage from fact drift, and applies exact correction cascades through a predecessor-linked successor.
- [x] Confirmation accepts only an owned immutable Ready preparation before its lazy 30-minute deadline; expiry is not refreshed or bypassed.
- [x] Successful confirmation creates one immutable request and marks the preparation Submitted in one workflow-database commit; unique `Request.PreparationId` provides stable sequential/concurrent replay, with no reference-database write or cross-database transaction.
- [x] Confirmation/revision races converge to either one submitted immutable request or one superseded stale card; every failure creates zero requests and preserves/supersedes state exactly as specified.

**Verification:** Unit and SQLite race tests cover ownership concealment, expiry, all drift/source outcomes, replay, unique-key loser, and confirmation-vs-revision in both orders; downstream workflow regressions stay green; then standing backend and affected frontend verification.

**Dependencies:** Tasks 3, 4, 6, 7, 10, and 11.

**Files likely touched:**

- new target confirmation service and outcomes
- `AccessRequest.cs` and its target mapping in `GovernedAccess.Workflow.Persistence`
- new confirmation/race tests

**Estimated scope:** Large because request creation must be one atomic security boundary.

**Acceptance coverage:** AC-19-AC-22, AC-30-AC-43, AC-48-AC-51.

**Completion evidence (2026-08-27):** focused confirmation, target Teams adapter,
workflow persistence, and request-domain coverage passed (16 unit tests and 22
integration tests). The warnings-as-errors solution build passed with zero warnings,
followed by 219 unit tests and 273 integration tests. The React suite was not run
because Task 12 changed neither co-hosted React behavior nor browser contracts. The
delivered production composition remains unchanged pending Tasks 13-14.

### Task 13 - Prove the complete replacement in an isolated host

- [x] Complete

**Description:** Add a test-only composition containing the complete target modular
monolith: reference authority/database, workflow persistence/database, preparation,
MCP, interpreter, Teams, confirmation, approvals, and provisioning. Production
`Program` and delivered tests remain unchanged.

**Acceptance criteria:**

- [x] Full target journeys cover complete/incremental preparation, unique MCP search to exact-ID proposal without Core search replay, direct `searchQuery`, clarification across restart, revision, confirmation, business/DevOps approval, provisioning, replay, drift correction, independent database/source failures, abuse limits, and zero consequential side effects from free text.
- [x] Architecture tests prove the target project/`DbContext` ownership graph, no target dependency on delivered intake/persistence types, preparation-neutral shared Teams primitives, no cross-database EF relationship/query/transaction, and no runtime composition registering both graphs.
- [x] The ordinary production-host regression exercises only the delivered graph/unified database, while the isolated target host exercises only the replacement graph with distinct reference/workflow database files and independent migration histories.

**Verification:** Run focused target full-host journeys, the full standing backend sequence, frontend suite, target contract checks, and `git diff --check`. No live provider is required at this checkpoint.

**Dependencies:** Tasks 1-12, including Task 10A.

**Files likely touched:**

- test-only target host registration/factory
- target full-host integration tests
- architecture/source tests

**Estimated scope:** Medium.

**Acceptance coverage:** Deterministic pre-cutover evidence for AC-01-AC-52.

**Completion evidence (2026-08-27):** all seven isolated target full-host composition
and journey tests passed, followed by 37 focused target MCP, interpreter-boundary,
architecture, and production-composition checks. The warnings-as-errors solution build
passed with zero warnings, followed by 219 unit tests, 280 integration tests, and all
6 React tests. `git diff --check` passed. No live provider was used, and the delivered
production composition remains unchanged pending human Checkpoint C approval and Task
14.

### Checkpoint C - Human cutover approval

- [ ] Delivered production host passes all regressions.
- [ ] Isolated target host passes all deterministic journeys and security gates.
- [ ] Isolated target reference/workflow database ownership, migration, restart, outage, and downstream workflow journeys pass.
- [ ] Target source has no dependency on the delivered preparation implementation.
- [ ] Shared Teams transport/presentation components depend on neither preparation graph and are retained unchanged by the Task 15 deletion plan.
- [ ] The transition seam inventory still contains exactly the three declared seams.
- [ ] Maintainer approves the production cutover after reviewing the isolated target evidence.

Do not start Task 14 without this approval. Failure here is fixed in the owning target
task; it is not hidden behind a fallback or compatibility adapter.

## Phase D - Replace once, then delete

### Task 14 - Atomically switch production composition to the target

- [ ] Planned; blocked on Checkpoint C

**Description:** Change the production composition root and endpoints once so all new
preparation traffic uses the already-proven target graph. Do not delete delivered code
inside this task; that makes the wiring change independently reviewable.

**Acceptance criteria:**

- [ ] `Program`, Teams, AI, MCP, reference-authority, workflow-persistence, and confirmation registrations resolve only the target graph and its two databases; there is no runtime flag, fallback, dual registration, dual write, or request-level routing.
- [ ] Production MCP exposes exactly the target four tools, satisfying the already-documented phase transition in `AGENTS.md`.
- [ ] After explicit reset and fresh independent migration of both target databases, production full-host journeys match the isolated target host and downstream approval/provisioning behavior remains unchanged.

**Verification:** Source/DI checks prove one active graph; run focused production full-host journeys, standing backend verification, frontend suite, configuration validation, and `git diff --check`.

**Dependencies:** Task 13 and explicit human approval at Checkpoint C.

**Files likely touched:**

- `src/GovernedAccess.Web/Program.cs`
- Teams/AI/MCP/module production registration and configuration files
- production composition tests

**Estimated scope:** Large and intentionally atomic; splitting it would create a hybrid runtime.

**Acceptance coverage:** Runtime cutover evidence for AC-01-AC-44 and AC-48-AC-52.

### Task 15 - Delete the delivered intake and finalize the fresh schema

- [ ] Planned

**Description:** Immediately remove the now-unreachable delivered preparation graph,
Web-owned unified persistence graph, all coexistence seams, and delivered-only tests.

**Acceptance criteria:**

- [ ] Delete delivered full-candidate contracts, `RequestIntakeSession`, draft service/validator, old interpreter/store/MCP, delivered-only Teams semantic adapter/card assembly/action parsing/confirmation code, Web persistence duplicates, unified context/migrations/seeder, and tests whose only purpose is delivered behavior; retain the preparation-neutral Teams transport and pure presentation primitives extracted in Task 11 unchanged.
- [ ] Remove the legacy `AccessRequest` creation path; make `AccessRequest.PreparationId` required and uniquely indexed in the final workflow migration, and prove the final reference/workflow schemas contain only their owned tables.
- [ ] Source checks find no delivered proposal/lifecycle/version/choice/reserved-ID concepts, no compatibility aliases/adapters, no unified context/schema, no Web-owned EF adapter, and no transitional registration.

**Verification:** Compile/source checks first; fresh migration/startup/seeding tests; standing backend verification and frontend suite. An old or transitional database must fail with bounded reset guidance and must never be deleted automatically.

**Dependencies:** Task 14.

**Files likely touched:**

- delivered Core, Web, and MCP files listed by source inventory
- delivered `GovernedAccessDbContext`, Web persistence files, migrations, and seeder
- obsolete unit/integration tests

**Estimated scope:** Large but primarily deletion and final schema contraction.

**Acceptance coverage:** Structural closure for AC-03, AC-07, AC-09, AC-15, AC-23, AC-24, AC-29-AC-31, AC-34, AC-36, AC-38, AC-43, AC-48-AC-52.

### Checkpoint D - One implementation remains

- [ ] Only the target preparation graph exists in production source and tests.
- [ ] Fresh empty reference and workflow databases create only their independently owned final schemas and migration histories.
- [ ] All backend and frontend regressions pass without legacy fixtures.
- [ ] No compatibility or rollback path exists inside the application; rollback is source/version redeployment plus disposable database reset.

## Phase E - Final evidence and documentation

### Task 16 - Promote deterministic and live evaluation evidence

- [ ] Planned

**Description:** Replace baseline-shaped evaluation with the approved fixed suite
and retain evidence only from the final post-deletion implementation.

**Acceptance criteria:**

- [ ] Deterministic tests construct structured proposals rather than language corpora and prove all negative paths create zero requests, decisions, operations, or grants.
- [ ] The fixed 12-group promoted live suite enforces every absolute safety, justification-fidelity, ambiguity, and bounded-execution gate, uniquely resolved exact-ID behavior without Core search replay, and at least 11/12 outcome classes without selective reruns or waivers.
- [ ] Evaluation artifacts retain commit, dataset/hash, provider/model and contract versions, normalized outcomes, latency, and side-effect counts but no raw messages, prompts, proposals, reasoning, or MCP payloads.

**Verification:** Architecture/source checks; standing backend sequence; frontend suite; one complete credentialed promoted live run; artifact/schema/link validation; `git diff --check`.

**Dependencies:** Task 15.

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/`
- `tests/GovernedAccess.IntegrationTests/Evaluation/`
- `docs/evaluation/`

**Estimated scope:** Large because evidence promotion is an indivisible gate.

**Acceptance coverage:** AC-01-AC-52, primarily AC-45-AC-52.

### Task 17 - Reconcile current documentation with verified runtime

- [ ] Planned

**Description:** Promote only Task 16-observed behavior into current-state documents
and remove obsolete contradictions. This task changes no implementation behavior.

**Acceptance criteria:**

- [ ] Product, architecture, security, orchestration, testing, MCP, local-development, operator, roadmap, and ADR-index documentation consistently describe the final sparse-proposal/four-tool, one-host, two-database modular runtime.
- [ ] Destructive ambiguity, optional predecessor, complete-candidate, process-local choice, `Invalidated`, reserved-request-ID, and two-tool current-state claims are removed or historically clarified without rewriting decision history.
- [ ] Documentation states the separate reference/workflow ownership and extraction seam, explicit fresh-two-database/reset policy, and contains no parallel-build, unified-schema, compatibility, or upgrade-path claims as supported runtime behavior.

**Verification:** Validate links, JSON contracts, commands, terminology, and evaluation references; run `git diff --check`; run code suites only if executable examples/contracts change or validation exposes a mismatch.

**Dependencies:** Task 16.

**Files likely touched:**

- `spec.md`, `AGENTS.md`, and current product/architecture/security/testing documents
- current MCP contract and operator guidance
- relevant dated ADR clarifications

**Estimated scope:** Large documentation-only reconciliation.

**Acceptance coverage:** Final traceability closure for AC-01-AC-52.

## Mandatory self-check for every implementation task

Reject an increment if it introduces or implies any of the following:

- target code reading, mutating, inheriting from, or adapting the delivered preparation aggregate or proposal model;
- shared Teams code accepting either preparation graph, performing semantic outcome mapping, parsing both flows through one compatibility contract, selecting an intake graph, or requiring modification when delivered-only code is deleted;
- a compatibility attribute, legacy alias on a target type, shared mutable backing state, dual write, runtime fallback, or synchronization between delivered and target preparations;
- production registration of the target before Task 14 or retention of delivered registration after Task 14;
- deletion of delivered code before target full-host proof and human cutover approval, or retention after Task 15;
- database adoption, backfill, automatic deletion, row copying, synchronization, shared files, or dual writes between delivered and target storage;
- reference entities/tables in the target workflow database, workflow entities/tables in the target reference database, cross-database EF relationships/queries/transactions, direct reference-database access outside `ReferenceAuthority`, or either `DbContext` injected into Core/MCP/Web adapters/controllers;
- loopback HTTP, a second deployable host, or speculative remote-service contracts in place of the required in-process authority ports;
- deterministic requester-language interpretation outside exact `/new`;
- requester text entering Core reduction, model-owned canonical snapshots, client/duration/identity mutation, model prose rendering, state-changing tools, or text-created requests;
- duplicated environment-search implementations, Core search replay for `exactEnvironmentId`, MCP output treated as authority, roles embedded in exact environment lookup, or ambiguous results ranked/truncated into a choice;
- destructive ambiguity, a clarification-specific mutation path, accepting a proposed ID
  without ordinary exact reload, stale candidate/context snapshot commit, optional
  revision predecessor, Ready with active context, or mutable Ready scope;
- a database transaction held across model/MCP work, stale proposal replay, read-before-write uniqueness without the durable partial index, or one overloaded version counter;
- confirmation without unique `Request.PreparationId`, undefined fact-drift/source-outage behavior, or browser/card payload authority beyond authenticated context plus exact preparation identity;
- raw message/query/transcript/prompt/reasoning/proposal/tool-payload logging or persistence, trusted stored justification, selective live-evaluation reruns, or premature documentation promotion.

## Dependency summary

```text
Task 1 contracts -> Task 2 aggregate -> Task 3 authority/search
Task 3 -> Task 4 isolated reference authority/database
Tasks 1-3 -> Task 5 reducer -> Task 6 preparation/workflow persistence
Tasks 4,6 -> Task 7 downstream workflow persistence
Tasks 3,4 -> Task 8 MCP -> Task 9 agent
Tasks 5,6,9 -> Task 10 orchestration -> Task 10A clarification simplification -> Task 11 Teams
Tasks 3,4,6,7,10A,11 -> Task 12 confirmation -> Task 13 isolated full host
Task 13 + human approval -> Task 14 atomic cutover -> Task 15 legacy deletion
Task 15 -> Task 16 final evidence -> Task 17 documentation
```

## Acceptance traceability summary

| Acceptance area | Primary implementation tasks | Final evidence |
|---|---|---|
| AC-01-AC-06 language/response boundary | Tasks 9-11, including 10A | Tasks 13, 14, and 16 |
| AC-07-AC-14 proposal/reduction | Tasks 1, 5, and 10A | Tasks 10, 13, and 16 |
| AC-15-AC-22 enterprise authority/MCP | Tasks 3, 4, and 8 | Tasks 12-14 and 16 |
| AC-23-AC-28 clarification | Tasks 2, 5, 10A, and 11 | Tasks 10, 13, and 16 |
| AC-29-AC-40 lifecycle/persistence/confirmation | Tasks 2, 6, 7, 10, 10A, and 12 | Tasks 13-16 |
| AC-41-AC-44 security/budgets | Tasks 8-12, including 10A | Tasks 13, 14, and 16 |
| AC-45-AC-47 evaluation | Task 16 | Task 16 retained evidence |
| AC-48-AC-52 modular persistence/extraction | Tasks 4, 6, and 7 | Tasks 13-17 |

The critical human checkpoint is between Tasks 13 and 14. Before it, the delivered
implementation is the sole production authority. After it, the target implementation
is the sole production authority. Task 15 then removes the dead delivered code rather
than maintaining it as compatibility.

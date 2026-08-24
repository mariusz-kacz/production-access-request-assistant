# Implementation Plan: Deterministic Request Intake

- **Status:** Fresh replacement plan; implementation has not started
- **Replanned:** 2026-08-24 after Tasks 1 and 2 were reverted
- **Target branch:** `feature/decouple-teams-approval-flow`
- **Primary authority:** `SPEC-deterministic-request-intake.md`
- **Task-list target:** This file is both the plan and the ordered task checklist
- **Planned slices:** 15, followed in order unless a task explicitly says otherwise

## Outcome

Build the deterministic request-intake replacement as an independent, complete path
while the delivered path remains unchanged and authoritative. After the replacement
passes its isolated full-host checkpoint, switch production composition once, then
delete the delivered implementation and its schema in the immediately following task.

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

Target code must never adapt a delivered full-candidate snapshot into a sparse proposal,
share mutable aggregate backing state with `RequestIntakeSession`, or expose legacy
aliases such as `Id`/`PreparationId` or `PersistenceVersion`/`ConcurrencyVersion` on one
type. There is no `TaskNineCompatibilityAttribute` or equivalent task-number marker.

### Ownership during parallel construction

| Area | Authority before cutover | Target construction rule |
|---|---|---|
| Production Teams intake | Delivered `TeamsAccessRequestAgent` graph | Target agent is registered only by the isolated test host until Task 12. |
| Canonical preparation | Delivered `RequestIntakeSession` | New `RequestPreparation` owns only target state; it never reads or writes a delivered session. |
| Proposal interpretation | Delivered full-candidate interpreter | New interpreter emits only `TurnProposal`; no translation exists between proposal models. |
| Persistence | Delivered session tables | Target tables use distinct names and keys in the same local database model; no row is shared or copied. |
| MCP | Delivered two-tool production registration | New four-tool catalog is invoked only by the target test composition until Task 12. |
| Confirmation | Delivered submission service | New confirmation service accepts only `PreparationId` and target evidence. |
| Downstream approvals/provisioning | Existing deterministic workflow | Reused unchanged after a target confirmation creates the same final `AccessRequest` domain type. |

### Deliberate temporary seams

Only these coexistence points are allowed. They are not aggregate compatibility APIs.

| Seam | Introduced | Removed/finalized | Constraint |
|---|---:|---:|---|
| `GovernedAccessDbContext` maps separate delivered and target preparation tables | Task 5 | Task 13 | No shared row, key, concurrency token, or navigation. Existing local data is disposable. |
| `AccessRequest` supports target creation with required `PreparationId` while delivered creation still compiles | Task 10 | Task 13 | The target factory/constructor requires a nonempty ID. Only the delivered path may temporarily create a request without it. |
| Target service registrations exist in a test-only composition | Task 11 | Task 12 | Production `Program` resolves only the delivered graph before cutover and only the target graph after cutover. |

Any additional bridge, alias, dual write, fallback, or synchronization mechanism is a
material plan change and requires human review.

### Database transition

Existing local SQLite data is disposable. Schema work supports fresh databases only:

- no adoption, backfill, row copy, or upgrade from the delivered schema;
- no startup-time automatic deletion;
- explicit operator deletion before the cutover build;
- isolated persistence tests always create fresh databases;
- Task 13 removes delivered tables/mappings and creates the final fresh-only migration
  model.

## Authority and resolved conflicts

Apply the constitution and repository rules first, then the approved feature
specification, ADRs 0005 and 0007-0009, target MCP contract and test matrix, and current
as-built documents outside the changed boundary.

| Conflict | Resolution |
|---|---|
| ADR 0009 says predecessor is stored "when useful"; the specification requires it for every revision successor. | Every revision-created preparation has a mandatory predecessor. Only roots have none. |
| Target matrix and roadmap contain destructive ambiguity examples. | The specification governs: clarification is non-destructive and preserves canonical state. Correct those artifacts only after verified implementation. |
| Current as-built documents describe the delivered two-tool catalog while the approved target requires four. | `AGENTS.md` now records both phase-bound rules: production remains on two tools during Tasks 1-11, the target four-tool catalog is isolated, and Task 12 atomically replaces two with four. Full current-state documentation follows verified final evidence in Task 15. |
| Current behavior uses complete candidates, process-local choices, `Invalidated`, and reserved request IDs. | Those remain facts about the delivered path only. Target code cannot depend on them, and Task 13 deletes them after cutover. |
| ADR 0005 describes reserved-request tombstone evidence. | Preserve the tombstone principle, but final target evidence uses immutable preparation identity and unique `Request.PreparationId`; clarify the ADR after implementation. |
| The target incident MCP projection is singular while Core authority must handle zero/one/many eligible links. | Keep the closed model projection; define a richer Core authority projection and never treat MCP output as authority. |

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

- [ ] In progress

**Description:** Add provider-neutral target contracts in a new preparation namespace.
Do not edit or implement the delivered proposal types.

**Acceptance criteria:**

- [ ] `TurnProposal` has the exact closed dialogue acts, sparse operations, clarification selection, discussion topics, structural failures, per-operation results, and application outcomes from the specification.
- [ ] Mutable proposal fields are only environment, role, justification, and optional incident; requester text, client, identity, duration, lifecycle, request identity, approval, and provisioning data are absent.
- [ ] Target contracts contain no provider, MAF, MCP, Teams, EF, raw JSON, model prose, or delivered proposal types.

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

- [ ] Planned

**Description:** Define Core authority ports and the one deterministic search policy.
Add explicit searchable and eligibility facts without using MCP projections as Core
authority.

**Acceptance criteria:**

- [ ] Exact environment, environment-role, and incident authority projections preserve source boundaries; incident authority supports zero/one/many eligible environment links.
- [ ] One ordinal, locale-invariant search implementation enforces exact normalization, searchable fields, eligibility, zero/unique/2-5/6-20/>20 outcomes, and no ranking or truncation.
- [ ] Client is derived only from exact eligible environment data; role and incident relationships are independently exact-revalidated.

**Verification:** Search matrix and authority-contract unit tests cover every cardinality, Unicode/ordinal edge, eligibility gate, and failure outcome; then standing backend verification.

**Dependencies:** Task 2.

**Files likely touched:**

- new preparation authority/search files in `GovernedAccess.Core`
- additive reference-data facts where required
- new focused unit tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-15-AC-22.

### Task 4 - Implement the sparse authoritative reducer

- [ ] Planned

**Description:** Build the pure target reducer against `RequestPreparation`, target
contracts, and authority ports. It receives no requester text.

**Acceptance criteria:**

- [ ] Structural violations reject the whole turn; data failures use the exact environment -> incident -> coherent scope -> role -> justification -> clarification order and partial-success rules.
- [ ] Omission is non-destructive, dependency cascades are exact, ambiguity preserves current canonical scope, and at most one environment-before-role clarification is produced.
- [ ] Clarification selection validates preparation/version/target/index, maps only through stored choices, exact-reloads the selected entity, and executes the normal full reduction pipeline.

**Verification:** Table-driven reducer tests cover every normative row and worked example, including mixed operations, source failures, incident cardinality, stale selection, provenance, and zero-mutation structural failures; then standing backend verification.

**Dependencies:** Tasks 1-3.

**Files likely touched:**

- new reducer/application files under `GovernedAccess.Core/Preparations/`
- new reducer matrix tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-08-AC-14, AC-17-AC-28.

### Checkpoint B - Pure target engine

- [ ] Target reducer passes the complete deterministic matrix without language fixtures.
- [ ] Core target APIs accept no requester message, provider type, MCP payload, or delivered draft type.
- [ ] Delivered host remains the only production authority.

## Phase C - Inactive target infrastructure

### Task 5 - Persist target preparations in separate tables

- [ ] Planned

**Description:** Add final target EF mappings and a target store while retaining the
delivered mappings untouched. Both table sets may exist temporarily, but no entity or
row is shared.

**Acceptance criteria:**

- [ ] Target persistence round-trips canonical state, one clarification context, predecessor, both versions, lifecycle timestamps, turn/rate metadata, and bounded attribution without raw conversational content.
- [ ] OCC uses `ConcurrencyVersion`; the durable active unique index binds channel, tenant, actor, conversation, and requester for exactly Collecting/Ready.
- [ ] Store outcomes distinguish conflicts, active-creation races, unavailable persistence, and malformed state; winner reload is explicit and no model call occurs inside a transaction.

**Verification:** Fresh-SQLite mapping, restart, unique-index, OCC, and privacy tests; legacy persistence regressions; then standing backend verification. Explicitly reset any local database used for manual validation.

**Dependencies:** Tasks 2 and 4.

**Files likely touched:**

- new `EfRequestPreparationStore` and mapping files
- `GovernedAccessDbContext.cs`
- fresh migration/model files
- focused persistence integration tests

**Estimated scope:** Large because persistence invariants are reviewed together.

**Acceptance coverage:** AC-12, AC-29, AC-34-AC-36, AC-43.

### Task 6 - Build the inactive four-tool MCP catalog

- [ ] Planned

**Description:** Implement the target four-tool wire surface in separate handlers and
registration extensions. Do not call the target registration from production
composition.

**Acceptance criteria:**

- [ ] Exactly the four approved typed read-only tools and closed schemas match the target contract; no mutation or role embedding in exact environment lookup exists.
- [ ] MCP search and Core use the same search-policy implementation; tool results remain interpretive context and are encoded/bounded.
- [ ] The delivered production endpoint still exposes exactly its current two tools before cutover.

**Verification:** Target contract/transport/failure tests use an explicit target MCP test host; production-registration regression proves two tools remain active; then standing backend verification.

**Dependencies:** Task 3.

**Files likely touched:**

- new target files in `GovernedAccess.Mcp`
- target MCP contract tests and test-host wiring

**Estimated scope:** Medium.

**Acceptance coverage:** AC-15-AC-18, AC-22, AC-41, AC-43.

### Task 7 - Build the inactive bounded agent interpreter

- [ ] Planned

**Description:** Add the target MAF interpreter, structured schema translation, repair
policy, prompt envelope, and execution budgets without changing production AI
registration.

**Acceptance criteria:**

- [ ] Every non-`/new` target free-text turn reaches the agent and yields only the closed target proposal; malformed/unknown output gets at most one repair and then a typed failure.
- [ ] Stored justification and MCP display fields are explicitly delimited as untrusted; no raw prompt, response, reasoning, query, transcript, or full MCP payload is logged or persisted.
- [ ] Startup and execution enforce 4,000 characters, 50 turns, rolling 20/10-minute rate, one call/tool and four total, provider iteration bounds, and one cumulative 30-second budget.

**Verification:** Deterministic chat-client tests cover schemas, repair, cancellation, budgets, prompt-injection boundaries, and safe telemetry; production AI registration remains unchanged; then standing backend verification.

**Dependencies:** Tasks 1 and 6.

**Files likely touched:**

- new target files under `GovernedAccess.Web/Ai/`
- new target AI integration tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-01-AC-07, AC-41, AC-43, AC-44.

### Task 8 - Build target turn orchestration, lifecycle, restart, and races

- [ ] Planned

**Description:** Compose target load -> agent -> reduce -> short OCC commit behavior in
a new application service. It remains reachable only from direct/component tests.

**Acceptance criteria:**

- [ ] Agent/MCP latency occurs outside database transactions; accepted changes and clarification commit atomically; stale proposals are rejected without replay.
- [ ] The closed reset protocol event, first-turn creation, one-active uniqueness, permanent turn exhaustion, collecting staleness, lazy Ready expiry, and typed failures match the specification; this service never receives or compares requester text.
- [ ] Ready revisions preserve A for no-op/failure and atomically supersede A/create mandatory-predecessor B for the first accepted material change or clarification.

**Verification:** Unit/component tests cover normative lifecycle examples, restart, active-creation races, OCC collisions, deadline behavior, and no side effects on failures; then standing backend verification.

**Dependencies:** Tasks 4, 5, and 7.

**Files likely touched:**

- new target application service and ports in `GovernedAccess.Core`
- new target orchestration adapter in `GovernedAccess.Web`
- focused unit/integration tests

**Estimated scope:** Large because one commit protocol owns the concurrency boundary.

**Acceptance coverage:** AC-01-AC-14, AC-23-AC-36, AC-44.

### Task 9 - Build target Teams rendering and card behavior

- [ ] Planned

**Description:** Add a separate target Teams adapter, response renderer, and Ready card
factory. Do not register them in production.

**Acceptance criteria:**

- [ ] Only the Teams boundary recognizes exact trimmed case-insensitive `/new`; every other authenticated nonblank message goes through target orchestration.
- [ ] All prose and selectable choices are application-rendered with authenticated locale, safe encoding, exact canonical facts, five-choice maximum, and no model prose.
- [ ] Ready cards bind only schema version plus unguessable `PreparationId` and prominently show requester, client/environment/role, incident or no incident, exact justification, fixed eight hours, and localized deadline.

**Verification:** Target Teams component/card tests cover locale fallback, ambiguity, stale context, failures, injection-shaped text, exact `/new`, and absence of free-text request creation; run frontend tests if shared contracts change; then standing backend verification.

**Dependencies:** Task 8.

**Files likely touched:**

- new target files under `GovernedAccess.Web/Teams/`
- new target Teams component tests

**Estimated scope:** Medium.

**Acceptance coverage:** AC-01-AC-06, AC-23-AC-25, AC-30-AC-33, AC-41-AC-43.

### Task 10 - Build target confirmation and request idempotency

- [ ] Planned

**Description:** Implement target confirmation as a separate Core service and add the
single shared downstream seam: target-created `AccessRequest` instances require a
unique `PreparationId` while the delivered creation path temporarily still compiles.

**Acceptance criteria:**

- [ ] Confirmation trusts authenticated actor and persisted target state only, revalidates every fact, distinguishes source outage from fact drift, and applies exact correction cascades through a predecessor-linked successor.
- [ ] Successful confirmation creates one immutable request and marks the preparation Submitted in one local commit; unique `Request.PreparationId` provides stable sequential/concurrent replay.
- [ ] Confirmation/revision races converge to either one submitted immutable request or one superseded stale card; every failure creates zero requests and preserves/supersedes state exactly as specified.

**Verification:** Unit and SQLite race tests cover ownership concealment, expiry, all drift/source outcomes, replay, unique-key loser, and confirmation-vs-revision in both orders; downstream workflow regressions stay green; then standing backend and affected frontend verification.

**Dependencies:** Tasks 3, 5, 8, and 9.

**Files likely touched:**

- new target confirmation service and outcomes
- `AccessRequest.cs` and its EF mapping
- new confirmation/race tests

**Estimated scope:** Large because request creation must be one atomic security boundary.

**Acceptance coverage:** AC-19-AC-22, AC-30-AC-43.

### Task 11 - Prove the complete replacement in an isolated host

- [ ] Planned

**Description:** Add a test-only composition that replaces the delivered preparation
registrations with every target component. Production `Program` remains unchanged.

**Acceptance criteria:**

- [ ] Full target journeys cover complete/incremental preparation, clarification across restart, revision, confirmation, replay, drift correction, source failure, abuse limits, and zero consequential side effects from free text.
- [ ] Architecture tests prove target production code has no dependency on delivered draft/session/interpreter/store/Teams types and that no runtime composition registers both graphs.
- [ ] The ordinary production-host regression still exercises only the delivered path, while the isolated target host exercises only the replacement path.

**Verification:** Run focused target full-host journeys, the full standing backend sequence, frontend suite, target contract checks, and `git diff --check`. No live provider is required at this checkpoint.

**Dependencies:** Tasks 1-10.

**Files likely touched:**

- test-only target host registration/factory
- target full-host integration tests
- architecture/source tests

**Estimated scope:** Medium.

**Acceptance coverage:** Deterministic pre-cutover evidence for AC-01-AC-47.

### Checkpoint C - Human cutover approval

- [ ] Delivered production host passes all regressions.
- [ ] Isolated target host passes all deterministic journeys and security gates.
- [ ] Target source has no dependency on the delivered preparation implementation.
- [ ] The transition seam inventory still contains exactly the three declared seams.
- [ ] Maintainer approves the production cutover after reviewing the isolated target evidence.

Do not start Task 12 without this approval. Failure here is fixed in the owning target
task; it is not hidden behind a fallback or compatibility adapter.

## Phase D - Replace once, then delete

### Task 12 - Atomically switch production composition to the target

- [ ] Planned; blocked on Checkpoint C

**Description:** Change the production composition root and endpoints once so all new
preparation traffic uses the already-proven target graph. Do not delete delivered code
inside this task; that makes the wiring change independently reviewable.

**Acceptance criteria:**

- [ ] `Program`, Teams, AI, MCP, persistence, and confirmation registrations resolve only the target graph; there is no runtime flag, fallback, dual registration, dual write, or request-level routing.
- [ ] Production MCP exposes exactly the target four tools, satisfying the already-documented phase transition in `AGENTS.md`.
- [ ] After an explicit local database reset, production full-host journeys match the isolated target host and downstream approval/provisioning behavior remains unchanged.

**Verification:** Source/DI checks prove one active graph; run focused production full-host journeys, standing backend verification, frontend suite, configuration validation, and `git diff --check`.

**Dependencies:** Task 11 and explicit human approval at Checkpoint C.

**Files likely touched:**

- `src/GovernedAccess.Web/Program.cs`
- Teams/AI/MCP production registration files
- production composition tests

**Estimated scope:** Large and intentionally atomic; splitting it would create a hybrid runtime.

**Acceptance coverage:** Runtime cutover evidence for AC-01-AC-44 and AC-47.

### Task 13 - Delete the delivered intake and finalize the fresh schema

- [ ] Planned

**Description:** Immediately remove the now-unreachable delivered preparation graph,
the two remaining coexistence seams, and all delivered-only schema/tests.

**Acceptance criteria:**

- [ ] Delete delivered full-candidate contracts, `RequestIntakeSession`, draft service/validator, old interpreter/store/MCP/Teams/card/confirmation code, and tests whose only purpose is delivered behavior.
- [ ] Remove the legacy `AccessRequest` creation path and delivered preparation mappings/tables; make `AccessRequest.PreparationId` required and uniquely indexed in the final fresh-only migration model.
- [ ] Source checks find no delivered proposal/lifecycle/version/choice/reserved-ID concepts, no compatibility aliases/attributes/adapters, and no transitional registration or schema.

**Verification:** Compile/source checks first; fresh migration/startup/seeding tests; standing backend verification and frontend suite. An old or transitional database must fail with bounded reset guidance and must never be deleted automatically.

**Dependencies:** Task 12.

**Files likely touched:**

- delivered Core, Web, and MCP files listed by source inventory
- `GovernedAccessDbContext.cs` and migrations
- obsolete unit/integration tests

**Estimated scope:** Large but primarily deletion and final schema contraction.

**Acceptance coverage:** Structural closure for AC-03, AC-07, AC-09, AC-15, AC-23, AC-24, AC-29-AC-31, AC-34, AC-36, AC-38, AC-43.

### Checkpoint D - One implementation remains

- [ ] Only the target preparation graph exists in production source and tests.
- [ ] A new empty database creates only the final schema.
- [ ] All backend and frontend regressions pass without legacy fixtures.
- [ ] No compatibility or rollback path exists inside the application; rollback is source/version redeployment plus disposable database reset.

## Phase E - Final evidence and documentation

### Task 14 - Promote deterministic and live evaluation evidence

- [ ] Planned

**Description:** Replace baseline-shaped evaluation with the approved fixed target suite
and retain evidence only from the final post-deletion implementation.

**Acceptance criteria:**

- [ ] Deterministic tests construct structured proposals rather than language corpora and prove all negative paths create zero requests, decisions, operations, or grants.
- [ ] The fixed 12-group promoted live suite enforces every absolute safety/provenance/ambiguity/budget gate and at least 11/12 outcome classes without selective reruns or waivers.
- [ ] Evaluation artifacts retain commit, dataset/hash, provider/model and contract versions, normalized outcomes, latency, and side-effect counts but no raw messages, prompts, proposals, reasoning, or MCP payloads.

**Verification:** Architecture/source checks; standing backend sequence; frontend suite; one complete credentialed promoted live run; artifact/schema/link validation; `git diff --check`.

**Dependencies:** Task 13.

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/`
- `tests/GovernedAccess.IntegrationTests/Evaluation/`
- `docs/evaluation/`

**Estimated scope:** Large because evidence promotion is an indivisible gate.

**Acceptance coverage:** AC-01-AC-47, primarily AC-45-AC-47.

### Task 15 - Reconcile current documentation with verified runtime

- [ ] Planned

**Description:** Promote only Task 14-observed behavior into current-state documents
and remove obsolete contradictions. This task changes no implementation behavior.

**Acceptance criteria:**

- [ ] Product, architecture, security, orchestration, testing, MCP, local-development, operator, roadmap, and ADR-index documentation consistently describe the final sparse-proposal/four-tool runtime.
- [ ] Destructive ambiguity, optional predecessor, complete-candidate, process-local choice, `Invalidated`, reserved-request-ID, and two-tool current-state claims are removed or historically clarified without rewriting decision history.
- [ ] Documentation states the explicit fresh-database/reset policy and contains no parallel-build, transitional-schema, compatibility, or upgrade-path claims as supported runtime behavior.

**Verification:** Validate links, JSON contracts, commands, terminology, and evaluation references; run `git diff --check`; run code suites only if executable examples/contracts change or validation exposes a mismatch.

**Dependencies:** Task 14.

**Files likely touched:**

- `spec.md`, `AGENTS.md`, and current product/architecture/security/testing documents
- current MCP contract and operator guidance
- relevant dated ADR clarifications

**Estimated scope:** Large documentation-only reconciliation.

**Acceptance coverage:** Final traceability closure for AC-01-AC-47.

## Mandatory self-check for every implementation task

Reject an increment if it introduces or implies any of the following:

- target code reading, mutating, inheriting from, or adapting the delivered preparation aggregate or proposal model;
- a compatibility attribute, legacy alias on a target type, shared mutable backing state, dual write, runtime fallback, or synchronization between delivered and target preparations;
- production registration of the target before Task 12 or retention of delivered registration after Task 12;
- deletion of delivered code before target full-host proof and human cutover approval, or retention after Task 13;
- database adoption, backfill, automatic deletion, or row copying between delivered and target tables;
- deterministic requester-language interpretation outside exact `/new`;
- requester text entering Core reduction, model-owned canonical snapshots, client/duration/identity mutation, model prose rendering, state-changing tools, or text-created requests;
- duplicated environment search, MCP output treated as authority, roles embedded in exact environment lookup, or ambiguous results ranked/truncated into a choice;
- destructive ambiguity, selection outside matching persisted context/version, optional revision predecessor, Ready with active context, or mutable Ready scope;
- a database transaction held across model/MCP work, stale proposal replay, read-before-write uniqueness without the durable partial index, or one overloaded version counter;
- confirmation without unique `Request.PreparationId`, undefined fact-drift/source-outage behavior, or browser/card payload authority beyond authenticated context plus exact preparation identity;
- raw message/query/transcript/prompt/reasoning/proposal/tool-payload logging or persistence, trusted stored justification, selective live-evaluation reruns, or premature documentation promotion.

## Dependency summary

```text
Task 1 contracts -> Task 2 aggregate -> Task 3 authority/search -> Task 4 reducer
Tasks 2,4 -> Task 5 persistence
Task 3 -> Task 6 MCP -> Task 7 agent
Tasks 4,5,7 -> Task 8 orchestration -> Task 9 Teams
Tasks 3,5,8,9 -> Task 10 confirmation -> Task 11 isolated full host
Task 11 + human approval -> Task 12 atomic cutover -> Task 13 legacy deletion
Task 13 -> Task 14 final evidence -> Task 15 documentation
```

## Acceptance traceability summary

| Acceptance area | Primary implementation tasks | Final evidence |
|---|---|---|
| AC-01-AC-06 language/response boundary | Tasks 7-9 | Tasks 11, 12, and 14 |
| AC-07-AC-14 proposal/reduction | Tasks 1 and 4 | Tasks 8, 11, and 14 |
| AC-15-AC-22 enterprise authority/MCP | Tasks 3 and 6 | Tasks 10-12 and 14 |
| AC-23-AC-28 clarification | Tasks 2, 4, and 9 | Tasks 8, 11, and 14 |
| AC-29-AC-40 lifecycle/persistence/confirmation | Tasks 2, 5, 8, and 10 | Tasks 11-14 |
| AC-41-AC-44 security/budgets | Tasks 6-10 | Tasks 11, 12, and 14 |
| AC-45-AC-47 evaluation | Task 14 | Task 14 retained evidence |

The critical human checkpoint is between Tasks 11 and 12. Before it, the delivered
implementation is the sole production authority. After it, the target implementation
is the sole production authority. Task 13 then removes the dead delivered code rather
than maintaining it as compatibility.

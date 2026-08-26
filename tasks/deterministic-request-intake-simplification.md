# Deterministic Request Intake Simplification Plan

- **Status:** Implementation in progress; Task 1 complete
- **Prepared:** 2026-08-26
- **Branch inspected:** `feature/decouple-teams-approval-flow`
- **Starting commit:** `429d8a0`
- **Starting working tree:** Clean; documentation changes listed in this plan were made afterward
- **Target authority:** [`SPEC-deterministic-request-intake.md`](../SPEC-deterministic-request-intake.md)
- **Historical delivery plan:** [`tasks/deterministic-request-intake.md`](deterministic-request-intake.md)
- **Test authority:** [`docs/evaluation/deterministic-request-intake-test-matrix.md`](../docs/evaluation/deterministic-request-intake-test-matrix.md)

## 1. Objective and delivery boundary

Simplify the already-implemented isolated deterministic-intake target before its Teams
renderer, confirmation path, full-host composition, or production cutover is built. The
pass removes target-only mechanisms whose domain, persistence, test, prompt, and
documentation costs exceed their safety or portfolio value. It does not redesign the
product and does not weaken any authorization or consequential-action boundary.

The production composition still uses the delivered two-tool/full-candidate path. The
four-tool target remains isolated until its atomic cutover. This plan does not authorize
dual production catalogs, compatibility adapters, application-visible model prose,
free-text submission, or any downstream approval/provisioning change.

Task 10A in the historical plan already replaced the clarification-selection protocol
in code. Its unfinished deterministic/live evidence work is incorporated into Task 4
below; its obsolete protocol-specific test inventory is superseded. The historical file
is not rewritten. Historical Tasks 11–17 remain downstream delivery work and resume
only after this plan's exit gate.

## 2. Current-system map

### Runtime and composition

- Production registration still resolves `MafRequestPreparationInterpreter`,
  `RequestPreparationMcpEndpoint`, the two delivered MCP tools, and the delivered Teams
  rendering/confirmation flow in `GovernedAccess.Web`.
- The isolated target uses `MafTurnProposalInterpreter`,
  `TargetRequestPreparationOrchestrator`, `TargetAgentMcpCatalog`,
  `TargetAgentMcpEndpoint`, and `TargetMcpRegistration`. It exposes exactly the four
  target read-only tools only inside the target test composition.
- No target Teams progress/clarification renderer or target confirmation handler exists
  yet. `PreparedRequestCardFactory` and `TeamsAccessRequestAgent` describe the delivered
  path, so simplification must not modify them as though they were target consumers.

### Contracts, reducer, and lifecycle

- Provider-neutral contracts are in
  `src/GovernedAccess.Core/Preparations/Contracts/TurnProposal.cs`, `DraftPatch.cs`, and
  `Outcomes.cs`. Clarification replies already use ordinary exact-ID `set`/`clear`
  operations; no production `selectClarification`, `ClarificationSelection`, or
  target/index conversion type remains.
- The old protocol survives only as explicit rejection/source-scan tests in
  `PreparationContractTests`, `TurnProposalJsonTranslatorTests`, and
  `TargetRequestPreparationArchitectureTests`.
- `RequestPreparationReducer`, `PreparationScopeEvaluator`, `PreparationRoleEvaluator`,
  `PreparationJustificationPolicy`, `PatchEvaluation`, and
  `RequestPreparationReduction` currently produce an ordered per-field
  `OperationResult` list with seven `OperationResultKind` values. `PatchEvaluation`
  holds temporary field transactions and dependency-propagation state.
- `PreparationTurnService` owns snapshot/reduce/short-commit behavior. It also owns the
  50-turn exhaustion and seven-day collecting-stale response paths.
- `RequestPreparation` owns immutable `PreparationId`, canonical candidate, five
  lifecycle states, `CandidateVersion`, `ConcurrencyVersion`,
  `InterpretedTurnCount`, bounded clarification, predecessor linkage, timestamps,
  30-minute ready expiry, and bounded material-change attribution.

### Agent, prompt, and model schema

- `MafTurnProposalInterpreter` contains the target instructions and still requires
  `requesterAuthoredNormalized`, checks preparation turn exhaustion, and performs no
  structured-output repair.
- `TurnProposalJsonTranslator` publishes the target JSON schema and maps the
  justification certification field into `JustificationProvenance`.
- `AgentInterpretationContracts` and `AgentExecutionLimitsTests` carry
  `MaximumInterpretedTurns=50`. `AgentExecutionBudget` correctly enforces per-turn
  tool/iteration limits but also retains search-result IDs solely to police the current
  20-result MCP versus five-result Core asymmetry.
- `TargetRequestPreparationOrchestrator` is the application boundary that reloads the
  snapshot, invokes the agent, and commits through Core. Requester-visible target
  rendering remains pending.

### Enterprise authorities and MCP

- `EnvironmentSearchPolicy` is already the shared protocol-neutral matcher behind Core
  and MCP, but it returns up to 20 results and distinguishes `NarrowQuery` (6–20) from
  `TooBroad` (over 20).
- Environment identity, environment-role assignments, and incidents use separate Core
  ports and adapters. `TargetMcpTools` and `TargetMcpToolExecutor` expose their four
  separate read-only wire contracts.
- `IncidentAuthorityProjection` exposes `EligibleEnvironmentIds`. Reference persistence
  represents this as `ReferenceIncidentEnvironmentLink` and the
  `IncidentEnvironmentLinks` join table with composite key, two foreign keys, and an
  environment index. Synthetic data and the accepted delivered product baseline use
  one incident-to-one-environment facts; only target-draft tests create zero/many cases.

### Persistence and migrations

- `RequestPreparationRecord`, `RequestPreparationRecordMapper`,
  `WorkflowDbContext`, `EfRequestPreparationStore`, and the workflow migration chain
  persist `CandidateVersion`, `ConcurrencyVersion`, `InterpretedTurnCount`, ready
  timestamps/deadline, candidate fields, clarification choices, predecessor linkage,
  and material-change attribution.
- The target workflow migration chain starts at
  `20260826071021_InitialWorkflowPersistence`; later migration designers and
  `WorkflowDbContextModelSnapshot` repeat the current preparation model.
- The reference initial migration `20260825072917_InitialReferenceAuthority`, its
  designer, and `ReferenceAuthorityDbContextModelSnapshot` create the incident link
  table.
- Durable active-preparation uniqueness exists. Unique `Request.PreparationId` is still
  specification-only and belongs to the pending target confirmation task; it must not
  be lost or claimed as current behavior.

### Tests, evaluation, and documents

- Core behavior is concentrated in `RequestPreparationReducerTests`,
  `RequestPreparationAggregateTests`, `PreparationTurnServiceTests`,
  `PreparationContractTests`, `EnvironmentSearchPolicyTests`, and their test base.
- Persistence, source boundaries, OCC, MCP, agent schema/limits, and isolated
  composition are covered in `WorkflowPreparationPersistenceTests`,
  `PreparationTurnConcurrencyTests`, `ReferenceAuthorityPersistenceTests`,
  `ReferenceAuthorityAdapterTests`, `TargetMcpContractTests`,
  `MafTurnProposalInterpreterTests`, `MafTurnProposalToolBoundaryTests`, and
  `TargetRequestPreparationArchitectureTests`.
- The implemented live-evaluation runner and `Evaluation/Datasets/intake-v1.json`
  still prove the delivered full-candidate design with 20 scenarios and an all-pass
  gate. The reviewed target 12-group/11-of-12 gate exists only in the target evaluation
  matrix; the isolated target live suite remains pending.
- The target specification, ADRs 0007–0009, target MCP contract, evaluation matrix, and
  roadmap were simplified by the planning run. Current/as-built baseline,
  architecture, security, orchestration, operator, and README documents deliberately
  remain unchanged until verified cutover.

## 3. Decision table

| ID | Current evidence | Final decision | Rationale and affected mechanisms | Class |
|---|---|---|---|---|
| S1 | Task 10A removed the selection act/type/index reducer path; only legacy rejection/source-scan tests remain. Ordinary exact-ID patches and restart-safe context are implemented. | Keep one ordinary sparse-patch path; delete protocol-specific negative fixtures and finish ordinary-patch restart/live evidence only. | No second command language or compatibility adapter. Preserve bounded context, exact reload, OCC, and deterministic consume/preserve rules. | Mandatory; approved and partially complete |
| S2 | `CandidateVersion` is stored, mapped, incremented, rendered into turn context, and heavily tested, but no correctness decision depends on it. OCC uses `ConcurrencyVersion`; card identity uses `PreparationId`; chronology has timestamps/attribution. | Remove it everywhere and do not rename it. | Deletes a domain property, persistence-state/record column, mapper paths, migration model, clean-reset checks, diagnostics, and version-only tests. | Mandatory after evidence gate; no remaining correctness use found |
| S3 | `PatchEvaluation` and `OperationResult` support arbitrary per-field accepted/rejected combinations and dependency verdict propagation. No target renderer consumes this yet. | Use one atomic scope group plus independent justification, with compact `Applied`, `NoOp`, `Rejected(reason)`, or `NeedsClarification` results. | Invalid/ambiguous explicit scope preserves current scope; a clarification may persist; valid justification remains independent; the complete commit is atomic. Delete per-field transactions and combinatorial summaries before the target renderer is built. | Mandatory target behavior |
| S4 | The prompt, JSON schema, `SetJustificationOperation`, translator, tests, and logs require the model to emit `requesterAuthoredNormalized`. Core cannot validate that linguistic claim. | Remove the certification field and enum; accept ordinary justification `set(text)`. | Preserve canonical formatting only, exact card text, requester language, and live fidelity gates. Use “justification fidelity,” not provenance, in audit/evaluation terminology. | Mandatory; approved |
| S5 | Target Core and reference schema support many incident environments through a list and join table. The accepted baseline and synthetic business data use one authoritative environment; no external integration requires many. | Collapse to nullable `Incident.EnvironmentId`. | Zero environment rejects scope; one derives/reloads; explicit conflict rejects scope. Delete join entity/table/index and multi-cardinality reducer/tests. | Evidence-gated; simplification accepted because no binding multi-environment evidence exists |
| S6 | Shared matcher code exists, but MCP exposes up to 20 results while Core renders only five; `NarrowQuery` and agent-side result-ID policing exist solely for the asymmetry. | Use shared 0/1/2–5/>5 semantics and a complete maximum of five on both surfaces. | Delete the hidden 6–20 set, `NarrowQuery`, 20-result schemas/messages, and selection-policing state. Keep deterministic normalization, approved fields, eligible-only filtering, stable order, and provider-conformance tests. | Mandatory target behavior |
| S7 | Aggregate, persistence, turn service, agent limits, responses, and tests enforce 50 interpreted turns. Core also emits a seven-day collecting-stale warning. | Remove both domain mechanisms. | Retain 4,000-character messages, zero-repair fail-closed output handling, tool/iteration limits, 30-second timeout/cancellation, and infrastructure rate limiting without lifecycle states. Bounded audit retention evicts or summarizes old metadata and never rejects a candidate change. | Mandatory; approved |
| S8 | Detailed target inventory, thresholds, reruns, retention, waiver, and rebaseline rules duplicated feature-spec concerns; actual target live suite is not implemented. | Keep only high-level promotion requirements in the feature spec; keep all detailed governance in the evaluation matrix. | Absolute safety gates remain unchanged. Task 4 builds the target suite from the matrix without cases dedicated to removed mechanisms. | Mandatory documentation separation |
| S9 | The accepted product baseline requires an owned **unexpired** ready intake at confirmation, and `docs/security-model.md` intentionally names the 30-minute expiry control. Current target aggregate implements lazy expiry. | Retain `Expired`, `ReadyAt`, `ReadyDeadline`, lazy expiry, confirm-before rendering, and expiry outcomes/tests. | Binding accepted evidence overrides the default removal target. Keep the implementation lazy; add no background worker or collecting TTL. | Evidence-gated; retained |
| S10 | Current target requires explicit role selection even when one assignable role exists; role is authorization scope and the mandatory card is confirmation, not initial selection. The one-to-five role clarification path is already required. | Retain explicit requester selection; create no separate auto-selection task. | Auto-selection adds a special branch and does not remove role-choice infrastructure. It can be reconsidered only through a product/authorization-scope decision. | Optional assessment; no implementation task |

## 4. Expected complexity delta

No line-count estimate is asserted. The implementation should remove or collapse these
named mechanisms:

| Area | Remove or collapse | Preserve |
|---|---|---|
| Contracts and types | `JustificationProvenance`, its operation property and JSON constant; `CandidateVersion`; `OperationResult`, seven-value `OperationResultKind`, and per-field result lists; `BudgetExhaustedGuidance`; `CollectingStaleWarning`; `EnvironmentSearchResultKind.NarrowQuery`; list-valued incident environment projection | Closed `TurnProposal`, sparse `set`/`clear`, bounded clarification context, typed safe reasons, one OCC token |
| Reducer/application | `PatchEvaluation` field transaction dictionary; arbitrary partial-apply aggregation; dependency-verdict propagation used only to explain discarded field operations; multi-environment incident branches; hidden search-result selection policing; candidate-version increment/check branches | Atomic scope transition and cascades; independent justification; one atomic persisted commit; deterministic response ownership |
| Workflow schema | `RequestPreparations.CandidateVersion` and `RequestPreparations.InterpretedTurnCount` columns and all mappings/snapshot entries | `PreparationId`, `ConcurrencyVersion`, candidate, clarification, predecessor, timestamps, `ReadyAt`, `ReadyDeadline`, active-preparation unique index, bounded safe attribution |
| Reference schema | `ReferenceIncidentEnvironmentLink`, `IncidentEnvironmentLinks`, composite key, both link foreign keys, and the link environment index | Nullable incident `EnvironmentId` with the minimum useful FK/index, separate incident authority port, no cross-database relationship |
| Lifecycle and outcomes | Permanent preparation exhaustion, `/new` exhaustion recovery, seven-day warning/age rendering, and two related outcomes | `Collecting`, `Ready`, `Submitted`, `Superseded`, `Expired`; immutable ready identity and lazy 30-minute expiry |
| Configuration | `MaximumInterpretedTurns` binding, hard maximum, test configuration, and diagnostics | Message, zero-repair fail-closed, per-tool, total-tool, provider-iteration, cumulative timeout/cancellation, and infrastructure rate bounds |
| Tests/evaluation | Candidate-version arithmetic/restore tests; exhaustion/stale-warning tests; 6–20 and over-20 tiers; multi-incident cardinality; per-field verdict permutations; selection-protocol payload/source-scan fixtures; self-certification assertions | Group atomicity, ordinary clarification patches across restart, OCC races, authoritative reloads, four-tool contract, expiry, justification fidelity, multilingual ambiguity, and zero-side-effect gates |
| Documentation | Duplicate evaluation-governance detail and obsolete dual semantics | Target spec/ADRs/contracts as normative intent; detailed release governance in one evaluation matrix; as-built docs only after cutover evidence |

## 5. Dependency order

```text
Task 1: contract + durable state contraction
    |
    +--> Task 2: authority + search contraction
             |
             +--> Task 3: grouped reducer + compact outcomes
                      |
                      +--> Task 4: tests + target evaluation evidence
                               |
                               +--> Task 5: cross-boundary verification + docs handoff
```

Tasks 1 and 2 establish the target schema and boundary contracts before Task 3 removes
the reducer framework. Test/evaluation pruning follows the implemented behavior in Task
4. Normative/as-built reconciliation follows passing implementation evidence in Task 5.
Do not preserve old and new contracts simultaneously between tasks; use direct branch
changes and keep each task's exit gate green before continuing.

## 6. Implementation tasks

### Task 1 — Contract, clarification, and durable preparation contraction

- [x] Complete

**Objective and complexity reduction**

Make one canonical preparation shape and one provider proposal shape: ordinary sparse
clarification patches, text-only justification, one concurrency token, no permanent
turn budget, and no collecting-stale response. This removes state and wire fields before
later reducer work depends on them.

**Current behavior to remove or change**

- `CandidateVersion` is initialized/incremented/validated separately from
  `ConcurrencyVersion`, persisted in every workflow model snapshot, copied into agent
  turn context, and used in clean-reset/version tests.
- Justification requires a model-authored `requesterAuthoredNormalized` certification.
- Every interpreted turn increments durable `InterpretedTurnCount`; the 50th limit
  returns `BudgetExhaustedGuidance` and makes `/new` recovery guidance special.
- A collecting preparation older than seven days emits `CollectingStaleWarning` with
  last-update/age data.
- Task 10A's removed clarification protocol still has dedicated legacy payload and
  source-string rejection tests.

**Likely files and consumers**

- Core: `Domain/Preparations/RequestPreparation.cs`, `PreparationValues.cs`,
  `Preparations/Contracts/DraftPatch.cs`, `Outcomes.cs`,
  `PreparationTurnContracts.cs`, `PreparationTurnService.cs`.
- Web AI: `MafTurnProposalInterpreter.cs`, `TurnProposalJsonTranslator.cs`,
  `AgentInterpretationContracts.cs`, `TargetRequestPreparationOrchestrator.cs`, and
  agent limit/diagnostic mappings.
- Workflow persistence: `WorkflowEntities.cs`, `RequestPreparationRecordMapper.cs`,
  `WorkflowDbContext.cs`, `EfRequestPreparationStore.cs`, workflow migrations,
  designers, and snapshot.
- Focused tests: `PreparationContractTests`, `RequestPreparationAggregateTests`,
  `PreparationTurnServiceTests`, `AgentExecutionLimitsTests`,
  `MafTurnProposalInterpreterTests`, `TurnProposalJsonTranslatorTests`,
  `WorkflowPreparationPersistenceTests`, and `PreparationTurnConcurrencyTests`.

**Explicit deletions and replacements**

- Delete `CandidateVersion` from domain state, persistence state/record, mappings,
  diagnostics, constructors, restore validation, and tests. Do not add another progress
  counter.
- Delete `JustificationProvenance` and the `provenance` JSON property/constant, prompt
  instruction, mapping, and audit vocabulary. `SetJustificationOperation` carries text
  only.
- Delete `InterpretedTurnCount`, `MaximumInterpretedTurns`, `CanInterpretTurn`,
  `RecordInterpretedTurn`, configuration binding, failure mapping, and exhaustion tests.
- Delete `BudgetExhaustedGuidance`, `CollectingStaleWarning`, stale-warning composition,
  age/last-update rendering inputs, and their tests.
- Delete selection-protocol-only negative payload fixtures/source scans. Retain closed
  allowlist tests and ordinary exact-ID clarification/restart evidence.
- Preserve zero structured-output repairs: the first malformed, schema-invalid, or
  structurally unacceptable output fails closed with zero mutation and no second
  interpreter invocation.
- Decouple material-change attribution capacity from turn count. At capacity, evict or
  summarize the oldest bounded diagnostic metadata; never reject a valid candidate
  transition or create a lifecycle state.

**Schema and data implications**

Remove the two workflow columns directly from the unpromoted target migration model and
regenerate the affected designers/snapshot. The target databases contain synthetic,
disposable data and have never been promoted, so recreate target test databases rather
than introduce a compatibility migration or backfill. Do not edit the delivered unified
database migrations. Keep ready timestamps/deadline and the active-preparation unique
index.

**Test and evaluation changes**

- Deterministic: prove text-only justification schema, first-invalid-output fail-closed
  behavior with no repair, no turn exhaustion, no stale warning, bounded audit eviction
  without domain rejection, one OCC token, and ordinary clarification patch shape.
- Integration: round-trip the contracted preparation schema, prove context-only writes
  advance OCC, and repeat stale-snapshot races without candidate version.
- Architecture: retain exact `/new`/no-parser and provider-neutral-contract checks;
  remove tests whose only purpose is naming deleted clarification types.
- Live model: schedule justification fidelity and clarification cases for Task 4; do
  not use model self-certification as a grade.

**Documentation reconciliation after evidence**

Verify the specification, ADR 0007, ADR 0009, MCP contract, evaluation matrix, and
roadmap against the implemented names. Do not update current/as-built product documents
until cutover.

**Dependencies**

None. Land before Tasks 2–4.

**Risks and preserved safeguards**

The main risks are weakening OCC while deleting a version or accidentally making audit
retention a new exhaustion path. Preserve one `ConcurrencyVersion`, short commits,
bounded safe metadata, immediate fail-closed output handling, cancellation, and no raw
content persistence.

**Exit gate**

No source, schema, migration snapshot, prompt, test, or diagnostic contains the deleted
version/budget/stale/certification mechanisms; workflow persistence round-trips with one
OCC token; zero-repair fail-closed tests pass; ordinary clarification patches still
survive restart.

### Task 2 — Contract the authority model and shared environment search

**Objective and complexity reduction**

Represent the actual product rule—one incident has zero or one authoritative
environment—and make the already-shared environment matcher expose the same complete
five-result semantics to MCP and Core.

**Current behavior to remove or change**

- `IncidentAuthorityProjection.EligibleEnvironmentIds` and the reference join table
  allow zero, one, or many environments; reducer/MCP tests preserve all cardinalities.
- `EnvironmentSearchPolicy.MaximumResultCount` is 20. `NarrowQuery` covers 6–20 and
  `TooBroad` starts at 21, while clarification maxes at five.
- `AgentExecutionBudget` records result IDs and rejects model exact-ID proposals solely
  to prevent collapsing the hidden larger set.

**Likely files and consumers**

- Core authority/search: `Ports/PreparationAuthority.cs`,
  `Preparations/Authority/AuthorityProjections.cs`, `EnvironmentSearchResult.cs`, and
  `EnvironmentSearchPolicy.cs`.
- Reference authority: `ReferenceEntities.cs`, `ReferenceAuthorityDbContext.cs`,
  `SyntheticReferenceData.cs`, `IncidentAuthorityAdapter.cs`, environment adapters,
  initial reference migration/designer/snapshot.
- MCP: `TargetMcpTools.cs`, `TargetMcpToolExecutor.cs`, target registration/contract
  assertions.
- Web AI: `AgentExecutionBudget.cs`, `MafTurnProposalInterpreter.cs`, catalog/tool
  result translation.
- Tests: `EnvironmentSearchPolicyTests`, `PreparationAuthorityContractTests`,
  `ReferenceAuthorityPersistenceTests`, `ReferenceAuthorityAdapterTests`,
  `TargetMcpContractTests`, `TargetMcpFailureTests`,
  `MafTurnProposalToolBoundaryTests`, and target test seeders/fixtures.

**Explicit deletions and replacements**

- Replace `EligibleEnvironmentIds` with nullable `EnvironmentId` in the Core projection,
  reference entity/adapter, MCP result, fake authorities, and seeds.
- Delete `ReferenceIncidentEnvironmentLink`, link navigation/`DbSet`, join mapping,
  multi-environment seeding, and zero/many cardinality branches that exist solely for
  the draft target.
- Set the one shared maximum to five. Keep 0=no match, 1=exact reload, 2–5=complete
  clarification, and >5=too broad.
- Delete `NarrowQuery`, the 20-result bound/messages/schemas, 6–20 tests, and
  `IsEnvironmentSelectionAllowed` plus its stored search-result ID set. Keep general
  per-tool/total-tool/iteration budget enforcement.
- Implement and enforce the approved MCP contract `3.0.0` and search policy `2.0.0`;
  do not create or expose a parallel target version.

**Schema and data implications**

Replace `IncidentEnvironmentLinks` with nullable `Incidents.EnvironmentId` in the
unpromoted reference initial migration, designer, and snapshot. Add only the useful FK
and index for that nullable column; remove the composite key, link FKs, and link
environment index. Recreate the isolated synthetic reference database. No workflow
schema or cross-database relationship is added.

**Test and evaluation changes**

- Deterministic: retain Unicode/token/approved-field/stable-order/provider-conformance
  tests and replace result-count cases with 0/1/2/5/6.
- Integration: prove nullable one-environment persistence/adaptation, inactive/missing
  handling, exact reload, and identical MCP/Core five-result behavior.
- Architecture: prove the four separate read-only tools and separate authority ports
  remain; MCP still has no EF/reference/workflow dependency.
- Live model: remove cases that grade the hidden 6–20 asymmetry; retain unique,
  ambiguous 2–5, and too-broad behavior.

**Documentation reconciliation after evidence**

Verify ADR 0008 and both target MCP contract files against emitted schemas and safe
messages. Keep low-level matching detail in the contract/component tests rather than
restoring it to the primary feature specification.

**Dependencies**

Task 1 complete. Task 3 consumes the contracted projection/result kinds.

**Risks and preserved safeguards**

Do not infer incident environment from title/text, collapse independently governed
ports, trust MCP payloads, depend on SQLite `NOCASE`, rank/truncate results, or expose
ineligible environments. A null/inactive incident or source failure rejects scope.

**Exit gate**

Reference schema has one nullable incident environment and no join table; MCP and Core
share the five-result policy/version; >5 exposes no hidden set; four-tool contract and
independent authority failure tests pass.

### Task 3 — Replace per-field reduction with two atomic application groups

**Objective and complexity reduction**

Replace arbitrary operation-level partial success with one atomic scope decision and
one independent justification decision before the target renderer is built.

**Current behavior to remove or change**

- `PatchEvaluation` accumulates per-field temporary transactions and a dictionary of
  seven verdict kinds.
- `PreparationScopeEvaluator` and `PreparationRoleEvaluator` propagate conflict and
  dependency verdicts field by field, and `RequestPreparationReduction`/
  `DraftUpdated`/`DraftUnchanged` carry ordered result lists.
- Tests assert many combinations whose only observable difference is which field-level
  rejection label appears.

**Likely files and consumers**

- `RequestPreparationReducer.cs`, `RequestPreparationReducer.Validation.cs`,
  `PreparationScopeEvaluator.cs`, `PreparationRoleEvaluator.cs`,
  `PreparationJustificationPolicy.cs`, `PatchEvaluation.cs`,
  `RequestPreparationReduction.cs`, `Contracts/Outcomes.cs`, and
  `PreparationTurnService.cs`.
- Future consumer boundary: `TargetRequestPreparationOrchestrator`; no target renderer
  exists, so define the compact application outcome before historical Task 11.
- Tests: `RequestPreparationReducerTests`, `RequestPreparationReducerTestBase`,
  `PreparationContractTests`, `PreparationTurnServiceTests`, and concurrency/source
  failure tests.

**Explicit deletions and replacements**

- Delete `PatchEvaluation`, its field transaction dictionary, `OperationResult`, the
  seven-value `OperationResultKind`, result ordering, and arbitrary accepted/rejected
  lists.
- Introduce the minimum behavior result required by the application: scope and
  justification each report `Applied`, `NoOp`, `Rejected(reason)`, or
  `NeedsClarification`; keep typed reason metadata only where rendering/diagnostics
  distinguish behavior.
- Evaluate proposed environment/incident/role facts into a temporary canonical scope.
  If any explicit scope operation is invalid, unavailable, conflicting, or ambiguous,
  discard every same-turn scope mutation. A complete bounded clarification may still
  persist without mutating current scope.
- Treat environment/client derivation, incident compatibility, role validation against
  final environment, and environment/incident/role clears as one transition with all
  deterministic cascades.
- Evaluate justification independently. Valid justification may commit beside rejected
  scope; invalid justification does not discard valid scope. Persist accepted groups,
  clarification, lifecycle, attribution, and OCC atomically.
- Preserve clarification precedence/consumption using ordinary target-field operations;
  do not reintroduce choice membership or index conversion.

**Schema and data implications**

No new columns or tables. The task consumes Tasks 1–2 schemas. Persist only final
canonical candidate/context and compact safe audit categories; do not persist reducer
transactions or per-field verdict lists.

**Test and evaluation changes**

- Deterministic: replace per-field combinatorics with a group matrix covering valid
  scope + invalid justification, invalid scope + valid justification, conflict,
  ambiguity with context/no scope mutation, exact role against final environment,
  cascades, clears, value-equal no-op, and persistence failure atomicity.
- Integration: prove authority failure rejects scope only, OCC rejects the complete
  commit, and no temporary mutation leaks.
- Architecture: assert application outcomes contain no model prose and no per-field
  transaction contract.
- Live model: grade proposed operations and final canonical outcome; do not require the
  model to predict reducer verdicts.

**Documentation reconciliation after evidence**

Verify ADR 0007, the specification reducer section, evaluation matrix, and roadmap.
Future target renderer documentation must describe group results only.

**Dependencies**

Tasks 1 and 2 complete.

**Risks and preserved safeguards**

The primary risk is allowing a role to apply against old scope or losing a valid
justification during a scope failure. Preserve authoritative reloads, final-environment
role validation, clarification precedence, cascades, typed failures, and one atomic
OCC commit.

**Exit gate**

No per-field transaction/result framework remains; every reducer case resolves into
the two groups; unit/integration matrices prove atomic scope, independent justification,
clarification without scope mutation, cascades, and no leaked temporary state.

### Task 4 — Prune obsolete tests and implement the target evaluation gate

**Objective and complexity reduction**

Make the evidence suite prove preserved behavior rather than deleted mechanisms, and
finish Task 10A's remaining ordinary-patch clarification evidence without duplicating
the historical selection protocol.

**Current behavior to remove or change**

- Unit/integration tests preserve candidate arithmetic, per-field verdicts, 50-turn
  exhaustion, seven-day warnings, 6–20 search, incident-many branches, justification
  certification, and explicit deleted-protocol payload/source strings.
- The credentialed evaluator/dataset still targets the delivered full-candidate design;
  the isolated four-tool sparse-patch target has no executable promoted suite.
- No target renderer exists, so renderer-summary permutations are not yet code; avoid
  creating them in historical Task 11.

**Likely files and consumers**

- All focused test files named in Tasks 1–3 plus target architecture/hosting tests.
- `src/GovernedAccess.Web/Evaluation/*`,
  `Evaluation/Contracts/evaluation-dataset.schema.json`, and versioned dataset/config
  selected for the isolated target run.
- Historical Task 10A remaining evidence items and historical Tasks 11/16 as downstream
  consumers.

**Explicit deletions and replacements**

- Delete tests dedicated only to removed counters, warnings, selection vocabulary,
  hidden result tiers, multi-cardinality, certification, and arbitrary field verdicts.
- Consolidate absence checks around the closed proposal enum/schema rather than keeping
  multiple old-protocol string fixtures.
- Retain negative tests for no parser, unknown schema/properties, immediate fail-closed
  malformed output with no repair, stale OCC, source failures, prompt injection, and
  zero consequential effects.
- Build one versioned isolated target live suite from the evaluation matrix. Do not
  weaken absolute gates or overwrite delivered evidence before atomic cutover. Do not
  add scenarios whose sole purpose is policing deleted mechanisms.
- Record model, prompt, proposal schema, MCP contract, search-policy, dataset version,
  environment/date, scenario outcomes, and zero-side-effect counts in safe retained
  evidence.

**Schema and data implications**

Update only evaluation dataset schema/versioning needed for the target proposal and
grouped expected canonical outcomes. Evaluation artifacts must contain no raw prompts,
reasoning, full tool payloads, secrets, or consequential state. No workflow/reference
data migration occurs in this task.

**Test and evaluation changes**

- Deterministic and integration suites follow the matrix in their prescribed layers;
  live-model credentials are never required for ordinary automated tests.
- Live cases retain multilingual/descriptive clarification, exact-ID validation,
  safe ambiguity, unique search, too-broad search, justification language/no-invention,
  reset/submission restraint, injection, failures, and zero side effects.
- Absolute live gates remain 100% for consequential side effects, unknown/mutating
  tools, model prose, authoritative IDs, reset/submission/injection restraint, expected
  clarification or conservative `unclear`, and justification invention/translation.
  The matrix remains sole authority for inventory, numerical quality threshold,
  reruns, waiver, retention, and rebaseline mechanics.

**Documentation reconciliation after evidence**

Update only the evaluation matrix when executable schema/inventory names differ from
the approved target. Do not copy its detailed governance back into the feature spec.

**Dependencies**

Tasks 1–3 complete.

**Risks and preserved safeguards**

Do not mistake fewer tests for weaker proof. Every removed test must map either to a
deleted mechanism or to consolidated equivalent coverage. Never let a live-model pass
override a deterministic failure, and never let evaluation create requests or
downstream rows.

**Exit gate**

All obsolete-mechanism tests are gone; the focused deterministic suite proves the
simplified behavior; isolated target live evaluation records a passing report under the
matrix's unchanged absolute safety gates and required quality threshold.

### Task 5 — Cross-boundary verification and post-evidence reconciliation

**Objective and complexity reduction**

Prove that removal is complete across contracts, schemas, runtime registrations, tests,
and documents, then hand the smaller target to the still-pending renderer/confirmation/
cutover work without reopening old protocols.

**Current behavior to remove or change**

This task adds no feature behavior. It closes residual references, validates migration
creation/restart, and prevents historical Tasks 11–17 from rebuilding deleted
complexity.

**Likely files and consumers**

- Solution/project composition and architecture tests; target reference/workflow
  fixtures; all files touched by Tasks 1–4.
- Normative target documents changed by this planning run.
- After target cutover evidence only: product baseline, architecture, security model,
  request-intake orchestration, testing strategy, operator guidance, README, canonical
  MCP contract, and roadmap.

**Explicit deletions and replacements**

- Run source/schema searches for every deleted type, field, setting, table, result tier,
  prompt term, and old clarification protocol; remove residual target references rather
  than adding adapters.
- Verify production never registers both tool catalogs and the target catalog has
  exactly four read-only tools.
- Update the historical delivery handoff so target renderer work consumes two group
  results and target confirmation keeps expiry/idempotency/revalidation. Do not rewrite
  completed historical task records.

**Schema and data implications**

Create fresh isolated reference/workflow databases from migrations, inspect tables,
columns, foreign keys, and indexes, restart them, and prove independent migration
histories. The delivered unified database remains unchanged. Any unexpected need to
preserve target data stops direct migration rewriting and requires a documented
migration decision; no such requirement exists now.

**Test and evaluation changes**

Run repository validation sequentially in the mandated order:

1. `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
2. `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore`
3. `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m`
4. Run the frontend suite only if the eventual target renderer or a frontend contract
   changed.
5. Run credentialed target live evaluation for promotion and retain its evidence.

Also run architecture/source checks, migration schema inspection, JSON-schema parsing,
documentation link validation, and `git diff --check`.

**Documentation reconciliation after evidence**

Reconcile the target specification, ADRs/index, MCP contract, evaluation matrix, plan,
and roadmap with verified code. Schedule current/as-built document reconciliation as
the final cutover task; do not describe the four-tool target as delivered beforehand.

**Dependencies**

Tasks 1–4 complete and deterministic/live evidence available.

**Risks and preserved safeguards**

The main risks are declaring a partial target delivered, accidentally editing the
delivered composition during migration cleanup, or allowing historical work to restore
old contracts. Preserve phase isolation, all confirmation requirements, downstream
determinism, and the authority order in `spec.md`.

**Exit gate**

All required builds/tests/evaluation pass; fresh target databases match the contracted
schema; deleted-name searches are clean; production/target composition isolation holds;
normative target docs match evidence; and historical Tasks 11–17 can resume against only
the simplified contracts.

## 7. Preserved-safety verification matrix

| Safety property | Simplification task and evidence | Current implementation note |
|---|---|---|
| No deterministic free-text parser | Task 4 architecture/source checks plus exact `/new` and `/new please` host tests | Implemented in target boundary; preserve unchanged |
| Exactly four read-only model tools | Task 2 target MCP schema/transport tests; Task 5 composition allowlist/static checks | Implemented only in isolated target; production remains two-tool until cutover |
| Authoritative reloads | Tasks 2–3 authority adapter, exact-ID, search drift, role-final-environment, incident, and confirmation-contract tests | Turn-time reload exists; confirmation-time proof remains pending |
| Clarification restart safety | Tasks 1, 3, and 4 persistence restart journey using ordinary exact-ID patch and no transcript | Implemented by Task 10A; remaining evidence is incorporated here |
| Optimistic concurrency | Tasks 1 and 3 aggregate/OCC tests; Task 4 `PreparationTurnConcurrencyTests`; Task 5 fresh-database replay | Implemented with `ConcurrencyVersion`; `CandidateVersion` is unnecessary |
| Stale-card safety | Task 1 preserves immutable ready identity/expiry; Task 5 contract checks; historical target confirmation Task 12 proves old-card rejection after successor creation | Aggregate behavior exists; target Teams confirmation is not yet implemented |
| Request idempotency | Task 5 prevents schema/design regression; historical target confirmation Task 12 adds unique `Request.PreparationId` and concurrent-confirm convergence tests | Specification-only on this branch; explicitly pending |
| Confirmation-time authoritative revalidation | Tasks 2–3 keep separate authority ports; Task 5 handoff check; historical target confirmation Task 12 exercises changed/ineligible source facts | Specification-only on target path; delivered flow has its own behavior |
| Zero free-text consequential side effects | Task 4 universal zero-row assertions/live gates; Task 5 target host architecture checks; historical Tasks 13–15 full-host/cutover proof | Target turn service has no mutating model tools; full target host remains pending |

No simplification task may claim the last three target confirmation/full-host properties
as fully implemented before historical Tasks 12–15 complete. Their contracts are
preserved now and their final proof remains a cutover prerequisite.

## 8. Contradictions, resolutions, and assumptions

### Resolved contradictions

- **Delivered versus target truth:** the accepted product baseline and current
  as-built documents correctly describe a two-tool/full-candidate production runtime,
  while the target specification describes the isolated four-tool replacement. This is
  intentional phase isolation, not authority to register both catalogs.
- **Incident cardinality:** the target draft/code supported many environments, but the
  accepted baseline and actual synthetic product data use one. No external contract or
  repository integration requires many, so the target-only join/list complexity is
  removed.
- **Ready expiry:** the simplification default suggested removal, but the accepted
  baseline explicitly requires an unexpired ready intake and the security model treats
  the 30-minute deadline as a submission control. Expiry remains.
- **Structured repair:** the explicit follow-up decision retains zero repairs, matching
  current target code and the pre-simplification contract. The first invalid output
  fails closed with no second interpreter invocation.
- **Evaluation authority versus implementation:** the target matrix defines 12 promoted
  groups and detailed gates, but the executable evaluator/dataset still proves the
  delivered 20-scenario design. Task 4 implements the isolated target suite; existing
  delivered evidence is not relabeled.
- **Clarification simplification:** code already uses ordinary patches, while some tests
  preserve deleted protocol strings only to prove absence. Those redundant fixtures are
  removed; ordinary-patch and closed-schema evidence remains.

### Genuine incomplete boundaries, not silent assumptions

- The target Teams renderer, target confirmation path, unique
  `Request.PreparationId`, confirmation-time target reloads, isolated full-host journey,
  atomic cutover, and legacy deletion are not implemented. They remain historical Tasks
  11–15 and are not pulled into this simplification pass.
- Because no target renderer exists, there is no combinatorial target renderer to
  delete. Task 3 contracts outcomes before one can be introduced.

### Planning assumptions

- The isolated target reference/workflow databases contain only synthetic disposable
  data and the target has not been promoted. Directly regenerate the target migration
  model and recreate test databases; do not build backward-compatibility adapters or
  alter delivered unified migrations.
- Bounded material-change audit metadata remains useful, but reaching its retention
  cap evicts or summarizes oldest diagnostics and never blocks a preparation.
- Explicit role selection is a deliberate authorization-scope rule until an approved
  product decision says otherwise.
- Current/as-built documents are reconciled only after target implementation and
  cutover evidence. This planning run changes target normative documents and the
  roadmap only.

No unresolved contradiction blocks implementation of Tasks 1–5.

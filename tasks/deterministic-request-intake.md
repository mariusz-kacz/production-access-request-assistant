# Implementation Plan: Deterministic Request Intake

- **Status:** Fresh implementation plan; implementation has not started
- **Target branch:** `feature/decouple-teams-approval-flow`
- **Primary authority:** `SPEC-deterministic-request-intake.md`
- **Planned slices:** 7, executed in order unless a task explicitly permits focused work in parallel
- **Planning boundary:** This file is the only artifact created in the planning run

## Outcome

Replace the delivered full-candidate request-preparation flow with one language boundary:

```text
exact trimmed case-insensitive /new -> deterministic reset protocol

all other authenticated nonblank requester text
    -> one bounded MAF agent
    -> closed provider-neutral TurnProposal
    -> deterministic authoritative reducer
    -> short optimistic commit
    -> application-owned response/card
```

The implementation must leave request creation exclusively behind authenticated Adaptive Card confirmation. It must not add another agent, another deployable service, a state-changing MCP tool, raw conversation persistence, or deterministic requester-language interpretation outside exact `/new`.

## Authority and resolved contradictions

Apply the repository constitution and rules first, then the specification, ADRs 0007–0009, the target MCP contract/test matrix, current as-built behavior outside the changed boundary, the roadmap, and finally any old or partial implementation.

| Conflict discovered | Resolution used by this plan |
|---|---|
| ADR 0009 says a revision predecessor is persisted “when useful,” while the specification requires it for every revision-created preparation. | The predecessor is mandatory for every revision successor. Root preparations alone have no predecessor. |
| The target test matrix sections 5.3, 5.5, and Journey D say environment/role ambiguity clears canonical scope. | The higher-authority specification governs: clarification is non-destructive and preserves the current canonical candidate. Those matrix expectations are corrected only in Task 7 after implementation evidence passes. |
| The roadmap repeats destructive environment ambiguity and calls predecessor linkage optional. | Treat those statements as obsolete, non-authoritative roadmap text; reconcile them in Task 7. |
| Current baseline/as-built documents describe two MCP tools, complete candidates, process-local choices, `Invalidated`, reserved request IDs, and Ready clarification that preserves the old confirmable card. | They remain truthful for the running system until promotion, but none of those changed-boundary behaviors constrain implementation. Reconcile them only in Task 7. |
| The target matrix lists build after several test layers, while repository `AGENTS.md` mandates build, unit, then integration after code changes. | Use architecture/source checks first, then the mandatory backend command order: build, unit, integration. Review integration evidence by persistence/MCP/full-host concern, then run affected frontend tests, live evaluation, and documentation reconciliation. |
| The target MCP incident projection contains one `environmentId`, while Core incident authority may return zero, one, or many eligible links. | Keep the closed model-visible projection unchanged. Define a richer Core authoritative incident projection for cardinality; never infer Core authority from the MCP projection. |

No lifecycle, version, clarification, reducer-order, confirmation, or budget decision remains open. The only operational assumption not specified by the feature authority is handling an existing local SQLite file. This plan takes the conservative path: add forward migrations that preserve compatible requests and tombstones, backfill derivable preparation links, and fail closed on unbackfillable data rather than silently deleting the database.

## Current-system findings

The branch contains the documentation overlay but no feature implementation. Relative to `main`, the only application-project edit is the evaluation-schema path move in `GovernedAccess.Web.csproj`; request-intake source still implements the delivered baseline.

| Required investigation | As-built evidence and disposition |
|---|---|
| Requester-text parsing outside `/new` | `TeamsAccessRequestAgent.OnMessageAsync` has the permitted exact `/new` comparison. No separate phrase/regex/numeric/ordinal parser was found. Preserve that single exception and add architecture checks so none is introduced. `EnvironmentDiscoveryAfterExactLookupGate` is tool-order policy, not text parsing, but it conflicts with the target diagnostic-only tool-order rule and is removed in Task 3. |
| Model-owned snapshot | `RequestCandidate`, `RequestPreparationProposal`, `RequestDraftService`, `RequestDraftValidator`, `MafRequestPreparationInterpreter.ProposalSchema`, its prompt, deterministic chat fixtures, and evaluation models carry a complete nullable candidate including model-supplied client. Replace, do not adapt, this snapshot contract. |
| Duplicate environment search | No target search implementation exists yet. The current MCP lists a complete catalog and the model filters it linguistically. Task 3 introduces exactly one shared deterministic policy used by both Core and MCP; no second matcher is permitted. |
| Destructive ambiguity | Collecting clarification persists the model’s complete candidate through `UpdateCandidate`, so nulls can erase canonical fields. Current unit tests explicitly expect null clearing. Replace with non-destructive context-only ambiguity semantics. |
| Clarification selection | Choices are not persisted. Numeric/ordinal replies depend on process-local MAF history and return another full candidate. There is no target/index-to-ID mapping, candidate-version binding, restart safety, or selected-entity full-pipeline operation. Replace the mechanism entirely. |
| Version semantics | `RequestIntakeSession.PersistenceVersion` is both the aggregate mutation counter and EF concurrency token; every update increments it. There is no `CandidateVersion` or post-commit context binding. Split the concepts. |
| Ready behavior | The domain prevents direct candidate mutation while Ready, but `RequestDraftService` leaves Ready and its card active during a revision clarification; wholly rejected revisions can supersede it. Target behavior instead creates a successor for a valid clarification-producing revision and preserves Ready for non-mutating/all-rejected/failure turns. |
| Predecessor | No predecessor property or relationship exists. Add it and require it on every revision-created successor. |
| Active uniqueness | `GovernedAccessDbContext` already has a filtered unique EF index for `Collecting`/`Ready`, but its key omits `RequesterId`, no migration artifact exists, and `EfRequestIntakeStore` maps the creation race to generic persistence failure instead of reloading the winner. Retain and complete the invariant rather than adding a read-before-write substitute. |
| Database lock across agent/tool latency | No explicit transaction currently spans `IRequestPreparationInterpreter.InterpretAsync`; the scoped context merely tracks entities. Preserve this and prove the load/invoke/short-OCC-commit protocol with instrumentation/race tests. |
| Confirmation fact drift | `RequestSubmissionService` converts changed facts to `Invalidated`, clears the candidate, and returns generic failure. Source unavailability also has no distinct closed confirmation outcome. Replace with corrected successor versus preserved-Ready semantics. |
| Confirmation idempotency | Ready reserves a future request ID, and replay relies on `ReservedRequestId`. `AccessRequest` has no `PreparationId` and no unique preparation key. Remove reservation as authority and add unique `Request.PreparationId`. |
| Logging/privacy | Normal Teams/MCP logs are structured and do not include requester text or tool arguments. However live-evaluation results retain `FinalApplicationOutcome.ModelResponse` and emit it in failure artifacts. Remove that raw model/proposal content and add negative logging/artifact tests. |
| Persisted justification replay | The MAF envelope replays the complete candidate and labels it prior application state, but it does not isolate stored justification as prompt-injection-capable untrusted data. Delimit and label it explicitly; never treat it as instructions or linguistic evidence. |
| Incident cardinality | `Incident.EnvironmentId` and `IRequestContextReader.GetIncidentAsync` expose one required link. Add a Core authority projection that can report zero/one/many eligible links; the synthetic adapter may project its current single-link data, while deterministic fakes cover all cardinalities. |
| Searchable environment facts | `ProductionEnvironment` currently has only ID, client ID, and display name. Region, primary/recovery classification, activity, production classification, and intake eligibility are not explicit authoritative facts. Add them before implementing exact eligibility and shared search. |
| Budgets | Current configuration has a 100-second Teams request timeout and six provider iterations only. It lacks the 4,000-character, 50-turn, rolling 20/10-minute, one-call-per-tool/four-total, one-repair, and cumulative 30-second contracts. |
| Partial Task 1 code | None was found in current application source. Existing intake types are delivered-baseline code and must be replaced where they cross the changed boundary; they are not a partial target implementation to preserve. |

## Schema and migration map

Task 1 owns the shape and migration; later tasks consume it.

| Current schema | Target impact |
|---|---|
| `RequestIntakeSessions.PersistenceVersion` | Replace/backfill into distinct `CandidateVersion` and `ConcurrencyVersion`; EF maps only the latter as the OCC token. Empty collecting rows start candidate version 0; non-empty rows start 1. |
| `Invalidated` lifecycle | Map existing rows to terminal `Superseded`; target lifecycle is exactly `Collecting`, `Ready`, `Submitted`, `Superseded`, `Expired`. |
| `ReservedRequestId` unique index | Remove after request/preparation backfill; request identity is generated at confirmation and idempotency uses `AccessRequests.PreparationId`. |
| No predecessor | Add nullable storage for roots, with domain/store APIs that require a value for every revision-created successor; add self-reference/index as useful for audit lookup. |
| No clarification storage | Add one bounded context per preparation containing preparation ID, post-commit candidate version, target, ordered canonical IDs, and UTC creation time; enforce target/count/active-state invariants in Core and model constraints in EF. |
| No interpreted-turn count | Add durable per-preparation interpreted-turn count for permanent exhaustion. Add bounded safe requester/timestamp rate-window metadata or an equivalently durable local mechanism; store no message or proposal content. |
| No accepted-change attribution | Add bounded records keyed to preparation/candidate version with changed field categories, provider/model, prompt/schema versions, timestamp, and correlation only. |
| `AccessRequests` has no preparation key | Add non-null `PreparationId` and a durable unique index. Backfill through submitted intake evidence; migration fails closed if an existing request cannot be mapped uniquely. |
| Existing active partial unique index | Recreate with a stable name and the complete authenticated actor/conversation binding (`Channel`, `TenantId`, `ChannelActorId`, `ConversationId`, `RequesterId`) filtered exactly to `Collecting` and `Ready`. |
| Environment eligibility facts are implicit | Add explicit region, primary/recovery classification, active/production state, and intake eligibility fields; seed exact synthetic values and validate them at startup. |
| No EF migrations | Introduce and test forward EF migrations, use migration-based startup for file databases, and keep isolated test setup explicit. Do not delete or recreate a user database implicitly. |

## Task sequence

### Task 1 — Replace snapshot contracts and establish the canonical schema

**Goal**

Create the provider-neutral proposal/outcome vocabulary and durable aggregate schema that all later work can depend on, while deleting incompatible full-candidate and lifecycle concepts. This task defines types and invariants; it does not implement the reducer, agent prompt, or MCP handlers.

**Why now / dependencies**

This is the dependency root. Reducer, adapter, persistence, and confirmation work cannot be reviewed safely while `RequestCandidate`, `PersistenceVersion`, `Invalidated`, `ReservedRequestId`, and ephemeral clarification remain the shared contracts. Depends only on the approved authority set.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Core/Ports/RequestDrafting.cs`
- `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- `src/GovernedAccess.Core/Ports/CorePorts.cs`
- `src/GovernedAccess.Core/Domain/Drafts/RequestIntakeSession.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/AccessRequest.cs`
- `src/GovernedAccess.Core/Domain/ReferenceData/ProductionEnvironment.cs`
- `src/GovernedAccess.Core/Domain/ReferenceData/Incident.cs`
- `src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs`
- new EF migration(s) and model snapshot under `src/GovernedAccess.Web/Persistence/Migrations/`
- `src/GovernedAccess.Web/Persistence/SyntheticDataSeeder.cs`
- `src/GovernedAccess.Web/Program.cs`
- `tests/GovernedAccess.UnitTests/RequestPreparationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/GovernedAccessDbContextModelTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/SyntheticDataSeederTests.cs`

**Required changes**

- Define closed Core types for `TurnProposal`, dialogue acts, sparse `set`/`clear` field operations, environment exact/search references, justification provenance, target/index clarification selection, discussion topics, structural rejection, per-operation verdicts, and application outcomes. Unknown enum values/properties remain an adapter/schema rejection, not an extensibility path.
- Make mutable proposal fields exactly environment, role, justification, and optional incident. Client, identity, duration, lifecycle, request identity, approvers, approvals, provisioning, retry, audit, and grants have no proposal operation.
- Redesign `RequestIntakeSession` around immutable unguessable `PreparationId`, exact five-state lifecycle, canonical candidate, `CandidateVersion`, `ConcurrencyVersion`, timestamps, optional active clarification, durable interpreted-turn count, and mandatory predecessor constructor/factory for revisions.
- Enforce clean creation version 0; non-empty creation version 1; one increment per later material candidate commit; context-only writes do not increment; every aggregate write advances the concurrency token.
- Model clarification as one bounded ordered context with at most five IDs and a candidate version supplied only as the post-commit value by the aggregate/store operation.
- Add bounded accepted-change attribution without raw values and add `AccessRequest.PreparationId` as a distinct immutable request idempotency key.
- Add explicit authoritative environment facts required for eligibility/search. Expose Core incident authority separately from the single-link MCP DTO so deterministic fakes can return zero/one/many eligible environment links.
- Add tested forward migrations and startup migration behavior. Backfill candidate/concurrency versions and request preparation links conservatively; map `Invalidated` tombstones to `Superseded`; fail closed on ambiguous/orphan data.
- Complete the partial active-preparation index key and name, and add the unique request-preparation index. Database constraints complement, not replace, domain invariants.

**Deletions / replacements**

- Delete `RequestPreparationProposalKind`, model-owned `RequestCandidate`, `RequestClarificationProposal.Message`, and the full-candidate `RequestPreparationProposal` contract.
- Remove `Invalidated`, `PersistenceVersion`, and `ReservedRequestId` from intake authority.
- Replace tests that expect 20 ephemeral environment options, complete candidate replacement, reserved request identity, or one overloaded version.
- Do not create `docs/deterministic-request-intake-design.md`, `tasks/todo.md`, or a second plan file.

**Tests / evidence**

- Core construction tests cover every valid/invalid act-payload combination, sparse omission, operation shape, clarification target/index bounds, five-choice maximum, lifecycle set, predecessor factories, UUID nonempty identity, canonical-version worked examples, and no requester-text property on Core reducer contracts.
- EF model/migration tests inspect the exact partial active index, request-preparation unique index, concurrency token, clarification constraints, attribution bounds, UTC mappings, foreign keys, and an upgrade from a representative current schema.
- Migration tests prove compatible requests/tombstones survive, no duplicate active row is introduced, and unbackfillable request-preparation evidence fails rather than guessing.
- Seeder tests prove explicit region/classification/eligibility data is exact and idempotent.
- Run the repository backend sequence in order: warnings-as-errors build, unit suite, integration suite.

**Acceptance criteria IDs**

AC-03, AC-07, AC-09, AC-23, AC-24, AC-29, AC-30, AC-31, AC-34, AC-36, AC-38, AC-43.

**Exit gate**

The new contracts and migrated schema compile and are independently testable; Core contract types contain no requester text or model prose; lifecycle/version/predecessor/index semantics exactly match the specification; incompatible snapshot/reserved-ID concepts no longer compile.

**Non-goals**

- No reducer behavior, prompt tuning, live MCP catalog promotion, Teams rendering, confirmation flow, or documentation promotion.
- No second aggregate, pending revision candidate, clarification queue, background expiry worker, or generic audit/event framework.

### Task 2 — Implement the authoritative sparse reducer

**Goal**

Implement one deterministic reducer that validates a structurally valid proposal against a canonical snapshot and authoritative ports, produces explicit per-operation outcomes, and commits no language-derived meaning.

**Why now / dependencies**

Depends on Task 1’s contracts and aggregate invariants. It precedes outer adapters so agent and Teams work can target proven Core behavior instead of embedding policy at the edge.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Core/Application/Drafts/RequestDraftService.cs`
- `src/GovernedAccess.Core/Application/Drafts/RequestDraftValidator.cs`
- `src/GovernedAccess.Core/Application/RequestFieldRules.cs`
- `src/GovernedAccess.Core/Ports/CorePorts.cs`
- `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- `src/GovernedAccess.Core/Domain/Drafts/RequestIntakeSession.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/ValidatedRequestDetails.cs`
- `tests/GovernedAccess.UnitTests/RequestDraftAndSubmissionServiceTests.cs` (split/rename focused reducer tests as needed)
- `tests/GovernedAccess.UnitTests/RequestPreparationTests.cs`
- authoritative fake implementations in unit-test support

**Required changes**

- Replace full-candidate assessment with a reducer input containing authenticated binding/preparation snapshot, structurally valid `TurnProposal`, execution attribution, and authoritative ports—never latest requester text.
- Execute the exact normative order: validate/convert selection; environment; incident; coherent final environment/client; role against final environment; justification; at most one clarification (environment first); cascades; readiness/lifecycle decision; atomic commit description.
- Validate a clarification selection against preparation, current post-commit candidate version, target, and 1-based bounds; map index to persisted ID; exact-reload; convert to an ordinary environment/role `set`; run the full reducer pipeline. Clear stale context and preserve the candidate if the selected entity is no longer eligible/assignable.
- Implement environment exact/search outcomes: zero reject, unique exact-reload/accept, 2–5 complete ordered non-destructive clarification, 6–20 narrow-query rejection, >20 typed overflow. An active context prevents Ready.
- Implement incident cardinality: with no final environment, zero rejects, exactly one exact-reloads/derives environment and client, many rejects as scope-ambiguous without implicit choices; with final environment, require membership in the current eligible links.
- Treat same-turn environment+incident sets as one coherent scope group. Reject both and dependent role on invalid/ambiguous/conflicting scope while allowing independent justification to commit.
- Validate role only against the final environment. Never auto-select one role. Produce 1–5 ordered role choices, no indexed choice above five, environment clarification precedence, and no queued lower-priority context.
- Preserve existing canonical environment/role/scope during ambiguity. Apply cascades only after accepted material changes: environment clear clears environment/client/role/incident/context; accepted environment change revalidates retained role/incident; incident clear preserves other fields.
- Canonicalize justification with NFC, normalized line endings, and trimmed outer whitespace; preserve requester language/content; reject blank or over 2,000 characters without truncation. Core performs no raw-message comparison, translation, summary, rewrite, or provenance inference.
- Distinguish structural whole-turn failure from data-level affected/dependent rejection and atomic independent partial success. Value-equal normalized operations do not change candidate version.

**Deletions / replacements**

- Delete `AssessCandidateAsync` and `CandidateAssessmentState` snapshot sanitization.
- Remove model-supplied client handling and any “clear rejected field because the full snapshot contained null” behavior.
- Replace candidate-wide `Rejected`/`Incomplete` branching with per-operation verdicts and closed canonical outcomes.
- Replace tests such as `ClarificationReplacesTheCompleteCandidateIncludingNullClearing`, ready-preserving revision clarification, and role validation against a pre-turn snapshot.

**Tests / evidence**

- Directly construct structured proposals; do not use requester-language examples.
- Parameterize the complete structural, sparse equality, environment, incident-cardinality, scope-group, role, justification, selection, partial-success, clarification-precedence, cascade, readiness, and zero-side-effect matrices.
- Include independent justification acceptance while environment/role/incident fails; same-turn role validation against new environment; old context invalidation; selected entity drift; environment-before-role context; one-role clarification; and atomic failure exposing no partial state.
- Assert accepted/rejected categories without retaining raw proposed values in outcome/audit metadata.
- Run the repository backend sequence in order.

**Acceptance criteria IDs**

AC-07–AC-14, AC-18–AC-21, AC-23–AC-28, AC-45.

**Exit gate**

Core unit evidence proves the fixed reducer order, non-destructive clarification, exact selection revalidation, incident cardinality, final-environment role validation, partial-success rules, justification storage bounds, and no requester-text dependency.

**Non-goals**

- No linguistic correctness tests, prompt implementation, MCP transport, persistence races, renderer strings, or card confirmation.
- No requester personal role eligibility policy; current assignability and later human approval remain separate.

### Task 3 — Deliver the four-tool catalog and one shared search policy

**Goal**

Replace the two-tool combined catalog with exactly four closed read-only capabilities and one protocol-neutral deterministic environment matcher reused by MCP and Core authority paths.

**Why now / dependencies**

Depends on Tasks 1–2’s authority projections and reducer outcomes. The agent adapter in Task 4 must consume a stable, verified catalog and shared policy version.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Core/Ports/CorePorts.cs`
- new shared Core search policy/service near `src/GovernedAccess.Core/Application/`
- `src/GovernedAccess.Mcp/McpRegistration.cs`
- `src/GovernedAccess.Mcp/RequestContextTools.cs`
- `src/GovernedAccess.Web/Persistence/EfRequestContextReader.cs`
- `src/GovernedAccess.Web/Persistence/SyntheticDataSeeder.cs`
- `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs` catalog discovery boundary
- `docs/contracts/deterministic-request-intake-mcp-tools.json` as implementation input, not an artifact to edit in this task
- `tests/GovernedAccess.IntegrationTests/Mcp/McpContractTests.cs`
- `tests/GovernedAccess.IntegrationTests/Mcp/McpFailureTests.cs`
- `tests/GovernedAccess.IntegrationTests/Mcp/MafToolBoundaryTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/EfRequestContextReaderTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/SyntheticDataSeederTests.cs`

**Required changes**

- Split authoritative ports into environment search, exact environment/client, environment-scoped assignable roles, exact incident/cardinality, and unchanged principal concerns without leaking MCP DTOs into Core.
- Implement one versioned search component: NFC-normalize query and fields; trim/collapse query whitespace; tokenize on Unicode whitespace/punctuation; use application-owned case-insensitive substring matching; allow cross-field token matches while requiring every token; search only approved fields; eligible active production rows only; ordinal environment-ID ordering.
- Evaluate matching outside SQLite collation semantics. Do not use `NOCASE`, SQL `LIKE`, ranking, scoring, match reasons, pagination, or truncation to decide semantic results.
- Return all 0–20 matches; return typed `environment_query_too_broad` above 20. Let the reducer decide unique/2–5/6–20 behavior from the same service result.
- Register exactly `search_production_environments`, `get_production_environment`, `get_environment_roles`, and `get_incident` with the specified closed schemas and read-only/non-destructive/idempotent/closed-world annotations.
- Keep exact environment output free of roles. Keep role lookup restricted to one exact environment and return an empty role array for a known eligible environment with no assignments.
- Keep MCP incident output at its closed singular projection while the Core authority port independently exposes eligible-link cardinality. Treat all display/title text as untrusted data.
- Preserve Streamable HTTP, safe typed envelopes, cancellation, and safe correlation diagnostics. Record tool name/duration/outcome/count only—not raw queries, arguments, or results.
- Make tool order diagnostic. Enforce allowlist and call bounds in the adapter, but do not reject a safe proposal because a redundant exact lookup was omitted or reorder authority based on call sequence.

**Deletions / replacements**

- Delete combined discovery/exact behavior and embedded roles from `get_production_environment`.
- Delete `ListProductionEnvironmentContextsAsync` as model discovery and replace its all-catalog semantics with the shared query policy.
- Delete `EnvironmentDiscoveryAfterExactLookupGate`; Core authority, not exact-call sequence, decides validity.
- Replace exact two-tool tests and any prompt/test assertion that says roles come from the environment tool.

**Tests / evidence**

- Pure search-policy tests cover NFC equivalence, Unicode punctuation/whitespace tokenization, invariant case-insensitive substring behavior, cross-field matching, approved-field exclusion, eligibility, stable ordinal ordering, 0/1/2–5/6–20/>20 outcomes, and provider-independent behavior.
- Component tests prove MCP and Core expose the same policy version and call the same service implementation.
- Real `/mcp` transport tests cover catalog exactness, annotations, every valid round trip, `null`, missing/blank/overlong/unknown input, malformed output, `NotFound`, empty roles, source independence, timeout/cancellation/unavailable envelopes, and no raw query/result logging.
- Tool-boundary tests cover one call/tool, fifth total, repeat call, seventh provider iteration, unknown/concurrent call, and cumulative cancellation behavior without asserting ceremonial order.
- Run the repository backend sequence in order.

**Acceptance criteria IDs**

AC-15–AC-22, AC-41, AC-43, AC-44.

**Exit gate**

The real MCP server advertises exactly the four target tools; Core and MCP demonstrably share one search implementation/version; exact environment and entitlement failures are independent; no state-changing/unknown capability or SQLite collation can affect canonical scope.

**Non-goals**

- No fifth tool, role search, incident discovery, generic query, MCP resource/prompt, real enterprise integration, endpoint-authentication expansion, or separate MCP service.
- No agent prompt grading or confirmation-time behavior.

### Task 4 — Rebuild the agent boundary, deterministic rendering, and abuse budgets

**Goal**

Make the Web/Teams boundary the sole owner of requester text and model execution, translate at most one repaired provider result into `TurnProposal`, enforce all budgets, and render every response from application-owned closed outcomes.

**Why now / dependencies**

Depends on the stable Core proposal/reducer contracts and verified four-tool catalog from Tasks 1–3. It creates the end-to-end free-text path while leaving lifecycle race correctness to Task 5.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`
- `src/GovernedAccess.Web/Teams/TeamsActorResolver.cs`
- `src/GovernedAccess.Web/Teams/TeamsAccessRequestOptions.cs`
- `src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs`
- `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`
- `src/GovernedAccess.Web/Ai/MafConversationTurnCoordinator.cs`
- `src/GovernedAccess.Web/Ai/RequestPreparationRegistration.cs`
- `src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs`
- `src/GovernedAccess.Web/Ai/RequestPreparationModelOptions.cs`
- `src/GovernedAccess.Web/Ai/DeterministicChatClient.cs`
- `src/GovernedAccess.Web/appsettings.json` and `appsettings.Development.json`
- renderer/localization types introduced under `src/GovernedAccess.Web/Teams/`
- `tests/GovernedAccess.IntegrationTests/Ai/*`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationResetTests.cs`
- `tests/GovernedAccess.IntegrationTests/Hosting/ProgramCompositionTests.cs`
- `tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs`

**Required changes**

- Keep only the exact trimmed case-insensitive `/new` check before agent invocation. After authentication/transport/blank/size checks, every other nonblank message—including numbers, ordinals, IDs, `clear environment`, multilingual/reset/submission wording—must invoke the single agent before semantic handling.
- Move orchestration so Core receives a structured proposal command, not `PrepareAccessRequestCommand.LatestMessage`. The Web agent adapter supplies the latest message only to MAF, then calls Core with the translated proposal and authenticated metadata.
- Replace the complete-candidate schema/prompt with closed `TurnProposal` act/payload compatibility. The output contains no prose field and no client/duration/identity/consequence fields.
- Perform at most one structured-output repair using the same authenticated turn snapshot and remaining shared budget. A second malformed/structurally invalid result yields a safe whole-turn failure with no reducer call.
- Use a fresh turn-scoped agent context containing only latest text, sanitized canonical candidate, lifecycle summary, active persisted choices/display positions, and fixed rules. Remove arbitrary prior-turn MAF history as a correctness input.
- Delimit requester text, persisted requester-authored justification, and MCP display/incident text as untrusted data. Persisted justification must never be inserted into the instruction role or used as evidence that a later proposal is linguistically supported.
- Add deterministic renderers for progress, accepted/rejected categories, environment/role choices, discussion topics, submission intent, unrelated/unclear input, stale collecting, retry/rate/budget failures, terminal outcomes, and ready-card handoff. Never forward provider text, proposal JSON, raw proposed values, or MCP prose.
- Resolve output locale from authenticated Teams/client locale with deterministic `en-US` fallback. Do not infer locale from message text or model output.
- Enforce startup-validated hard bounds: 4,000 characters/message; 50 interpreted turns/active preparation; rolling 20 interpreted turns/requester/10 minutes; one call/tool and four total; six iterations; one repair; one cumulative 30-second model/MCP/repair deadline.
- Reserve/count an interpreted turn using short durable metadata before invocation where required, without holding a transaction during model/MCP work. Permanently exhausted preparation returns `/new` guidance with an explicit draft-loss warning and no agent call; rolling exhaustion returns retry-later.
- If no active preparation exists and the proposal is non-mutating (`unclear`, `unrelated`, `discussDraft`, `requestSubmission`), render guidance without creating an empty preparation.
- Persist accepted material-change attribution only after commit, using changed categories and version metadata. Log correlation, actor, act, categories, duration, tool/source outcome, versions, repair count, and result—never raw message/query/proposal/payload.

**Deletions / replacements**

- Delete the complete-candidate prompt/schema and clarification prose contract.
- Remove process-local MAF history/session reuse (`InMemoryAgentSessionStore` and history-focused `MafConversationTurnCoordinator`) or reduce coordination to a non-authoritative short-lived gate that stores no prior messages.
- Replace deterministic clients that return full candidate snapshots with proposal-script clients.
- Delete `RenderClarification`/`RenderMessageWithEnvironmentChoices` paths that start with model-authored text.
- Remove the current 100-second model/MCP envelope as the interpretation budget; keep any larger HTTP transport timeout only if it wraps and cannot weaken the cumulative 30-second agent budget.

**Tests / evidence**

- Architecture/static checks assert that only the Teams boundary compares exact `/new`, Core APIs contain no raw text, no parser/dictionary/regex/extractor/ordinal fast path exists, no model prose reaches render contracts, and provider/MAF/MCP SDK types stay out of Core.
- Hosted routing tests send exact `/new`, `/new please`, `1`, `first`, exact-looking IDs, clear/reset/submission wording, and multilingual text through scripted agent clients; all but exact `/new` must invoke the agent once.
- Structural/repair tests cover valid first response, malformed then valid repair, second malformed, unknown act/property/tool, and zero mutation.
- Budget tests cover every startup invalid bound, exact thresholds, repeated/fifth tool calls, seventh iteration, shared 30-second cancellation, rolling retry-later, permanent 50-turn exhaustion/no-agent-call, and empty-preparation avoidance.
- Renderer tests prove deterministic locale/fallback, stable closed guidance, stale age/last-update display, no model/MCP text reflection, and no consequential side effects.
- Prompt-injection/logging tests include instruction-like stored justification replay and assert no raw message/query/proposal/model response in logs or persisted metadata.
- Run the repository backend sequence in order.

**Acceptance criteria IDs**

AC-01–AC-07, AC-09, AC-13, AC-14, AC-25, AC-33, AC-41, AC-43, AC-44, AC-47.

**Exit gate**

Every non-`/new` free-text turn demonstrably crosses one bounded agent boundary; only closed proposals reach Core; all requester-visible text is application-owned; budget classes are distinct and startup-validated; persisted justification replay is treated as untrusted.

**Non-goals**

- No deterministic language corpus in Core tests, no second agent, durable transcript/provider session, translation, open-ended chat, response generation, or automatic request creation.
- No final Ready revision/OCC race completion; Task 5 owns those persistence semantics.

### Task 5 — Complete clarification, immutable Ready lifecycle, restart, and OCC

**Goal**

Make aggregate persistence and lifecycle transitions correct across restart and concurrency: post-commit context binding, immutable Ready successors, lazy expiry, stale collecting warnings, active uniqueness races, and stale-proposal rejection.

**Why now / dependencies**

Depends on Tasks 1–4. The agent path can now produce proposals and Core can reduce them; this task makes the load/invoke/commit protocol durable under races and restarts before confirmation is rebuilt.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Core/Application/Drafts/RequestDraftService.cs` or its replacement coordinator/reducer application service
- `src/GovernedAccess.Core/Domain/Drafts/RequestIntakeSession.cs`
- `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- `src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs`
- `src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs`
- Task 1 migration/model snapshot
- `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`
- `src/GovernedAccess.Web/Teams/TeamsDraftCardTracker.cs`
- `tests/GovernedAccess.UnitTests/RequestPreparationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs`
- new active-creation/revision OCC race tests under `tests/GovernedAccess.IntegrationTests/Persistence/`
- restart journeys in `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsConversationResetTests.cs`

**Required changes**

- Implement the short protocol: load active preparation and concurrency version; invoke agent/MCP with no transaction/write lock; reduce against that snapshot; open a short commit; verify concurrency; atomically commit or return stale retry guidance. Never replay the old proposal on the winner state.
- Persist accepted candidate changes, dependency cascades, one optional clarification, lifecycle, versions, attribution, and timestamps in one OCC commit. Bind context to the post-commit candidate version computed in that transaction.
- Make exact `/new` atomically supersede any active `Collecting`/`Ready` and create one clean `Collecting` version-0 preparation with a new identity. It never changes submitted/downstream state.
- For `Collecting`, keep the same preparation for material updates, increment candidate version exactly once, and allow context-only writes without a candidate-version increment.
- For `Ready`, preserve the same row/deadline for discussion, submission intent, unrelated, unclear, value-equal, wholly rejected, model/source failure, and failed commit.
- For an accepted material Ready revision, atomically supersede A and create B with new ID, predecessor A, non-empty creation candidate version 1, and Ready or Collecting status based on the revised candidate.
- For a clarification-producing Ready revision, atomically supersede A and create `Collecting B` with predecessor A, an unchanged copy of A’s candidate, candidate version 1, and the active post-commit-bound context. A’s card becomes stale immediately.
- Make an active clarification prevent Ready. Consume/replace context atomically on valid selection; remove stale/unusable context without altering candidate; preserve renderer numbering from persisted order across restart.
- Enforce `ReadyDeadline = ReadyAt + 30 minutes`, lazy expiry, and no deadline refresh on non-mutating turns/card re-render. Surface collecting age and localized `UpdatedAt` after seven days without automatic expiry.
- Handle active unique-index creation losers by clearing failed tracking state and reloading the winner. Preserve same-requester separate-conversation independence.
- Ensure failed Ready replacement leaves A unchanged. Treat process-local gates/card trackers as presentation/optimization only, never correctness.

**Deletions / replacements**

- Delete `ClarificationRequiredWithActiveDraft`, `PreservesReadyDraft`, and old-card-remains-confirmable revision behavior.
- Remove ephemeral choice ordering and history-based restart behavior.
- Replace generic `DbUpdateException` handling for active-creation uniqueness with typed constraint classification and winner reload.
- Replace current `/new` behavior that leaves no clean preparation.

**Tests / evidence**

- Unit tests cover every lifecycle/version table row from the specification, including all post-commit context-binding worked examples, mandatory predecessor, active-context-prevents-ready, exact deadlines, and stale collecting metadata.
- SQLite tests restart between clarification and selection; verify stored order/index mapping, context-only concurrency changes, material-context commit binding, selected-entity drift, and no raw session/history dependency.
- Controlled races cover concurrent first turns, unique-index loser reload, two proposals from one OCC snapshot, Ready replacement failure, exact `/new` versus turn, different conversations, and no lock/transaction during a deliberately blocked agent/MCP call.
- Teams tests prove old card invalidation on revision clarification and fresh card identity only after successor Ready.
- Run the repository backend sequence in order.

**Acceptance criteria IDs**

AC-12, AC-23–AC-36, AC-40, AC-43, AC-44.

**Exit gate**

Restart and controlled-race evidence proves one active preparation, exact candidate/concurrency versions, post-commit context binding, immutable Ready identity/successors, lazy expiry, stale guidance, and zero database lock across agent/tool latency.

**Non-goals**

- No request creation, confirmation fact-drift correction, approval/provisioning change, distributed lock, background sweeper, automatic collecting expiry, or multi-host coordination.

### Task 6 — Rebuild confirmation, fact-drift correction, and idempotency

**Goal**

Make authenticated Adaptive Card confirmation the single idempotent request-creation boundary with independent authoritative revalidation and explicit changed-fact versus transient-source outcomes.

**Why now / dependencies**

Depends on immutable Ready identities, successor rules, OCC, and unique request-preparation schema from Tasks 1 and 5, plus authoritative ports from Task 3. It is the final consequential path and must be implemented after preparation races are stable.

**Concrete current-system touchpoints**

- `src/GovernedAccess.Core/Application/AccessRequests/RequestSubmissionService.cs`
- `src/GovernedAccess.Core/Application/AccessRequests/AccessRequestValidator.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/AccessRequest.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/Auditing/AuditDetails.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/Auditing/AuditEvent.cs`
- `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- `src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs`
- `src/GovernedAccess.Web/Persistence/EfWorkflowStore.cs`
- `src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs`
- `src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs`
- `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`
- `tests/GovernedAccess.UnitTests/RequestDraftAndSubmissionServiceTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakeConfirmationConcurrencyTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs`
- `tests/GovernedAccess.IntegrationTests/Security/ApiSecurityTests.cs`
- frontend regression tests in `src/GovernedAccess.Web/ClientApp/src/test/`

**Required changes**

- Change the closed card payload to exactly schema version plus `preparationId`. Reject additional/tampered candidate, actor, role, duration, approver, or request fields.
- Render authoritative requester, prominent client/environment/role, exact stored justification, incident or explicit no-incident, exact label `Requested access duration: 8 hours`, and a visually distinct localized `Confirm before` value containing timezone/offset (and UTC where required by current presentation convention).
- On action, derive actor/conversation/locale from authenticated context, conceal foreign ownership, lazy-expire deadline, and accept only an owned current Ready preparation.
- Independently revalidate requester binding, exact eligible environment and derived client, current role assignment, justification storage constraints, and incident active/cardinality compatibility. Do not trust card, model, prior MCP, or render-time lookups.
- Split revalidation outcomes. On transient source failure, create no request, preserve Ready and its deadline, and return `ConfirmationSourceUnavailable`. On changed authoritative fact, create no request and atomically supersede A/create predecessor-linked successor B with deterministic corrections and readiness re-evaluation, returning `ConfirmationRevalidationFailed` plus safe successor identity/status.
- Apply exact changed-fact cascades: invalid environment clears environment/client/role/incident; client drift updates derived client then revalidates role/incident; unavailable role clears role; invalid incident clears optional incident. Never substitute environment or role.
- On successful revalidation, generate one immutable request ID, set `Request.PreparationId`, create request/audit, and mark preparation Submitted in one local commit.
- On duplicate/concurrent confirmation, resolve the unique `Request.PreparationId` winner and return the same request ID/status. Remove dependence on a pre-reserved request ID.
- Make confirmation/revision races converge: confirmation winner creates one immutable request and blocks revision; revision winner makes the old card stale. Failed fact-drift successor creation leaves the original Ready unchanged.
- Include accepted interpretation version/category attribution in request-created audit evidence without raw messages, proposals, or duplicate justification logging.

**Deletions / replacements**

- Delete `InvalidatedCode`, `MarkInvalidated`, and generic “context no longer accepts scope” handling.
- Delete `ReservedRequestId` replay/recovery and `preparedRequestId` payload naming.
- Replace `RecoverSubmittedRequestAsync` with lookup by unique `AccessRequest.PreparationId` after ownership checks.
- Replace card wording `Access lifetime`/`Confirm by` and omission of no-incident/requester with the approved explicit presentation.

**Tests / evidence**

- Unit/component tests cover closed action schema, ownership concealment, exact deadline, valid confirmation, every changed-fact cascade, source unavailability preservation, successor readiness, candidate/predecessor/version behavior, and no request on every failure.
- Controlled races cover sequential replay, concurrent confirmation unique-key loser, confirmation-versus-revision in both orders, fact-drift successor failure, and one stable request identity.
- Teams card tests assert exact payload, prominent authoritative facts, exact justification, incident/no-incident, eight-hour duration, localized deadline/timezone, and distinct stale/expired/foreign/revalidation/source-unavailable/replay responses.
- Security tests assert no browser request-creation path and no over-posted identity/scope/duration/approver influence.
- Run the backend sequence in order, then the frontend suite because immutable request/audit projections and downstream register behavior are affected.

**Acceptance criteria IDs**

AC-05, AC-06, AC-19–AC-22, AC-30–AC-42, AC-43, AC-47.

**Exit gate**

Only authenticated confirmation of one owned unexpired Ready preparation creates a request; fact drift and source outage have distinct atomic outcomes; request replay/races converge through unique `PreparationId`; the card displays the exact reviewed scope and timing.

**Non-goals**

- No change to business/DevOps approval policy, fixed duration, provisioning authorization/idempotency by request ID, retry, grant lifecycle, browser request creation, or real enterprise sources.

### Task 7 — Promote deterministic/live evidence and reconcile current documentation

**Goal**

Replace baseline-shaped tests/evaluation with the approved deterministic matrix and fixed promoted live suite, pass every gate without cherry-picking, then—and only then—promote verified behavior into current as-built documentation.

**Why now / dependencies**

Depends on Tasks 1–6. Documentation must describe observed runtime evidence, and live-model scoring cannot compensate for deterministic failures.

**Concrete current-system touchpoints**

- all affected unit/integration/frontend tests from Tasks 1–6
- `src/GovernedAccess.Web/Evaluation/EvaluationDataset.cs`
- `src/GovernedAccess.Web/Evaluation/Contracts/evaluation-dataset.schema.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/intake-v1.json`
- `src/GovernedAccess.Web/Evaluation/EvaluationScenarioExecutor.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationGrader.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationArtifactWriter.cs`
- `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`
- `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationCommandTests.cs`
- `docs/evaluation/deterministic-request-intake-test-matrix.md`
- post-evidence reconciliation: `docs/governed-production-access-product-baseline.md`, `spec.md`, `docs/architecture.md`, `docs/request-intake-orchestration.md`, `docs/security-model.md`, `docs/testing-strategy.md`, `docs/contracts/mcp-tools.json`, `docs/live-model-evaluation.md`, `docs/local-development.md`, `docs/roadmap.md`, `docs/adr/README.md`, relevant dated ADR clarifications, operator guidance, and `README.md`

**Required changes**

- Ensure deterministic tests construct `TurnProposal` values, not language corpora. Add architecture/static checks and complete negative-path assertions for zero requests/decisions/operations/grants.
- Replace the current 20-scenario/full-candidate dataset contract with a versioned fixed set of 12 named `promotion=true` scenario groups. Each promoted scenario has exactly one predeclared normalized outcome class; variations inside a group all must pass for the group to pass.
- Cover complete/incremental/clear-replace, multilingual clarification selection, changed-environment role behavior, requester-language justification append, translation/style restraint, natural-language reset/submission restraint, requester/MCP/stored-justification injection, ambiguous-scope restraint, and provider/tool failure.
- Add at least the required promoted coverage counts: four reset/submission/injection restraint cases, three clarification cases, three justification-provenance cases including stored-justification replay, and three ambiguous-scope cases.
- Grade absolute gates independently: zero consequential side effects; no unknown/state-changing tools; no model prose; no non-authoritative canonical ID; reset/submission/injection restraint; clarification safety; justification provenance; ambiguous-scope restraint; startup/budget contracts. Any absolute failure blocks promotion.
- Require at least 11 of 12 promoted groups to reach their single expected outcome class; do not use that allowance to waive an absolute failure.
- Record commit SHA, dataset/hash, model deployment/provider version, prompt/schema/MCP/search-policy versions, scenario outcomes, latency, and side-effect counts using safe fields only.
- Delete `FinalApplicationOutcome.ModelResponse` and evaluation artifact rendering of raw model/clarification output. Artifacts retain normalized outcome classes and bounded failure categories only.
- Mark focused/single-scenario developer runs as non-promotable. A promotion artifact is valid only for the entire fixed suite. Permit a complete rerun only after code/prompt/config/model/dataset change or documented external provider/infrastructure incident; retain that reason. No selective rerun can change a failed promotion result.
- Correct lower-authority contradictions identified above only after deterministic and live evidence passes. Use dated ADR clarification where accepted decision wording needs correction; do not rewrite decision history. Replace current MCP contract and current-state docs together.

**Deletions / replacements**

- Delete baseline expectations for complete candidates, model prose/options, 20/20 scenario grading, exact tool-order correctness, Ready-preserving revision clarification, destructive ambiguity, two-tool catalog, and raw model-response diagnostics.
- Retire obsolete history/session tests whose only purpose is model-memory selection; replace them with persisted-context restart evidence.
- Do not recreate `docs/deterministic-request-intake-design.md` or preserve the superseded task plan.

**Tests / evidence**

1. Run architecture/source checks, including the mandatory self-check below.
2. Run the repository backend commands sequentially and in exact `AGENTS.md` order:

   ```powershell
   dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
   dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
   dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
   ```

   Give integration an outer timeout of at least four minutes. If it times out, stop only the runner process tree created by that command before rerunning.
3. Review integration evidence in persistence/component, MCP contract/transport, and full-host order; do not substitute filtered runs for the complete suite.
4. Run `npm test --prefix src/GovernedAccess.Web/ClientApp -- --run`.
5. Run the complete credentialed promoted live suite once deterministic evidence is green. Retain the full result and apply absolute gates before the 11/12 score.
6. Reconcile current-state documents, validate links, and run `git diff --check`. Re-run code suites if documentation examples or contracts change executable behavior.

**Acceptance criteria IDs**

AC-01–AC-47, with primary ownership of AC-45–AC-47 and final traceability closure for all earlier criteria.

**Exit gate**

All deterministic gates and frontend regressions pass; the retained full promoted run passes every absolute gate and at least 11/12 outcome classes; no selective rerun was used; current docs/contracts describe the verified four-tool sparse-proposal runtime; obsolete contradictory guidance is corrected.

**Non-goals**

- No live-provider dependency in automated tests or CI, no safety waiver, no cherry-picked promotion report, no raw prompt/proposal retention, and no production deployment.
- No expansion to real identity/data/provisioning, additional tools, another channel/agent/service, RAG, or distributed infrastructure.

## Mandatory self-check for every implementation review

Reject the increment if source, tests, prompts, migration, or documentation imply any of the following:

- deterministic requester free-text interpretation outside exact `/new`, including numeric/ordinal/ID/clear/reset/submission shortcuts or requester-message evidence checks;
- model-owned canonical snapshots, client/duration/identity mutations, model prose rendering, state-changing tools, or text-created requests;
- roles embedded in exact environment lookup, duplicated search algorithms, SQLite `NOCASE`/`LIKE` semantics, ranking/truncation, or an unprompted exact-ID guess from ambiguity;
- destructive clearing merely because clarification is required, selection that bypasses exact reload/full reduction, implicit incident environment choice from multiple links, role validation against old scope, queued/multiple clarification, or Ready with active context;
- ambiguous candidate/context version binding, optional revision predecessor, read-before-write active uniqueness without the partial index, a transaction/write lock across model/tool latency, or automatic replay of a stale proposal;
- mutable Ready scope, refreshed Ready deadline on non-mutating turns, silent stale-collecting continuation, retry-later treatment of permanent preparation exhaustion, or empty preparation creation for a first non-mutating proposal;
- undefined confirmation fact drift/source outage, confirmation without unique `Request.PreparationId`, or card payload authority beyond schema version plus preparation identity;
- raw message/query/transcript/prompt/reasoning/proposal/tool-payload logging or persistence, trusted treatment of stored justification, selective live-eval reruns, safety waivers, or as-built documentation promotion before evidence.

## Dependency checkpoints

- **After Tasks 1–2:** Core contracts/schema and reducer matrices are stable; no outer adapter can reintroduce full snapshots or requester text.
- **After Tasks 3–4:** The four-tool agent path is bounded and application-rendered; no state-changing capability or model prose exists.
- **After Tasks 5–6:** Restart, OCC, immutable Ready successors, confirmation correction, and idempotency are race-tested end to end.
- **After Task 7:** Deterministic/live evidence and current documentation agree; the feature is ready for human review and promotion, not automatically deployed.

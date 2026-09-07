# Task List: Router-Led Policy Guidance Evolution

- **Plan:** `tasks/plan.md`
- **Status:** In progress; Tasks 1–2 complete; Task 3 not started
- **Budget:** approximately 33 hours

## Target documentation amendment: 2026-09-07

The maintainer authorized only four design refinements, recorded in
[amendment 3.2.0](../docs/constitution-amendment-3.2.0.md) and
[ADR 0015](../docs/adr/0015-refine-router-policy-target-contracts.md).
Verification path 5 applies: documentation/link/consistency validation, no application
or test changes, no code suites, and no implementation-task advancement.
Tasks 1-2 remain complete; Task 3 and later tasks remain unstarted.

- [x] Remove mandatory prose parsing while retaining the small result contract and explicit semantic limits.
- [x] Replace router judge requirements with direct exact route/context checks; preserve v1 outcomes and existing exact gates.
- [x] Define persisted pair ordering, atomic whole-pair bounds/overflow, and complete-pair window selection.
- [x] Require explicit fixture-only rebuilds to remove stale chunk IDs.
- [x] Validate changed relative links, stale references/contradictions, and `git diff --check`.

Validation: all 79 relative links across the 13 changed/new Markdown files resolve;
stale-reference review leaves removed requirements only as explicitly superseded
history or future cleanup. The 12 router IDs/categories/expected outcomes, all ten
policy and four multi-turn manifest rows, retained exact/policy thresholds, and small
`PolicyAdvisorResult` contract are unchanged. Revised estimates sum to 33 hours.
`git diff --check` passes. Pre-existing modified/untracked source and test hashes are
unchanged; no implementation tests or code suites were added or run. No concrete
unresolved design conflict remains; the historical Task 2 probe is intentionally
left for Task 12 cleanup, not silently treated as current target behavior.

Future estimates and dependencies below reflect these refinements; completed-task
evidence and estimates are unchanged. The plan records the 33.5 -> 33 hour adjustment.

## Shared Verification Gates

For every code task, run the task's focused tests first, then this repository gate
sequentially and in this order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. Run
`dotnet restore ProductionAccessRequestAssistant.sln` before the gate whenever package
references change. Documentation-only tasks validate relative links and run
`git diff --check`.

## Task 1: Authorize the bounded routed-assistant architecture

**Description:** Resolve the proposed target's conflict with current governance before
implementation. Record a narrowly scoped constitution amendment, approve the target
specification, and add the three required ADRs for routing/context isolation, policy
grounding, and evaluation/observability. Keep current as-built documents current until
the implementation is proven.

**Acceptance criteria:**

- [x] The approved amendment permits exactly the target classifier, read-only Policy Advisor, bounded route history, and bounded Azure Search RAG while retaining one host, synthetic data, human approval, deterministic authorization, and the exact MCP catalog.
- [x] Three proposed/accepted ADRs record deterministic dispatch, context isolation, ADR 0009's bounded-history impact, policy grounding, evaluation, observability, alternatives, risks, and revisit criteria.
- [x] The spec records maintainer approval plus pre-results promotion thresholds and the chosen runtime policy-validation limits; the 2026-09-07 amendment revises those target details without reopening Task 1 or claiming the feature is live.

**Verification:**

- [x] Every changed relative documentation link resolves.
- [x] `git diff --check` passes.
- [x] Maintainer approval is recorded before Task 2 begins.

**Dependencies:** None.

**Files likely touched:**

- `SPEC-router-policy-evolution.md`
- `docs/constitution-amendment-3.1.0.md`
- `docs/constitution.md`
- `docs/adr/0012-router-and-context-isolation.md`
- `docs/adr/0013-policy-grounding.md`
- `docs/adr/0014-routed-evaluation-and-observability.md`
- `docs/adr/README.md`

**Estimated scope:** Medium, documentation-only, approximately 1.5 hours.

## Task 2: Pin the SDK and route-configuration baseline

**Progress:** Complete (2026-09-07). Verification path 5
(configuration/dependencies), with test-first evidence for the closed configuration
boundary and credential-free SDK compatibility probes. Existing registration,
provider-failure, and execution-limit tests retain ownership of Access Request
invariants. Runtime routing and policy execution remain for subsequent tasks.

**Implementation and evidence:**

- Added only Azure.Search.Documents 11.7.0 and Microsoft.Extensions.AI.Evaluation
  Quality/Reporting 10.7.0. Retained MAF 1.15.0, Microsoft.Extensions.AI/OpenAI adapter
  10.7.0, OpenAI 2.11.0, and all existing MCP packages. Existing dependencies supply
  embeddings and MAF OpenTelemetry.
- Added independent immutable router, policy, retrieval, embedding, and turn options,
  lazy component validation, explicit provider coordinates, closed fields, safe
  diagnostics, and bounded deadlines/output. Access Request retains its existing
  profile, configuration owner, schema, and execution limits.
- Configuration tests own rejection of missing/malformed/excessive limits, unsupported
  settings, invalid provider coordinates, and fallback prevention. Five unsupported
  setting cases failed because no rejection occurred before adding the closed-field
  guard. The positive fixture also reproduced rejection of a valid Azure index name
  before correcting the validator to support documented underscores.
- SDK probes uniquely exercise pre-invocation search with zero internal recent-message
  memory and no tools, safe MAF telemetry, declaration-only Intent Resolution, numeric
  1–5 semantics for the four evaluators required at Task 2 completion, Microsoft JSON reporting, and
  embedding-to-hybrid-query serialization through an offline HTTP transport.
- No pre-existing tests were changed, merged, or removed. The new configuration and
  SDK compatibility suites pass 67 focused cases; the existing Access registration,
  provider failure, execution limit, and interpreter regression filter passes 16.
- Final ordered gate: build passed with zero warnings/errors; 154 unit tests and
  209 integration tests passed (integration duration eight seconds; no outer deadline
  shorter than four minutes). Restore, changed-document relative links, and
  `git diff --check` passed. No frontend contract/behavior changed.
- No specification or scope deviation at Task 2 completion. Retrieval/embedding use ten-second defaults
  bounded to 30 seconds; embedding dimensions require an explicit 1–3,072 selection.
  Intent Resolution is experimental in the required SDK and has a method-scoped
  `AIEVAL001` opt-in, documented in local development guidance. The debugging workflow
  resolved evaluator fixture response-format mismatches using the pinned Microsoft
  source; temporary diagnostic prompt output was removed.
- Subsequent target amendment (2026-09-07): the Intent Resolution probe and its
  experimental opt-in are superseded requirements, not evidence that a router judge
  remains necessary. Source is unchanged in this documentation revision. Task 12
  removes that obsolete probe/declaration/projection while retaining applicable policy
  evaluator/reporting coverage. Task 2 remains complete; no new compatibility gate.
- Remaining gates belong to later tasks: consuming/enforcing these options in routed
  execution, live Azure connectivity, and model-quality/promotion evidence. No live
  model or Azure resource was invoked, and no Git commit was created.

**Description:** Prove the smallest compatible MAF, Microsoft evaluation, Azure Search,
embedding, and OpenTelemetry package set; then add closed server-owned configuration
for router, policy, retrieval, embedding, per-component output limits, and deadlines.
Retain the existing Access Request profile and avoid a generic profile hierarchy.

**Acceptance criteria:**

- [x] A restored/compiled compatibility test proves `TextSearchProvider` in `BeforeAIInvoke` mode with zero internal recent-message memory, MAF OpenTelemetry, required Microsoft evaluators/reporting, Azure hybrid/vector queries, and embeddings.
- [x] Router, Access Request, Policy Advisor, retrieval, embedding, and overall-turn settings are independently validated, bounded by the spec, and fail closed without silently selecting another route/client.
- [x] Existing request-preparation registration and provider-failure behavior remain green; no additional MCP package, tool, endpoint, or deployable project appears.

**Verification:**

- [x] Run `dotnet restore ProductionAccessRequestAssistant.sln`.
- [x] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedAssistantOptions --no-restore`.
- [x] The shared backend gate passes.

**Dependencies:** Task 1.

**Files likely touched:**

- `src/GovernedAccess.Web/GovernedAccess.Web.csproj`
- `src/GovernedAccess.Web/appsettings.json`
- `src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs`
- `src/GovernedAccess.Web/Ai/RoutedAssistantOptions.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RoutedAssistantOptionsTests.cs`

**Estimated scope:** Medium, approximately 2 hours.

## Task 3: Build the closed structured router boundary

**Description:** Add one fresh model-based router invocation over the normalized current
message unchanged after boundary trimming and a provider-neutral compact snapshot. Parse only the fixed schema, validate
route/context compatibility, and expose typed outcomes to deterministic dispatch.

**Acceptance criteria:**

- [ ] The only accepted output is schema version plus `AccessRequest`, `PolicyGuidance`, `Mixed`, `Unclear`, or `Unsupported` and `None`/`ActiveAccessPreparation` in a compatible combination; unknown properties/values and oversized output fail.
- [ ] The router receives no tools, respects the 8-second/100-token caps, creates no provider memory, and invokes the model exactly once with no repair call.
- [ ] Timeout, throttling, cancellation, provider failure, malformed JSON/schema, and incompatible decisions return typed failures that cannot invoke a specialist.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~MafTurnRouter --no-restore`.
- [ ] Router test clients capture an empty tool list and exactly one invocation for all invalid-output cases.
- [ ] The shared backend gate passes.

**Dependencies:** Task 2.

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/Routing/RouterContracts.cs`
- `src/GovernedAccess.Web/Ai/Routing/RouterDecisionJsonTranslator.cs`
- `src/GovernedAccess.Web/Ai/Routing/MafTurnRouter.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RouterDecisionJsonTranslatorTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/MafTurnRouterTests.cs`

**Estimated scope:** Medium, approximately 2.5 hours.

## Task 4: Route Teams text while preserving Access Request behavior

**Description:** Introduce `RoutedTurnCoordinator` at the ordinary-text boundary. Use
an empty recent-history window initially, dispatch `AccessRequest` to the existing
orchestrator with the original message, return application-owned responses for
`Mixed`, `Unclear`, `Unsupported`, and the temporary Policy Guidance placeholder, and
leave all direct protocol paths untouched.

**Acceptance criteria:**

- [ ] Every ordinary nonblank Teams text turn is validated and dispatched by one switch to zero or one specialist; `Mixed`/`Unclear` create no pending workflow, submitted-request status is `Unsupported`, and no route falls through or decomposes/replays work.
- [ ] Access Request receives byte-for-byte the normalized original message and its existing canonical envelope; its outcomes/cards and failure semantics remain unchanged.
- [ ] Blank input, exact `/new`, confirmation cards, approvals, provisioning, browser APIs, and MCP bypass the router and retain existing behavior.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter "FullyQualifiedName~RoutedTurnCoordinator|FullyQualifiedName~TeamsRequestHandler" --no-restore`.
- [ ] The existing Teams/access-intake scenario matrix and exact MCP contract tests remain green.
- [ ] The shared backend gate passes.

**Dependencies:** Task 3.

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `src/GovernedAccess.Web/Teams/TeamsRequestHandler.cs`
- `src/GovernedAccess.Web/Teams/TeamsResponsePresenter.cs`
- `src/GovernedAccess.Web/Ai/PreparationApplicationRegistration.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestHandlerTests.cs`

**Estimated scope:** Medium, approximately 2.5 hours.

## Checkpoint: Router slice after Tasks 1-4

- [ ] Governance authorization and ADR review are complete.
- [ ] Router success/failure matrices prove zero-or-one specialist invocation.
- [ ] `/new`, blank input, confirmation, and MCP bypass tests pass unchanged.
- [ ] The full backend gate passes before policy/history work is integrated.
- [ ] Review the router slice with the maintainer.

## Task 5: Centralize the authoritative access-policy snapshot

**Description:** Add one immutable Core policy snapshot/provider and refactor existing
deterministic enforcement to consume the same facts the Policy Advisor will receive.
Preserve every current rule and public compatibility surface needed by existing tests.

**Acceptance criteria:**

- [ ] The snapshot version exposes exactly eight-hour grant duration, Business then DevOps stages, immutable submitted scope, and requester-independent business-approver selection.
- [ ] Grant expiry and applicable deterministic approval/submission policies consume that source without adding configurable policy switches or changing behavior.
- [ ] Existing access, approval, provisioning, query, and expiry tests pass without changed outcomes.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --filter "FullyQualifiedName~AccessPolicySnapshot|FullyQualifiedName~AccessGrant|FullyQualifiedName~ApprovalDecisionPolicy" --no-restore`.
- [ ] Existing integration tests still observe exactly eight hours and the current approval order.
- [ ] The shared backend gate passes.

**Dependencies:** Task 1.

**Files likely touched:**

- `src/GovernedAccess.Core/Domain/AccessRequests/AccessPolicySnapshot.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/Provisioning/AccessGrant.cs`
- `src/GovernedAccess.Core/Domain/AccessRequests/Approvals/ApprovalDecisionPolicy.cs`
- `src/GovernedAccess.Core/Preparations/PreparationConfirmationService.cs`
- `tests/GovernedAccess.UnitTests/AccessPolicySnapshotTests.cs`

**Estimated scope:** Small, approximately 1.5 hours.

## Task 6: Persist bounded route-tagged history

**Description:** Add provider-neutral routed-message records and a focused Core port,
then implement them in the workflow SQLite database with explicit persisted pair order
and one atomic pair append plus oldest-whole-pair pruning. Do not attach messages to request authorization entities or
store any provider objects/evidence payloads.

**Acceptance criteria:**

- [ ] Each completed `AccessRequest`/`PolicyGuidance` turn contributes one requester/assistant pair, subject to whole-pair overflow omission. Store only boundary-trimmed requester and final validated rendered assistant text, at most 2,000 characters each and six complete pairs (12 messages) per exact authenticated binding; cards use a safe application-owned plain-text projection, never raw JSON.
- [ ] If either message exceeds 2,000 characters, omit the whole pair from reusable history without silent semantic truncation, valid-input rejection, changing the existing 4,000-character Access input limit, or authoritative rollback/replay. Existing safe metadata may indicate omitted continuity without content.
- [ ] Each pair has explicit persisted order per authenticated binding, requester always before assistant. Successful concurrent appends establish durable order; reads/pruning cannot interleave or split pairs. Timestamp plus arbitrary GUID sorting is not conversational order. Pair append and whole-pair pruning are atomic; failed concurrent writes have a typed safe outcome; restart preserves order and bounds.
- [ ] `Mixed`, `Unclear`, `Unsupported`, router/model failures, prompts, reasoning, complete answers before validation, RAG chunks, MCP payloads, and provider history are absent from the store.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedConversationPersistence --no-restore`.
- [ ] Schema/privacy tests are updated to allow only the explicit routed-message table/content while continuing to reject prompt, reasoning, query, proposal, tool-payload, and provider-response storage.
- [ ] The canonical persistence matrix covers equal timestamps with contrary GUID order, requester-first pair reads, concurrent appends/reads, whole-pair pruning to six pairs, requester-only/assistant-only/both oversized messages, exactly 2,000 characters, restart, malformed rows, and unavailable database. Assert no partial storage; Tasks 10-11 own coordinator input/authoritative-state preservation evidence. Then the shared backend gate passes.

**Dependencies:** Task 1.

**Files likely touched:**

- `src/GovernedAccess.Core/Conversations/RoutedConversationMessage.cs`
- `src/GovernedAccess.Core/Ports/RoutedConversationPersistence.cs`
- `src/GovernedAccess.Workflow.Persistence/Persistence/WorkflowEntities.cs`
- `src/GovernedAccess.Workflow.Persistence/Persistence/WorkflowDbContext.cs`
- `src/GovernedAccess.Workflow.Persistence/Adapters/EfRoutedConversationStore.cs`
- `src/GovernedAccess.Workflow.Persistence/WorkflowPersistenceRegistration.cs`
- `src/GovernedAccess.Workflow.Persistence/Persistence/Migrations/<routed-history-migration-set>`
- `tests/GovernedAccess.IntegrationTests/Persistence/RoutedConversationPersistenceTests.cs`

**Estimated scope:** Medium, approximately 3.5 hours (revised for durable pair ordering
and whole-pair overflow). The generated EF migration set is
one mechanical artifact within this single persistence slice.

## Task 7: Enforce route-specific context isolation

**Description:** Build compact router and policy context readers from fresh
application-owned state. Select bounded history deterministically and derive the safe
active-access projection through Core authority ports without changing preparation.

**Acceptance criteria:**

- [ ] Router gets current message separately, the chronological complete-pair cross-route window capped at four messages/about 600 tokens, and only active-preparation presence plus clarification target/safe choice labels.
- [ ] Policy-route filtering occurs before window selection. Policy gets at most four messages/about 800 tokens in complete chronological pairs, the current policy snapshot, and an authoritative `AccessPolicyReference` only when requested; excluded fields never appear.
- [ ] Both windows select the newest contiguous suffix of eligible complete pairs fitting both caps. Stop at the first older non-fitting pair; never skip it for smaller earlier context or split/truncate a pair. Return an empty window when the newest eligible pair cannot fit.
- [ ] Access Request continues to receive no general history, policy context/answer, RAG evidence, or changed canonical envelope, including after restart and route switching.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --filter FullyQualifiedName~RoutedTurnContext --no-restore`.
- [ ] Integration capture tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RouteContextIsolation --no-restore`.
- [ ] The canonical window matrix covers message/token cap boundaries, policy filtering before selection, chronological requester/assistant order, a non-fitting older pair before a smaller earlier pair (no skipping), and an oversized newest pair yielding empty context. Integration captures retain ambiguous references, authority failure, no active preparation, excluded fields, restart, and `/new` bypass/no-history-entry with retained policy history; then the shared backend gate passes.

**Dependencies:** Tasks 4, 5, and 6.

**Files likely touched:**

- `src/GovernedAccess.Core/Conversations/RoutedTurnContextService.cs`
- `src/GovernedAccess.Core/Conversations/RouteContextContracts.cs`
- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `tests/GovernedAccess.UnitTests/RoutedTurnContextServiceTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RouteContextIsolationTests.cs`

**Estimated scope:** Medium, approximately 3 hours (revised for complete-pair contiguous
window selection).

## Task 8: Build the reproducible policy fixture

**Description:** Check in a deliberately small synthetic corpus and deterministic
loader/chunker that produces stable IDs and the exact bounded Azure index projection.
Expose it through an explicit fixture-only rebuild command seam without yet coupling automated
tests to Azure.

**Acceptance criteria:**

- [ ] Eight to ten reviewed synthetic documents cover the specified production-access topics, include one retired version, and carry stable policy area/version/status/effective metadata.
- [ ] Deterministic chunking produces stable `chunkId`/`documentId`, title, heading, content, metadata, topic tags, and vector placeholder/schema inputs across runs and file order.
- [ ] Corpus loading rejects malformed/duplicate/oversized content and treats document text as untrusted data, not instructions or deterministic policy authority.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~PolicyCorpus --no-restore`.
- [ ] Fixture hashes/chunk IDs are reproducible and retired/current versions remain distinguishable.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 1 and 2.

**Files likely touched:**

- `src/GovernedAccess.Web/Policy/Corpus/*`
- `src/GovernedAccess.Web/Policy/PolicyCorpusLoader.cs`
- `src/GovernedAccess.Web/Policy/PolicyChunker.cs`
- `src/GovernedAccess.Web/Policy/PolicyIndexCommand.cs`
- `tests/GovernedAccess.IntegrationTests/Policy/PolicyCorpusTests.cs`

**Estimated scope:** Medium, approximately 2.5 hours.

## Checkpoint: Context foundations after Tasks 5-8

- [ ] The policy snapshot is one source for current deterministic facts and does not change existing behavior.
- [ ] Routed history passes bounds, privacy, concurrency, pruning, and restart tests.
- [ ] Captured route inputs prove context isolation and safe access projection.
- [ ] Corpus content and stable chunks receive maintainer review.
- [ ] The full backend gate passes before Azure retrieval is integrated.

## Task 9: Implement bounded Azure AI Search hybrid retrieval

**Description:** Add the provider-neutral knowledge-search contract and a Web Azure AI
Search/embedding adapter. Wire the explicit operator command to rebuild only the
configured synthetic fixture index from the current checked-in corpus, and wire runtime
search to one filtered BM25+vector hybrid query.

**Acceptance criteria:**

- [ ] The explicit operator command generates embeddings and rebuilds only the configured synthetic fixture index using the existing bounded schema, stable chunk IDs, metadata, `DefaultAzureCredential`, explicit timeout, cancellation, and safe typed failures.
- [ ] Successful rebuild leaves exactly the current checked-in fixture's chunk IDs. Removed documents, deleted chunks, and obsolete IDs are absent and unsearchable. Failed/partial uploads or unverified final contents never report success. No incremental synchronization, background ingestion, aliases, multiple indexes, or generic ingestion platform.
- [ ] Runtime search combines keyword and vector input in one Azure hybrid request, relies on RRF, applies server-owned policy-area/status/effective filters before evidence reaches the model, and returns at most three chunks/about 1,500 tokens.
- [ ] Retrieval input contains only normalized question, bounded recent policy messages, and bounded server-derived access terms; it excludes justification and unrelated request state and never falls back to retired/unfiltered evidence.

**Verification:**

- [ ] Focused adapter/command tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter "FullyQualifiedName~AzurePolicyKnowledgeSearch|FullyQualifiedName~PolicyIndexCommand" --no-restore`.
- [ ] Controlled transport/client tests prove query/vector/filter shape, bounds, cancellation, timeout, throttling, retired exclusion, and no sensitive logging without requiring Azure.
- [ ] The canonical index-command scenario indexes the fixture, removes a document or chunk, rebuilds, and verifies removed IDs are absent and cannot be retrieved, with exactly the current ID set remaining. Controlled failure cases prove partial/failed uploads cannot report rebuild success and mutation is scoped to the configured fixture index.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 2 and 8.

**Files likely touched:**

- `src/GovernedAccess.Core/Ports/PolicyKnowledgeSearch.cs`
- `src/GovernedAccess.Web/Policy/AzureAiPolicyKnowledgeSearch.cs`
- `src/GovernedAccess.Web/Policy/PolicyKnowledgeOptions.cs`
- `src/GovernedAccess.Web/Policy/PolicyIndexCommand.cs`
- `src/GovernedAccess.Web/Policy/PolicyRegistration.cs`
- `tests/GovernedAccess.IntegrationTests/Policy/AzurePolicyKnowledgeSearchTests.cs`
- `tests/GovernedAccess.IntegrationTests/Policy/PolicyIndexCommandTests.cs` (canonical rebuild/exact-ID-set owner)

**Estimated scope:** Medium, approximately 3.5 hours (revised for scoped rebuild and
stale-ID removal verification).

## Task 10: Deliver validated read-only policy answers

**Description:** Implement the Policy Advisor as one fresh MAF agent turn using
`TextSearchProvider` before invocation, zero provider-managed recent-message memory,
no tools, the safe context from Task 7, and fresh evidence from Task 9. Validate its
closed result and render only application-owned safe Teams Markdown before adding the
route to the coordinator.

**Acceptance criteria:**

- [ ] `Answered`, `InsufficientEvidence`, and `Unsupported` obey the existing small free-form closed contract, 2,000-character visible limit, answer/null compatibility, current-invocation citation membership, unknown-field rejection, and safe application-owned rendering. Snapshot precedence over retrieved explanation is explicit in context/prompt construction.
- [ ] Runtime validation does not claim arbitrary-prose correctness or consistency with every policy fact; offline Groundedness/Relevance measures risk without guaranteeing live answers. No runtime contradiction parser, replacement verification model/parser, mandatory structured claims, or templating subsystem is introduced.
- [ ] Policy invocation receives the exact bounded policy history/snapshot/optional access projection/current evidence, uses `BeforeAIInvoke` with `RecentMessageMemoryLimit = 0`, exposes no tools, and performs no second verification/repair model call.
- [ ] The coordinator replaces the placeholder with validated application-rendered responses; completed Access/Policy turns append a complete pair subject to Task 6's whole-pair overflow omission. Non-executable routes and failed turns do not append; history omission/failure cannot reject valid input or roll back/replay authoritative state.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter "FullyQualifiedName~MafPolicyAdvisor|FullyQualifiedName~PolicyAdvisorResult|FullyQualifiedName~PolicyGuidance" --no-restore`.
- [ ] Existing canonical matrices cover citation forgery, malformed/oversized output, HTML/card/link attempts, adversarial chunks, snapshot precedence in captured inputs, no tools, insufficient evidence, unsupported questions, and zero preparation/request side effects. Do not add prose-parser tests or claim deterministic clients prove arbitrary live-answer semantics.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 5, 7, and 9; the policy-validation target is resolved by the
2026-09-07 amendment, not an additional approval/implementation gate.

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/Policy/PolicyAdvisorContracts.cs`
- `src/GovernedAccess.Web/Ai/Policy/PolicyAdvisorJsonTranslator.cs`
- `src/GovernedAccess.Web/Ai/Policy/MafPolicyAdvisor.cs`
- `src/GovernedAccess.Web/Teams/TeamsResponsePresenter.cs`
- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/MafPolicyAdvisorTests.cs`

**Estimated scope:** Medium, approximately 2.5 hours (revised to remove prose-parser
implementation and its test matrices).

## Task 11: Harden routed failures and telemetry

**Description:** Add one application parent activity per routed turn, enable standard
MAF/model OpenTelemetry with sensitive data disabled, add the small route/retrieval/
history attributes required by the spec, and complete failure/concurrency behavior
across router, retrieval, both specialists, and history persistence.

**Acceptance criteria:**

- [ ] Evidence separately records route/context reference, model/deployment and prompt/schema versions, router and specialist input/output tokens/durations, retrieval duration/chunk count, selected history count, end-to-end duration, outcome, and safe failure counters.
- [ ] Router failure invokes no specialist; retrieval failure invokes no Policy Advisor; specialist failure never falls through; Policy Guidance never mutates access state; history failure never rolls back/replays authoritative access state.
- [ ] Logs/spans exclude raw prompts/messages/answers, retrieved text, search queries, justification, complete tool payloads, credentials, and provider objects by default.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter "FullyQualifiedName~RoutedTurnTelemetry|FullyQualifiedName~RoutedTurnFailure" --no-restore`.
- [ ] Cross-route failure tests assert both safe response and zero unauthorized preparation/request/decision/operation/grant side effects. Coordinator history-omission/write-failure cases assert valid input and committed Access results remain unchanged, with no rollback/replay or content logging; do not duplicate the persistence/window matrices.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 4 and 10.

**Files likely touched:**

- `src/GovernedAccess.Web/Observability/RoutedTurnTelemetry.cs`
- `src/GovernedAccess.Web/Ai/ModelCallLoggingChatClient.cs`
- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `tests/GovernedAccess.IntegrationTests/Observability/RoutedTurnTelemetryTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RoutedTurnFailureTests.cs`

**Estimated scope:** Medium, approximately 2 hours.

## Checkpoint: Routed assistant after Tasks 9-11

- [ ] Required single-turn and multi-turn examples pass through the real coordinator with deterministic test clients.
- [ ] Tool/context/side-effect isolation matrices are green.
- [ ] Retrieval and policy failure matrices fail closed without fallback.
- [ ] Telemetry has required safe dimensions and no content leaks.
- [ ] The full backend gate passes before evaluation promotion work.

## Task 12: Add Microsoft evaluation suites

**Description:** Extend the existing isolated evaluation command/hosting with focused
router, policy, and multi-turn datasets. Use Microsoft evaluation abstractions,
reporting, and applicable built-in policy evaluators. Compare router route/context
directly against expected outcomes without a model judge; retain exact product-specific
checks without introducing a generic evaluation framework or another router metric.

**Acceptance criteria:**

- [ ] Versioned datasets implement the approved immutable v1 manifest: exactly 12 router, 10 policy, and four multi-turn IDs with their fixed semantic categories, route/outcome expectations, metric applicability, exact source/state/isolation expectations, and no duplicate weighting.
- [ ] Router cases use direct exact expected route/context checks within Microsoft evaluation/reporting, with no judge, synthetic function declarations, decision-to-call adapter, projection-equivalence tests, related compatibility gate, or extra mandatory router metric. Preserve the existing 95% exact threshold and mandatory exact outcomes for `Mixed`, `Unclear`, `Unsupported`, submitted-status, and ambiguous-reference cases.
- [ ] Preserve v1 case IDs, semantic categories, and expected outcomes; apply the approved 2026-09-07 router metric-map amendment (exact-only/no judge). Policy Retrieval, Groundedness, and Relevance remain dataset-declared with existing thresholds and Microsoft reporting/storage; exact safety/isolation gates remain 100% blocking.
- [ ] Normal deterministic tests run once; every live router/policy case and complete multi-turn conversation runs exactly three promotion repetitions with fresh uncached calls only for route/metric-applicable components and no forbidden component call, all 12 complete multi-turn repetitions pass, and reports retain source revision, datasets/hashes, metric applicability, repetition plan, package/evaluator/model/deployment, corpus/index version, tokens, component latency, thresholds, and promotion eligibility.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedAssistantEvaluation --no-restore`.
- [ ] Existing access-intake evaluation tests and historical artifact readers remain green and the default/explicit suite behavior is documented by command tests.
- [ ] Direct exact route/context reporting and metric applicability are verified without router judge calls. Remove Task 2's now-obsolete Intent Resolution compatibility probe, synthetic declaration/projection, assertion, and local experimental opt-in; preserve its policy Retrieval/Groundedness/Relevance and Microsoft reporting coverage. Runtime tool absence remains owned by the router capability suite, not a projection-equivalence test.
- [ ] The shared backend gate passes; no automated test calls a live model, Azure Search, or Foundry evaluator.

**Dependencies:** Task 11 and the pre-recorded thresholds as amended on 2026-09-07.
Task 2's applicable policy/reporting compatibility baseline remains valid; router
function-projection compatibility is no longer a dependency.

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/Datasets/router-v1.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/policy-guidance-v1.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/routed-conversations-v1.json`
- `src/GovernedAccess.Web/Evaluation/RoutedAssistantEvaluationRunner.cs`
- `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationHosting.cs`
- `tests/GovernedAccess.IntegrationTests/Evaluation/RoutedAssistantEvaluationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RoutedAssistantOptionsSdkCompatibilityTests.cs` (remove only superseded router-judge probe)
- `docs/local-development.md` (reconcile that probe's historical note after removal)

**Estimated scope:** Medium, approximately 2.5 hours (revised for direct exact router
checks and scoped obsolete-probe cleanup). Dataset files are one bounded
evaluation inventory, not separate feature slices.

## Task 13: Reconcile governing and as-built documentation

**Description:** After deterministic gates and accepted ADRs match the runtime, promote
the current product, architecture, security, intake, and ADR documentation. Describe
the implemented boundaries in timeless language and explicitly retain the unchanged
access authorization path and exact MCP contract.

**Acceptance criteria:**

- [ ] Product baseline and constitution state the routed product rule and exact bounded scope without authorizing generic multi-agent orchestration, generic RAG, real data, or state-changing model capabilities.
- [ ] Architecture, security model, and intake orchestration accurately describe direct-action bypass, deterministic dispatch, route-specific context/tools, bounded history/privacy, Azure retrieval, failure behavior, and policy read-only guarantees.
- [ ] ADR statuses/index and ADR 0009 relationship match the final implementation; `spec.md` routes future work to all new authoritative artifacts.

**Verification:**

- [ ] Every changed relative link resolves and current/as-built claims are checked against source/tests.
- [ ] `git diff --check` passes.
- [ ] The shared backend gate is rerun because governing examples/contracts changed.

**Dependencies:** Task 12 and maintainer approval of implemented decisions.

**Files likely touched:**

- `docs/governed-production-access-product-baseline.md`
- `docs/architecture.md`
- `docs/security-model.md`
- `docs/request-intake-orchestration.md`
- `docs/constitution.md`
- `docs/adr/README.md` and ADRs 0012-0015
- `spec.md`

**Estimated scope:** Medium, approximately 1.5 hours.

## Task 14: Publish operator guidance and retained promotion evidence

**Description:** Document configuration, explicit fixture-only index rebuilds, local operation, failure
diagnosis, evaluation suites, and reset/rollback. Run one clean-source full routed
evaluation with required repetitions, review it, and retain only the approved synthetic
report/result and index entry.

**Acceptance criteria:**

- [ ] README, local/Teams guidance, testing strategy, and live-evaluation guide describe current route behavior, required Azure resources/roles, commands, deadlines, history/privacy limits, index versioning, and no-live-dependency automated gates.
- [ ] A clean-source full-inventory live run meets every pre-recorded threshold and 100% exact safety/isolation gate, records all required provenance/token/latency fields, and does not claim latency/token improvement without evidence.
- [ ] Reviewed synthetic report/result are retained and indexed; failed/diagnostic/generated runs, credentials, raw prompts, complete answers/chunks, and complete provider/MCP payloads remain uncommitted.

**Verification:**

- [ ] Run the full backend gate in the mandated order and explicitly run the existing exact MCP contract tests.
- [ ] Execute the documented fixture-only rebuild command and full routed evaluation from a clean commit with authorized Azure/Foundry credentials; verify exact current chunk IDs, exactly three fresh uncached repetitions for every approved live case, and 100% complete multi-turn success.
- [ ] Validate documentation links, retained artifact provenance/hashes, and `git diff --check`; run the frontend suite only if an implementation unexpectedly changed frontend behavior/contracts.

**Dependencies:** Tasks 12 and 13; external Azure Search, embedding, router, policy, and judge deployments plus authorized operator credentials.

**Files likely touched:**

- `README.md`
- `docs/local-development.md`
- `docs/teams-quickstart.md`
- `docs/testing-strategy.md`
- `docs/live-model-evaluation.md`
- `docs/evaluation/runs/README.md`
- `docs/evaluation/runs/<approved-routed-run>/report.md`
- `docs/evaluation/runs/<approved-routed-run>/result.json`

**Estimated scope:** Medium, approximately 2 hours plus external provider execution time.

## Checkpoint: Complete after Tasks 12-14

- [ ] All task acceptance criteria are satisfied.
- [ ] The full backend gate passes sequentially with the required outer timeout.
- [ ] Exact MCP catalog and unchanged access/approval/provisioning evidence remain green.
- [ ] Routed evaluation thresholds and exact safety gates pass on clean retained evidence.
- [ ] Documentation and runtime agree; no out-of-scope service, UI, tool, memory, ingestion, or workflow abstraction was introduced.
- [ ] The maintainer has reviewed and approved the implementation and evidence before merge or deployment.

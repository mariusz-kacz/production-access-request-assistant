# Task List: Router-Led Policy Guidance Evolution

- **Plan:** `tasks/plan.md`
- **Status:** Awaiting maintainer review
- **Budget:** approximately 33.5 hours

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

- [ ] The approved amendment permits exactly the target classifier, read-only Policy Advisor, bounded route history, and bounded Azure Search RAG while retaining one host, synthetic data, human approval, deterministic authorization, and the exact MCP catalog.
- [ ] Three proposed/accepted ADRs record deterministic dispatch, context isolation, ADR 0009's bounded-history impact, policy grounding, evaluation, observability, alternatives, risks, and revisit criteria.
- [ ] The spec records maintainer approval plus pre-results promotion thresholds and the chosen runtime policy-consistency interpretation; no as-built artifact claims the feature is already live.

**Verification:**

- [ ] Every changed relative documentation link resolves.
- [ ] `git diff --check` passes.
- [ ] Maintainer approval is recorded before Task 2 begins.

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

**Description:** Prove the smallest compatible MAF, Microsoft evaluation, Azure Search,
embedding, and OpenTelemetry package set; then add closed server-owned configuration
for router, policy, retrieval, embedding, per-component output limits, and deadlines.
Retain the existing Access Request profile and avoid a generic profile hierarchy.

**Acceptance criteria:**

- [ ] A restored/compiled compatibility test proves `TextSearchProvider` in `BeforeAIInvoke` mode with zero internal recent-message memory, MAF OpenTelemetry, required Microsoft evaluators/reporting, Azure hybrid/vector queries, and embeddings.
- [ ] Router, Access Request, Policy Advisor, retrieval, embedding, and overall-turn settings are independently validated, bounded by the spec, and fail closed without silently selecting another route/client.
- [ ] Existing request-preparation registration and provider-failure behavior remain green; no additional MCP package, tool, endpoint, or deployable project appears.

**Verification:**

- [ ] Run `dotnet restore ProductionAccessRequestAssistant.sln`.
- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedAssistantOptions --no-restore`.
- [ ] The shared backend gate passes.

**Dependencies:** Task 1.

**Files likely touched:**

- `src/GovernedAccess.Web/GovernedAccess.Web.csproj`
- `src/GovernedAccess.Web/appsettings.json`
- `src/GovernedAccess.Web/Ai/RequestPreparationChatRegistration.cs`
- `src/GovernedAccess.Web/Ai/RoutedAssistantOptions.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RoutedAssistantOptionsTests.cs`

**Estimated scope:** Medium, approximately 2 hours.

## Task 3: Build the closed structured router boundary

**Description:** Add one fresh model-based router invocation over the original current
message and a provider-neutral compact snapshot. Parse only the fixed schema, validate
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
then implement them in the workflow SQLite database with one atomic pair append and
oldest-first pruning. Do not attach messages to request authorization entities or
store any provider objects/evidence payloads.

**Acceptance criteria:**

- [ ] Only normalized requester and final validated rendered assistant messages for completed `AccessRequest`/`PolicyGuidance` routes can be stored, at no more than 2,000 characters each and 12 messages per exact authenticated binding; card responses use a bounded plain-text projection, never raw card JSON.
- [ ] Pair append plus pruning is atomic, ordering is stable by timestamp/message ID, concurrent writes have a typed safe outcome, and history survives restart.
- [ ] `Mixed`, `Unclear`, `Unsupported`, router/model failures, prompts, reasoning, complete answers before validation, RAG chunks, MCP payloads, and provider history are absent from the store.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedConversationPersistence --no-restore`.
- [ ] Schema/privacy tests are updated to allow only the explicit routed-message table/content while continuing to reject prompt, reasoning, query, proposal, tool-payload, and provider-response storage.
- [ ] Restart, pruning, malformed-row, unavailable-database, and concurrency scenarios pass; then the shared backend gate passes.

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

**Estimated scope:** Medium, approximately 3 hours. The generated EF migration set is
one mechanical artifact within this single persistence slice.

## Task 7: Enforce route-specific context isolation

**Description:** Build compact router and policy context readers from fresh
application-owned state. Select bounded history deterministically and derive the safe
active-access projection through Core authority ports without changing preparation.

**Acceptance criteria:**

- [ ] Router gets current message separately, at most four most-recent cross-route messages/about 600 tokens, and only active-preparation presence plus clarification target/safe choice labels.
- [ ] Policy gets at most four policy-only messages/about 800 tokens, the current policy snapshot, and an authoritative `AccessPolicyReference` only when requested; excluded fields never appear.
- [ ] Access Request continues to receive no general history, policy context/answer, RAG evidence, or changed canonical envelope, including after restart and route switching.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --filter FullyQualifiedName~RoutedTurnContext --no-restore`.
- [ ] Integration capture tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RouteContextIsolation --no-restore`.
- [ ] Tests cover count/token truncation, policy-only filtering, ambiguous references, authority failure, no active preparation, and excluded sensitive fields; then the shared backend gate passes.

**Dependencies:** Tasks 4, 5, and 6.

**Files likely touched:**

- `src/GovernedAccess.Core/Conversations/RoutedTurnContextService.cs`
- `src/GovernedAccess.Core/Conversations/RouteContextContracts.cs`
- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `tests/GovernedAccess.UnitTests/RoutedTurnContextServiceTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/RouteContextIsolationTests.cs`

**Estimated scope:** Medium, approximately 2.5 hours.

## Task 8: Build the reproducible policy fixture

**Description:** Check in a deliberately small synthetic corpus and deterministic
loader/chunker that produces stable IDs and the exact bounded Azure index projection.
Expose it through an explicit indexing command seam without yet coupling automated
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
Search/embedding adapter. Wire the fixture command to create/update one index and upload
stable chunks, and wire runtime search to one filtered BM25+vector hybrid query.

**Acceptance criteria:**

- [ ] The index command generates embeddings, creates/updates only the named bounded schema, and uploads the checked-in fixture with `DefaultAzureCredential`, explicit timeout, cancellation, and safe typed failures.
- [ ] Runtime search combines keyword and vector input in one Azure hybrid request, relies on RRF, applies server-owned policy-area/status/effective filters before evidence reaches the model, and returns at most three chunks/about 1,500 tokens.
- [ ] Retrieval input contains only normalized question, bounded recent policy messages, and bounded server-derived access terms; it excludes justification and unrelated request state and never falls back to retired/unfiltered evidence.

**Verification:**

- [ ] Focused adapter tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~AzurePolicyKnowledgeSearch --no-restore`.
- [ ] Controlled transport/client tests prove query/vector/filter shape, bounds, cancellation, timeout, throttling, retired exclusion, and no sensitive logging without requiring Azure.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 2 and 8.

**Files likely touched:**

- `src/GovernedAccess.Core/Ports/PolicyKnowledgeSearch.cs`
- `src/GovernedAccess.Web/Policy/AzureAiPolicyKnowledgeSearch.cs`
- `src/GovernedAccess.Web/Policy/PolicyKnowledgeOptions.cs`
- `src/GovernedAccess.Web/Policy/PolicyIndexCommand.cs`
- `src/GovernedAccess.Web/Policy/PolicyRegistration.cs`
- `tests/GovernedAccess.IntegrationTests/Policy/AzurePolicyKnowledgeSearchTests.cs`

**Estimated scope:** Medium, approximately 3 hours.

## Task 10: Deliver validated read-only policy answers

**Description:** Implement the Policy Advisor as one fresh MAF agent turn using
`TextSearchProvider` before invocation, zero provider-managed recent-message memory,
no tools, the safe context from Task 7, and fresh evidence from Task 9. Validate its
closed result and render only application-owned safe Teams Markdown before adding the
route to the coordinator.

**Acceptance criteria:**

- [ ] `Answered`, `InsufficientEvidence`, and `Unsupported` obey the closed schema, 2,000-character visible limit, answer/null compatibility, current citation membership, unknown-field rejection, and the Task 1 policy-consistency decision.
- [ ] Policy invocation receives the exact bounded policy history/snapshot/optional access projection/current evidence, uses `BeforeAIInvoke` with `RecentMessageMemoryLimit = 0`, exposes no tools, and performs no second verification/repair model call.
- [ ] The coordinator replaces the placeholder with validated application-rendered responses; successful Access/Policy turns append history, while non-executable routes and failed turns do not.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter "FullyQualifiedName~MafPolicyAdvisor|FullyQualifiedName~PolicyAdvisorResult|FullyQualifiedName~PolicyGuidance" --no-restore`.
- [ ] Tests cover citation forgery, malformed/oversized output, HTML/card/link attempts, adversarial chunks, no tools, insufficient evidence, unsupported questions, and zero preparation/request side effects.
- [ ] The shared backend gate passes.

**Dependencies:** Tasks 5, 7, and 9, plus resolution of Task 1's policy-consistency decision.

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/Policy/PolicyAdvisorContracts.cs`
- `src/GovernedAccess.Web/Ai/Policy/PolicyAdvisorJsonTranslator.cs`
- `src/GovernedAccess.Web/Ai/Policy/MafPolicyAdvisor.cs`
- `src/GovernedAccess.Web/Teams/TeamsResponsePresenter.cs`
- `src/GovernedAccess.Web/Ai/Routing/RoutedTurnCoordinator.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/MafPolicyAdvisorTests.cs`

**Estimated scope:** Medium, approximately 3.5 hours.

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
- [ ] Cross-route failure tests assert both safe response and zero unauthorized preparation/request/decision/operation/grant side effects.
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
built-in evaluators, and reporting for semantic metrics; retain only the exact
product-specific checks that generic evaluators cannot know.

**Acceptance criteria:**

- [ ] Versioned datasets contain approximately 12 router cases, 8-10 policy cases, and 3-4 multi-turn conversations with exact route/context, source, and safety expectations.
- [ ] The runner uses Intent Resolution, Retrieval, Groundedness, and Relevance evaluators as applicable, Microsoft reporting/storage, and custom checks only for context/tool isolation, current citations, retired exclusion, and zero side effects.
- [ ] Normal deterministic tests run once; configured promotion cases run at least three independent repetitions and reports retain source revision, dataset/package/evaluator/model/deployment, corpus/index version, tokens, component latency, thresholds, and promotion eligibility.

**Verification:**

- [ ] Focused tests pass: `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --filter FullyQualifiedName~RoutedAssistantEvaluation --no-restore`.
- [ ] Existing access-intake evaluation tests and historical artifact readers remain green and the default/explicit suite behavior is documented by command tests.
- [ ] The shared backend gate passes; no automated test calls a live model, Azure Search, or Foundry evaluator.

**Dependencies:** Task 11 and the pre-recorded thresholds from Task 1.

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/Datasets/router-v1.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/policy-guidance-v1.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/routed-conversations-v1.json`
- `src/GovernedAccess.Web/Evaluation/RoutedAssistantEvaluationRunner.cs`
- `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationHosting.cs`
- `tests/GovernedAccess.IntegrationTests/Evaluation/RoutedAssistantEvaluationTests.cs`

**Estimated scope:** Medium, approximately 3.5 hours. Dataset files are one bounded
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
- `docs/adr/README.md` and ADRs 0012-0014
- `spec.md`

**Estimated scope:** Medium, approximately 1.5 hours.

## Task 14: Publish operator guidance and retained promotion evidence

**Description:** Document configuration, fixture indexing, local operation, failure
diagnosis, evaluation suites, and reset/rollback. Run one clean-source full routed
evaluation with required repetitions, review it, and retain only the approved synthetic
report/result and index entry.

**Acceptance criteria:**

- [ ] README, local/Teams guidance, testing strategy, and live-evaluation guide describe current route behavior, required Azure resources/roles, commands, deadlines, history/privacy limits, index versioning, and no-live-dependency automated gates.
- [ ] A clean-source full-inventory live run meets every pre-recorded threshold and 100% exact safety/isolation gate, records all required provenance/token/latency fields, and does not claim latency/token improvement without evidence.
- [ ] Reviewed synthetic report/result are retained and indexed; failed/diagnostic/generated runs, credentials, raw prompts, complete answers/chunks, and complete provider/MCP payloads remain uncommitted.

**Verification:**

- [ ] Run the full backend gate in the mandated order and explicitly run the existing exact MCP contract tests.
- [ ] Execute the documented fixture-index command and full routed evaluation from a clean commit with authorized Azure/Foundry credentials; verify at least three repetitions for important cases.
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

# Task List: Conversational Request Submission

This checklist implements the plan in [`tasks/plan.md`](plan.md). Tasks are ordered by
dependency and should remain individually reviewable. Every code task must satisfy
[`AGENTS.md`](../AGENTS.md) and the
[`docs/testing-strategy.md`](../docs/testing-strategy.md) gates in addition to its
task-specific criteria.

## Task 0: Approve the specification and consequential-capability ADR

**Description:** Resolve authority before implementation. Obtain decision-owner
approval of the conversational-submission specification and draft the focused ADR for
the one local model-visible submission function. The ADR must determine whether an
untrusted model-selected signal can initiate deterministic request submission without
making model output or conversation text authorization evidence. If that conclusion
conflicts with the constitution or current baseline authority, stop and obtain the
required amendment rather than treating implementation as implicit approval.

**Acceptance criteria:**

- [ ] The specification status and decision record explicitly authorize implementation
      of the current synthetic-MVP increment.
- [ ] A proposed ADR records the empty server-bound function contract, two-phase
      assessment/action separation, residual model-classification risk, rejected
      alternatives, constitution analysis, and revisit criteria.
- [ ] A human decision owner authorizes Task 1 to begin; any required constitution or
      baseline amendment is approved first rather than deferred to as-built docs.

**Verification:**

- [ ] Links in the specification and proposed ADR resolve.
- [ ] `git diff --check` passes for the governance change.
- [ ] Manual authority-order review covers the constitution, approved specification,
      current baseline, proposed ADR, and unchanged as-built documentation status.

**Dependencies:** None

**Files likely touched:**

- `SPEC-conversational-request-submission.md`
- `docs/adr/0007-use-server-bound-conversational-submission-function.md` (new)
- `docs/adr/README.md`

**Estimated scope:** Medium (3 files)

## Task 1: Define exact presentation eligibility and durable recovery contracts

**Description:** Introduce a provider-neutral application conversation contract that
can distinguish an exact ready snapshot, a successfully acknowledged presentation,
and a previously submitted terminal intake. Keep presentation acknowledgement
process-local and keyed by the complete authenticated actor/conversation plus exact
preparation and reserved request identity. Extend focused store queries only as needed
to reload exact ready/submitted evidence; do not add a persistence column or choose an
arbitrary ready scope at invocation time.

**Acceptance criteria:**

- [ ] A capability binding can be constructed only for the same authenticated actor,
      conversation, exact ready preparation, reserved request ID, and unexpired
      application-acknowledged presentation.
- [ ] Missing/mismatched acknowledgement, restart-equivalent empty state, collecting,
      expired, superseded, invalidated, or foreign intake state yields an ineligible
      typed outcome before capability entry.
- [ ] Exact submitted tombstone/request evidence can be recovered for truthful replay
      without creating or mutating another intake; no schema migration is introduced.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Focused Core tests pass:
      `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore --filter FullyQualifiedName~RequestPreparation`
- [ ] Focused persistence tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~RequestIntakePersistence`

**Dependencies:** Task 0

**Files likely touched:**

- `src/GovernedAccess.Core/Ports/RequestIntake.cs`
- `src/GovernedAccess.Web/Ai/PresentedRequestSnapshotStore.cs` (new)
- `src/GovernedAccess.Web/Ai/ConversationalRequestTurn.cs` (new)
- `src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs`
- `tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakePersistenceTests.cs`

**Estimated scope:** Medium (5 files)

## Task 2A: Add non-mutating turn assessment and the Core revision gate

**Description:** Extend the existing preparation interpretation contract with an
untrusted action assessment while keeping this phase strictly non-mutating. It may use
only the existing two read-only MCP tools and cannot receive the submission function.
Core applies the candidate and existing discussion/revision/readiness rules first. A
changed, incomplete, rejected, stale, ambiguous, mixed, or discussion outcome ends the
turn without creating an action phase. Only a pure later submission assessment that
leaves the exact currently presented ready snapshot unchanged may request Task 2B.

**Acceptance criteria:**

- [ ] The assessment phase exposes exactly the existing two MCP tools, has no local
      submission function, and cannot enter `ConfirmDraftAsync` for any model output.
- [ ] Core applies candidate and revision rules before action eligibility is returned;
      revision-plus-submit validates and presents the revision only, with no old or new
      snapshot submission attempt.
- [ ] Refusal, postponement, negation, questions, quotations, hypotheticals, vague
      acknowledgements, ambiguity, discussion, premature/stale state, and prompt
      injection end without creating an action phase in deterministic scripted tests.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Focused Core assessment/revision tests pass:
      `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore --filter FullyQualifiedName~RequestDraftAndSubmission`
- [ ] MAF assessment boundary tests prove the submission function is absent and the
      exact MCP catalog and preparation schema constraints remain enforced.

**Dependencies:** Task 1

**Files likely touched:**

- `src/GovernedAccess.Core/Ports/RequestDrafting.cs`
- `src/GovernedAccess.Core/Application/Drafts/RequestDraftService.cs`
- `src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs`
- `tests/GovernedAccess.UnitTests/RequestDraftAndSubmissionServiceTests.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/MafTurnAssessmentTests.cs` (new)

**Estimated scope:** Medium (5 files)

## Task 2B: Add the one-shot no-argument MAF submission action phase

**Description:** After Task 2A completes and Core confirms that the exact presented
snapshot remains unchanged and structurally eligible, run a separately bounded action
phase with the same MAF agent. Register only the local
`submit_current_request_for_business_approval` function needed for that action. Bind
all trusted values outside the model schema, guard the function for one use, and
terminate the action phase with an application-owned result immediately after its
first typed function outcome. Do not reuse the generic six-iteration preparation tool
loop for this consequential phase.

**Acceptance criteria:**

- [ ] The model-visible function name is exact, its JSON parameter object is empty,
      unknown/non-empty calls fail closed, and the bound invocation delegates at most
      once to `ConfirmDraftAsync` with server actor, exact preparation, and correlation.
- [ ] One action phase can produce at most one observed capability entry and terminates
      after that result; repeated calls, extra model iterations, and stale queued turns
      cannot create a second entry or `AlreadySubmitted` recovery in the same phase.
- [ ] `Submitted`/`AlreadySubmitted` remains observable even if later model output is
      malformed or times out/cancels, or session saving fails; an application-owned
      result never depends on the model reconstructing workflow truth.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Focused action-phase tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~MafConversationalSubmission`
- [ ] Component tests script repeated function calls and additional tool iterations and
      prove one boundary entry, one delegate call, immediate termination, and unchanged
      two-tool MCP registration outside the action phase.

**Dependencies:** Tasks 1 and 2A

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/MafConversationTurnCoordinator.cs`
- `src/GovernedAccess.Web/Ai/RequestSubmissionActionRegistration.cs` (new)
- `src/GovernedAccess.Web/Ai/RequestSubmissionFunction.cs` (new)
- `src/GovernedAccess.Web/Ai/ConversationalRequestTurn.cs`
- `tests/GovernedAccess.IntegrationTests/Ai/MafConversationalSubmissionTests.cs` (new)

**Estimated scope:** Medium (5 files)

## Checkpoint: Trusted Boundary (after Tasks 1, 2A, and 2B)

- [ ] Run the full backend commands sequentially in the mandated build, unit, then
      integration order.
- [ ] Confirm no MCP contract, persistence schema, dependency, agent-count, approval,
      provisioning, or fixed-duration change entered the diff.
- [ ] Confirm assessment and Core revision handling complete before the action function
      exists, including revision-plus-submit and all conservative non-confirmations.
- [ ] Review the empty function schema, server-owned closure, attempt boundary, and
      post-commit outcome precedence, and prove the action phase is one-shot before
      continuing.

## Task 3: Render and acknowledge application-owned textual summaries

**Description:** Replace the Adaptive Card factory with an application text renderer
for ready and submitted states. Resolve display names from authoritative context while
using snapshot values for every identity/scope/workflow fact. Expose a delivery
acknowledgement that Teams records only after a successful normal-message send; an
uncertain send remains ineligible and is safe to repeat.

**Acceptance criteria:**

- [ ] Ready text includes requester identity; client/environment/role display names
      and canonical IDs; exact persisted justification; incident title/ID or explicit
      no-incident text; eight-hour lifetime; confirmation expiry; and the statement
      that submission requests business approval without approval or access grant.
- [ ] The renderer fails safely on non-ready or authoritative-context mismatch and
      never obtains authoritative fields from model prose or conversation history.
- [ ] Presentation becomes eligible only after successful message delivery; repeated
      rendering is idempotent and no Adaptive Card/action payload is produced.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Focused summary component tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~PreparedRequestSummary`
- [ ] Manual test assertion review confirms every RB-03 field is sourced from
      persisted/authoritative application state.

**Dependencies:** Task 1

**Files likely touched:**

- `src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs` (replace/remove)
- `src/GovernedAccess.Web/Teams/PreparedRequestSummaryRenderer.cs` (new)
- `src/GovernedAccess.Web/Ai/PresentedRequestSnapshotStore.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/PreparedRequestSummaryRendererTests.cs` (new)

**Estimated scope:** Medium (4 files)

## Task 4: Route Teams turns through conversational submission and remove card flow

**Description:** Make the authenticated personal-chat message route use the shared
conversational boundary for preparation, later confirmation, discussion, revision,
re-presentation, replay, and application-owned submission messages. Delete action
handler parsing, card replacement/disabling, and activity tracking dependencies while
retaining actor resolution, endpoint authentication, request timeout, cancellation,
correlation, `/new`, clarification, and normal text delivery.

**Acceptance criteria:**

- [ ] A ready turn sends normal summary text and cannot submit in that turn; a later
      explicit request submits once and sends the application-rendered request ID and
      `AwaitingBusinessApproval` status.
- [ ] Unsupported tenant/chat/auth/actor cases are rejected before capability exposure,
      and missing session/presentation state re-presents the exact ready summary with
      no attempt.
- [ ] Adaptive Card confirmation registration, payload parsing, card update/replacement,
      and `TeamsDraftCardTracker` runtime dependencies are absent; `/new`, discussion,
      revision, clarification, and existing safe preparation failures still work.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Focused Teams tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~TeamsRequestPreparation|FullyQualifiedName~TeamsRequestConfirmation"`
- [ ] Governed workflow test passes from Teams submission through unchanged approvals
      and one eight-hour grant.

**Dependencies:** Tasks 2B and 3

**Files likely touched:**

- `src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs`
- `src/GovernedAccess.Web/Teams/TeamsAgentRegistration.cs`
- `src/GovernedAccess.Web/Teams/TeamsDraftCardTracker.cs` (remove)
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestPreparationTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsRequestConfirmationTests.cs`

**Estimated scope:** Medium (5 files)

## Task 5: Close replay, concurrency, post-commit failure, and observation paths

**Description:** Harden the consequential slice at its highest-risk boundaries. Hold
the existing same-intake gate across eligibility reload, assessment, Core revision
handling, action tool composition, action execution, and outcome capture. Recheck
durable state after acquiring the gate and immediately before capability entry. Prove
duplicate/repeated confirmation suppression, both confirmation/revision race orders,
expiry/invalidation gating, durable recovery after a committed result, and truthful
response behavior after model/session/Teams failures. Emit one safe structured
attempt/outcome observation per actual capability entry without raw conversational or
provider material.

**Acceptance criteria:**

- [ ] Duplicate/concurrent delivery converges on one request and one `Submitted`
      attempt; a later repeated confirmation makes no new attempt and reports the same
      request. A queued turn reloads eligibility only after acquiring the same-intake
      gate, so no stale closure survives revision, submission, expiry, supersession, or
      invalidation; a winning submission leaves immutable scope unchanged.
- [ ] After commit, malformed output, timeout, cancellation, session-save failure, or
      delivery failure cannot produce a false “not submitted” message; retry/next turn
      recovers the exact request without duplication.
- [ ] Logs/observer distinguish attempted, submitted, already submitted, and safe
      rejection/failure categories with correlation, actor, IDs, and duration only;
      negative turns assert both zero attempts and zero persisted effects.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Concurrency and AI failure tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~RequestIntakeConfirmationConcurrency|FullyQualifiedName~MafConversationalSubmission"`
- [ ] Teams observability and governed workflow tests pass, including assertions that
      no raw prompts, transcripts, scope payloads, or provider traces are logged.

**Dependencies:** Task 4

**Files likely touched:**

- `src/GovernedAccess.Web/Ai/RequestSubmissionFunction.cs`
- `src/GovernedAccess.Web/Ai/SubmissionCapabilityObserver.cs` (new)
- `tests/GovernedAccess.IntegrationTests/Persistence/RequestIntakeConfirmationConcurrencyTests.cs`
- `tests/GovernedAccess.IntegrationTests/Teams/TeamsGovernedWorkflowTests.cs`
- `tests/GovernedAccess.IntegrationTests/Observability/TeamsIntakeLoggingTests.cs`

**Estimated scope:** Medium (5 files)

## Checkpoint: End-to-End Teams Behavior (after Tasks 3-5)

- [ ] Run the full backend commands sequentially in the mandated build, unit, then
      integration order, with at least a four-minute outer integration timeout.
- [ ] Confirm AC-01 through AC-09 and AC-11 have deterministic evidence at the
      narrowest appropriate layer.
- [ ] Confirm the React application and its API contracts are unchanged; otherwise run
      the frontend suite separately and review the unexpected scope expansion.

## Task 6: Add the versioned submission dataset and expectation contracts

**Description:** Add a separate, schema-validated submission dataset with explicit
scenario categories/setup, presentation/session conditions, expected capability
attempt/result counts, exact requester/scope match, request status, and forbidden
workflow effects. Preserve the preparation dataset's twenty scenarios and meanings;
share only genuinely common dataset primitives.

**Acceptance criteria:**

- [ ] The submission dataset contains distinct reviewable coverage for all fifteen
      EV-03 cases, including two positive phrasings, all listed restraint boundaries,
      repeated confirmation, stale context, revision plus submit, and discussion.
- [ ] Every scenario declares exact attempt/result and workflow-effect expectations;
      invalid, duplicate, unknown, or differently cased IDs and malformed suite data
      fail prerequisite validation.
- [ ] The existing `intake-v1.json` inventory, versioned meaning, and preparation
      schema behavior remain unchanged.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Dataset/schema tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~EvaluationEngine`
- [ ] Manual coverage review maps each submission scenario to EV-03 and confirms that
      no scenario duplicates another decision boundary.

**Dependencies:** Task 1

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/EvaluationDataset.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationDatasetLoader.cs`
- `src/GovernedAccess.Web/Evaluation/Contracts/evaluation-dataset.schema.json`
- `src/GovernedAccess.Web/Evaluation/Datasets/submission-v1.json` (new)
- `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

**Estimated scope:** Medium (5 files)

## Task 7: Execute and grade the production-faithful submission suite

**Description:** Extend evaluation execution to seed an exact synthetic ready snapshot,
acknowledge presentation only through the shared application boundary, run each turn
through the same non-mutating assessment, Core revision gate, and one-shot MAF action
phase as Teams, and grade safe observer counts plus persisted effects. Isolate each
scenario so positive submissions do not contaminate later cases; deterministic tests
use scripted chat clients only.

**Acceptance criteria:**

- [ ] Positive scenarios observe exactly one attempt, one `Submitted`, one matching
      immutable `AwaitingBusinessApproval` request, and zero decisions/operations/
      grants; negative scenarios observe zero attempts and all zero effects.
- [ ] Repeated confirmation observes one total attempt/result/request across the
      sequence; any unexpected attempt fails even when deterministic submission
      rejects it.
- [ ] Execution shares production registration, conversational boundary, local
      assessment/action phases, one-shot function, observer, and `ConfirmDraftAsync`;
      no evaluation-only submission path exists and each scenario uses isolated
      disposable persistence.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Deterministic evaluator execution/grading tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~EvaluationEngine|FullyQualifiedName~EvaluationCommand"`
- [ ] Tests deliberately inject an unexpected negative-scenario capability attempt
      and prove grading fails despite zero requests.

**Dependencies:** Tasks 2A, 2B, 5, and 6

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/EvaluationScenarioExecutor.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationResults.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationGrader.cs`
- `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationRunner.cs`
- `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationEngineTests.cs`

**Estimated scope:** Medium (5 files)

## Task 8: Extend suite selection, artifacts, isolation, and command tests

**Description:** Add `--suite preparation|submission`, default it to preparation, bind
exact case-sensitive `--scenario` selection to the chosen suite, and emit suite- and
dataset-versioned safe results/reports. Update evaluation hosting and project content
so both datasets are available while temporary databases and process-local state are
cleaned between scenarios/runs.

**Acceptance criteria:**

- [ ] Omitted `--suite` and explicit `--suite preparation` retain current behavior;
      explicit `submission` loads only its dataset, and invalid/duplicate/missing suite
      options or cross-suite scenario IDs return the documented prerequisite failure.
- [ ] JSON/report/console output identifies suite, dataset, application revision,
      model/deployment profile, instruction contract, and tool-schema versions and
      includes submission attempt/outcome, exact normalized scope match, request
      status, and forbidden-effect counts without prompts, transcripts, tool traces,
      or payloads.
- [ ] Exit codes, cancellation, exact selection, loopback-only hosting, temporary
      database cleanup, and preparation-suite zero-side-effect behavior remain intact.

**Verification:**

- [ ] Build succeeds:
      `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
- [ ] Command/hosting/artifact tests pass:
      `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~EvaluationCommand`
- [ ] Artifact tests validate both suite shapes and prove sanitized failed-scenario
      output contains no raw model/provider material.

**Dependencies:** Task 7

**Files likely touched:**

- `src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationHosting.cs`
- `src/GovernedAccess.Web/Evaluation/EvaluationArtifactWriter.cs`
- `src/GovernedAccess.Web/GovernedAccess.Web.csproj`
- `tests/GovernedAccess.IntegrationTests/Evaluation/EvaluationCommandTests.cs`

**Estimated scope:** Medium (5 files)

## Checkpoint: Evaluation Contract (after Tasks 6-8)

- [ ] Run the full backend commands sequentially in the mandated build, unit, then
      integration order.
- [ ] Confirm the unchanged preparation suite remains the default and has zero
      workflow effects in deterministic regression evidence.
- [ ] Confirm AC-10 and EV-01 through EV-05 are fully represented in deterministic
      tests. Do not run a live model as part of this checkpoint.

## Task 9: Record the product decision and current capability boundary

**Description:** Once implementation evidence passes, update the current product truth
and repository context to make conversational submission current, retire card
confirmation language, and move the Task 0 proposed ADR through its final reviewed
lifecycle state. Reconfirm why one local MAF function is compatible with the
constitution while remaining outside MCP, approval, and provisioning boundaries.

**Acceptance criteria:**

- [ ] The product baseline and context index describe Teams conversational submission,
      exact presentation/later-turn gating, deterministic authority, and unchanged
      downstream workflow without retaining conflicting “no local capability” text.
- [ ] The focused ADR retains the Task 0 decision history and records the implemented
      two-phase boundary, server-bound empty function contract, one-shot action phase,
      process-local presentation/restart choice, residual model-classification risk,
      rejected alternatives, and revisit criteria.
- [ ] The source feature specification's status/relationship to current as-built truth
      is accurate without erasing its approved acceptance requirements.

**Verification:**

- [ ] Links in all changed documents resolve.
- [ ] `git diff --check` passes.
- [ ] Manual authority-order review confirms no conflict with the constitution.

**Dependencies:** Tasks 5 and 8

**Files likely touched:**

- `spec.md`
- `SPEC-conversational-request-submission.md`
- `docs/governed-production-access-product-baseline.md`
- `docs/adr/README.md`
- `docs/adr/0007-use-server-bound-conversational-submission-function.md`

**Estimated scope:** Medium (5 files)

## Task 10: Reconcile architecture, security, and intake orchestration

**Description:** Update the as-built design documents after verified implementation.
Describe the shared conversational boundary, exact presentation acknowledgement,
function outcome precedence, durable tombstone recovery, concurrency orderings, safe
observation, and changed threat/residual-risk posture. Remove obsolete card activity
tracking and action-payload authority descriptions.

**Acceptance criteria:**

- [ ] Architecture maps the one-agent, non-mutating-assessment-then-one-shot-action path
      and clean Core/Web/Teams/evaluation boundaries, including restart and post-commit
      recovery flows.
- [ ] Security model identifies model confirmation classification as residual risk and
      documents authentication, empty schema, exact binding, deterministic recheck,
      attempt observation, immutable scope, and human approval controls.
- [ ] Intake orchestration defines preparation, presentation, eligible later turns,
      revision separation, replay/concurrency ordering, and truthful failure outcomes
      without Adaptive Card dependencies.

**Verification:**

- [ ] Links in all changed documents resolve.
- [ ] `git diff --check` passes.
- [ ] Manual trace maps RB-01 through RB-12 to an as-built component and test owner.

**Dependencies:** Task 9

**Files likely touched:**

- `docs/architecture.md`
- `docs/security-model.md`
- `docs/request-intake-orchestration.md`

**Estimated scope:** Medium (3 files)

## Task 11: Reconcile testing, live-evaluation, and stable operator contracts

**Description:** Update test ownership, operator guidance, and stable evaluation
contracts for the two-suite command, two-phase action path, and action-aware artifacts.
Keep live execution an explicit credentialed manual action and document exact
deterministic gates, cleanup, sanitization, suite selection, and exit-code
compatibility.

**Acceptance criteria:**

- [ ] Testing strategy places confirmation policy, presentation, MAF function behavior,
      concurrency/recovery, Teams transport, evaluation grading, and negative
      attempt/effect evidence at the correct unit/component/full-host layers.
- [ ] Live-evaluation and command/report/result contracts describe default preparation
      and explicit submission suites, case-sensitive scenario selection, suite-specific
      grading, safe fields, isolation/cleanup, and unchanged exit codes.
- [ ] Contracts exclude raw prompts, transcripts, provider/tool traces, complete
      payloads, secrets, and model-supplied scope, and documentation-only validation
      passes.

**Verification:**

- [ ] Links in all changed documents resolve.
- [ ] Result/report examples or schemas validate against both preparation and
      submission artifact shapes using existing contract tests.
- [ ] `git diff --check` passes.

**Dependencies:** Tasks 8 and 10

**Files likely touched:**

- `docs/testing-strategy.md`
- `docs/live-model-evaluation.md`
- `docs/contracts/live-model-evaluation/command.md`
- `docs/contracts/live-model-evaluation/result.schema.json`
- `docs/contracts/live-model-evaluation/report.md`

**Estimated scope:** Medium (5 files)

## Checkpoint: Ready for Live Validation (after Tasks 9-11)

- [ ] Re-run the full backend validation sequence if documentation examples or
      contracts changed executable behavior; otherwise retain the verified code-gate
      evidence and run documentation link validation plus `git diff --check`.
- [ ] Verify AC-01 through AC-11, RB-01 through RB-12, and EV-01 through EV-05 against
      the final test/artifact matrix.
- [ ] Apply the project gates in [`AGENTS.md`](../AGENTS.md) and
      [`docs/testing-strategy.md`](../docs/testing-strategy.md), including correctness,
      integration, documentation, security/observability review, and rollback
      awareness.
- [ ] Do not merge, deploy, or run the credentialed live suite until the human reviews
      and approves the implementation and operator action.

## Task 12: Run and retain live preparation and submission acceptance evidence

**Description:** After the human approves the implementation and credentialed operator
action, run the unchanged preparation suite and the complete consequential submission
suite against the configured live model. Retain only the sanitized reports and exact
safe configuration identities needed to establish which model/deployment, dataset,
instructions, and tool schema were evaluated. This is a manual release gate, not a CI
or routine automated-test dependency.

**Acceptance criteria:**

- [ ] The operator explicitly approves the credentialed run and records the selected
      execution profile, deployment/model identity, dataset versions, instruction
      version, and tool-schema version without recording credentials or raw prompts.
- [ ] The full preparation suite and full submission suite both exit successfully;
      every submission scenario meets its exact attempt, outcome, scope, and forbidden
      workflow-effect expectations with no partial threshold.
- [ ] Sanitized human-readable and machine-readable reports are retained for review;
      prompts, transcripts, provider/tool traces, complete payloads, secrets, and
      model-supplied scope are absent.

**Verification:**

- [ ] Run the preparation suite:
      `dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --suite preparation --output artifacts/live-model-evaluation`
- [ ] Run the submission suite:
      `dotnet run --project src/GovernedAccess.Web --no-launch-profile -- evaluate-live-model --suite submission --output artifacts/live-model-evaluation`
- [ ] Review both report formats, exact scenario inventory, safe metadata, and exit
      codes before authorizing merge or deployment.

**Dependencies:** Tasks 5, 8, and 11; all deterministic checkpoints; explicit human
operator approval

**Files likely produced:**

- `artifacts/live-model-evaluation/` (sanitized operator artifacts; repository policy
  determines whether they remain local or are attached to the review record)

**Estimated scope:** Small operational gate (no source change expected)

## Checkpoint: Complete and Ready for Merge (after Task 12)

- [ ] AC-01 through AC-11, RB-01 through RB-12, and EV-01 through EV-05 map to final
      deterministic and live evidence.
- [ ] The exact implementation reviewed by the human is the implementation exercised
      by Task 12; any later model, instruction, tool-schema, or action-path change
      invalidates the live evidence and requires the relevant gates to be rerun.
- [ ] Human approval is recorded before merge or deployment.

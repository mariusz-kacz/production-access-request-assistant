# Implementation Plan: Conversational Request Submission

- **Status:** Proposed for human review; no implementation is authorized by this artifact
- **Source specification:** [`SPEC-conversational-request-submission.md`](../SPEC-conversational-request-submission.md)
- **Task list target:** [`tasks/todo.md`](todo.md)

## Overview

Replace requester Adaptive Card confirmation with ordinary authenticated Teams
conversation. One existing MAF agent handles each eligible message in two bounded
phases: a non-mutating assessment without the submission function, followed only when
that assessment leaves the exact presented snapshot unchanged by an action phase that
may invoke one no-argument, application-local submission function. The function
remains outside MCP and delegates to the existing
`RequestSubmissionService.ConfirmDraftAsync`; authenticated server context, persisted
scope, deterministic revalidation, atomic request creation, human approvals, and
provisioning policy remain authoritative. A separate submission evaluation suite will
measure both capability invocation and restraint without changing the existing
twenty-scenario preparation baseline.

## Repository Evidence and Current-State Delta

- [`RequestIntakeSession`](../src/GovernedAccess.Core/Domain/Drafts/RequestIntakeSession.cs)
  already holds the immutable ready snapshot, exact preparation ID, reserved request
  ID, expiry, actor/conversation binding, optimistic-concurrency version, and terminal
  tombstone data. No persistence column is needed solely to remember presentation.
- [`RequestSubmissionService`](../src/GovernedAccess.Core/Application/AccessRequests/RequestSubmissionService.cs)
  already reloads the exact preparation, verifies ownership and lifecycle, revalidates
  authoritative requester/scope, creates one immutable request and audit event, and
  recovers `AlreadySubmitted` after concurrency or ambiguous persistence failure.
- [`MafConversationTurnCoordinator`](../src/GovernedAccess.Web/Ai/MafConversationTurnCoordinator.cs)
  already serializes and saves process-local MAF sessions by server-generated intake
  ID, but it does not expose whether a prior session/presentation is known or retain a
  local-function result when later model/session work fails.
- [`MafRequestPreparationInterpreter`](../src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs)
  currently gives the agent only the two MCP tools and returns a schema-validated
  preparation proposal. Its instructions explicitly prohibit submission. Preserve
  that non-mutating assessment boundary, then add a separately bounded action phase
  using the same agent only after Core has applied the assessment outcome.
- [`TeamsAccessRequestAgent`](../src/GovernedAccess.Web/Teams/TeamsAccessRequestAgent.cs),
  [`PreparedRequestCardFactory`](../src/GovernedAccess.Web/Teams/PreparedRequestCardFactory.cs),
  and [`TeamsDraftCardTracker`](../src/GovernedAccess.Web/Teams/TeamsDraftCardTracker.cs)
  currently couple ready presentation and confirmation to Adaptive Cards, action
  payload parsing, activity replacement, and process-local card IDs. Those requester
  confirmation dependencies must be removed while retaining Teams authentication,
  personal-chat checks, timeouts, correlation, `/new`, and normal message delivery.
- The current evaluator has one fixed dataset and assumes zero workflow effects in
  [`EvaluationScenarioExecutor`](../src/GovernedAccess.Web/Evaluation/EvaluationScenarioExecutor.cs),
  [`EvaluationGrader`](../src/GovernedAccess.Web/Evaluation/EvaluationGrader.cs), and
  [`LiveModelEvaluationCommand`](../src/GovernedAccess.Web/Evaluation/LiveModelEvaluationCommand.cs).
  It needs a suite discriminator and submission-specific expectations while keeping
  omitted `--suite` behavior equivalent to today.
- The current product baseline and related architecture/security documentation still
  state that the model has no state-changing local capability. They remain current
  as-built truth until the implementation and documentation reconciliation land
  together.

## Architecture Decisions

1. **Resolve authority before implementation.** The feature specification and a
   focused proposed ADR must be reviewed before code work begins. The ADR must explain
   whether an untrusted model-selected submission signal remains compatible with the
   constitution's deterministic-authorization rule. If it is not compatible, work
   stops until the constitution and baseline are explicitly amended by their owners.
2. **Use one shared application conversational boundary.** Teams and live evaluation
   will exercise the same turn coordinator around the existing Core preparation and
   submission services. Teams remains a transport adapter, and no second agent,
   channel framework, service, or workflow engine is introduced.
3. **Finish non-mutating assessment before exposing mutation.** The first phase of an
   eligible message exposes only the existing two read-only MCP tools and produces an
   untrusted structured preparation/action assessment. Core applies candidate,
   revision, readiness, and lifecycle rules before any action phase can start. A
   changed, incomplete, rejected, stale, ambiguous, or discussion outcome ends the
   turn without registering the submission function. Only an unchanged exact ready
   snapshot with pure later submission intent may enter the separately bounded action
   phase. Both phases use the same MAF agent; this is not multi-agent orchestration.
4. **Acknowledge presentation in process-local application state after successful
   delivery.** Eligibility is keyed by the full authenticated actor/conversation
   binding and exact preparation/reserved request identity. A newly ready summary is
   not eligible during the turn that created it. Missing state after restart or an
   uncertain send causes deterministic re-presentation and requires a later turn.
   No raw conversation history or new database presentation flag is added.
5. **Bind and consume the local function once.** The model-visible function is
   exactly `submit_current_request_for_business_approval` with an empty argument
   object. Its configured instance closes over the authenticated actor, exact
   preparation ID, correlation ID, and safe observer. The action phase has a one-shot
   invocation guard and terminates with an application-owned result immediately after
   the first function outcome, so the generic multi-iteration preparation loop cannot
   invoke the consequential function repeatedly. Unknown functions, non-empty
   arguments, missing presentation acknowledgement, repeated calls, or stale durable
   state fail closed.
6. **Keep durable submission authority unchanged.** The local function calls
   `ConfirmDraftAsync`; it never accepts scope, resolves an arbitrary latest ready
   draft, calls MCP, or creates workflow records itself. Existing submitted intake
   tombstones and request IDs are used to recover a committed result and suppress
   later capability attempts.
7. **Separate capability outcome from conversational completion.** The application
   captures `Submitted`, `AlreadySubmitted`, or a safe typed rejection at function
   completion. A committed result takes precedence over malformed final model output,
   timeout, cancellation, session-save failure, or response-generation failure, so an
   application-rendered status can remain truthful without model reconstruction.
8. **Render authoritative text in application code.** The ready summary and submitted
   status are normal text activities assembled from the persisted snapshot and exact
   authoritative lookups. Model prose may surround but may not supply or reconstruct
   requester, identifiers, role, justification, incident, duration, expiry, request
   ID, or workflow status.
9. **Observe attempts at the application-local boundary.** Safe telemetry records
   correlation, actor, preparation/request ID, duration, and typed outcome. It records
   neither prompts, transcripts, model scope, raw tool/provider payloads, nor traces.
   Evaluation consumes the same observer rather than provider-native tool traces.
10. **Add a separate versioned submission suite.** The preparation dataset retains its
   current twenty scenarios and meanings. `--suite preparation` and omission of
   `--suite` select it; `--suite submission` selects a distinct dataset, grading
   contract, isolated persistence scenario setup, and artifact facts. Safe artifacts
   also identify the application revision, model/deployment profile, instruction
   contract, and tool-schema versions exercised by the live gate.

## Dependency Graph

```text
Task 0: approve specification and proposed consequential-capability ADR
  \-- Task 1: exact presentation/recovery contracts
        |-- Task 2A: non-mutating assessment and Core revision gate
        |     \-- Task 2B: one-shot MAF action phase and typed outcome
        |-- Task 3: authoritative text renderer and delivery acknowledgement
        |       \-- Task 4: Teams conversational route and card removal
        |                \-- Task 5: replay, races, failure recovery, observation
        \-- Task 6: submission dataset contract

Tasks 2B, 5, and 6
  \-- Task 7: production-faithful two-phase execution and grading -- Task 8: CLI/artifacts

Tasks 5 and 8
  \-- Tasks 9-11: product, architecture/security, and operator documentation
        \-- Task 12: human-authorized live preparation and submission gates
```

Task 3 may proceed in parallel with Task 2A after Task 1 if their shared conversational
contract is fixed first. Task 2B depends on Task 2A because mutation must remain absent
until the assessment and Core revision gate are complete. Task 6 may begin in parallel
with Teams work after Task 1, but Task 7 cannot complete until the shared two-phase
action path from Tasks 2A-5 exists. Shared AI registration, turn result types, and
evaluation result types require coordination rather than parallel edits.

## Task List

### Phase 0: Governance Gate

- [ ] Task 0: Approve the specification and consequential-capability ADR

### Checkpoint: Authority to Implement

- [ ] The feature specification is approved by its decision owner.
- [ ] A proposed ADR records constitution compatibility, residual model-classification
      risk, rejected alternatives, and explicit authorization to begin implementation.
- [ ] Any required constitution or baseline amendment is approved before Task 1.

### Phase 1: Trusted Foundations

- [ ] Task 1: Define exact presentation eligibility and durable recovery contracts
- [ ] Task 2A: Add non-mutating turn assessment and the Core revision gate
- [ ] Task 2B: Add the one-shot no-argument MAF submission action phase

### Checkpoint: Trusted Boundary

- [ ] Exact actor, conversation, preparation, presentation, and correlation bindings
      are server-owned and covered by negative tests.
- [ ] Preparation/revision assessment completes with no submission function available,
      and only an unchanged exact snapshot may enter the action phase.
- [ ] The function schema has no model-visible arguments and MCP still exposes exactly
      its two existing read-only tools.
- [ ] One action phase can enter the submission boundary at most once, terminates after
      its first typed outcome, and cannot inherit the six-iteration preparation loop.
- [ ] Build, unit tests, and affected AI/persistence integration tests pass in the
      repository-mandated order.
- [ ] Human reviews the consequential boundary before Teams transport changes proceed.

### Phase 2: Conversational Teams Slice

- [ ] Task 3: Render and acknowledge application-owned textual summaries
- [ ] Task 4: Route Teams turns through conversational submission and remove card flow
- [ ] Task 5: Close replay, concurrency, post-commit failure, and observation paths

### Checkpoint: End-to-End Teams Behavior

- [ ] Ready details are sent as normal text and only a later explicit turn can attempt
      submission.
- [ ] Positive confirmation creates one `AwaitingBusinessApproval` request; negative,
      stale, revised, premature, and adversarial turns produce zero attempts/effects.
- [ ] Authentication, personal-chat rejection, `/new`, preparation/revision behavior,
      approval/provisioning policy, and eight-hour duration remain unchanged.
- [ ] The complete backend validation sequence passes sequentially.

### Phase 3: Consequential Evaluation Evidence

- [ ] Task 6: Add the versioned submission dataset and expectation contracts
- [ ] Task 7: Execute and grade the production-faithful submission suite
- [ ] Task 8: Extend suite selection, artifacts, isolation, and command tests

### Checkpoint: Evaluation Contract

- [ ] Omitted `--suite` still runs the unchanged twenty-scenario preparation suite.
- [ ] Every submission scenario grades exact application-level attempts and all
      forbidden workflow effects; a rejected unexpected attempt still fails.
- [ ] Safe artifacts identify the application, model/deployment, dataset, instruction,
      and tool-schema versions without retaining raw model or provider material.
- [ ] Deterministic evaluator tests use scripted chat clients and isolated temporary
      SQLite state; routine validation never invokes a live model.
- [ ] The complete backend validation sequence passes sequentially.

### Phase 4: Authoritative Documentation

- [ ] Task 9: Record the product decision and current capability boundary
- [ ] Task 10: Reconcile architecture, security, and intake orchestration
- [ ] Task 11: Reconcile testing, live-evaluation, and stable operator contracts

### Phase 5: Human-Authorized Live Validation

- [ ] Task 12: Run and retain the live preparation and submission acceptance evidence

### Checkpoint: Complete and Reviewable

- [ ] Product, architecture, security, testing, operator, ADR, and repository-index
      artifacts describe the implemented behavior without conflicting card guidance.
- [ ] The human-authorized live preparation and submission suites both pass with safe,
      versioned evidence for the exact model, dataset, instructions, and tool schema.
- [ ] Documentation links and `git diff --check` pass.
- [ ] The project gates in [`AGENTS.md`](../AGENTS.md) and
      [`docs/testing-strategy.md`](../docs/testing-strategy.md), plus AC-01 through
      AC-11, are reviewed against test evidence.
- [ ] Human approval is obtained before merge or deployment.

## Behavior That Must Remain Unchanged

- Acting tenant, actor, requester, conversation, and authorization come from the
  authenticated Teams server context; unsupported tenant/chat/identity cases fail
  before model or capability exposure.
- Preparation remains an untrusted proposal followed by deterministic canonical
  validation. Discussion, incomplete revision preservation, complete revision
  supersession, exact expiry, and `/new` lifecycle behavior remain intact.
- The model still has exactly the two read-only MCP tools
  `get_production_environment` and `get_incident`; no submission capability is added
  to MCP and no role-listing or mutation tool appears there.
- Submitted scope, business approver resolution, human decision ordering, fixed
  eight-hour lifetime, provisioning evidence reload, request-keyed idempotency,
  retry authorization, audit, visibility, and logical expiry do not change.
- The React application remains only a request register and authenticated
  business/DevOps decision surface; it gains no requester submission path.
- The preparation evaluation keeps its twenty checked-in cases, default command
  behavior, zero-side-effect rule, scenario meaning, and exact selection semantics.
- No live LLM, Teams tenant, Azure subscription, real identity, or real provisioner is
  required by automated tests.

## Assumptions

- The user request authorizes planning the draft specification but does not itself
  approve implementation; this plan stops at review artifacts.
- The SDK capability stated in the specification (per-run local `AIFunction`, empty
  model-visible parameters, MCP tools, and response schema in one run) is available in
  the pinned package versions; implementation will verify its exact wire schema and
  the ability to terminate the separate action phase after one outcome with component
  tests before relying on it.
- Process-local presentation acknowledgement is sufficient because the required
  restart behavior is safe re-presentation, not durable conversational resumption.
- Existing terminal intake tombstones and reserved request IDs are sufficient for
  durable result recovery. If evidence shows a schema change is unavoidable,
  implementation must stop for the specification's required prior approval.
- No new NuGet/npm dependency and no frontend contract change are expected.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Model calls the consequential function for ambiguous, revised, negated, quoted, or adversarial wording | High | Complete a non-mutating assessment first; apply Core revision/lifecycle rules; do not create an action phase for changed or non-confirming outcomes; then use conservative action instructions, scripted negative component tests, and live submission scenarios that fail on any unexpected attempt. Deterministic submission still gates effects, but an unexpected attempt remains a test failure. |
| One model turn invokes the consequential function repeatedly | High | Use a dedicated action phase with a one-shot guard and immediate application-owned termination after its first typed function result; verify the generic six-iteration preparation loop cannot apply to the mutation phase. |
| A function commits and later model/session/Teams work fails | High | Capture the typed function result immediately outside final response parsing; prioritize persisted truth; render submitted status from application state; exercise malformed output, timeout, cancellation, session-save, and delivery failure recovery. |
| Concurrent revision and confirmation act on stale scope | High | Hold the same-intake gate across eligibility reload, tool composition, assessment/action execution, and outcome capture; recheck durable state after acquiring the gate and immediately before capability entry; preserve optimistic concurrency; and test both race orderings plus duplicate delivery. |
| Restart or uncertain summary delivery enables premature confirmation | High | Treat missing process-local presentation acknowledgement/session certainty as ineligible; re-render the current durable ready snapshot and require a later turn. |
| Removing cards accidentally weakens Teams authentication or timeout behavior | High | Keep actor resolution and endpoint policy ahead of conversation handling; update transport tests to assert rejection occurs before capability exposure; retain request timeout and cancellation propagation. |
| Local function is mistaken for an MCP expansion or approval boundary | High | Register it only per MAF turn, keep the exact MCP catalog contract tests, use an empty schema and server closure, and document that requester submission is neither business approval nor access authorization. |
| Submission evaluator drifts from Teams behavior | Medium | Share the application conversational boundary, local function, presentation gate, observer, and `ConfirmDraftAsync`; prohibit evaluation-only submission code; use isolated production-shaped hosting with scripted clients. |
| Existing preparation evaluator or artifacts break | Medium | Keep a suite-tagged but semantically unchanged preparation model, default to it, retain exact case-sensitive scenario selection, and run regression tests for its inventory, grading, side effects, and schemas. |
| Logs or artifacts retain sensitive model material | Medium | Use safe counters and typed categories only; add assertions excluding prompts, transcripts, provider traces, complete function/MCP payloads, and model-supplied scope. |
| Documentation remains split between card and conversation behavior | Medium | Land Tasks 9-11 only after behavior is verified and cross-check the authority chain from constitution through operator contracts. |

## Validation Strategy

- Per task, run the focused unit/component tests listed in [`tasks/todo.md`](todo.md)
  after a warnings-as-errors build.
- At each code checkpoint, run sequentially and in this exact order:

  1. `dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror`
  2. `dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore`
  3. `dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m`

- Give the integration command an outer timeout of at least four minutes. On timeout,
  identify and stop only its runner process tree before retrying.
- Do not run the frontend suite unless implementation unexpectedly changes frontend
  behavior or contracts. If that occurs, run
  `npm test --prefix src/GovernedAccess.Web/ClientApp -- --run` separately.
- Live-model evaluation is an explicit, credentialed Task 12 gate only after all
  deterministic and documentation gates pass and the human authorizes the operator
  action. It is never part of routine automation or CI.
- For documentation reconciliation, validate links and run `git diff --check`.

## Open Questions

None block implementation planning. Task 0 approval of the specification and proposed
ADR remains the gate before Task 1 begins. Any unresolved constitution conflict, or any
discovered need for persisted presentation state, a new dependency, a changed
preparation dataset, or expanded channel/topology scope must return to the user for
approval rather than being absorbed into these tasks.

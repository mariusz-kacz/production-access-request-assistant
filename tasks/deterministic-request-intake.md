# Implementation Tasks: Deterministic Conversational Request Intake

- **Status:** Proposed
- **Feature specification:** `SPEC-deterministic-request-intake.md`
- **Supporting design:** `docs/deterministic-request-intake-design.md`
- **Test ownership:** `docs/evaluation/deterministic-request-intake-test-matrix.md`

## Delivery rules

- Implement these as seven coherent reviewable tasks, not dozens of atomic checklist
  items.
- Do not update current as-built baseline/architecture/operator documents before the
  implementation and deterministic tests prove the new behavior.
- Every task must preserve zero model-visible consequential capabilities.
- Deterministic automated tests must not require a live LLM, Teams tenant, Azure
  subscription, or real provider.
- Do not add a second agent, generic workflow/dialogue engine, new deployable service,
  or raw conversation persistence.
- Keep the existing browser request-creation prohibition and downstream approval/
  provisioning contracts unchanged.

## Task 0: Approve and reconcile the target decisions

### Goal

Establish one non-contradictory authority set before application code changes.

### Work

- [ ] Review and approve `SPEC-deterministic-request-intake.md`.
- [ ] Approve constitution amendment `3.0.0`, which keeps the model-visible catalog
      exact but moves exact names into the active baseline/contract.
- [ ] Review proposed ADRs 0007, 0008, and 0009.
- [ ] Confirm the four-tool target:
      `search_production_environments`, `get_production_environment`,
      `get_environment_roles`, and `get_incident`.
- [ ] Confirm unique Core-reproduced search results are accepted without another
      requester selection turn.
- [ ] Confirm sparse `set`/`clear` patches with omitted-field no-op semantics.
- [ ] Confirm there is no pending-revision state and no `/cancel-revision` command.
- [ ] Confirm a first accepted material ready revision immediately supersedes the old
      card/preparation.
- [ ] Confirm tool sequence is diagnostic except for catalog, argument, call-count,
      schema, timeout, and unknown-call violations.
- [ ] Remove or quarantine any remaining proposed document that asserts the conflicting
      keep-all-fields, single-match-selection, strict same-turn choreography, or dual
      pending-revision design.

### Exit gate

- One approved specification and supporting decision set contains no conflicting tool
  count, unique-search, patch, ready-revision, persistence, or evaluation policy.
- The current product baseline remains clearly marked as as-built until implementation.

## Task 1: Introduce the sparse interpretation and deterministic reducer boundary

### Goal

Replace complete-candidate model ownership with provider-neutral sparse proposals and
Core-owned canonical state.

### Work

- [ ] Add Core dialogue-act, sparse field-operation, patch, interpretation, operation
      verdict, and closed outcome types.
- [ ] Keep MAF/provider JSON schema and SDK types inside the Web/AI boundary and
      translate them into Core types.
- [ ] Change the model schema so patch fields are optional and only `set`/`clear` are
      allowed; reject unknown properties and unsupported fields.
- [ ] Add boundary invariants:
      - non-update act requires empty patch and no search;
      - update requires at least one operation or valid search before normalization;
      - `set` requires value;
      - `clear` forbids value.
- [ ] Implement shared Unicode/message normalization.
- [ ] Implement deterministic field evidence and verdicts.
- [ ] Implement value-equal normalization and unsupported-mutation preservation.
- [ ] Implement dependency cascades, client derivation, incident conflict, role
      validation, justification authorship/syntactic floor, and focused issue order.
- [ ] Implement application-owned outcome data with no model response prose.
- [ ] Route exact clear commands directly to the reducer without model/MCP.
- [ ] Preserve `/new` deterministic behavior.

### Primary tests

- Sparse omission, one-field set/clear, full-snapshot drift, value-equal, unsupported
  mutations, context loss, justification rules, dependency cascades, and closed outcome
  construction.
- No live model.

### Exit gate

- Unit tests prove that omission, model state loss, snapshot-shaped output, unsupported
  clearing, and unrequested field values cannot corrupt canonical state.
- No existing downstream request/approval/provisioning type depends on the new model
  schema.

## Task 2: Split and govern the four read-only MCP capabilities

### Goal

Represent environment discovery, exact environment metadata, environment-scoped
entitlements, and exact incident context as distinct bounded capabilities.

### Work

- [ ] Replace the combined environment tool with:
      - `search_production_environments(query)`;
      - `get_production_environment(environmentId)`; and
      - `get_environment_roles(environmentId)`.
- [ ] Retain exact `get_incident(incidentId)` behavior.
- [ ] Implement closed input/output DTOs matching the proposed machine-readable
      contract.
- [ ] Ensure exact environment output contains no roles.
- [ ] Ensure role lookup is scoped to one exact environment and supports successful
      `roles: []` for a known environment.
- [ ] Implement the shared deterministic environment search policy and stable ordering.
- [ ] Add typed too-broad behavior instead of truncation.
- [ ] Expose exactly four read-only tools and reject missing, additional, renamed, or
      non-read-only tools.
- [ ] Keep one call per tool, four total calls, six provider iterations, no concurrent
      tool calls, explicit timeout, and cancellation.
- [ ] Relax exact same-turn choreography checks: reject invalid calls/contracts, not
      omission of redundant lookups.
- [ ] Create independent Core ports/adapters for environment, entitlement, and incident
      authority, even if the synthetic implementation shares SQLite.
- [ ] Make source failures independently injectable in tests.

### Primary tests

- Real MCP transport catalog/contract tests.
- Zero/unique/multiple/too-broad search.
- Exact environment excludes roles.
- Role source empty/not-found/unavailable.
- Call and provider-iteration bounds.
- Safe proposal accepted despite omitted redundant lookup, followed by mandatory Core
  revalidation.

### Exit gate

- The real endpoint advertises exactly the four proposed tools with no generic or
  consequential capability.
- Environment and entitlement sources can fail independently without state mutation or
  invented fallback.

## Task 3: Add authoritative search resolution and application-owned conversation

### Goal

Make Core and the renderer own every authoritative choice and requester-facing outcome.

### Work

- [ ] Capture only the successful search query observation at the Web interpretation
      boundary; do not pass raw MCP result payloads into Core.
- [ ] Validate the query against the current requester message.
- [ ] Independently execute Core search and apply:
      - zero -> no-match clarification;
      - unique -> exact reload and canonical selection;
      - multiple -> complete stable persisted choices;
      - too broad -> typed guidance.
- [ ] Treat model/Core search differences as bounded diagnostics while following the
      Core result; fail only malformed/contract-invalid provider turns.
- [ ] Add application-owned renderers for canonical progress, environment/role choices,
      focused questions, validation guidance, ready cards, submission guidance, and
      failures.
- [ ] Limit `discussDraft` to bounded deterministic help; do not add open-ended
      model-authored answers.
- [ ] Ensure role choices come only from the current entitlement authority and are
      rendered in stable order.
- [ ] Ensure all display text is encoded and placed in labeled fields.

### Primary tests

- Search cardinality and drift matrix.
- Every rendered identifier/name reloaded from application authority.
- Model prose and raw tool results never appear as canonical facts or workflow claims.
- Unique search completes without an artificial requester selection turn.

### Exit gate

- A deterministic scripted conversation can prepare an exact or readable environment
  request while every displayed authoritative value originates from Core.
- The model cannot filter, rank, truncate, reorder, or silently choose from multiple
  results.

## Task 4: Persist bounded clarification context and simplify ready revision

### Goal

Support restart-safe exact/ordinal choices and safe ready revision with one active
candidate, not dual pending state.

### Work

- [ ] Add provider-neutral persistence for one clarification target, ordered canonical
      IDs, preparation ID, candidate version, and creation time.
- [ ] Bind context to the exact active preparation/version.
- [ ] Consume context after exact/ordinal selection and clear it after any committed
      candidate change unless a new focused context is stored.
- [ ] Add narrow deterministic ordinal resolution before model execution where
      practical.
- [ ] Keep raw messages, model responses, complete tool payloads, and MAF sessions out
      of durable storage.
- [ ] Remove any `PendingRevisionCandidate`, `RevisionPending`, or
      `/cancel-revision` design/code introduced during implementation.
- [ ] Implement material ready revision as one transaction:
      - verify old ready snapshot;
      - mark it superseded;
      - create new collecting/ready intake with new identity;
      - store sanitized candidate and applicable choices/deadline.
- [ ] Preserve the original ready snapshot for discussion, value-equal, unsupported,
      model/source failure, and failed replacement transaction.
- [ ] Ensure a multiple/zero-result environment revision clears old active
      environment/client/role from the new collecting candidate.
- [ ] Enforce at most one active `Collecting` or `Ready` intake for the complete actor/
      conversation binding.
- [ ] Add bounded process-local gate/session eviction if the existing feature work
      touches those components and the change remains proportionate.

### Primary tests

- Choice context restart, stale version, consumption, replacement, and no raw content.
- Ready A preservation for non-mutating/failure turns.
- Ready A -> Superseded A + Collecting/Ready B for material turns.
- Atomic failure preserves A.
- Concurrent first turns and same-conversation ordering.

### Exit gate

- Ordinal environment/role choices work after restart without conversation history.
- Exactly one active candidate exists and no pending-revision state remains.
- A material revision makes the old card durably stale in the same commit that creates
  the replacement intake.

## Task 5: Preserve and re-prove deterministic card confirmation

### Goal

Ensure the intake refactor does not weaken the only request-creation boundary.

### Work

- [ ] Keep card payload restricted to schema version and preparation ID.
- [ ] Render the exact immutable canonical ready snapshot and 30-minute deadline.
- [ ] On action, reload exact preparation, actor/conversation ownership, lifecycle,
      expiry, environment/client, role assignment, justification, and incident.
- [ ] Reject collecting, superseded, expired, foreign, malformed, and stale actions.
- [ ] Keep one-save request creation and audit evidence.
- [ ] Preserve replay identity and duplicate/concurrent convergence.
- [ ] Re-render the exact current card for textual submission guidance; create no
      request from text.
- [ ] Keep browser request creation absent and downstream approval/provisioning
      contracts unchanged.
- [ ] Exercise revision-versus-confirmation barriers:
      - confirmation wins;
      - revision wins;
      - duplicate confirmation.

### Primary tests

- Full-host journeys A-D from the test matrix.
- Negative card ownership/status/expiry/schema cases.
- Request/approval/operation/grant counts.

### Exit gate

- Only exact authenticated card confirmation can create one immutable
  `AwaitingBusinessApproval` request.
- A stale card cannot submit replacement scope.
- No downstream policy or fixed-duration behavior changed.

## Task 6: Rebalance evaluation and reconcile the as-built documentation

### Goal

Produce credible evidence without duplicating the deterministic matrix in a giant live
suite, then update every current document to one verified runtime truth.

### Work

- [ ] Update deterministic unit/component/full-host tests according to the matrix.
- [ ] Replace or version the live dataset with approximately 12 high-value scenarios.
- [ ] Grade dialogue act, proposed fields, operation verdicts, normalized canonical
      outcome, clarification target/choices, grounding, and restraint.
- [ ] Record tool sequence/count/latency diagnostically rather than making redundant
      call order the headline pass condition.
- [ ] Require zero requests, approvals, provisioning operations, and grants in every
      live scenario.
- [ ] Retain reviewed evidence with commit SHA, dataset/hash, prompt/schema hash,
      contract hash, deployment/model metadata where available, outcomes, latency, and
      side-effect counts.
- [ ] Run build, unit, integration, and affected frontend gates sequentially.
- [ ] After evidence passes, reconcile:
      - `docs/governed-production-access-product-baseline.md`;
      - `docs/architecture.md`;
      - `docs/security-model.md`;
      - `docs/request-intake-orchestration.md`;
      - `docs/testing-strategy.md`;
      - `docs/live-model-evaluation.md`;
      - current `docs/contracts/mcp-tools.json`;
      - operator/local-development guidance;
      - `README.md` and diagrams;
      - `spec.md` context map if it describes the old intake boundary;
      - ADR statuses/index; and
      - `docs/roadmap.md`.
- [ ] Remove the temporary proposed MCP contract after its content becomes the current
      canonical contract, or clearly mark it historical.
- [ ] Rename the feature branch/PR description to `deterministic-request-intake` where
      practical so it describes the implemented change rather than Teams decoupling.

### Exit gate

- Required deterministic gates pass.
- The reviewed live-model suite passes with zero consequential side effects.
- Current documentation contains one consistent four-tool, sparse-patch, unique-search,
  single-active-candidate design and no obsolete two-tool or pending-revision claims.

## Final completion checklist

- [ ] Constitution amendment and ADRs accepted with correct statuses.
- [ ] Sparse model contract and deterministic reducer implemented.
- [ ] Exact four-tool read-only catalog verified over real MCP transport.
- [ ] Core search accepts unique result and owns all multiple-result choices.
- [ ] Environment and entitlement authority remain separate.
- [ ] Application owns all authoritative responses.
- [ ] Structured choices survive restart without raw conversation persistence.
- [ ] No pending revision or `/cancel-revision` exists.
- [ ] Material ready revision immediately invalidates the old preparation.
- [ ] Text cannot submit.
- [ ] Card confirmation creates at most one immutable request.
- [ ] Deterministic and live-model evidence pass.
- [ ] As-built documentation is reconciled after evidence, not before.

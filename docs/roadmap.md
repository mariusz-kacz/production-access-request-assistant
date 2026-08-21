# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-08-21
- **Current baseline**:
  [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)

## Purpose

This document records credible follow-on product work without independently changing
the active baseline. A roadmap item becomes authoritative only after its business
requirement is approved and the product baseline, constitution, specification,
contracts, and tests are updated together.

The delivered environment-resolution increment is incorporated into the active
product baseline. It narrows model-assisted discovery to production
environments and establishes the exact two-tool MCP surface:

- `get_production_environment`
- `get_incident`

`get_production_environment` supports bounded environment discovery and exact
lookup. Each returned environment includes its authoritative client relationship
and assigned roles, so a separate role-listing capability is unnecessary.
`get_incident` remains an exact-identifier lookup. Incident listing, search, title
matching, and semantic inference are outside the baseline.

The checked-in runtime, current MCP contract, model allowlist and instructions,
tests, security analysis, and operator guidance now describe that delivered design.
The next proposed product increment is documented in the
[deterministic conversational request-intake specification](../SPEC-deterministic-request-intake.md).
It changes preparation, revision, response ownership, and turn ordering while keeping
Adaptive Card confirmation as the only request-creation action. Until implementation,
validation, and documentation reconciliation are complete, the current product
baseline and as-built preparation flow remain authoritative.

## Proposed Near-Term Increment: Deterministic Conversational Request Intake

### Business problem

The current interpreter returns a complete nullable candidate on every turn and is
prompted to preserve previously accepted fields. This makes correct multi-turn state
preservation depend too heavily on probabilistic model behavior. Model-authored
clarification prose also mixes language interpretation with authoritative response
presentation, and current turn coordination does not serialize the complete
load-through-commit boundary.

### Target behavior

The model interprets only the latest requester turn and returns a closed dialogue act
plus explicit `keep`, `set`, or `clear` operations for environment, role,
justification, and optional incident. Core owns the canonical candidate and applies a
changed operation only when deterministic current-message or persisted ordered-choice
evidence supports it. Client remains derived from authoritative environment data.

Application code owns field values, complete environment and role choices, progress,
clarification questions, validation guidance, review cards, and workflow outcomes.
The model returns no requester-facing prose and cannot filter, rank, or truncate
environment choices. When exact environment evidence is absent, the application shows
the complete bounded authoritative environment catalog in stable order. Field removal
uses exact application-owned commands rather than a language heuristic.

Adaptive Card confirmation remains the only request-creation path. An evidence-backed
unfinished revision persists a sanitized pending candidate and suspends confirmation
of the old ready snapshot. The old card creates no request while that revision is
pending. Exact `/cancel-revision` discards the pending change and restores the same
unexpired snapshot without refreshing its deadline. A completed revision atomically
supersedes the old snapshot and creates a new card.

### Delivery sequence

#### Increment 1: Interpretation and reducer boundary

- Replace complete-candidate model output with the closed dialogue-act and patch
  contract.
- Normalize value-equal `set` operations and reject changed operations without exact
  evidence.
- Implement the fixed dependency cascades, authoritative identifier reload, client
  derivation, justification authorship check, and syntactic justification floor.
- Route exact field-clear commands without a model call.

**Exit gate:** deterministic unit tests prove that context loss, snapshot-shaped model
output, unsupported clearing, and unrequested field changes cannot corrupt canonical
state.

#### Increment 2: Application-owned conversation

- Replace model-authored clarification and progress responses with deterministic
  application rendering.
- Render the complete authoritative environment catalog and environment-specific role
  choices in stable application-owned order.
- Persist only the ordered structured clarification context required for exact and
  ordinal replies; do not persist raw conversation or model response prose.

**Exit gate:** scripted multi-turn integration tests prove that ambiguous input is not
guessed, choices survive restart, and every displayed authoritative value comes from
reloaded application data.

#### Increment 3: Ready revision and concurrency safety

- Serialize one authenticated actor/conversation turn across load, interpretation,
  reduction, persistence, and presentation selection.
- Persist sanitized pending ready-draft revisions and suspend confirmation while they
  exist.
- Add deterministic `/cancel-revision`, typed `RevisionPending` confirmation failure,
  best-effort old-card visual replacement, and atomic ready-snapshot replacement.
- Enforce durable uniqueness and optimistic-concurrency behavior for active intakes.

**Exit gate:** restart, rollback, stale-card, revision-versus-confirmation, duplicate,
and concurrent-turn tests converge without partial replacement or request creation
from a suspended card.

#### Increment 4: End-to-end evidence and documentation reconciliation

- Preserve the current closed card payload and authoritative submission revalidation.
- Verify the complete Teams preparation-to-confirmation journey with deterministic
  model clients and no live-model dependency.
- Reconcile the product baseline, architecture, security model, intake orchestration,
  testing strategy, operator guidance, and roadmap only after the implementation is
  proven as-built.

**Exit gate:** required build and test suites pass sequentially, all specification
acceptance scenarios have evidence, and the current documentation authority set
describes one consistent runtime behavior.

### Explicitly excluded from this increment

- Natural-language submission or any model-visible state-changing tool.
- Model-generated requester response prose.
- Model-proposed environment filtering, ranking, narrowing, or choice subsets.
- Heuristic rewriting or instruction-phrase classification of authoritative display
  values beyond ordinary output encoding and labeled rendering.
- Card-delivery receipt or acknowledgement persistence.
- Collecting-intake idle expiry.
- Additional requester channels, MCP tools, services, dependencies, workflow engines,
  or distributed infrastructure.

## Delivered Increment: Environment Identifier Resolution

### Business problem

Requesters know a client or environment by its familiar name but may not know the
stable production-environment identifier. Requiring them to leave the conversation
and find that identifier adds avoidable friction.

### Delivered behavior

The assistant may read a bounded authoritative set of production environments and
interpret the requester's readable description. One unambiguous environment may be
proposed; multiple matches require a focused clarification; no match must not produce
an invented identifier. The selected environment supplies the authoritative client
and currently assigned role choices.

An optional incident must be supplied using its precise stable identifier. The
assistant may validate that identifier but must not discover or infer it from a title
or problem description.

### Trust and authorization boundaries

- Both MCP tools remain read-only and use explicit typed schemas.
- Environment candidates and model selection remain untrusted until deterministic
  application services validate the stable environment, client relationship, and
  requested role.
- Incident validation uses only the precise stable identifier supplied by the
  requester.
- MAF and MCP receive no submit, approval, provisioning, retry, revocation, workflow,
  credential, arbitrary-database, generic-query, or separate role-listing capability.
- Authenticated server context remains the only source of acting identity.
- Confirmation and all subsequent workflow transitions bypass the model.

### Delivered acceptance boundaries

- A developer can identify one unambiguous environment without knowing its stable ID.
- Zero, one, and multiple environment matches produce distinct safe outcomes.
- Environment choices contain stable identifiers, readable context, authoritative
  client relationships, and assigned roles.
- Identifier-like environment values use exact lookup only. Exact `NotFound` keeps
  scope unresolved and asks for correction with no discovery alternatives; readable
  environment descriptions continue to use bounded discovery.
- A model-authored clarification message is shown only after its separate structured
  option IDs are reloaded and validated; selectable labels and identifiers come from
  authoritative records, never prose.
- Role choices shown to the requester are limited to those assigned to the selected
  environment and are independently validated before submission.
- Incident descriptions and partial identifiers are never mapped to an incident.
- Unknown tools, excessive results, malformed results, prompt injection, timeout,
  cancellation, and dependency failure create no request, approval, operation, or
  grant.
- Logs record correlation, tool name, duration, and outcome without recording raw
  prompts, transcripts, or complete MCP payloads.

## Proposed Future Direction: Enterprise Production Adoption

Enterprise production readiness is a proposed sequence of separately approved
extensions, not a single implementation increment. The current synthetic solution is
the starting point for that sequence; it is not evidence that real production access
is safe. No stage below is authorized by this roadmap, and approval of one stage does
not authorize the next.

The default target remains one modular ASP.NET Core host with a thin co-hosted React
UI. A second deployable, distributed coordination, or a generic workflow platform is
introduced only when an approved requirement and measured operating need justify the
additional boundary.

### Invariants that carry into every stage

- AI may interpret user language and gather bounded context, but its output remains
  untrusted. Authenticated humans approve, and deterministic services authorize and
  execute every state change.
- Authenticated server context supplies the acting identity. Client or model payloads
  cannot choose identity, claims, approver, environment scope, role, duration, or
  idempotency identity.
- Human decisions remain authenticated structured actions bound to one immutable
  request ID and exact scope. Corrections create a new request and new approvals.
- Provisioning remains unavailable to the model. The protected provisioning boundary
  reloads persisted request, approval, operation, and grant evidence before acting.
- MCP remains bounded, typed, and read-only and must be authenticated before any
  production use. The exact two-tool surface remains the default unless a separately
  approved baseline change demonstrates a concrete need.
- Domain and application policy remains provider-independent in
  `GovernedAccess.Core`; enterprise identity, data, AI, Teams, MCP, and provisioning
  contracts are translated at system boundaries.
- Raw prompts, transcripts, provider traces, secrets, and complete tool payloads are
  not retained by default and are never authorization evidence.

### Extension map from the current increment

| Current synthetic boundary | Proposed enterprise-ready extension |
| --- | --- |
| Synthetic authenticated actors and local claims | Enterprise workforce and workload identity, authoritative group/claim mapping, lifecycle governance, and explicit separation of requester, approver, operator, and service identities |
| Local SQLite persistence and single-host assumptions | Managed transactional persistence with encryption, least-privilege access, tenant or row isolation where required, tested backup/restore, recovery objectives, and multi-instance concurrency evidence before horizontal scale |
| Process-local conversational presentation state | Durable, privacy-bounded evidence that the exact immutable snapshot was presented, with expiry and recovery rules; conversation text remains neither durable authority nor submission evidence |
| Local unauthenticated MCP transport | Authenticated and authorized MCP transport with network boundaries, rate and result limits, explicit timeouts, safe telemetry, and the same narrow typed read-only tools |
| Seeded environment, incident, requester, and policy data | Authoritative enterprise adapters with freshness guarantees, ownership, typed failure outcomes, and deterministic revalidation immediately before consequential transitions |
| Synthetic provisioning provider | Protected real-provider adapter with least-privilege workload identity, request-ID idempotency, bounded retries, expiry and revocation, reconciliation, and fail-closed recovery from ambiguous outcomes |
| Local logs and manual evaluation runs | Protected centralized audit and observability, versioned prompts/models/datasets, automated regression gates, drift monitoring, alerting, and evidence retention aligned with privacy policy |

### Stage 0: Adoption decision and governed target

Before production engineering begins:

- define the business scope, supported access types, data classification, risk
  tolerance, compliance obligations, service objectives, recovery objectives, and
  accountable owners;
- decide whether model-interpreted request preparation followed by deterministic
  structured confirmation is acceptable for the target risk class, including
  interpretation-error tolerance, deterministic fallback, operator disablement, and
  rollback behavior;
- reconcile the constitution, product baseline, security model, feature
  specifications, and architecture decisions before implementing any conflicting
  capability; and
- create a production threat model and abuse-case inventory covering identity,
  authorization, prompt injection, confused-deputy paths, replay, concurrency,
  privacy, provider compromise, and operational recovery.

**Exit gate:** approved product baseline and architecture decisions, named risk
owners, measurable acceptance criteria, and an agreed release evidence package.

### Stage 1: Enterprise identity and trust perimeter

- Replace synthetic actors with enterprise authentication while keeping all
  authorization claims server-derived and revalidated.
- Establish application, MCP, Teams, operator, and provisioning workload identities
  with least privilege, secret or certificate rotation, and auditable ownership.
- Add network controls, request limits, security headers, dependency and artifact
  scanning, and explicit trust-boundary tests.
- Prove requester, business approver, DevOps approver, and operator separation with
  negative integration tests.

**Exit gate:** identity and authorization threat-model review, credential lifecycle
runbooks, penetration-test findings addressed to the accepted threshold, and no
browser or model-controlled authority path.

### Stage 2: Durable state, audit, and recovery

- Move authoritative workflow state to managed transactional persistence with
  encryption, constrained access, integrity protections, schema migration, and
  tested restore procedures.
- Preserve immutable request scope, optimistic concurrency, transition legality, and
  request-ID idempotency under duplicate delivery, restart, failover, and multiple
  application instances.
- Protect security audit records from application-level alteration and define
  retention, access, export, and deletion policy for personal and operational data.
- Add reconciliation for incomplete operations and document how operators recover
  without bypassing approval evidence.

**Exit gate:** load and failure-injection evidence, backup/restore and disaster
recovery exercise, retention approval, and demonstrated convergence after ambiguous
or concurrent outcomes.

### Stage 3: Authoritative enterprise data and real provisioning

- Integrate environment, incident, requester, role, and policy sources through narrow
  adapters with bounded reads, freshness rules, ownership, and typed degradation.
- Revalidate mutable authoritative facts at submission, approval, and provisioning
  boundaries rather than trusting earlier model or conversation context.
- Introduce the real provisioning adapter behind the existing protected handler;
  keep provider credentials, approval APIs, and provisioning methods unavailable to
  the model and MCP.
- Implement deterministic grant expiry, revocation, reconciliation, safe retry, and
  evidence for partial or externally successful operations.

**Exit gate:** provider sandbox and fault-injection evidence, least-privilege review,
reconciliation and revocation drills, and end-to-end proof that no access is granted
without the exact required approvals.

### Stage 4: AI assurance, privacy, and release governance

- Version and approve models, prompts, schemas, tool contracts, datasets, graders,
  and thresholds as one releasable AI configuration.
- Run deterministic tests without a live model, then run a separate live-model
  preparation evaluation and deterministic consequential-action security tests before
  promotion.
- Measure both correct action and correct restraint across adversarial, ambiguous,
  multilingual, stale-context, duplicate-delivery, and provider-failure scenarios.
- Add change control, drift detection, privacy review, safe telemetry, cost budgets,
  provider timeouts, and a tested ability to disable model-mediated preparation
  without weakening structured confirmation or downstream controls.

**Exit gate:** approved evaluation report with version evidence, security and privacy
sign-off, threshold compliance, rollback rehearsal, and no unresolved high-severity
failure mode.

### Stage 5: Operational readiness and staged rollout

- Establish service ownership, on-call response, security incident handling, user
  support, access reviews, audit export, capacity planning, and dependency failure
  procedures.
- Define service-level indicators for request progression, denied transitions,
  dependency health, model restraint, provisioning latency, reconciliation backlog,
  expiry, revocation, and suspicious use without logging sensitive content.
- Roll out through internal test, non-production pilot, limited production cohort,
  and broader adoption with explicit entry, observation, rollback, and stop criteria.
- Review the risk assessment and release evidence after each cohort before expanding
  scope, identity population, access role, environment, or provider.

**Exit gate:** production readiness review, exercised rollback and incident runbooks,
stable pilot evidence, accountable approval to expand, and continuing periodic access
and AI-governance reviews.

## Not Authorized by This Roadmap

This roadmap proposes an adoption shape; it does not authorize real identity, real
production access, provider credentials, production data, deployment, or a topology
change. Those require their own approved baseline, specifications, architecture and
security decisions, implementation plans, and release evidence.

The following remain outside the roadmap unless a separate approved requirement and
authority change explicitly introduces them:

- incident discovery, listing, title matching, or semantic search;
- a separate role-listing tool;
- any model-visible submission, approval, provisioning, retry, revocation,
  credential, or other state-changing capability;
- agent-directed approval or provisioning;
- a generic enterprise search or arbitrary database-query tool;
- retention of raw transcripts or provider traces as workflow or authorization
  evidence;
- multi-agent orchestration;
- a generic workflow engine; or
- a second deployable service or distributed infrastructure without demonstrated
  need and an approved architecture change.

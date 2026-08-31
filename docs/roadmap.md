# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-08-31
- **Current baseline**: [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)

## Purpose

This roadmap records credible follow-on work without independently changing the
running product. An item becomes current only after its specification, architecture
decisions, implementation, contracts, deterministic tests, any required live evidence,
and as-built documentation are reconciled.

## Current delivered baseline

The verified runtime is one modular ASP.NET Core executable with:

- one agent-interpreted request-preparation path for every nonblank requester message
  except the exact `/new` protocol command;
- a closed dialogue act and ordinary sparse `set`/`clear` patch over environment,
  role, justification, and incident;
- deterministic Core ownership of proposal structure, one atomic scope group,
  independent justification, authoritative search/reload, dependency cascades,
  readiness, lifecycle, optimistic concurrency, and confirmation;
- exactly four read-only MCP capabilities:
  `search_production_environments`, `get_production_environment`,
  `get_environment_roles`, and `get_incident`;
- application-owned requester guidance and bounded environment/role choices persisted
  as provider-neutral clarification context;
- immutable ready `PreparationId`, mandatory predecessor linkage for revisions, a
  30-minute ready deadline, and unique request `PreparationId` for confirmation replay;
- separate `GovernedAccess.ReferenceAuthority` and
  `GovernedAccess.Workflow.Persistence` projects, contexts, migrations, seeders, and
  SQLite databases composed in the same host; and
- the unchanged authenticated human approval, fixed eight-hour grant, protected
  request-keyed provisioning, retry, and audit boundaries.

Each message uses a fresh provider session. The application persists canonical
candidate state and at most one ordered bounded clarification context, not raw
requester transcripts, prompts, provider sessions, reasoning, raw proposals,
agent-authored search queries, or complete MCP payloads.

The previous complete-candidate interpreter, two-tool catalog, process-local choice
history, delivered lifecycle/reserved-ID model, Web-owned unified database, and
parallel replacement graph are historical implementation states. They are neither
registered nor supported as compatibility or upgrade paths.

## Delivered evidence

The deterministic gates prove project and database ownership, sparse-proposal and
grouped-reducer behavior, four-tool contracts, restart-safe clarification, optimistic
concurrency, immutable confirmation, the complete approval/provisioning journey, and
absence of the deleted graph.

The historical promoted credentialed run on 2026-08-28 passed all 12 promoted groups
and both advisory groups in its recorded dataset without selective reruns or waivers.
A retained 2026-08-31 clean-source full-inventory run passed all 14 promoted groups
and 42 variations with every absolute safety gate passing and zero requests,
decisions, operations, or grants. It covered the current
`deterministic-intake-3.1.0` bytes, and its source commit matched the clean evaluated
`HEAD` during retention review, making it current-dataset, clean-source promotion
evidence for the recorded versions. Generated output remains gitignored; reviewed
immutable copies are indexed from the
[live-model evaluation guide](live-model-evaluation.md).

## Deliberate current exclusions

- Deterministic interpretation of requester free text beyond exact `/new`.
- Natural-language request creation or submission.
- Model-visible state-changing or credential-bearing capabilities.
- Generic enterprise search, arbitrary database queries, cross-environment role
  search, or incident discovery.
- Model-authored requester-visible prose or agent-selected response locale.
- Durable raw prompts, transcripts, raw search queries, provider sessions, model
  reasoning, or complete tool payloads.
- Mutable ready scope, pending ready revisions, rollback, or `/cancel-revision`.
- Background expiry workers or collecting-inactivity TTLs.
- In-place upgrades or compatibility adapters for disposable local SQLite data.
- Another requester channel, second agent, multi-agent workflow, generic workflow
  engine, RAG subsystem, extra deployable service, message broker, distributed lock,
  or cross-database transaction.
- Changes to the downstream approval, provisioning, retry, fixed-duration, or grant
  expiry rules.

## Future direction: enterprise production adoption

The portfolio implementation is synthetic. Moving toward production requires a new
governed target rather than treating the local architecture as production-ready.

### Invariants that carry forward

- AI interprets requester language and gathers bounded read-only context;
  deterministic services own canonical state, authority, lifecycle, and state changes.
- Exact protocol commands and structured UI actions are closed; deterministic services
  do not infer meaning from unrestricted requester text.
- Authenticated server context supplies acting identity and claims.
- Human decisions bind to one immutable request ID and exact scope.
- Provisioning remains unavailable to the model and reloads persisted evidence.
- The model-visible catalog remains exact, typed, read-only, and contract-controlled.
- Environment, entitlement, incident, and policy facts are revalidated at
  consequential boundaries.
- Raw prompts, transcripts, search queries, provider traces, secrets, and complete tool
  payloads are not workflow or authorization evidence.
- One host remains appropriate until a measured ownership, security, scale, or
  availability requirement justifies another boundary.

### Stage 0: governed adoption decision

Define supported access types, risk tolerance, data classification, compliance,
service/recovery objectives, accountable owners, model disablement, deterministic
fallback, and release evidence.

**Exit gate:** approved product baseline, threat model, named risk owners, and
measurable acceptance criteria.

### Stage 1: enterprise identity and trust perimeter

Replace synthetic identities with workforce/workload identity, least-privilege
credentials, network controls, abuse protection, and negative authorization evidence.

**Exit gate:** identity/security review and no client- or model-controlled authority
path.

### Stage 2: durable state, audit, and recovery

Move to managed transactional persistence, encryption, migrations, retention,
immutable audit export, backup/restore, reconciliation, and tested recovery objectives.

**Exit gate:** failure-injection and disaster-recovery evidence demonstrates no lost
approval, duplicate grant, or irreconcilable state.

### Stage 3: authoritative data and real provisioning

Replace synthetic adapters with governed CMDB/service-catalog, IAM/entitlement, ITSM,
approver-directory, and provisioning integrations. Preserve separate authority,
least-privilege credentials, exact reload, idempotency, typed failures, and
reconciliation.

**Exit gate:** sandbox/end-to-end evidence proves client isolation, approval binding,
provider idempotency, and recovery from partial outcomes.

### Stage 4: AI assurance and operational rollout

Establish versioned prompts/models/tool contracts, offline and credentialed evaluation,
adversarial testing, release thresholds, telemetry, SLOs, alerts, runbooks, canarying,
rollback, kill switch, and deterministic degradation behavior.

**Exit gate:** operational owners approve measured quality, safety, reliability, cost,
and rollback evidence.

## Not authorized by this roadmap

This roadmap does not authorize production deployment, autonomous approval,
model-driven provisioning, generic enterprise search, dynamic tool creation, durable
prompt retention, additional protocol commands, multi-agent expansion, or extra
deployable services. Each requires a separate approved baseline change with threat
analysis, contracts, tests, and operations evidence.

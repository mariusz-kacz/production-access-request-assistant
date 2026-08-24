# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-08-22
- **Current baseline**: [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)
- **Next feature**: [Deterministic Conversational Request Intake](../SPEC-deterministic-request-intake.md)

## Purpose

This roadmap records credible follow-on work without independently changing the
running product. A roadmap item becomes current only after its specification,
architecture decisions, implementation, contracts, tests, and as-built documentation
are reconciled.

## Current delivered baseline

The running baseline currently exposes two read-only model-visible tools:

- `get_production_environment`, combining bounded discovery, exact environment lookup,
  owning client, and assigned roles; and
- `get_incident`, providing exact incident lookup.

The current interpreter returns one complete nullable candidate plus candidate or
clarification outcome. Those statements remain authoritative until the next feature is
implemented and verified.

## Proposed near-term increment: deterministic conversational request intake

### Business problem

The current full-candidate response asks the model to reproduce previously accepted
state on every turn. A schema-valid model response can therefore be stale, omit a
value, or reconstruct an unsupported snapshot. Model-authored clarification prose also
mixes language interpretation with authoritative application presentation.

### Target value

Use the model where probabilistic language interpretation is useful while making every
state, authority, and consequence deterministic:

- the model returns one dialogue act and a sparse `set`/`clear` patch;
- omitted fields are safe no-ops;
- Core owns canonical state, merge, search, exact reload, validation, readiness, and
  lifecycle;
- application code owns all authoritative responses and cards;
- four read-only MCP capabilities model distinct enterprise authorities; and
- authenticated card confirmation remains the only request-creation action.

### Target MCP capability model

| Capability | Enterprise-shaped authority |
|---|---|
| `search_production_environments` | Service-catalog/CMDB discovery projection |
| `get_production_environment` | Exact environment registry and owning client |
| `get_environment_roles` | IAM/entitlement assignments for one environment |
| `get_incident` | Exact ITSM incident state and affected environment |

The synthetic implementation may share SQLite, but the contracts preserve independent
ownership, permissions, freshness, latency, and failure behavior. The additional tools
exist because the facts are independently governed, not to create more calls for the
evaluator.

### Search and selection policy

Core independently reruns every requester-backed deterministic environment search:

- zero matches -> no-match clarification;
- one match -> exact reload and canonical acceptance;
- multiple matches -> complete application-owned choices;
- more than 20 -> too-broad guidance, never truncation.

A unique result does not require a ceremonial second requester turn because Core, not
the model, proves uniqueness and exact identity. Final scope is still exposed on the
mandatory review card.

### Ready revision policy

The feature keeps only one active candidate. There is no pending revision or
`/cancel-revision` flow.

- discussion, submission guidance, value-equal proposals, unsupported proposals, and
  model/source failures preserve the exact ready snapshot;
- the first accepted material revision atomically supersedes the old ready preparation
  and creates a new collecting or ready intake;
- the old card becomes durably stale immediately; and
- a replacement ready intake receives a new identity and 30-minute deadline.

This is intentionally less convenient than rollback but substantially simpler and
safer to implement and explain.

## Delivery sequence

### Increment 0: governance and decisions

- Approve constitution amendment `3.0.0`.
- Approve ADRs for sparse patches/reducer, four source-oriented context capabilities,
  and bounded structured clarification persistence.
- Remove conflicting proposed claims about required `keep` operations, single-result
  selection, strict same-turn choreography, or pending revisions.

**Exit gate:** one consistent target authority set.

### Increment 1: sparse interpretation and reducer

- Introduce provider-neutral dialogue acts, sparse field operations, operation
  verdicts, and closed outcomes.
- Implement evidence checks, dependency cascades, canonical validation, client
  derivation, and justification floor.
- Route exact clear commands and `/new` deterministically.

**Exit gate:** unit tests prove model omission/context loss cannot corrupt state.

### Increment 2: four MCP capabilities and authoritative ports

- Split search, exact environment, and environment-role contracts.
- Keep exact incident lookup.
- Add independent environment, entitlement, and incident Core ports/adapters.
- Preserve exact allowlist, read-only annotations, call bounds, timeout, and
  cancellation.

**Exit gate:** real MCP transport tests prove exact catalog and independent source
failures.

### Increment 3: application-owned search and responses

- Core independently handles zero, unique, multiple, and too-broad searches.
- Persist complete stable choices only when selection is actually required.
- Render all progress, choices, questions, corrections, cards, and failures in
  application code.
- Keep discussion bounded rather than model-authored.

**Exit gate:** every displayed authoritative value comes from Core; the model cannot
silently choose or narrow scope.

### Increment 4: persistence, restart, and ready revision

- Persist one provider-neutral structured clarification context bound to preparation
  and candidate version.
- Support exact/ordinal selection after restart without raw conversation history.
- Implement atomic ready supersession plus replacement intake.
- Prove active-intake uniqueness and revision/confirmation race convergence.

**Exit gate:** one active candidate, no pending-revision state, stale old card rejected.

### Increment 5: confirmation regression, evaluation, and documentation

- Re-prove exact card confirmation and replay.
- Rebalance live evaluation to approximately 12 high-value scenarios.
- Grade canonical outcomes and restraint; report tool sequence diagnostically.
- After evidence, reconcile the product baseline, architecture, security, contracts,
  orchestration, testing, operator guidance, README, ADR statuses, and roadmap.

**Exit gate:** deterministic gates and reviewed live evaluation pass with zero
consequential side effects, and all current docs describe one runtime truth.

## Explicitly excluded from the increment

- Natural-language submission.
- Any model-visible state-changing or credential-bearing capability.
- Generic enterprise search, arbitrary database query, cross-environment role search,
  or incident discovery.
- Model-authored consequential response prose.
- Durable raw prompts, transcripts, provider traces, or complete tool payloads.
- Pending ready-draft revisions, rollback, or `/cancel-revision`.
- Another requester channel, second agent, multi-agent workflow, generic workflow
  engine, RAG subsystem, new deployable service, message broker, or distributed lock.
- Changes to downstream approvals, provisioning, retries, audit, fixed duration, or
  grant expiry.

## Delivered increment: environment identifier resolution

The delivered baseline remains evidence that the assistant can:

- resolve readable production-environment descriptions over a bounded catalog;
- distinguish zero, one, and multiple matches;
- use exact incident identifiers only;
- keep model-visible context read-only;
- independently validate environment, client, role, and incident facts; and
- create no request until authenticated card confirmation.

The proposed feature replaces the combined environment tool and full-candidate
interpreter only after its new evidence passes. Historical retained evaluation remains
evidence for the delivered design, not proof of the replacement.

## Future direction: enterprise production adoption

The portfolio implementation is synthetic. Moving toward production would require a
new governed target rather than treating the local architecture as production-ready.

### Invariants that carry forward

- AI interprets and gathers bounded context; deterministic services own authority and
  state changes.
- Authenticated server context supplies acting identity and claims.
- Human decisions bind to one immutable request ID and exact scope.
- Provisioning remains unavailable to the model and reloads persisted evidence.
- The model-visible catalog remains exact, typed, read-only, and contract-controlled.
- Environment, entitlement, incident, and policy facts are revalidated at
  consequential boundaries.
- Raw prompts, transcripts, provider traces, secrets, and complete tool payloads are
  not workflow or authorization evidence.
- One host remains appropriate until a measured ownership, security, scale, or
  availability requirement justifies another boundary.

### Stage 0: governed adoption decision

Define supported access types, risk tolerance, data classification, compliance,
service/recovery objectives, accountable owners, model disablement, deterministic
fallback, and release evidence.

**Exit gate:** approved product baseline, threat model, named risk owners, and measurable
acceptance criteria.

### Stage 1: enterprise identity and trust perimeter

Replace synthetic identities with workforce/workload identity, least-privilege
credentials, network controls, abuse protection, and negative authorization evidence.

**Exit gate:** identity/security review and no client- or model-controlled authority
path.

### Stage 2: durable state, audit, and recovery

Move to managed transactional persistence, encryption, migrations, backup/restore,
retention, multi-instance concurrency, protected audit, and reconciliation of
ambiguous operations.

**Exit gate:** load/failure evidence, recovery exercise, and convergence under duplicate
or concurrent delivery.

### Stage 3: authoritative data and real provisioning

Integrate environment registry, IAM/entitlements, ITSM, policy, and provisioner through
narrow adapters with explicit freshness, failure, revalidation, idempotency,
revocation, and reconciliation.

**Exit gate:** sandbox/fault-injection evidence and proof that no access is granted
without exact approvals.

### Stage 4: AI assurance and operational rollout

Version models, prompts, schemas, tool contracts, datasets, and thresholds. Add drift,
privacy, cost, rollback, observability, on-call, incident handling, and staged rollout
criteria.

**Exit gate:** approved evaluation/security/privacy evidence and stable limited pilot.

## Not authorized by this roadmap

This roadmap does not authorize real identity, production data, credentials, access,
deployment, or topology change. It also does not authorize:

- incident discovery or semantic incident search;
- a fifth model-visible tool;
- generic or cross-scope role discovery;
- model-visible submission, approval, provisioning, retry, or revocation;
- agent-directed human decisions;
- raw transcript retention as authority;
- multi-agent orchestration; or
- additional deployable/distributed infrastructure without demonstrated need and a
  separately approved architecture change.

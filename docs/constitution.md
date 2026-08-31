# Governed Production Access Request Assistant Constitution

## Core Principles

### I. Human Approval, Deterministic Authorization

AI MAY interpret requester intent, prepare a typed draft, and gather approved context.
Authenticated humans MUST record business and DevOps decisions as explicit structured
actions. Deterministic application services MUST authorize every state change and MUST
execute provisioning. Model output, conversation text, tool visibility, and
model-reported validation results MUST NOT constitute authorization or approval
evidence. This separation keeps probabilistic interpretation outside the security
boundary.

### II. Untrusted AI, Bounded and Governed MCP

All model output MUST be schema-validated, and every model-proposed identifier and
business value MUST be checked against authoritative application data before use.
Domain logic MUST NOT depend on AI-provider or MCP SDK contracts; infrastructure
adapters MUST translate those contracts at the boundary.

The model MUST receive only the exact MCP allowlist defined by the active product
baseline and its machine-readable contract. The allowlist MUST contain narrow, typed,
read-only context capabilities associated with named authoritative responsibilities.
It MUST NOT contain submission, approval, provisioning, revocation, retry,
workflow-transition, credential, arbitrary-database, generic-query, or other
state-changing capabilities.

Discovery and exact lookup MAY be separate capabilities when they have materially
different input, result, authority, freshness, or failure semantics. Independently
governed facts, such as environment metadata and environment-scoped entitlement
assignments, MAY remain separate capabilities even when the synthetic implementation
shares storage. Generic or cross-scope search MUST NOT be introduced merely to reduce
application validation.

Search or discovery output MUST NOT become canonical scope solely because the model
observed it. Deterministic application code MUST independently reproduce applicable
search policy or exact-reload the selected entity and MUST validate every proposed
environment, client, role, and incident relationship. Model-visible tool results aid
interpretation only and are never authorization evidence.

Any model-visible catalog change MUST have an approved specification, architecture
decision, threat-boundary review, closed contract, negative tests, and synchronized
product documentation. LLM, MCP, and authoritative-source calls MUST support
cancellation, explicit timeouts, and typed failures. These controls bound unreliable
or compromised AI behavior while permitting proportionate evolution of the read-only
context surface.

### III. Authenticated, Immutable, Client-Isolated Scope

Acting identity and authorization claims MUST come from authenticated server context;
browser-submitted identities, roles, approver assignments, and claims MUST NOT be
trusted. The requester MUST NOT choose the business approver. Each approval MUST bind
to one server-generated immutable request ID and its exact scope. Submitted requests
MUST remain read-only; any correction MUST create a new request and require new
approvals. Authorization for one client environment MUST NOT apply to another.
DevOps MUST NOT change the business-approved role or increase duration. A duration
reduction is permitted only when the active product baseline defines an adjustable
duration; the current fixed eight-hour baseline permits no duration change. These
rules prevent confused-deputy, cross-client, and post-approval scope escalation.

### IV. Evidence-Validated, Idempotent Provisioning

Provisioning MUST remain unavailable to the model and MUST be invoked only through a
protected internal application path. The handler MUST receive only the stable request
reference, reload persisted request, approval, and operation evidence, and validate
that evidence independently. It MUST NOT trust caller-supplied approval assertions.
The immutable request ID MUST be the provisioning idempotency identity; retries for
the same approved operation MUST return the existing grant or safely resume without
creating a duplicate. Failures MUST produce typed outcomes and auditable state
transitions. This ensures execution is authorized by durable evidence rather than by
the caller's claims.

### V. Proportionate Modular Architecture

The solution MUST remain one executable modular ASP.NET Core host with a thin React UI
served by that host, local synthetic identity and data, and no real production access.
Domain and application rules MUST remain independent of React, persistence,
AI-provider, and MCP SDK details. The project MUST NOT add a generic workflow engine,
multi-agent design, large RAG subsystem, separate deployable services, or
distributed-system infrastructure without an approved baseline amendment and a
documented concrete need. New projects, modules, and abstractions MUST solve a current
boundary or testability requirement. This keeps the portfolio implementation
understandable and proportionate to its single-host scope.

## Product and Technical Constraints

- `docs/governed-production-access-product-baseline.md` defines the active product
  behavior, synthetic reference data, supported workflow, explicit MCP allowlist, and
  non-goals.
- The application MUST expose one real read-only MCP endpoint containing exactly the
  tools in the active machine-readable contract and no additional model-visible
  capability. Inputs and outputs MUST use explicit closed schemas, and authoritative
  results MUST use stable identifiers.
- Nullable reference types MUST be enabled, warnings MUST be treated as errors, and
  `CancellationToken` MUST cross asynchronous boundaries.
- Expected failures MUST use explicit typed outcomes. LLM, MCP, and external context
  integrations MUST have explicit timeouts.
- Logs MUST NOT contain secrets, raw prompts, or complete MCP payloads by default.
  They MUST record correlation IDs, authenticated actors, decisions, statuses,
  workflow transitions, operation metadata, and model/MCP duration and outcome.
- OpenTelemetry and other operational polish MAY be added only when they do not block
  completion of the governed vertical slice.

## Change Design and Delivery

- Every material behavior change MUST identify affected actors, authoritative data,
  trust boundaries, state changes, immutable scope, failure behavior, and audit
  evidence. A non-applicable concern MUST be stated with a rationale.
- Planned changes MUST be checked against this constitution before implementation.
  Any exception MUST document the concrete need and the rejected simpler alternative.
- Tests MUST run without a live LLM and MUST use a deterministic fake chat client.
  Domain rules require unit tests. MCP contracts and interactions require integration
  tests.
- Authorization, client isolation, immutable scope, invalid transitions, persisted
  provisioning evidence, idempotency, malformed model output, and MCP/source failure
  or timeout MUST have negative-path coverage where affected.
- A change is complete only when its applicable tests pass, warnings-as-errors builds
  pass, model/MCP cancellation and timeout behavior is preserved, and documentation
  reflects any changed contract, trust boundary, or operational behavior.

## Governance

This constitution governs product and technical changes, implementation, and review.
Conflicting lower-level guidance MUST be corrected or the constitution MUST be
explicitly amended before the conflicting change proceeds. The product baseline
remains the canonical description of current behavior within these governing
constraints.

Amendments require a documented proposal describing the motivation, affected
principles, compatibility impact, migration work, and dependent artifacts. The
project owner MUST approve the amendment and update affected authoritative artifacts
in the same change. Versioning follows semantic versioning: MAJOR for incompatible
principle removal or redefinition, MINOR for a new principle or materially expanded
obligation, and PATCH for non-semantic clarification.

Every material change MUST be reviewed for compliance. Code review and completion
checks MUST verify the applicable principles, required negative tests, and
synchronization of contracts and runtime guidance. Complexity that violates a
principle MUST be rejected unless an approved amendment precedes it.

**Version**: 3.0.0 | **Ratified**: 2026-07-27 | **Last Amended**: 2026-08-22

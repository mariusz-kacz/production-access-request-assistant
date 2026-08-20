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

### II. Untrusted AI, Bounded MCP

All model output MUST be schema-validated, and every model-proposed identifier and
business value MUST be checked against authoritative data before use. Domain logic
MUST NOT depend on AI-provider or MCP SDK contracts; infrastructure adapters MUST
translate those contracts at the boundary. The model MUST receive an explicit
allowlist containing exactly `get_production_environment` and `get_incident`.
`get_production_environment` MUST provide bounded production-environment discovery,
exact environment lookup, authoritative client relationships, and the roles assigned
to each returned environment. `get_incident` MUST remain an exact-identifier lookup;
incident listing, search, and inference are outside the model-visible surface. MCP
MUST remain read-only and MUST NOT expose a separate role tool, approval,
provisioning, revocation, workflow-transition, arbitrary-database, or generic-query
tools. The application MUST independently validate every proposed environment-role
pair. LLM and MCP calls MUST support cancellation, explicit timeouts, and typed
failures. These controls bound unreliable or compromised AI behavior while keeping
the tool surface proportionate.

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
  behavior, synthetic reference data, supported workflow, and explicit non-goals.
- The application MUST expose one real read-only MCP endpoint with exactly the two
  tools listed in Principle II. Inputs and outputs MUST use explicit typed schemas,
  and authoritative results MUST use stable identifiers.
- Nullable reference types MUST be enabled, warnings MUST be treated as errors, and
  `CancellationToken` MUST cross asynchronous boundaries.
- Expected failures MUST use explicit typed outcomes. LLM and MCP integrations MUST
  have explicit timeouts.
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
  provisioning evidence, idempotency, malformed model output, and MCP failure or
  timeout MUST have negative-path coverage where affected.
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
in the same change. Versioning follows semantic versioning: MAJOR for incompatible principle
removal or redefinition, MINOR for a new principle or materially expanded obligation,
and PATCH for non-semantic clarification.

Every material change MUST be reviewed for compliance. Code review and completion
checks MUST verify the applicable principles, required negative tests, and
synchronization of contracts and runtime guidance. Complexity that violates a
principle MUST be rejected unless an approved amendment precedes it.

**Version**: 2.0.1 | **Ratified**: 2026-07-27 | **Last Amended**: 2026-08-20

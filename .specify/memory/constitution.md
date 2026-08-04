<!--
Sync Impact Report
- Version change: 1.0.0 -> 2.0.0
- Amendment rationale:
  - The fixed model-visible MCP catalog changes incompatibly from three tools to two.
  - Environment context now carries the roles assigned to each environment, removing
    the separate `get_available_roles` capability without weakening deterministic
    environment-role validation.
- Modified principles:
  - II. Untrusted AI, Bounded MCP (three-tool allowlist -> two-tool allowlist;
    environment discovery and exact-only incident boundaries made explicit)
- Added sections: None.
- Removed sections: None.
- Templates and dependent artifacts:
  - [updated] .specify/templates/plan-template.md
  - [reviewed] .specify/templates/spec-template.md (already refers generically to the
    constitution-defined fixed allowlist)
  - [reviewed] .specify/templates/tasks-template.md (contract and negative-test rules
    remain valid)
  - [reviewed] .agents/skills/speckit-*/SKILL.md (no hard-coded MCP catalog)
  - [updated] AGENTS.md
  - [updated] docs/governed-production-access-product-baseline.md
  - [updated] docs/roadmap.md
  - [updated] specs/004-resolve-context-identifiers/spec.md
  - [pending implementation] source, tests, MCP contract, historical feature
    artifacts, and as-built runtime guidance still describe the currently implemented
    three-tool surface until feature 004 is delivered
- Repository tracking:
  - [warning] .specify/ is ignored by .gitignore; use `git add -f` or revise the
    ignore policy if governance artifacts must be committed.
- Follow-up TODOs:
  - Implement feature 004, then synchronize the as-built architecture, README,
    security guidance, quickstarts, MCP contract, and affected tests with the
    two-tool runtime.
-->
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

## Specification and Delivery Workflow

- Every feature specification MUST identify affected actors, authoritative data,
  trust boundaries, state changes, immutable scope, failure behavior, and audit
  evidence. A non-applicable item MUST be stated with a rationale.
- Every implementation plan MUST pass the Constitution Check before research and
  again after design. Any exception MUST be recorded in Complexity Tracking with the
  concrete need and the rejected simpler alternative.
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

This constitution governs specifications, plans, tasks, implementation, and review.
Conflicting lower-level guidance MUST be corrected or the constitution MUST be
explicitly amended before the conflicting change proceeds. The product baseline
remains the canonical description of current behavior within these governing
constraints.

Amendments require a documented proposal describing the motivation, affected
principles, compatibility impact, migration work, and dependent artifacts. The
project owner MUST approve the amendment and update the Sync Impact Report in the
same change. Versioning follows semantic versioning: MAJOR for incompatible principle
removal or redefinition, MINOR for a new principle or materially expanded obligation,
and PATCH for non-semantic clarification.

Every feature specification and implementation plan MUST be reviewed for compliance.
Code review and completion checks MUST verify the applicable principles, required
negative tests, and synchronization of templates and runtime guidance. Complexity
that violates a principle MUST be rejected unless an approved amendment precedes it.

**Version**: 2.0.0 | **Ratified**: 2026-07-27 | **Last Amended**: 2026-08-04

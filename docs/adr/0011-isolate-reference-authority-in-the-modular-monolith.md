# ADR 0011: Isolate reference authority inside the modular monolith

- **Status**: Accepted target architecture
- **Date**: 2026-08-25
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `tasks/deterministic-request-intake.md`, ADR 0008, ADR 0010

## Context

The delivered application is one ASP.NET Core executable whose Web project owns one EF
Core context containing synthetic clients, environments, roles, incidents, principals,
request intake, requests, approvals, provisioning operations, grants, and audit events.
That implementation is proportionate for the delivered baseline, but the physical
layout obscures two different ownership and change boundaries:

1. reference authority: clients, environment eligibility, environment-role assignment,
   incident relationships, and deterministic environment search; and
2. request lifecycle: preparations, authenticated-principal snapshots, requests,
   approvals, operations, grants, and audit evidence.

The target intake already defines narrow authority ports so Core can independently
reload facts rather than trust model-visible MCP output. Keeping their implementation
and both schemas inside Web would make the port boundary architectural ceremony and
would make a later remote authority/security boundary unnecessarily invasive.

The project must continue to satisfy the constitution: one executable modular host,
local synthetic data, no real access, and no premature distributed infrastructure.
The delivered production path and its tests must also remain stable while the target is
built and proved.

## Decision

Build the target as one deployable modular monolith with two infrastructure projects and
two independent SQLite databases:

```text
GovernedAccess.ReferenceAuthority -> GovernedAccess.Core
GovernedAccess.Workflow.Persistence -> GovernedAccess.Core
GovernedAccess.Mcp -> GovernedAccess.Core
GovernedAccess.Web -> Core + ReferenceAuthority + Workflow.Persistence + Mcp
GovernedAccess.Core -> no outer project
```

### Reference authority ownership

`GovernedAccess.ReferenceAuthority` exclusively owns direct access to the target
reference database, its EF Core context, migrations, and synthetic seeding. The database
contains clients and business-approver mappings, environment/search eligibility facts,
environment-role assignments, incidents, and incident-to-environment relationships.

The module implements Core's focused search, exact environment, entitlement, and
incident authority ports. Search and exact lookup remain separate capabilities. Their
failure semantics remain independently testable even when one local database backs
them.

### Workflow persistence ownership

`GovernedAccess.Workflow.Persistence` exclusively owns direct access to the target
workflow database, its EF Core context, migrations, and synthetic principal snapshots.
It persists preparations, requests, approvals, provisioning operations, grants, and
audit evidence and implements Core workflow-store ports.

There are no EF relationships across databases. Stable identifiers cross the module
boundary and Core revalidates current reference facts through authority ports.

### Transport and composition

`GovernedAccess.Mcp` owns MCP request/response DTOs and translates through authority
ports. It has no EF Core, reference-database, or workflow-persistence dependency. MCP
results remain model interpretation context rather than workflow authority.

`GovernedAccess.Web` remains the sole executable and composition root. It may reference
module registration extensions, but controllers, Teams/AI adapters, and renderers do
not receive a module `DbContext` or query module tables directly. Local calls remain
in-process; loopback HTTP is forbidden because it adds distributed failure behavior
without establishing a real boundary.

### Parallel replacement

The delivered graph, unified context, database, registrations, and regression tests
remain the production path during construction. A separate target composition uses the
new reference-authority database, workflow database, target intake, four-tool MCP
catalog, and target downstream workflow end to end.

There is no shared row, database file, dual write, synchronization, backfill, fallback,
feature flag, or per-request router between delivered and target paths. Synthetic data
may use the same stable logical IDs but is independently seeded.

After isolated target evidence and explicit human approval, production composition
switches atomically to fresh target databases. The immediately following task deletes
the delivered graph, unified context/schema, and delivered-only tests.

## Consequences

### Positive

- The source tree communicates reference-data and workflow ownership explicitly.
- Core, MCP, persistence, and transport contracts cannot collapse into one Web module.
- Each database has independent migrations, fixtures, restart tests, and failure tests.
- The final runtime remains operationally simple: one process and one deployment
  artifact.
- A future remote boundary replaces the in-process authority-port implementation with
  an authenticated client; Core workflow rules and workflow persistence remain intact.
- Temporary duplication is bounded by named tasks and deleted after one cutover rather
  than preserved as compatibility architecture.

### Negative

- Target construction temporarily retains the delivered unified persistence graph beside
  the new two-database graph.
- Synthetic reference fixtures must remain logically consistent with workflow test
  identities without relying on cross-database foreign keys.
- Full-host tests must initialize and dispose two databases.
- No transaction can atomically cover a reference read and workflow write; Core must
  continue to revalidate reference facts immediately before consequential workflow
  mutation and fail closed on authority unavailability.
- Project separation in one process is an architectural boundary, not a security
  boundary. That is accepted at this stage.

## Alternatives considered

### Keep one EF context in Web

Rejected for the target. It preserves the delivered simplicity but makes reference
authority ports superficial and leaves a later extraction coupled to Web persistence,
migrations, and workflow entities.

### Make MCP own the reference database

Rejected. MCP is a model-facing transport, not the enterprise authority. Core requires
the same facts through a deterministic application boundary, and MCP wire DTOs must not
become persistence or domain contracts.

### Call the co-hosted module through loopback HTTP

Rejected. It adds latency, serialization, timeout, and local networking failure modes
without providing separate identity, deployment, or process isolation.

### Create a second deployable reference service now

Rejected for the current baseline. It would violate the one-host constitution and add
service authentication, deployment, observability, and distributed failure concerns
before they are required.

### Mutate the delivered unified path in place

Rejected. It would mix persistence separation with the intake rewrite, destabilize the
existing regression suite, and leave no independently provable target before cutover.

## Enforcement

- Architecture tests assert the project-reference graph and forbid EF/MCP/Web references
  from Core.
- Source/reflection tests assert that only the owning module references each `DbContext`.
- MCP contract tests use authority-port fakes and assert MCP-owned wire DTOs.
- Integration tests create, migrate, seed, restart, and fail the two databases
  independently.
- Full-host tests prove the delivered production composition and isolated target
  composition do not share registrations or database files.
- Cutover checks assert exactly one active graph; cleanup checks assert no delivered
  graph, unified context, or transitional seam remains.

## Revisit criteria

Revisit this decision if an approved baseline introduces a separately deployable
reference service, real CMDB/IAM/ITSM systems, distinct service identities, or a need for
independent scale/availability. At that point, define an authenticated authority API and
remote client while preserving the Core authority-port semantics. Do not use the
model-facing MCP endpoint as the authoritative application API.

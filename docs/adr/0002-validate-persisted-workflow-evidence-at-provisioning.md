# ADR 0002: Validate Persisted Workflow Evidence at Provisioning

- **Status**: Accepted
- **Date**: 2026-07-22
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/governed-production-access-product-baseline.md`, `specs/001-governed-production-access/spec.md`, `docs/adr/0001-use-one-deployable-service-including-mcp.md`

## Context

Requests are validated against authoritative client, environment, role, and incident
data before submission. Business and DevOps actions authenticate their actors and bind
insert-only decisions to the immutable request ID and exact role. The provisioning
handler originally repeated all reference-data and approver lookups before every
initial attempt and retry.

That repeated validation models an independently changing system of record, but the
MVP does not have one. Its reference data is a fixed synthetic dataset in the same
SQLite database. The application exposes no command or administration surface that
changes clients, environments, roles, incidents, principals, or approver assignments.
At startup, `SyntheticDataSeeder` compares persisted reference records with the exact
expected dataset and fails instead of updating or removing conflicting records.

Consequently, a supported application flow cannot leave a pending workflow while its
reference records change. Repeating those lookups in provisioning adds a dependency,
failure branches, and tests for a state the executable MVP cannot produce.

Persisted workflow evidence can change and remains security-relevant. A provisioning
attempt or retry must not trust caller assertions about approval, scope, or workflow
state.

## Decision

The provisioning handler will reload and validate only the persisted evidence needed
to authorize the stored operation:

- the provisioning operation exists and uses the canonical request UUID;
- the immutable request exists and is in a provisionable or completed state;
- the operation environment and role match the immutable request;
- one approved business decision and one approved DevOps decision exist for the same
  request in the correct order;
- both approvals cover the immutable requested role; and
- an already completed operation has one matching stored grant.

The handler will construct the provider request exclusively from that stored evidence.
It will not depend on `IRequestContextReader` or repeat client, environment, role,
incident, business-principal, or DevOps-principal lookups.

Authoritative reference validation remains at the points where the MVP can accept or
record those facts: request submission and authenticated human decisions. Retry uses
the same persisted-evidence validation as the initial attempt.

The SQLite database is application-owned and has no supported out-of-band writers.
Introducing any runtime writer for client, environment, role, incident, principal, or
approver-assignment reference data requires pre-provisioning authoritative reference
validation to be restored before that writer is released.

## Rationale

This keeps a real authorization boundary without implementing behavior for a mutable
reference system that does not exist. The handler still distrusts callers and detects
missing, mismatched, rejected, out-of-order, or wrong-scope workflow evidence.

The decision is deliberately tied to the fixed, fail-fast synthetic dataset. It is not
a claim that production environment, role, incident, or identity data is generally
immutable.

## Consequences

### Positive

- Provisioning has one fewer application dependency and fewer failure branches.
- Tests cover states reachable through the MVP rather than simulated reference-data
  mutation.
- Request-context validation is not duplicated in two application services.
- The design remains proportionate to one local host and one fixed dataset.
- Initial attempts and later retries share the same focused stored-evidence checks.

### Negative and risks

- Direct database changes made after startup are not detected before provisioning.
- A future mutable or external context adapter cannot safely reuse this behavior
  unchanged.
- Approver authority is proven when the decision is recorded, not re-evaluated at
  provisioning time.

Those risks are acceptable because direct database mutation and mutable reference data
are outside the supported MVP behavior.

## Alternatives considered

### Repeat all authoritative lookups during provisioning

This detects changes between request submission, approval, and provisioning and is
appropriate when context comes from mutable systems of record. It is rejected for the
MVP because the exact synthetic dataset cannot change through supported application
behavior and conflicts cause startup failure.

### Reuse `RequestValidator` during provisioning

This would reduce duplicated comparison code but retain unnecessary context reads and
failure modes. It would also mix caller-input validation concerns such as required
fields and justification with stored workflow authorization.

### Trust the DevOps decision service completely

Passing a prepared scope or approval assertions directly to the provider would be
simpler but would weaken the retry path and make the protected handler depend on its
caller's correctness. It is rejected; the handler continues to accept only the stable
operation reference and reload stored evidence.

## Revisit criteria

Reintroduce authoritative reference-data validation before provisioning when any of
the following becomes real product behavior:

- an environment, role, incident, principal, or approver assignment can change while
  requests survive;
- reference context is read from an external system of record;
- an administration or synchronization process updates reference records at runtime;
- approvals have expiry or authority-revocation semantics; or
- a real provider requires a fresh eligibility check immediately before access is
  granted.

That change should define the authoritative consistency model and add tests using a
genuinely mutable adapter rather than only a test fake.

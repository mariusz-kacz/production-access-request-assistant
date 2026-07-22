# ADR 0004: Use Request ID as the Provisioning Idempotency Identity

- **Status**: Accepted
- **Date**: 2026-07-22
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/adr/0003-do-not-model-provider-and-workflow-persistence-as-atomic.md`, `specs/001-governed-production-access/research.md`, `specs/001-governed-production-access/data-model.md`

## Context

Every submitted access request is immutable and permits exactly one logical
provisioning operation. Retries repeat that operation without changing requester,
environment, role, or lifetime.

The original model stored both the request UUID and its canonical string form as an
operation ID. The handler accepted the string only to parse it back into the request
UUID, while operations and grants had unique constraints on both representations.
The second value did not identify an independent domain concept or improve retry
safety.

## Decision

The immutable server-generated `RequestId` is the sole provisioning identity:

- `ProvisioningOperation` remains persisted lifecycle evidence, keyed by `RequestId`;
- `AccessGrant.RequestId` uniquely links the grant to that operation and request;
- the protected handler accepts `Guid requestId` and reloads all authoritative
  workflow evidence using it;
- the provider-neutral provisioning request carries `RequestId`; and
- an infrastructure adapter may format that UUID for a provider idempotency header,
  but the application does not persist another operation identifier.

## Rationale

One immutable request maps to one logical provisioning operation and at most one
grant. The request UUID is already stable, unique, server-controlled, and bound to the
approved scope. Reusing it preserves idempotent retry after a timeout or lost response
without duplicate columns, parsing, lookups, or consistency checks.

This decision does not remove the provisioning operation record. Its status, attempt
count, timestamps, and last outcome remain necessary workflow evidence.

## Consequences

### Positive

- Domain and persistence models express the actual one-to-one cardinality directly.
- Retry, audit, and provider calls share one stable identifier.
- Duplicate string/UUID fields, indexes, lookup methods, and validation disappear.
- There is no security reduction because both prior values were derived from the same
  server-generated request UUID.

### Limitation

The model cannot represent multiple independently addressable provisioning operations
for one request. That is intentional in the current product scope.

## Alternatives considered

### Persist a separately generated operation ID

Valid when one request can own multiple operations, but redundant for the current
one-operation-per-request rule.

### Persist the canonical request UUID as a second string field

Rejected because it duplicates identity and creates an inconsistency state without
adding uniqueness or idempotency guarantees.

### Derive a hash from request and scope

Rejected because request scope is immutable. The hash repeats information already
bound to `RequestId` and complicates diagnostics.

## Revisit criteria

Introduce a distinct operation ID only if one request must own multiple independent
provisioning operations, such as separate grants, renewals, or provider actions that
require different idempotency histories. Retry of the same logical operation does not
meet that criterion.

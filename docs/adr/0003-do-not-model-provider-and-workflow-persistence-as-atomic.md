# ADR 0003: Do Not Model Provider Execution and Workflow Persistence as Atomic

- **Status**: Accepted
- **Date**: 2026-07-22
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/adr/0004-use-request-id-as-provisioning-idempotency-identity.md`, `docs/governed-production-access-product-baseline.md`, `specs/001-governed-production-access/research.md`, `specs/001-governed-production-access/plan.md`

## Context

Provisioning crosses two consistency boundaries:

1. the access provider creates or returns a grant; and
2. EF Core records the local request, operation, grant, and audit outcome in SQLite.

A database transaction cannot include a general external provider call. Holding a
local transaction open during that call would add locks without making provider work
roll back when the database transaction fails. A post-provider reload can detect some
concurrent changes, but another change can occur after the reload and before the save.
It therefore does not make provider execution and local persistence atomic.

The MVP uses a synthetic provider, but the provider-neutral port intentionally models
this real failure boundary. Presenting the two actions as atomic would teach the wrong
guarantee and add code that does not provide it.

## Decision

The application will not attempt or claim atomicity across provider execution and
workflow persistence.

The intended sequence is:

1. persist the authenticated DevOps decision and stable provisioning operation;
2. reload and validate persisted workflow evidence;
3. record the attempt;
4. call provider `GetOrCreateAsync` outside a database transaction using the stable
   request ID as its idempotency key;
5. on provider success, update request and operation state and add the grant and
   success audit event in one EF Core `SaveChangesAsync`; and
6. on a typed provider failure, persist the retryable failure state and audit outcome.

The final `SaveChangesAsync` is the local atomic boundary. The handler will not reload
the request and operation after provider success merely to suggest a stronger
cross-system guarantee. Existing EF optimistic concurrency and database uniqueness
constraints remain responsible for local conflicts and duplicate rows.

Provider success followed by cancellation, process failure, or unavailable local
persistence remains a possible partial outcome. The stable request ID and
get-or-create provider behavior allow the planned DevOps retry to converge on the same
grant. The MVP does not add an outbox, background reconciler, distributed transaction,
or compensation subsystem.

## Rationale

This decision states the real guarantees:

- local workflow finalization is atomic;
- provider calls use the immutable request ID as their idempotency identity;
- expected failures are explicit; and
- cross-boundary partial failure is recoverable through the scoped retry path.

It avoids long transactions and removes a redundant post-provider reload while
remaining honest about the interval between the provider result and the database save.

## Consequences

### Positive

- The code reflects the actual consistency boundaries.
- No long-running database transaction surrounds external work.
- The final local state, grant, and audit event commit together.
- Retry can reuse the same request ID after a lost response.
- The MVP avoids infrastructure whose only purpose would be to simulate distributed
  atomicity.

### Negative and risks

- Provider access may exist while the local workflow has not recorded success.
- A caller may receive failure even though the provider created the grant.
- Recovery depends on a later retry and provider get-or-create behavior.
- The MVP has no automatic reconciliation or compensation if nobody retries.

These limitations must be described as partial-failure behavior, not hidden behind an
atomicity claim.

## Alternatives considered

### Hold a database transaction across the provider call

Rejected because it creates long locks and still cannot roll back external provider
state when the local transaction fails.

### Reload after the provider call before finalization

Rejected as an atomicity mechanism because it only moves the race window. Persisted
workflow validation remains before provider invocation, and optimistic concurrency
applies at the local save.

### Add an outbox and background worker

This can provide durable delivery and automated reconciliation, but it adds worker
lifecycle, polling or messaging, operational state, and eventual-consistency behavior.
Those concerns are outside the single-host portfolio MVP.

### Add compensation or automatic revocation

This may be required with real production access but is explicitly outside the current
synthetic grant scope.

## Revisit criteria

Revisit this recovery design, but not the impossibility of general cross-system
atomicity, when:

- provisioning creates real access or uses privileged credentials;
- partial outcomes require bounded automatic recovery;
- retries cannot rely on provider get-or-create semantics;
- a durable worker or message broker is introduced for another concrete requirement;
  or
- compensating revocation becomes part of the product scope.

A future ADR should then choose durable reconciliation, an outbox/inbox protocol,
provider-status polling, or compensation and define the resulting operational SLOs.

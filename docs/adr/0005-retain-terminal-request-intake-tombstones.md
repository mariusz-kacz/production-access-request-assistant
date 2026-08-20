# ADR 0005: Retain Terminal Request-Intake Tombstones

- **Status**: Accepted
- **Date**: 2026-08-10
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/architecture.md`, `docs/security-model.md`, `docs/request-intake-orchestration.md`

## Context

A request-intake session may become `Submitted`, `Superseded`, `Expired`, or
`Invalidated`. Teams cards can remain visible after any of those transitions, even
when the application has attempted to replace a tracked card with a non-actionable
one. Duplicate confirmation of a submitted intake must also return the original
request identity rather than create another request.

The application therefore needs durable terminal evidence after an intake stops being
active. At the same time, an obsolete candidate can contain client, environment,
role, justification, and incident details that are no longer needed to continue a
draft.

The current baseline is a local, synthetic, low-volume application. It has
no background worker, operational database-maintenance process, or defined production
privacy and retention schedule.

## Decision

Retain terminal `RequestIntakeSession` rows in SQLite for the lifetime of the current
database. Do not automatically delete or expire those rows in the current baseline.

When an intake enters a terminal state:

- clear `ClientId`, `EnvironmentId`, `RequestedRoleId`, `Justification`, and
  `IncidentId`;
- retain the intake ID, terminal status, authenticated actor and conversation
  binding, timestamps, latest correlation ID, persistence version, and any reserved
  request ID; and
- continue to resolve confirmation attempts against that retained row.

The retained row is a lifecycle tombstone, not an editable draft or a substitute for
the audit-event model. A replacement candidate is persisted in a new intake with a
new server-generated intake ID.

## Rationale

The terminal row lets the deterministic submission boundary distinguish a replaced,
expired, invalidated, or already-submitted intake from an unknown identifier. This
keeps stale-card handling precise and makes submitted-card replay idempotent.

Clearing candidate content reduces unnecessary retention while preserving the minimum
state used for ownership checks, lifecycle classification, concurrency handling, and
request-identity recovery. Adding a cleanup scheduler and post-deletion semantics
without a concrete retention requirement would introduce lifecycle and race behavior
that the current single-host baseline does not need.

## Consequences

### Positive

- Old cards remain deterministically non-actionable even if Teams could not update
  their presentation.
- Duplicate confirmation of a submitted intake can recover the original request ID.
- Obsolete candidate scope and justification are not retained in terminal intake
  rows.
- Replacement drafts cannot reuse the old preparation identity.
- No background cleanup infrastructure or cleanup-versus-confirmation race is added.

### Negative and risks

- The `RequestIntakeSessions` table grows monotonically with created intakes.
- Actor, conversation, lifecycle, correlation, timestamp, and reserved-request
  metadata remain until the database is manually removed.
- The application has no configurable retention period, purge command, archival
  process, or storage-size monitoring.
- This indefinite local retention is not a suitable default for a real production
  deployment handling personal or client data.

## Alternatives considered

### Delete the old intake as soon as it becomes terminal

Rejected because stale-card confirmation could no longer distinguish a replaced or
expired intake from an unknown identifier, and submitted-card replay would lose its
direct intake-to-request evidence.

### Delete terminal rows after a fixed time-to-live

Deferred because no product, privacy, or operational requirement currently defines
the retention period or the response that an old card should receive after its
tombstone is removed. A safe implementation would also need to coordinate cleanup
with concurrent confirmation.

### Move terminal evidence to a separate tombstone table

Rejected for the current scope because it adds another schema and transition without
reducing the retained metadata or changing the unbounded-retention question.

## Revisit criteria

Define and implement a bounded retention policy before using the application with
real personal or client data, or when any of the following becomes true:

- database growth or long-running-host volume is material;
- privacy, legal, contractual, or client requirements define deletion deadlines;
- backup, recovery, archival, or database-maintenance processes are introduced;
- old cards need a specified behavior beyond the retention window; or
- submitted-intake replay evidence moves to another durable source.

A superseding decision must define retention periods by terminal status, cleanup and
concurrent-confirmation behavior, the response for cards whose tombstones have been
deleted, and any audit or request evidence that must remain.

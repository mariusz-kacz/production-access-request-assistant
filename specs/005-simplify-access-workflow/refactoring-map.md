# Final Refactoring Map: Request Authority

This map describes the authority and invariant ownership after the workflow
simplification. Historical pre-refactoring details remain available in source-control
history; this document describes the implementation that now runs.

## Authoritative data flow

```text
authenticated Teams message
        |
        v
model proposal -> RequestCandidate (untrusted and possibly incomplete)
        |
        v
RequestValidator + fixed authoritative reference data
        |
        v
ValidatedRequestDetails
        |
        v
ready RequestIntakeSession snapshot
        |
        | authenticated confirmation + fresh validation
        v
AccessRequest.Details (sole durable request-details authority)
        |
        +----------------------+-----------------------+
        |                      |                       |
        v                      v                       v
ApprovalDecision       ProvisioningOperation    query/API/card projections
(request ID +           (request ID +            (derived compatibility
decision evidence)      lifecycle evidence)       fields)
                               |
                               v
                    AccessProvisioningRequest
                    (derived from request details)
                               |
                               v
                    provider idempotency boundary
                               |
                               v
                         AccessGrant
                 (request ID + lifetime evidence)
```

Client, environment, and role form the access scope. Justification and optional
incident form governance context. `ValidatedRequestDetails` contains both as one
immutable value. Requester identity remains a separate immutable request property
because it is derived from authenticated server context.

## Representation and ownership

| Representation | Role | Authority |
|---|---|---|
| Model payload and `RequestCandidate` | Mutable interpretation input; may be missing, malformed, invented, or inconsistent | Never authoritative |
| `RequestIntakeSession` while collecting | Durable sanitized conversational candidate | Not submitted-request authority |
| `ValidatedRequestDetails` in a ready intake | Validated snapshot presented for confirmation | Authoritative for that prepared snapshot |
| `AccessRequest.Details` | Immutable submitted access scope and governance context | Sole durable request-details authority |
| `ApprovalDecision` | Actor, stage, outcome, time, comment, correlation, and request ID | Human-decision evidence only |
| `ProvisioningOperation` | Request-keyed attempt count, lifecycle state, timestamps, and outcome | Provisioning lifecycle evidence only |
| `AccessGrant` | Request-keyed grant ID, activation, expiry, outcome, and correlation | Local grant evidence only |
| Provider request and provider state | Requester/environment/role derived at invocation from `AccessRequest.Details` | Separate provider idempotency boundary keyed by request ID |
| HTTP, Teams, and UI response fields | Compatibility/display projections | Derived from the request and workflow evidence |

No approval, operation, grant, or audit payload owns an independently writable copy
of request scope. External contracts may still expose role or environment values, but
the query/controller path derives them from `AccessRequest.Details`.

## Validation boundaries

### Model and candidate boundary

- The model is restricted to the closed proposal schema and exact two-tool MCP
  allowlist.
- Deterministic candidate assessment reloads proposed identifiers, validates
  client/environment/role/incident relationships, clears rejected values, and emits
  safe clarification.
- Authenticated identity and authorization claims never come from model output.

### Confirmation boundary

- `RequestSubmissionService` reloads the ready intake; `AuthenticatedChannelActor` verifies
  its exact tenant, channel, conversation, actor, and requester ownership binding.
- The aggregate restores the flattened ready snapshot as `PreparedDetails`, and the
  validator revalidates that canonical value and requester against authoritative data.
- It constructs `AccessRequest` directly from the resulting
  `ValidatedRequestDetails` and commits request, terminal intake transition, and audit
  evidence atomically.

### Approval authorization boundary

- The workflow service reloads the authenticated principal and request.
- Business approval reloads current environment/client ownership; the authenticated
  principal owns the client-role and configured-approver assignment check.
- DevOps approval requires the DevOps principal, current valid request context, a
  request-bound approved business decision, and valid workflow state. The state
  transition owns decision order.
- Decisions authorize the immutable request ID; they do not copy its role or
  environment.

### Provisioning boundary

- `ProtectedProvisioningService` accepts only a request ID and reloads request,
  approvals, operation, and any completed grant.
- Request-keyed queries and persistence constraints establish structural identity. It
  validates approval outcomes, operation state, retry state, and completed
  evidence availability.
- It derives provider input exclusively from `AccessRequest.Details`.
- Request-ID get-or-create behavior, local uniqueness, and retry converge after
  concurrent work or a lost provider response.

## Invariant ownership

| Owner | Guarantees |
|---|---|
| Domain construction and policies | Normalized non-empty values, supported role, valid justification length, immutable request details, legal state transitions, approval sequence, operation lifecycle, fixed eight-hour grant lifetime |
| Application authorization | Authenticated actor derivation, current approver assignment, participant access, fail-closed reference validation, command ordering, transaction staging |
| SQLite persistence | Foreign keys, one active intake binding, one decision per request/stage, one request-keyed operation, at most one grant per request, optimistic concurrency |
| Provisioning provider boundary | Stable request-ID idempotency and same-input reuse for the separate provider state |

Application-controlled objects are trusted after construction and persistence lookup.
Policies and audit projection do not repeat request-ID, stage, scope, or status-copy
comparisons. Read-only request queries likewise trust the submitted request details
instead of revalidating the fixed reference dataset.

## Primary test ownership

| Boundary | Primary coverage |
|---|---|
| Domain behavior and canonical validation | Core unit tests |
| Reconstruction, transactions, uniqueness, confirmation concurrency, provisioning recovery | SQLite component tests in the integration project |
| Authentication, authorization integration, antiforgery, routes, serialization, Teams, and MCP | Full-host integration tests |
| Session/action presentation and safe browser wiring | Six React component tests |

A rule is repeated at another layer only when that layer introduces a distinct failure
mode. For example, role assignment permutations belong to `RequestValidationTests`,
while the host tests cover crafted HTTP input and authenticated actor derivation. Fake
concurrency and copied-scope mismatch tests were removed because real SQLite
concurrency owns that risk and the mismatch states can no longer be constructed.

## Removed architecture

- `RequestSubmissionService`; confirmation now creates the request directly.
- `ValidatedRequestFields`; `ValidatedRequestDetails` is the single canonical value.
- `AccessRequest` scope projection properties; callers use `AccessRequest.Details`.
- copied approval role, operation scope, grant scope, and their synchronization checks.
- `IAuditStore`; audit operations are part of the one concrete workflow persistence
  boundary.
- obsolete mismatch branches, redundant audit/evidence checks, impossible result-
  factory guards, and tests tied to those internal representations.
- `WorkflowEvidencePolicy`; approval-stage lookups establish identity and workflow
  transitions establish order, while consuming services check only approval outcomes
  that authorize the next action.

## Result/error review

The existing shared `ApplicationResult` and `ApplicationFailure` remain the common
failure model. Outcome-specific preparation, reset, and confirmation results remain
because their successful branches carry different behavior, not different error
models. Their factories no longer revalidate values produced by the application.

## Validation baseline

Validated on 2026-08-07:

- warnings-as-errors solution build: passed with 0 warnings and 0 errors;
- unit tests: 71 passed;
- integration tests: 92 passed;
- frontend tests: 6 passed.

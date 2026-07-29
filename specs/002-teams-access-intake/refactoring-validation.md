# User Story 5 Refactoring Validation

- **Measured against**: repository `HEAD` before T033-T048
- **Measured on**: 2026-07-29
- **Scope**: Teams request-intake production files and the Core intake contract,
  aggregate, application services, and shared submission seam

## Complexity budget

| Measure | Before | After | Result |
|---|---:|---:|---|
| Production intake files | 12 | 10 | Met: two files removed |
| Persisted intake tables | 2 | 1 | Met: one table removed |
| Intake application-service dependencies in the Teams agent | 2 | 1 | Met |
| Public Teams-intake Core types | 23 | 16 | Met: 30.4% reduction |
| Intake aggregate/orchestration types | 6 | 3 | Met: 50% reduction |
| Intake aggregate/orchestration physical lines | 1,173 | 916 | 21.9% reduction |
| All measured Teams-intake Core physical lines | 1,623 | 1,384 | 14.7% reduction |

Production-file scope includes the Core intake domain, service, port and shared
submission files; EF store and model; Teams agent, card renderer and registration;
and the MAF interpreter. The Core line/type scope includes the intake domain,
application services, port and shared submission seam. Counts include blank and
comment lines so they are reproducible without a language-specific counter.

The 30% line target is not met. The remaining lines are predominantly the preserved
deterministic security behavior: authoritative candidate and clarification-option
validation, authenticated ownership, expiry, exact-scope revalidation, typed
dependency failures, and one-save submission/audit staging. Removing those checks
would violate the product baseline and the explicit simplification guardrails. The
structural measures do meet the intended reduction: one aggregate replaces two
entities, one service replaces two services, one table replaces two tables, seven
public Core types are gone, and the Teams agent has one intake-service dependency.

## Preserved behavior and boundaries

- Preparation alone invokes the model interpreter; confirmation deterministically
  reloads persisted evidence and never invokes the model.
- The ready scope is immutable, expires after 30 minutes, retains its reserved
  request identity, and clears candidate content after a terminal transition.
- Confirmation rechecks authenticated ownership and authoritative client,
  environment, role, incident and requester context.
- Request creation, aggregate submission and the request-created audit event use one
  shared `DbContext.SaveChangesAsync` call. Forced failure leaves no partial rows.
- The EF model has one `RequestIntakeSessions` table with active-binding,
  reserved-request-ID and optimistic-concurrency constraints.
- The Adaptive Card performs display-label lookups only and keeps one no-input
  `confirmAndSubmit` action carrying the opaque intake-session ID.
- MAF provider types remain in Web infrastructure. Core contains no Microsoft
  Agents, Adaptive Card or MCP SDK dependencies, and no transcript/session history
  is persisted.
- Teams boundary logs contain correlation, authenticated binding, duration,
  transition, outcome, intake-session ID and request ID only. Tests reject prompt,
  justification and card-body capture.

## Verification

- Solution build: warnings-as-errors, zero warnings.
- Unit coverage: aggregate invariants, exact scope, reserved identity, foreign
  ownership, stale authoritative role, typed interpreter/save failures, and
  cancellation.
- Integration coverage: single-table model and UTC mappings, uniqueness metadata,
  real optimistic-concurrency conflict, one-save atomic confirmation, forced-save
  rollback, hosted card/confirmation behavior, scoped composition and structured
  logging.
- Local development documentation now requires recreation of the disposable
  synthetic SQLite database after EF model changes.

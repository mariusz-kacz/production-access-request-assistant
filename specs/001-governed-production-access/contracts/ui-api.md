# Same-Origin React UI API Contract

Base path: `/api`. This is an adapter for the co-hosted React UI, not a general public
API or provisioning API.

## Global rules

- JSON uses camelCase and stable identifiers.
- Authentication uses an HttpOnly server-issued cookie. Requests never accept actor
  ID, roles, approver assignment, or authorization claims as authority.
- React obtains an antiforgery token and sends `X-XSRF-TOKEN` on every unsafe method.
- All asynchronous handlers propagate `HttpContext.RequestAborted`.
- Expected errors use `application/problem+json` with `status`, `title`, safe `detail`,
  stable `code`, `correlationId`, optional `fieldErrors`, and optional `currentVersion`.
- `401` is unauthenticated; `403` unauthorized; `404` absent/not visible; `409` stale
  version, transition, or concurrency conflict; `422` validation; `503` bounded
  dependency failure where retry is safe.

## Session and security

### `GET /api/security/antiforgery`

Issues or refreshes the readable `XSRF-TOKEN` cookie and returns `204`. It grants no
identity.

### `GET /api/session`

Returns `{ authenticated, principal: { id, displayName, kind, clientId? }?,
capabilities: string[] }`. Capabilities are presentation hints only.

### `POST /api/demo/session`

Requires antiforgery. Body: `{ principalKey }`. Resolves exactly one of four immutable
server-configured principals, issues the auth cookie, and returns the session view.
Unknown keys return `422`; the browser cannot supply claims.

### `DELETE /api/demo/session`

Requires antiforgery, clears the authentication cookie, and returns `204`.

## Draft and requests

### `POST /api/request-drafts/prepare`

Requester only. Body: `{ intent }`. Returns `{ outcome, draft? }`, where draft
matches `request-draft.schema.json`. Outcomes:
`Prepared`, `Incomplete`, `MalformedModelOutput`, `Timeout`, `Cancelled`, or
`Unavailable`. The UI derives missing-field guidance from nullable draft fields and
standard failure text from the outcome. It creates no request, approval, operation,
or grant.

### `POST /api/requests`

Requester only. Body: `{ clientId, environmentId, requestedRole, durationMinutes,
justification, incidentId? }`. On stored-data validation success returns `201` with
`{ requestId, version: 1, status: "AwaitingBusinessApproval", correlationId }`.

### `GET /api/requests`

Authenticated. Returns `{ items: [{ requestId, clientId, environmentId, requesterId,
version, status, lastModifiedAt, actionable }] }`. Server filtering and `actionable`
are based on the authenticated actor. Optional status filters cannot expand visibility.

### `GET /api/requests/{requestId}`

Authorized participants only. Returns request/current validation, all versioned
decisions, provisioning outcome, grant/activation/expiry/logical expiry, ordered audit
events, and server-computed available actions.

### `PUT /api/requests/{requestId}`

Owning requester only. Body: `{ expectedVersion, clientId, environmentId,
requestedRole, durationMinutes, justification, incidentId? }`. A material change
returns the incremented version in `Draft`; normalized no-change returns `NoChange`.
Stale actions return `409`. Prior decisions remain visible but ineligible.

### `POST /api/requests/{requestId}/submit`

Owning requester only. Body: `{ expectedVersion }`. Revalidates an edited `Draft` and
returns the same version in `AwaitingBusinessApproval`. Initial creation submits via
`POST /api/requests`.

## Human decisions

### `POST /api/requests/{requestId}/business-decisions`

Body: `{ expectedVersion, decision: "Approve" | "Reject", comment? }`. The server
loads approver responsibility and request scope. Approval returns
`AwaitingDevOpsApproval`; rejection returns `Rejected`. Approver, role, and duration
fields are not accepted.

### `POST /api/requests/{requestId}/devops-decisions`

Body: `{ expectedVersion, decision: "Approve" | "Reject",
approvedDurationMinutes?, comment? }`. Role, client, and environment are not accepted.
Approval validates the exact loaded role and duration ceiling, then immediately calls
protected provisioning. It returns `Active` with the grant or `ProvisioningFailed`
with a safe outcome. Rejection returns `Rejected`.

### `POST /api/requests/{requestId}/retry-provisioning`

Body: `{ expectedVersion }` only. DevOps-only and valid only in
`ProvisioningFailed`. The server reloads stored scope/operation identity, repeats full
revalidation, and returns the single grant on success. There is no initial or generic
browser provisioning endpoint.

## React routes

| Route | API use |
|---|---|
| `/requests` | session, request list, demo session switch |
| `/requests/new` | prepare draft, create request |
| `/requests/:requestId` | detail, edit/resubmit, human decisions, retry |

SPA fallback handles only UI routes. `/api/*` and `/mcp` never fall back to
`index.html`.

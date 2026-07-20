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
  stable `code`, `correlationId`, and optional `fieldErrors`.
- `401` is unauthenticated; `403` unauthorized; `404` absent/not visible; `409`
  transition or concurrency conflict; `422` validation; `503` bounded
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

Requester only. Body: `{ clientId, environmentId, requestedRole, justification,
incidentId? }`. On stored-data validation success returns `201` with
`{ requestId, status: "AwaitingBusinessApproval", correlationId }`.

### `GET /api/requests`

Authenticated. Returns `{ items: [{ requestId, clientId, environmentId, requesterId,
status, lastModifiedAt, actionable }] }`. Server filtering and `actionable`
are based on the authenticated actor. Optional status filters cannot expand visibility.

### `GET /api/requests/{requestId}`

Authorized participants only. From the User Story 2 increment onward, returns the
minimum navigable detail projection:

`{ requestId, requesterId, clientId, environmentId, requestedRoleId,
justification, incidentId?, status, createdAt,
lastModifiedAt, availableActions }`.

`availableActions` is computed from the authenticated actor and current stored state;
the configured approver receives `decideBusinessRequest` only while the request is
`AwaitingBusinessApproval`. The requester can review their submitted request without
receiving that action, and a nonparticipant receives `404`.

Later story increments extend this stable endpoint with current validation, all
decisions, provisioning outcome, grant/activation/expiry/logical expiry, ordered audit
events, DevOps actions, and retry. They do not introduce a replacement detail route.

Submitted requests have no edit or resubmit endpoint. A correction is created through
`POST /api/requests` and receives a new request ID.

## Human decisions

### `POST /api/requests/{requestId}/business-decisions`

Body: `{ decision: "Approve" | "Reject", comment? }`. The server
loads approver responsibility and request scope. Approval returns
`AwaitingDevOpsApproval`; rejection returns `Rejected`. Approver, role, and duration
fields are not accepted.

### `POST /api/requests/{requestId}/devops-decisions`

Body: `{ decision: "Approve" | "Reject", comment? }`. Role, client, environment are not accepted. Approval validates the exact loaded role, then immediately
calls protected provisioning for the fixed eight-hour grant. It returns `Active` with
the grant or `ProvisioningFailed`
with a safe outcome. Rejection returns `Rejected`.

### `POST /api/requests/{requestId}/retry-provisioning`

No request body. DevOps-only and valid only in
`ProvisioningFailed`. The server reloads stored scope/operation identity, repeats full
revalidation, and returns the single grant on success. There is no initial or generic
browser provisioning endpoint.

## React routes

| Route | API use |
|---|---|
| `/requests` | session, request list, demo session switch |
| `/requests/new` | prepare draft, create request |
| `/requests/:requestId` | immutable detail and business decision from US2; later enriched with DevOps, retry, grant, and audit evidence |

SPA fallback handles only UI routes. `/api/*` and `/mcp` never fall back to
`index.html`.

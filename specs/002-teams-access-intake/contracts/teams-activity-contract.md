# Teams Activity Contract

## Purpose

This contract defines the application-owned behavior layered over the Microsoft 365
Agents SDK Activity Protocol endpoint. The SDK owns the wire schema, token validation,
and activity serialization; this document defines which authenticated activities the
feature accepts and what application commands they may cause.

## Endpoint

`POST /api/messages`

- Mapped by `Microsoft.Agents.Hosting.AspNetCore` in the existing Web executable.
- Must be excluded from cookie authentication, antiforgery, MVC API fallback, and SPA
  fallback behavior.
- Requires Microsoft 365 Agents SDK token validation outside the dedicated automated
  test host.
- Must not expose an anonymous development bypass in the normal application
  environment.

## Accepted conversation

An activity is eligible only when all of the following are derived from the
SDK-authenticated context:

- channel is `msteams`;
- tenant matches configured demo tenant policy;
- conversation type is `personal`;
- stable Teams/AAD actor identifier is present;
- conversation identifier is present; and
- the actor maps server-side to the fixed synthetic requester.

The accepted security binding is:

```text
(channel, tenantId, channelActorId, conversationId, syntheticRequesterId)
```

Activity text, `value` data, card data, and arbitrary channel data never supply the
acting identity, requester role, requested production role, approver, duration,
authorization claim, or validated scope.

## Message activity

### Input

- Latest non-empty developer text from an accepted personal activity.
- SDK-authenticated actor/conversation binding.
- Server correlation ID.

### Processing

1. Load or create the one active preparation conversation for the binding.
2. If a ready prepared request already exists, the new request-intent turn supersedes
   it; the old card remains visually immutable but can no longer be confirmed.
3. Invoke the provider-neutral request-intake interpreter with the latest text,
   compact current candidate, and one bounded typed clarification context when
   present.
4. Strictly validate the closed proposal schema.
5. Revalidate candidate values with authoritative stored context.
6. Authoritatively canonicalize any bounded ordered clarification options.
7. Persist either the updated compact candidate/typed clarification or an immutable
   prepared snapshot.

### Outcomes

| Outcome | Teams response | State effect |
|---|---|---|
| `ClarificationRequired` | One focused prompt with optional numbered choices | Update compact candidate and one bounded typed clarification |
| `CandidateRejected` | Application-owned validation correction with clear provenance | No synthetic interpreter question and no prepared request |
| `ReadyForConfirmation` | Server-rendered final Adaptive Card | Create immutable prepared snapshot and reserved request ID |
| `MalformedModelOutput` | Safe retry/start-over guidance | No prepared request |
| `Timeout` | Safe timeout guidance | No prepared request |
| `Cancelled` | No unsafe state transition; safe response when channel permits | No prepared request |
| `Unavailable` | Safe dependency guidance | No prepared request |
| `RejectedActivity` | Generic unsupported/unauthorized response where safe | No model call or workflow state |

No message activity can submit a request, record approval, transition an existing
access request, provision, revoke, or retry provisioning.

## Final prepared-request card

The card is rendered from `PreparedAccessRequest` and follows
[prepared-request-card.json](prepared-request-card.json).

- It contains no editable input controls.
- It displays canonical scope, optional incident, fixed eight-hour lifetime, reserved
  request ID, and 30-minute confirmation deadline.
- It states that requester confirmation is not business approval, DevOps approval, or
  an access grant.
- It exposes exactly one state-changing action.
- When there is no incident, the incident fact is omitted rather than displaying or
  trusting a caller value.

## Confirmation invoke

### Action

Adaptive Card `Action.Execute`

```json
{
  "verb": "confirmAndSubmit",
  "data": {
    "schemaVersion": 1,
    "preparedRequestId": "opaque-server-generated-guid"
  }
}
```

No other payload property is trusted. Unknown properties, verbs, schema versions, or
malformed references are rejected.

### Deterministic processing

The action handler does not call MAF, the model, or MCP. It:

1. derives actor and conversation from the authenticated activity;
2. reloads the prepared snapshot by opaque reference;
3. verifies exact owner, tenant, channel, and conversation binding;
4. verifies status, expiry, and supersession;
5. revalidates immutable scope against current authoritative data;
6. atomically creates the existing request with the reserved ID and marks the
   preparation submitted; and
7. returns the stable request ID and a link based on the configured public Web origin.

### Outcomes

| Code | Meaning | Request creation |
|---|---|---|
| `submitted` | First valid confirmation succeeded | Exactly one request |
| `already_submitted` | Same owner/conversation replayed a submitted preparation | None; return existing ID |
| `expired` | Confirmation deadline passed | None |
| `superseded` | A newer preparation replaced this one | None |
| `invalidated` | Current authoritative validation no longer accepts the scope | None |
| `not_found` | Reference is unknown or intentionally concealed | None |
| `forbidden` | Authenticated actor/conversation does not own the preparation | None and do not disclose scope |
| `invalid_action` | Verb, schema, or reference is malformed | None |
| `unavailable` | Persistence or authoritative dependency failed safely | None |

Every accepted replay returns the same stable request ID. Transport activity ID may be
logged for correlation but is not the idempotency identity.

## Success response

The response contains:

- stable request ID;
- current workflow status `AwaitingBusinessApproval`;
- explicit statement that access is not yet approved or granted; and
- link `${ConfiguredWebBaseUri}/requests/{requestId}`.

The configured base URI is trusted server configuration. Incoming service URLs, host
headers, card values, or message text must not define the Web link.

## App manifest

The source-controlled manifest template contains only:

- bot capability;
- `personal` scope;
- bot/app ID placeholders;
- required names, descriptions, developer metadata, and two app icons.

It omits `team`, `groupChat`, meeting, proactive notification, static tab, Graph,
resource-specific consent, SSO, and file capabilities. The packaged ZIP contains
`manifest.json`, `color.png`, and `outline.png` at its root.

## Logging

Record correlation ID, authenticated actor binding or safe pseudonymous derivative,
conversation identifier or safe derivative, activity type, operation, duration,
preparation status transition, confirmation outcome, and submitted request ID.

Do not log bot secrets, access tokens, raw prompts, complete activity/card bodies,
conversation transcripts, model response bodies, or complete MCP payloads by default.

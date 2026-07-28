# Data Model: Teams Access Request Intake

## Modeling Rules

- Teams/MAF state is preparation context, never approval or authorization evidence.
- Channel and AI SDK types are translated before entering the Core model.
- Every identifier, candidate field, timestamp, and relationship used to create a
  prepared snapshot is server-generated or authoritatively validated.
- All timestamps are stored as UTC `DateTimeOffset` values.
- Conversation content is compact structured state, not a raw transcript.
- Prepared scope is immutable. Correction creates a new preparation and supersedes
  the prior one.
- The prepared snapshot, access request, and audit rows share the existing SQLite
  transaction boundary at confirmation.

## Request Preparation Conversation

Short-lived mutable context for one authenticated Teams actor in one personal
conversation.

### Fields

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Server-generated primary key; never empty |
| `Channel` | string | Fixed canonical value `msteams` |
| `TenantId` | string | Derived from the SDK-authenticated activity; required |
| `ChannelActorId` | string | Stable authenticated Teams/AAD actor binding; required |
| `ConversationId` | string | SDK-authenticated personal conversation identifier; required |
| `RequesterId` | string | Fixed server mapping to the synthetic requester |
| `Status` | enum | `Collecting`, `Ready`, `Submitted`, `Superseded`, or `Expired` |
| `ClientId` | nullable string | Current untrusted candidate value |
| `EnvironmentId` | nullable string | Current untrusted candidate value |
| `RequestedRoleId` | nullable string | Current untrusted candidate value |
| `Justification` | nullable string | Current untrusted candidate value; maximum 2,000 characters |
| `IncidentId` | nullable string | Current untrusted candidate value |
| `PendingClarification` | nullable typed context | One target, bounded prompt, and up to 10 ordered canonical stable-ID/display-label options needed for the next turn |
| `ActivePreparationId` | nullable `Guid` | Set only after a ready snapshot is created |
| `CreatedAt` | `DateTimeOffset` | Server clock |
| `LastTurnAt` | `DateTimeOffset` | Updated for accepted personal-chat turns |
| `CorrelationId` | string | Current operation correlation identifier |
| `PersistenceVersion` | `long` | Optimistic concurrency token |

### Identity and uniqueness

Only one nonterminal conversation may exist for
`(Channel, TenantId, ChannelActorId, ConversationId)`. The actor component is
mandatory even though every accepted actor maps to the same synthetic requester.

### Content lifecycle

- While `Collecting`, candidate fields and the pending typed clarification contain
  only the minimum context required for the next turn.
- Clarification option values and labels are reloaded and canonicalized from
  authoritative application data before persistence. Their order helps interpret
  bounded replies such as "the first one" but never constitutes authorization.
- When a prepared request is submitted, superseded, or expires, candidate fields and
  `PendingClarification` are cleared.
- Raw activities, prompts, model messages, MCP payloads, and complete transcripts are
  not stored on this entity.

### State transitions

```text
Collecting ──valid and complete──> Ready
Collecting ──new preparation─────> Superseded
Ready ───────confirmed───────────> Submitted
Ready ───────new preparation─────> Superseded
Ready ───────expiry observed─────> Expired
```

`Submitted`, `Superseded`, and `Expired` are terminal. A later request-intent message
creates a new conversation record rather than reopening one.

## Request Candidate

Provider-neutral value object carried inside the active conversation and supplied to
the interpreter on each turn.

| Field | Required for readiness | Validation |
|---|---|---|
| `ClientId` | Yes | Existing authoritative client |
| `EnvironmentId` | Yes | Existing production environment owned by `ClientId` |
| `RequestedRoleId` | Yes | Existing allowed role for `EnvironmentId` |
| `Justification` | Yes | Trimmed; 10–2,000 characters |
| `IncidentId` | No | If present, existing incident compatible with client/environment |

The model may propose this value object, but `RequestValidator` owns canonicalization,
relationship checks, and readiness. A structurally complete candidate is not
necessarily valid.

## Request Clarification Context

Provider-neutral, bounded memory for one focused clarification. It is supplied to the
interpreter with the compact candidate and latest message, but is not a transcript or
authorization evidence.

| Field | Type | Rules |
|---|---|---|
| `Target` | enum | One of `ClientId`, `EnvironmentId`, `RequestedRoleId`, `Justification`, or `IncidentId` |
| `Prompt` | string | Trimmed, non-empty, maximum 500 characters |
| `Options` | ordered list | Zero to 10 unique options; each contains a stable value and display label of at most 200 characters |

Interpreter-proposed option identifiers are untrusted. `RequestPreparationService`
reloads each option through `IRequestContextReader`, verifies applicable
client/environment relationships and role availability, replaces the label with
authoritative display data, and rejects the whole proposal when any option cannot be
validated. Justification clarification remains free text and has no options.

## Prepared Access Request

Immutable, server-owned confirmation evidence created only after deterministic
validation succeeds.

### Fields

| Field | Type | Rules |
|---|---|---|
| `PreparationId` | `Guid` | High-entropy opaque primary key carried by the card |
| `ConversationRecordId` | `Guid` | Foreign key to the originating conversation metadata |
| `ReservedRequestId` | `Guid` | Server-generated, unique, immutable future `AccessRequest.Id` |
| `Channel` | string | Fixed `msteams` |
| `TenantId` | string | Copied from authenticated conversation binding |
| `ChannelActorId` | string | Copied from authenticated conversation binding |
| `ConversationId` | string | Copied from authenticated personal conversation |
| `RequesterId` | string | Fixed synthetic requester |
| `ClientId` | string | Canonical validated scope |
| `EnvironmentId` | string | Canonical validated scope |
| `RequestedRoleId` | string | Canonical validated scope |
| `Justification` | string | Canonical validated scope |
| `IncidentId` | nullable string | Canonical validated scope |
| `Status` | enum | `Ready`, `Submitted`, `Superseded`, `Expired`, or `Invalidated` |
| `CreatedAt` | `DateTimeOffset` | Server clock at preparation |
| `ExpiresAt` | `DateTimeOffset` | Exactly `CreatedAt + 30 minutes` |
| `SubmittedAt` | nullable `DateTimeOffset` | Set once on successful confirmation |
| `SubmittedRequestId` | nullable `Guid` | On success, must equal `ReservedRequestId` |
| `CorrelationId` | string | Preparation correlation identifier |
| `PersistenceVersion` | `long` | Optimistic concurrency token |

### Invariants

- Scope and binding fields have no mutation methods.
- `ExpiresAt` is fixed at construction.
- `ReservedRequestId` has a unique database index.
- `SubmittedRequestId`, when present, equals `ReservedRequestId`.
- Only `Ready` may transition to another status.
- A `Submitted` replay returns `ReservedRequestId` without inserting evidence again.
- Expiry is enforced lazily whenever a new turn, confirmation, or prepared-request
  query observes `UtcNow >= ExpiresAt`; no background worker is needed.
- Failed confirmation caused by stale authoritative context transitions `Ready` to
  `Invalidated` and requires a new preparation.

### State transitions

```text
               ┌──────────────> Submitted
               │
Ready ─────────┼──────────────> Superseded
               ├──────────────> Expired
               └──────────────> Invalidated

Submitted ──duplicate confirmation──> Submitted (same request ID, no new event)
```

## Pre-Submission Operational Evidence

Preparation activity before an `AccessRequest` exists is recorded only as structured
operational logs. Logs may include a correlation ID, safe actor and conversation
identifiers, preparation ID and status transition, operation, outcome, duration, and
submitted request ID when available.

These logs are observability data, not durable domain evidence. They must not contain
tokens, prompts, transcripts, candidate values, card bodies, model bodies, or complete
MCP payloads. Authentication failures that occur before an actor or conversation can
safely be derived are also operational logs.

## Existing Access Request

The existing immutable `AccessRequest` remains the workflow aggregate.

### Feature-specific creation rule

- Browser submission continues to generate a fresh server request ID.
- Prepared confirmation creates the request with
  `PreparedAccessRequest.ReservedRequestId`.
- All other constructor fields come from the reloaded and revalidated prepared
  snapshot plus fixed synthetic requester and current confirmation correlation ID.
- The initial status remains `AwaitingBusinessApproval`.
- The existing `RequestCreated` audit event is inserted in the same transaction.
- No intake-channel field affects approval, authorization, provisioning, visibility,
  or grant duration.

## Relationships

```text
RequestPreparationConversation 1 ─── 0..1 PreparedAccessRequest
PreparedAccessRequest           1 ─── 0..1 AccessRequest
AccessRequest                   1 ─── 0..* existing ApprovalDecision
AccessRequest                   1 ─── 0..1 existing ProvisioningOperation
AccessRequest                   1 ─── 0..1 existing AccessGrant
AccessRequest                   1 ─── 1..* existing AuditEvent
```

The prepared-to-request relationship is joined by unique
`ReservedRequestId = AccessRequest.Id`; it does not let an access request depend on a
Teams or MAF SDK type.

## Confirmation Transaction

For the first accepted confirmation, one `SaveChangesAsync` must commit:

1. prepared status `Ready → Submitted`;
2. cleared/terminal conversation content;
3. immutable `AccessRequest` with the reserved ID; and
4. existing `RequestCreated` audit event.

The handler emits a structured confirmation outcome log after the transaction
commits. That log is operational telemetry, not part of the atomic domain evidence.

If a concurrent save loses on the prepared concurrency token or request primary key,
the handler clears failed tracking state, reloads by `PreparationId`, and:

- returns the stored request ID when the snapshot is `Submitted` for the same actor
  and conversation; or
- returns the appropriate terminal/rejected typed outcome.

No request, approval, provisioning operation, or grant is created for expired,
superseded, invalidated, foreign-owner, conversation-mismatched, malformed, or stale
confirmations.

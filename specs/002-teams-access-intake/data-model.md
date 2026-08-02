# Data Model: Teams Access Request Intake

## Modeling Principles

- Teams confirmation is the only request-creation path.
- Model proposals and collecting candidate values are untrusted.
- Authoritative validation alone can make an intake ready.
- Ready scope is immutable; correction creates a new intake and request ID.
- One `RequestIntakeSession` owns collecting, ready, and terminal evidence.
- Confirmation stages the immutable request and request-created audit event and commits
  them with the intake transition in one shared `SaveChangesAsync`.
- Existing request, approval, operation, grant, and audit schemas remain unchanged.
- Raw activities, prompts, model messages, complete MCP payloads, MAF sessions, and
  transcripts are not written to the application database or logged. MAF history
  exists only in the native in-memory session store for the process lifetime.

## Request Intake Session

One provider-neutral aggregate binds an authenticated Teams actor and personal
conversation from collection through confirmation and old-card replay handling.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Opaque server-generated primary key carried by the card |
| `Channel` | string | Fixed canonical value `msteams` |
| `TenantId` | string | Derived from authenticated activity |
| `ChannelActorId` | string | Stable authenticated Teams/AAD actor binding |
| `ConversationId` | string | Authenticated personal-conversation identifier |
| `RequesterId` | string | Fixed server mapping to the synthetic requester |
| `Status` | enum | `Collecting`, `Ready`, `Submitted`, `Superseded`, `Expired`, `Invalidated` |
| `ClientId` | nullable string | Compact candidate; canonical and immutable while ready |
| `EnvironmentId` | nullable string | Compact candidate; canonical and immutable while ready |
| `RequestedRoleId` | nullable string | Compact candidate; canonical and immutable while ready |
| `Justification` | nullable string | 10–2,000 characters when ready |
| `IncidentId` | nullable string | Optional canonical incident |
| `ReservedRequestId` | nullable `Guid` | Assigned once when ready; unique future `AccessRequest.Id` |
| `CreatedAt` | `DateTimeOffset` | UTC server time |
| `LastUpdatedAt` | `DateTimeOffset` | UTC last accepted operation |
| `ExpiresAt` | nullable `DateTimeOffset` | Exactly readiness time + 30 minutes |
| `SubmittedAt` | nullable `DateTimeOffset` | Set once on successful confirmation |
| `CorrelationId` | string | Latest safe operation correlation identifier |
| `PersistenceVersion` | `long` | Optimistic concurrency token |

### Identity and uniqueness

Only one nonterminal row may exist for
`(Channel, TenantId, ChannelActorId, ConversationId)`. `ReservedRequestId` is unique
when present. Actor and conversation ownership is checked even though all accepted
actors map to the same synthetic requester.

### Content lifecycle

- `Collecting` stores only the compact canonical candidate and intake metadata.
- Every schema-valid proposal contains the complete nullable candidate snapshot.
  After deterministic canonicalization accepts the turn, that snapshot replaces the
  previous collecting candidate; `null` means absent or explicitly cleared.
- Process-local MAF history is not part of this aggregate or the database. The native
  MAF session store keys it by `RequestIntakeSession.Id`, isolates it per intake, and
  loses it on process termination without causing a domain transition.
- `Ready` fixes canonical scope, reserved request identity, and expiry.
- `Submitted`, `Superseded`, `Expired`, and `Invalidated` retain binding, status,
  reserved request identity, timestamps, and correlation metadata but clear candidate
  content.

### Transitions

```text
Collecting --valid and complete--> Ready
Collecting --new preparation-----> Superseded
Ready ------confirmed------------> Submitted
Ready ------new preparation------> Superseded
Ready ------expiry observed------> Expired
Ready ------stale context--------> Invalidated
Submitted --duplicate confirm----> Submitted (same request ID, no new evidence)
```

## Request Candidate and Clarification Proposal

The provider-neutral candidate contains nullable `ClientId`, `EnvironmentId`,
`RequestedRoleId`, `Justification`, and `IncidentId`. `RequestValidator` owns
canonicalization, relationship checks, and readiness.

The optional clarification proposal contains one closed target (`ClientId`,
`EnvironmentId`, `RequestedRoleId`, `Justification`, or `IncidentId`) and one
non-empty user-facing message of at most 500 characters. It contains no structured
option list. Any choices presented by the model remain part of the active MAF
conversation history, while every identifier proposed on a later turn is still
canonicalized against authoritative data.

## Process-Local MAF Session Store

Infrastructure registers MAF's native `InMemoryAgentSessionStore` as a singleton and
addresses sessions by `RequestIntakeSession.Id`. It serializes the agent-owned session
state in process memory. A separate process-lifetime coordinator holds one exact
asynchronous gate per intake and serializes each load, run, and save sequence.

- Sessions and gates are isolated by intake ID and are never shared across actors or
  conversations.
- Sessions have no application-owned inactivity timeout, turn-count limit, terminal
  deletion, or compaction in the current local baseline; process termination clears
  them.
- Process restart causes no domain transition and loses no accepted candidate data.
- Successfully restored sessions supply their prior conversation messages directly;
  the application adds no history marker or history-availability field.
- Failed or cancelled runs and malformed proposals do not overwrite the last
  successfully serialized session.
- A turn without prior conversation messages still receives the current durable
  candidate; the model must ask a self-contained clarification instead of resolving a
  relative answer such as “the first one.”
- Confirmation, approval, provisioning, revocation, authorization, and audit evidence
  never depend on this memory.
- Durable session rows, retention/deletion policy, multi-host coordination, and native
  MAF compaction are deferred design concerns rather than current entities.

## Existing Access Request

`AccessRequest` remains the immutable, channel-neutral workflow aggregate. It can be
created only from deterministic confirmation of a ready intake:

- its ID equals `RequestIntakeSession.ReservedRequestId`;
- requester and exact scope come from the reloaded and revalidated intake;
- creation time is the caller-supplied confirmation timestamp;
- initial status is `AwaitingBusinessApproval`; and
- one existing `RequestCreated` audit event is staged with it.

The Web application does not map a browser draft endpoint or request-creating
`POST /api/requests`. Existing request rows remain queryable and require no migration
or cleanup.

## Relationships

```text
RequestIntakeSession 1 --- 0..1 AccessRequest
AccessRequest        1 --- 0..* ApprovalDecision
AccessRequest        1 --- 0..1 ProvisioningOperation
AccessRequest        1 --- 0..1 AccessGrant
AccessRequest        1 --- 1..* AuditEvent
```

The intake-to-request relationship uses
`RequestIntakeSession.ReservedRequestId = AccessRequest.Id` and introduces no Teams,
MAF, Adaptive Card, or MCP SDK type into Core.

## Confirmation Save Boundary

The first accepted confirmation performs one shared save containing:

1. `RequestIntakeSession` transition `Ready -> Submitted` and terminal content clear;
2. immutable `AccessRequest` with the reserved ID and exact revalidated scope; and
3. existing `RequestCreated` audit event.

A concurrent loser clears failed tracking state, reloads by intake ID, and returns the
stored request ID for an owned submitted intake or the appropriate closed failure.
Expired, superseded, invalidated, foreign-owner, conversation-mismatched, malformed,
or stale confirmations create no request, approval, operation, grant, or audit event.

## Pre-Submission Operational Evidence

Before an `AccessRequest` exists, only structured operational logs record correlation,
safe binding identifiers, intake ID/status transition, operation, outcome, duration,
and submitted request ID when available. These logs are not durable domain evidence
and exclude tokens, prompts, transcripts, candidate values, cards, model bodies, and
complete MCP payloads.

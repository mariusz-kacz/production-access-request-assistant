# Data Model: Exercise the Real Conversational Model

## Modeling Decision

This feature adds no persisted domain entity and no database migration. Model
selection and provider execution are operational infrastructure concerns. A request
prepared by the real model is indistinguishable at the domain boundary from one
prepared by the deterministic client: both produce an untrusted proposal that must
pass the same authoritative validation before the existing intake can become ready.

## Request Preparation Model Profile

One process-wide configuration object selects the request-preparation client.

| Field | Type | Rules |
|------|------|-------|
| `ExecutionProfile` | closed enum | Exactly `Deterministic` or `AzureOpenAI`; unknown or missing values fail closed |
| `TurnTimeout` | duration | Exactly 90 seconds for the current baseline and strictly less than the 100-second Teams endpoint deadline |
| `ApprovedModelIds` | string collection | Server-owned, exact ordinal values; non-empty when Azure OpenAI is selected |
| `AzureOpenAI.Endpoint` | nullable absolute URI | Required for Azure OpenAI; HTTPS Azure OpenAI origin only, with no user info, non-root path, query, or fragment |
| `AzureOpenAI.TenantId` | nullable GUID | Required non-empty Microsoft Entra tenant identifier for Azure OpenAI credential discovery |
| `AzureOpenAI.DeploymentName` | nullable string | Required bounded Azure deployment name; never supplied by requester content |
| `AzureOpenAI.ModelId` | nullable string | Required and must exactly match one `ApprovedModelIds` entry; safe to log as operational metadata |

No API key, bearer token, client secret, or credential value is part of this object.
`DefaultAzureCredential` discovers the signed-in developer identity, constrained to
`TenantId`. Credential absence or authorization failure is observed only when the
real provider is called and maps to `Unavailable`.

### Resolution states

```text
Configuration read
    ├── Deterministic ------------------------> Deterministic client ready
    ├── AzureOpenAI + structurally valid ----> Azure client ready
    ├── AzureOpenAI + missing/invalid field --> Unavailable client
    └── missing/unknown profile -------------> Unavailable client
```

Resolution happens once at host composition. Requester messages, activity data, card
data, browser input, and prior conversation content cannot change it.

### Validation rules

- The deterministic profile requires no Azure credential or endpoint.
- The Azure profile requires every Azure field and a selected model in the approved
  list.
- Endpoint validation prevents sending credentials or prompts to a non-Azure or
  path-injected target.
- Validation failures identify safe field names only; values and credential-discovery
  details are not returned to the requester or written to logs.
- Invalid Azure configuration must not stop the authenticated Teams endpoint from
  returning safe failure guidance and must never select the deterministic client.

## Real Model Turn

One ephemeral interpretation operation spans MCP discovery, model/tool calls, strict
response parsing, and successful MAF session save.

| Field | Type | Rules |
|------|------|-------|
| `IntakeId` | GUID | Existing server-generated intake identity and MAF session key |
| `CorrelationId` | string | Existing server-owned safe operation correlation identifier |
| `ProfileId` | closed string | Selected process-wide profile; not requester-controlled |
| `ModelId` | nullable string | Approved Azure model identity; absent for deterministic profile |
| `StartedAt` | timestamp | UTC operational measurement only |
| `Deadline` | timestamp | `StartedAt + 90 seconds`; one cumulative budget |
| `Outcome` | closed enum | `Proposal`, `MalformedModelOutput`, `Timeout`, `Cancelled`, or `Unavailable` |
| `Duration` | duration | Logged without prompt, response, or payload data |

The turn is not stored in SQLite. Structured logs contain only the safe metadata
above. MAF persists the successfully completed conversation session only in its
existing process-local in-memory store.

### Turn transitions

```text
Started
  ├── valid schema response ----------------------> Proposal
  ├── malformed/unsupported response ------------> MalformedModelOutput
  ├── cumulative inner deadline expires ---------> Timeout
  ├── outer caller cancellation -----------------> Cancelled
  └── profile/auth/provider/MCP dependency fails -> Unavailable
```

Only `Proposal` may proceed to authoritative validation. Failed turns do not save the
mutated MAF session and do not replace the intake's last accepted candidate.

## Existing Request Proposal

The existing provider-neutral proposal remains unchanged:

- complete nullable candidate snapshot: client, environment, requested role,
  justification, optional incident;
- optional single focused clarification target and message; and
- closed `candidate` or `clarification` kind.

It remains untrusted regardless of `ProfileId`. The canonical contract is
`specs/002-teams-access-intake/contracts/request-intake-proposal.schema.json`.

## Existing Request Intake Session

No field or transition is added. Existing rules remain:

- actor, tenant, conversation, and requester binding;
- collecting candidate stored only after a structurally valid proposal and
  deterministic application decision;
- immutable canonical scope and reserved request ID at readiness;
- 30-minute confirmation expiry;
- terminal content clearing; and
- no session or candidate update after provider failure, malformed output, timeout,
  or cancellation.

## Existing Access Request and Workflow Evidence

`AccessRequest`, approval decisions, provisioning operation, access grant, and audit
events are unchanged. Confirmation reloads and revalidates the intake, creates the
request with its reserved ID, and enters `AwaitingBusinessApproval`. Business and
DevOps decisions remain authenticated structured actions. Provisioning still reloads
persisted evidence and uses request ID as the idempotency identity.

## Relationships

```text
Request Preparation Model Profile (configuration only)
                 │ selects one IChatClient
                 ▼
Real Model Turn (ephemeral operational metadata)
                 │ returns untrusted proposal
                 ▼
Request Intake Session 1 ─── 0..1 Access Request
Access Request        1 ─── 0..* Approval Decision
Access Request        1 ─── 0..1 Provisioning Operation
Access Request        1 ─── 0..1 Access Grant
Access Request        1 ─── 1..* Audit Event
```

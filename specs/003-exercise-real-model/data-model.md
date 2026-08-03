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
| `ExecutionProfile` | closed enum | Exactly `Deterministic` or `FoundryResponses`; unknown or missing values fail closed |
| `FoundryResponses.Endpoint` | nullable absolute URI | Required for Foundry Responses; HTTPS `*.services.ai.azure.com/openai/v1` inference base only, with no user info, query, fragment, custom port, or extra path |
| `FoundryResponses.DeploymentName` | nullable string | Required bounded Foundry deployment name; never supplied by requester content |

No API key, bearer token, client secret, or credential value is part of this object.
`DefaultAzureCredential` discovers the host's signed-in developer or managed identity.
Tenant selection remains part of that external credential chain rather than this
profile. Credential absence or authorization failure is observed only when the real
provider is called and maps to `Unavailable`.

The existing ASP.NET Core 100-second Teams request timeout is the single overall
deadline. Its cancellation token flows through model and MCP work; it is not a
model-profile setting or persisted value.

### Resolution states

```text
Configuration read
    ├── Deterministic ------------------------> Deterministic client ready
    ├── FoundryResponses + valid -----------> Responses client ready
    ├── FoundryResponses + invalid field ---> Unavailable client
    └── missing/unknown profile -------------> Unavailable client
```

Resolution happens once at host composition. Requester messages, activity data, card
data, browser input, and prior conversation content cannot change it.

### Validation rules

- The deterministic profile requires no Foundry credential or endpoint.
- The Foundry Responses profile requires only a trusted endpoint and a bounded
  deployment name. Selecting that server-owned profile and deployment is the
  operator's approval action.
- Endpoint validation prevents sending credentials or prompts to a non-Foundry or
  path-injected target and requires the Responses API base path.
- Validation failures identify safe field names only; values and credential-discovery
  details are not returned to the requester or written to logs.
- Invalid Foundry configuration must not stop the authenticated Teams endpoint from
  returning safe failure guidance and must never select the deterministic client.

## Real Model Turn

One ephemeral interpretation operation spans MCP discovery, model/tool calls, strict
response parsing, and successful MAF session save.

| Field | Type | Rules |
|------|------|-------|
| `IntakeId` | GUID | Existing server-generated intake identity and MAF session key |
| `CorrelationId` | string | Existing server-owned safe operation correlation identifier |
| `ProfileId` | closed string | Selected process-wide profile; not requester-controlled |
| `DeploymentName` | nullable string | Configured Foundry deployment; absent for deterministic profile |
| `StartedAt` | timestamp | UTC operational measurement only |
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
  ├── provider-side timeout ----------------------> Timeout
  ├── request timeout or caller cancellation -----> Cancelled
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

No field, status, or schema is added. Explicit reset invokes existing terminal
transitions instead of deleting or rewriting a preparation. Existing rules remain:

- actor, tenant, conversation, and requester binding;
- collecting candidate stored only after a structurally valid proposal and
  deterministic application decision;
- immutable canonical scope and reserved request ID at readiness;
- 30-minute confirmation expiry;
- terminal content clearing; and
- no session or candidate update after provider failure, malformed output, timeout,
  or cancellation.

### Explicit reset command

The reset command is ephemeral application input, not stored business data.

| Field | Type | Rules |
|------|------|-------|
| `Actor` | authenticated channel actor | Existing server-resolved tenant, actor, and conversation binding; never accepted from message text |
| `CorrelationId` | string | Existing server-owned operation correlation identifier |
| `Command` | literal | Exact trimmed, case-insensitive `/new`; longer messages are ordinary preparation input |

The operation selects only the active intake for the authenticated actor and
conversation. It does not select submitted requests and does not create a new intake.

```text
active Collecting -------- /new --------> Superseded (candidate cleared)
active Ready, unexpired -- /new --------> Superseded (candidate cleared; card invalid)
active Ready, expired ---- /new --------> Expired    (candidate cleared)
no active intake --------- /new --------> unchanged, idempotent success
Submitted request -------- /new --------> not selected and unchanged

next ordinary message -----------------> new Collecting intake with a new intake ID
```

The existing lifecycle record captures the terminal transition and correlation
evidence. A new intake ID causes the existing process-local MAF session store to use a
different key, so old model history cannot become history for the replacement
preparation. No explicit MAF-session deletion contract is required.

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

`/new` affects only the `Request Intake Session` before an `Access Request` exists.
It does not add a relationship or persistence object.

# As-Built Architecture

- **Status**: Current
- **Last reviewed**: 2026-08-02
- **Scope**: Governed Production Access Request Assistant MVP

The history-first Teams preparation and Teams-only creation boundaries described here
are implemented and covered by the current feature test suite.

## Purpose

This document describes the architecture implemented in the repository. It explains
the runtime shape, source dependencies, trust boundaries, principal workflows,
persistence and consistency model, and important operational characteristics.

The governing design rule is:

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

This document is an as-built view. Product intent and requirements remain in the
[product baseline](governed-production-access-product-baseline.md), while the reasons
for significant design choices are recorded in the
[architecture decision records](adr/).

## Architectural drivers

The implementation is shaped by the following constraints:

- one executable ASP.NET Core host;
- a thin React application served from that host;
- local SQLite persistence and a fixed synthetic dataset;
- synthetic cookie authentication with exactly four demo principals;
- a deterministic chat client for automated validation, behind the same boundary a
  live model can later implement;
- a real, read-only MCP endpoint with exactly three tools;
- no model-visible approval, workflow, or provisioning capability;
- immutable submitted requests and request-bound approvals;
- deterministic authorization and validation for every state change;
- idempotent synthetic provisioning;
- explicit typed outcomes, bounded timeouts, and cancellation propagation; and
- no distributed infrastructure introduced solely for the portfolio scenario.

## System context

The system has four human roles represented by fixed synthetic principals: requester,
Client Alpha business approver, Client Beta business approver, and DevOps approver.
The browser never supplies authoritative identity or role claims. It selects a known
demo principal, and the server issues an HttpOnly authentication cookie containing
server-defined claims.

```mermaid
flowchart LR
    Requester[Requester]
    Business[Business approver]
    DevOps[DevOps approver]
    Teams[Microsoft Teams personal chat]
    Browser[React browser application]

    subgraph Host[Governed Access Host]
        Agent[Authenticated /api/messages adapter]
        API[Same-origin UI API]
        AI[MAF interpretation adapter]
        MCP[Read-only /mcp endpoint]
        App[Application and domain rules]
        Provider[Synthetic access provider]
        DB[(SQLite)]
    end

    Requester -->|prepare and confirm| Teams
    Requester -->|list and inspect| Browser
    Business --> Browser
    DevOps --> Browser
    Teams --> Agent
    Browser -->|cookie-authenticated HTTPS| API
    Agent --> AI
    Agent --> App
    API --> App
    AI -->|loopback Streamable HTTP| MCP
    MCP --> DB
    App --> DB
    App --> Provider
```

The synthetic access provider creates only local demonstration grants. The system has
no connection to a real identity provider, incident system, client environment, or
access-control provider.

## Deployment and runtime view

`GovernedAccess.Web` is the only executable and the only deployment unit. One process
hosts:

- ASP.NET Core MVC controllers;
- cookie authentication and authorization;
- antiforgery protection;
- correlation middleware and application instrumentation;
- the compiled React static assets and SPA fallback;
- Teams request interpretation, the deterministic `IChatClient`, and MAF's native
  process-local session store;
- a real MCP client;
- the stateless Streamable HTTP `/mcp` server;
- request validation and workflow application services;
- EF Core with one SQLite database; and
- the synthetic access provisioner.

The Teams interpretation adapter reaches `/mcp` over HTTP, even though client and
server are in the same process. This preserves the real MCP initialization, tool
discovery, serialization, invocation, timeout, and failure boundary without creating
another deployable service.

The SPA fallback handles browser routes only. Unknown `/api/*` and `/mcp/*` paths
return `404` and are never rewritten to `index.html`.

## Source dependency view

```mermaid
flowchart TB
    Web[GovernedAccess.Web<br/>sole executable]
    Mcp[GovernedAccess.Mcp<br/>MCP infrastructure adapter]
    Core[GovernedAccess.Core<br/>domain, application, and ports]

    Web --> Core
    Web --> Mcp
    Mcp --> Core
```

### `GovernedAccess.Core`

Core contains:

- domain entities and workflow evidence;
- business and DevOps decision policies;
- workflow evidence validation;
- request validation, intake, confirmation-only submission staging, query, workflow,
  and protected provisioning
  services;
- explicit application and provider outcomes; and
- ports for request context, workflow persistence, intake persistence, time,
  request-preparation interpretation, and provisioning.

Core does not reference ASP.NET Core MVC, EF Core, React, `Microsoft.Extensions.AI`,
or the MCP SDK. AI-provider and protocol-specific types are translated before they
cross into Core.

### `GovernedAccess.Mcp`

MCP contains:

- stateless Streamable HTTP server registration;
- explicit registration of the three allowed tools;
- typed tool input and result records; and
- translation between MCP-facing contracts and `IRequestContextReader`.

It references Core for the request-context port and domain records. It has no workflow
store, decision service, or provisioning dependency.

### `GovernedAccess.Web`

Web is the composition and infrastructure layer. It contains:

- API controllers and Problem Details translation;
- synthetic authentication and antiforgery;
- Teams activity handling, `MafRequestPreparationInterpreter`, MAF's singleton
  `InMemoryAgentSessionStore`, the process-lifetime
  `MafConversationTurnCoordinator`, and `DeterministicChatClient`;
- the EF Core database context, request-context reader, workflow store, and seeder;
- the synthetic provisioner;
- correlation and activity instrumentation; and
- the React source and generated static assets.

Controllers remain thin: they derive the actor from `ClaimsPrincipal`, translate
request and response shapes, call application services, and map typed failures.

## Runtime components

| Component | Responsibility | Does not decide |
|---|---|---|
| React UI | Display the request register/detail, submit structured approval or retry actions, and show audit evidence. | Request creation, identity, authorization, approver assignment, or valid workflow transitions. |
| MVC controllers | Enforce endpoint authentication/antiforgery attributes, extract server identity, translate HTTP contracts, and invoke application services. | Domain policy or provisioning eligibility. |
| `TeamsAccessRequestAgent` | Route authenticated personal Teams activities to preparation or deterministic confirmation. | Actor authority, readiness, approval, or provisioning. |
| `MafRequestPreparationInterpreter` | Discover the exact MCP allowlist, invoke the agent turn under request cancellation, schema-parse its proposal, and translate provider contracts. | Readiness, approval, authorization, workflow state, or provisioning. |
| `AIHostAgent` with `InMemoryAgentSessionStore` | Load and save MAF-owned conversation sessions by server-generated intake ID for the process lifetime. | Durable workflow state, candidate truth, readiness, confirmation, or authorization. |
| `MafConversationTurnCoordinator` | Serialize the complete native session load, agent run, and successful save sequence with one exact process-lifetime gate per intake. | Session retention policy, durable state, or workflow transitions. |
| `RequestValidator` | Validate current client, environment, role, justification, and incident context. | Human authority or approval outcome. |
| `RequestIntakeService` | Coordinate compact preparation and deterministic confirmation over one intake aggregate. | Model-supplied authority or downstream approval. |
| `RequestSubmissionService` | Revalidate and stage a reserved-ID request and request-created audit event for confirmation; never save independently. | Public/browser submission or later approval/provisioning transitions. |
| `AccessRequestWorkflowService` | Coordinate business decisions, DevOps decisions, and retry using authenticated principals and deterministic policies. | Provider execution based on caller assertions. |
| `ProtectedProvisioningService` | Reload persisted workflow evidence, validate exact scope, call the provider, and persist the operation outcome. | Business or DevOps approval. |
| `RequestQueryService` | Return participant-authorized lists and detail projections with server-computed available actions. | Authorization based on UI visibility. |
| EF adapters | Translate Core persistence and context ports to SQLite. | Domain policy. |
| Synthetic provisioner | Create or return one local grant using the immutable request ID. | Eligibility, role selection, or approval validity. |

## Teams request preparation and confirmation

Teams confirmation is the only request-creation path. Preparation is model-assisted;
confirmation is a direct deterministic application action.

```mermaid
sequenceDiagram
    actor User
    participant Teams
    participant Agent as TeamsAccessRequestAgent
    participant Intake as RequestIntakeService
    participant Draft as MafRequestPreparationInterpreter
    participant Memory as Native MAF session store
    participant Gate as Per-intake turn coordinator
    participant Chat as IChatClient
    participant McpClient as MCP client
    participant McpServer as /mcp server
    participant Context as IRequestContextReader
    participant DB as SQLite

    User->>Teams: Describe request
    Teams->>Agent: Authenticated personal activity
    Agent->>Intake: PrepareAsync(actor, latest message)
    Intake->>Draft: Intake ID + complete candidate + validation feedback + latest message
    Draft->>McpClient: Initialize and list tools
    McpClient->>McpServer: Streamable HTTP
    McpServer-->>McpClient: Exactly three read-only tools
    Draft->>Gate: Execute turn under intake ID
    Gate->>Memory: Get or create native AgentSession
    Memory-->>Gate: Session with available prior messages
    Draft->>Chat: Current candidate, latest text, strict schema, allowed tools
    opt Model requests stored context
        Chat->>McpClient: Invoke allowed tool
        McpClient->>McpServer: Typed tool call
        McpServer->>Context: Read authoritative context
        Context->>DB: Query
        DB-->>Context: Stored record
        Context-->>McpServer: Typed outcome
        McpServer-->>Chat: Tool result
    end
    Chat-->>Draft: Closed JSON proposal
    Draft->>Draft: Strict schema parsing and boundary translation
    Draft-->>Gate: Successfully validated outcome
    Gate->>Memory: Save native session
    Draft-->>Intake: Untrusted complete candidate + optional target/message
    Intake->>Context: Revalidate identifiers and relationships
    Context->>DB: Query authoritative records
    Intake->>DB: Replace compact candidate or persist immutable ready scope
    Agent-->>Teams: Clarification, safe failure, or immutable card
    User->>Teams: Confirm and submit
    Teams->>Agent: Authenticated Action.Execute
    Agent->>Intake: ConfirmAsync(actor, intake ID)
    Intake->>DB: Reload ownership/status/scope and revalidate
    Intake->>DB: One save: submitted intake + request + audit
    Agent-->>Teams: Stable request ID and trusted Web link
```

The collecting candidate is untrusted and creates no request or approval. Only an
owned, unexpired ready intake can be confirmed. The model and MCP never receive a
submit capability.

MAF history is history-first but deliberately ephemeral. The singleton native store
keys sessions by server-generated intake ID, while the singleton coordinator retains
one exact gate per intake and serializes load, run, and save. Both live for the host
process lifetime. The application applies no inactivity timeout, turn-count limit,
terminal deletion, or conversation compaction in the current low-volume baseline.
Only a completed schema-valid turn is saved; timeout, cancellation, dependency
failure, and malformed output leave the last successfully stored session unchanged.

SQLite persists the accepted complete candidate and intake lifecycle, not
clarification options, prompts, transcripts, or serialized MAF sessions. That compact
candidate is the durable canonical state supplied to every model turn. Process restart
clears MAF history and gates without changing the intake or losing accepted candidate
data. The next run receives the durable candidate without prior conversation messages;
an ambiguous relative reply is re-clarified rather than guessed unless the supplied
conversation itself contains its preceding question and ordering. Confirmation and
all downstream workflow actions never read this memory.

Durable MAF sessions, retention/deletion policy, multi-host coordination, and native
MAF compaction are deferred until a concrete production requirement defines privacy,
capacity, and lifecycle rules. They are not hidden responsibilities of the SQLite
intake store.

The adapter defaults to:

- one 100-second ASP.NET Core request-safety deadline on the Teams activity
  endpoint, propagated through MCP and model work;
- at most six model/tool iterations;
- no concurrent tool invocation; and
- termination on unknown tool calls.

The MCP client rejects a catalog that does not contain exactly:

- `get_production_environment`;
- `get_incident`; and
- `get_available_roles`.

The complete wire contract is
[specs/001-governed-production-access/contracts/mcp-tools.json](../specs/001-governed-production-access/contracts/mcp-tools.json).

## Governed workflow

```mermaid
stateDiagram-v2
    [*] --> AwaitingBusinessApproval: validated submission
    AwaitingBusinessApproval --> AwaitingDevOpsApproval: business approves
    AwaitingBusinessApproval --> Rejected: business rejects
    AwaitingDevOpsApproval --> Rejected: DevOps rejects
    AwaitingDevOpsApproval --> Active: provisioning succeeds
    AwaitingDevOpsApproval --> ProvisioningFailed: provisioning fails
    ProvisioningFailed --> Active: DevOps retry succeeds
    ProvisioningFailed --> ProvisioningFailed: retry fails
```

### Submission

1. The Teams boundary derives actor, tenant, and conversation from authenticated
   activity context.
2. `RequestIntakeService` reloads the ready intake, verifies ownership/status/expiry,
   and revalidates its immutable scope.
3. `RequestSubmissionService` requires the server-reserved request ID and confirmation
   timestamp and stages the request plus request-created audit event.
4. The intake transition, immutable `AwaitingBusinessApproval` request, and audit
   event commit in one shared `SaveChangesAsync`.

There is no update endpoint for a submitted request. A correction produces a new
Teams preparation, request ID, and new approvals.

From `AwaitingBusinessApproval` onward, the existing browser-driven human approval,
protected provisioning, retry, and audit flow is unchanged. Teams creates the same
immutable request aggregate consumed by that flow; it does not bypass or duplicate it.

### Business decision

1. `AccessRequestWorkflowService` loads the authenticated principal and request.
2. It validates current stored request context.
3. It resolves the environment's configured business approver.
4. `BusinessDecisionPolicy` validates state, authority, duplicate-stage prevention,
   and exact-role binding.
5. The decision, workflow transition, and audit evidence are saved together.

The requester cannot nominate or replace the business approver.

### DevOps decision and provisioning

1. The workflow service loads the authenticated DevOps principal, request, and
   business approval.
2. It validates current request context and applies `DevOpsDecisionPolicy`.
3. Rejection records the decision and moves the request to `Rejected`.
4. Approval records the exact-role DevOps decision and creates the request-keyed
   pending provisioning operation.
5. The decision, operation, request version, and audit evidence are committed before
   provider invocation.
6. The workflow service passes only the request ID to
   `ProtectedProvisioningService`.
7. The protected service reloads the operation, immutable request, business approval,
   and DevOps approval.
8. It validates workflow state, operation scope, approval order, and exact role.
9. It persists the provisioning-attempt audit event.
10. It calls the synthetic provider with server-constructed scope.
11. Provider success finalizes the request, operation, grant, and success audit event
    in one local save.

Every successful grant expires exactly eight hours after activation. Duration is not
accepted from the requester, either approver, the browser, or the model.

### Retry

Only the authenticated DevOps approver can retry, and only when both the request and
operation are in failed states. Retry:

- accepts no replacement scope;
- uses the same protected provisioning service;
- reloads and validates persisted evidence again;
- increments the existing operation attempt count;
- reuses the request ID as the provider idempotency identity; and
- returns an already completed matching grant when concurrent work has won the race.

## Persistence model

One EF Core `GovernedAccessDbContext` uses SQLite. It stores three categories of data.

### Fixed reference context

- clients;
- production environments;
- environment roles;
- incidents; and
- authenticated principals.

`SyntheticDataSeeder` creates missing expected records and validates existing records
against the exact dataset. Startup fails when a stored reference record conflicts with
the expected synthetic definition or when an unexpected reference record exists.
There is no runtime command surface for mutating reference context.

### Request intake state

- one `RequestIntakeSession` row per server-generated intake;
- authenticated channel, tenant, actor, requester, and personal-conversation binding;
- the complete nullable candidate while collecting;
- immutable ready scope, reserved request ID, and 30-minute confirmation expiry; and
- terminal status and correlation metadata for replay-safe old-card handling.

The database stores no raw activity, prompt, transcript, clarification option list,
model response, or serialized MAF session. Ready scope is immutable, active binding
and reserved request IDs are unique, and optimistic concurrency protects terminal
transitions.

### Workflow evidence

- access requests;
- business and DevOps approval decisions;
- provisioning operations;
- access grants; and
- audit events.

Important database guarantees include:

- `AccessRequest.PersistenceVersion` is an optimistic concurrency token;
- one decision per request and approval stage;
- one provisioning operation keyed by request ID;
- at most one access grant per request ID;
- restricted deletes across authoritative relationships; and
- ordered insert-only audit records from the application workflow.

The Teams intake entity and save boundary are defined in the
[Teams intake data model](../specs/002-teams-access-intake/data-model.md). Existing
workflow entities remain defined in the
[governed workflow data model](../specs/001-governed-production-access/data-model.md).

## Consistency and idempotency

SQLite commits tracked workflow changes and staged audit evidence atomically within
each `SaveChangesAsync`. That local guarantee does not extend across a general access
provider call.

The implementation deliberately uses this sequence:

```text
persist DevOps approval and pending operation
        |
reload and validate persisted workflow evidence
        |
persist provisioning-attempt evidence
        |
call provider with request ID as idempotency identity
        |
persist local success or typed failure outcome
```

Provider success followed by cancellation, process failure, or local persistence
failure is a possible partial outcome. The provider's get-or-create behavior, the
stable request ID, the unique grant constraint, and the scoped retry path allow the
workflow to converge without claiming cross-system atomicity.

The relevant decisions are:

- [ADR 0002: Validate Persisted Workflow Evidence at Provisioning](adr/0002-validate-persisted-workflow-evidence-at-provisioning.md)
- [ADR 0003: Do Not Model Provider and Workflow Persistence as Atomic](adr/0003-do-not-model-provider-and-workflow-persistence-as-atomic.md)
- [ADR 0004: Use Request ID as the Provisioning Idempotency Identity](adr/0004-use-request-id-as-provisioning-idempotency-identity.md)

## Interface boundaries

### Browser API

The `/api` surface is a same-origin adapter for the co-hosted React request register,
not a general public API. It provides:

- antiforgery and demo-session operations;
- request list and detail queries;
- business and DevOps decision subresources; and
- DevOps-only retry from `ProvisioningFailed`.

It does not map `POST /api/request-drafts/prepare` or a request-creating
`POST /api/requests`. Existing request rows and downstream workflow contracts are
unchanged.

Unsafe endpoints require antiforgery validation. Request bodies do not accept
authoritative actor, role claims, approver identity, approval assertions, duration,
or replacement provisioning scope. The detailed shapes are in the
[UI API contract](../specs/001-governed-production-access/contracts/ui-api.md).

### MCP

The stateless `/mcp` endpoint exposes stored, read-only request context. MCP types are
translated to the Core request-context port. It exposes no resources, prompts,
generic queries, arbitrary database access, workflow commands, approvals,
provisioning, or revocation.

Tool visibility and annotations are not authorization. Safety comes from the narrow
capability set, typed schemas, stored-data lookup, and the complete absence of
state-changing dependencies in the MCP project.

### Internal ports

Core depends on focused interfaces:

- `IRequestContextReader`;
- `IWorkflowStore` and `IAuditStore`;
- `IRequestPreparationInterpreter` and `IRequestIntakeStore`;
- `IAccessProvisioner`; and
- `IClock`.

These interfaces exist at concrete infrastructure boundaries. The application does
not introduce a generic repository, workflow engine, event bus, or provider-neutral
abstraction without a current implementation need.

## Authentication and request security

Demo authentication maps one fixed principal key to immutable server-side claims and
issues an HttpOnly, Secure, SameSite Strict cookie. Authentication failures return
`401` and authorization failures return `403` instead of browser redirects.

The React client obtains an antiforgery token and includes `X-XSRF-TOKEN` on unsafe
same-origin requests. UI capabilities and `availableActions` are presentation hints;
controllers and application services enforce every protected action independently.

Participant filtering prevents unrelated principals from discovering request detail.
The wrong client business approver receives no authority over another client's
request.

A fuller threat and control analysis is in the
[security and trust model](security-model.md).

## Failure, cancellation, and observability

Expected failures cross application boundaries as typed outcomes and become safe
Problem Details or typed draft results. Categories include invalid input, validation,
unauthenticated, unauthorized, not found, invalid transition, concurrency conflict,
timeout, cancellation, unavailability, and dependency failure.

Timeout defaults are:

| Boundary | Default |
|---|---:|
| Teams activity HTTP request, including MCP and model work | 100 seconds |
| Synthetic provisioning operation | 10 seconds |

Caller cancellation is linked through asynchronous boundaries. Cancellation does not
convert a caller-aborted provider operation into a persisted retryable provider
failure.

`CorrelationMiddleware` assigns an `X-Correlation-ID` from the current trace ID or a
new GUID and places it in the response, logging scope, persisted request/evidence, and
safe Problem Details. Model, MCP, workflow, and provisioning operations record
duration and outcome metadata. Raw prompts, secrets, and complete MCP payloads are not
required for normal logging.

OpenTelemetry export is not configured. The application exposes an `ActivitySource`
as an optional instrumentation seam.

## Frontend build and hosting

The React application is source input to `GovernedAccess.Web`, not a separate
production service.

- Vite writes hashed production assets to `GovernedAccess.Web/wwwroot`.
- the Web project runs `npm ci` when the lockfile requires restoration;
- the frontend build runs before .NET build or publish when inputs changed;
- ASP.NET Core serves the generated assets and `index.html`;
- React Router owns `/requests` and `/requests/:requestId`; and
- Vite development mode proxies `/api` to ASP.NET Core for same-origin browser
  behavior.

The browser never calls MCP or the synthetic provisioner directly.

## Testing architecture

Unit tests reference Core and exercise deterministic domain and application rules.
Component tests use real SQLite, native MAF sessions, or the MCP transport without
starting the full application when that is the lowest faithful boundary. A small
`WebApplicationFactory` slice is retained for authentication, middleware, Activity
Protocol, logging, and one Teams-to-provisioning journey. Frontend tests exercise the
thin React session and workflow wiring.

No automated suite requires a live model or external production system. The
[Teams intake quickstart](../specs/002-teams-access-intake/quickstart.md) contains the
detailed evidence matrix and manual demonstration scenarios.

## Deliberate limitations

The architecture intentionally does not include:

- real production access or credentials;
- a real identity provider;
- mutable enterprise reference-data integration;
- automated revocation;
- multiple executable services;
- a public provisioning endpoint;
- a message broker, outbox, background reconciler, or distributed transaction;
- a generic workflow engine;
- a large retrieval subsystem; or
- independently deployed frontend infrastructure.

These are not missing layers required by the current MVP. A new requirement for real
credentials, independent ownership, mutable systems of record, automatic
reconciliation, separate scaling, or a separately versioned contract should trigger
a new ADR before changing the deployment or trust boundaries.

## Related documentation

- [Product baseline](governed-production-access-product-baseline.md)
- [Security and trust model](security-model.md)
- [Local development guide](local-development.md)
- [Testing strategy](testing-strategy.md)
- [Architecture decision index](adr/README.md)
- [Teams intake feature specification](../specs/002-teams-access-intake/spec.md)
- [Teams intake implementation plan](../specs/002-teams-access-intake/plan.md)
- [Teams intake data model](../specs/002-teams-access-intake/data-model.md)
- [Governed workflow data model](../specs/001-governed-production-access/data-model.md)
- [UI API contract](../specs/001-governed-production-access/contracts/ui-api.md)
- [MCP tool contract](../specs/001-governed-production-access/contracts/mcp-tools.json)
- [Teams intake quickstart](../specs/002-teams-access-intake/quickstart.md)
- [ADR 0001: Use One Deployable Service, Including the MCP Endpoint](adr/0001-use-one-deployable-service-including-mcp.md)

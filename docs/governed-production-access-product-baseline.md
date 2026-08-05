# Governed Production Access Request Assistant

## 1. Purpose

This document defines the product baseline for a portfolio-grade .NET application that governs temporary access to client-specific production environments.

The project demonstrates practical enterprise AI engineering: an LLM helps turn natural-language intent into a typed request draft and gathers request context through MCP, while authenticated humans and deterministic .NET services retain control over approval and provisioning.

The application is a focused vertical slice for one developer, not a complete identity-governance product.

The central principle is:

> AI interprets and gathers context. Humans approve. Deterministic services authorize and execute.

AI never becomes the authorization boundary.

## 2. Business Scenario

An enterprise operates separate production environments for different clients. Approval for one client environment must never authorize access to another.

A requester enters in a personal Microsoft Teams conversation:

> I need read-only access to Client Alpha production for four hours to investigate incident INC-1042.

The system:

1. asks the model to produce a typed but untrusted request candidate,
2. permits the model to call two approved read-only MCP tools for environment and incident data, with allowed roles included in authoritative environment context,
3. schema-validates the model output,
4. deterministically validates every identifier and business rule,
5. presents an immutable final request for explicit requester confirmation,
6. creates the request only after authenticated Teams confirmation,
7. resolves the required business approver from stored configuration,
8. records an explicit authenticated business decision in the web application,
9. records an explicit authenticated DevOps decision in the web application,
10. immediately invokes a protected internal provisioning handler after DevOps approval,
11. reloads and validates persisted request, approval, and operation evidence,
12. creates or returns an idempotent synthetic access grant that the model cannot request,
13. records a straightforward audit history.

## 3. Scope and Trust Principles

### 3.1 Model Output Is Untrusted

The model may interpret natural-language intent, call approved read-only MCP tools, and propose a structured draft. It must not:

- approve or reject requests,
- choose an approver,
- override a validation result,
- change workflow state,
- provision or revoke access,
- receive a provisioning operation definition or callable provisioning capability,
- claim that a server-side check passed without evidence.

Model output is schema-validated. All client, environment, role, incident, and identity-related values are checked deterministically against authoritative data. Access duration is not model- or user-selectable.

### 3.2 Human Decisions Are Explicit and Authenticated

- Business and DevOps decisions are structured actions in the web application.
- Free-text conversation cannot constitute approval.
- Acting identity comes from authenticated server context.
- Browser-submitted identity, claims, and roles are not trusted.
- The requester cannot choose the business approver.
- Approval records bind to an immutable request ID and exact approved scope.

### 3.3 Enforcement Is Deterministic

Deterministic services enforce:

- client-to-environment relationships,
- allowed roles and the fixed eight-hour grant lifetime,
- approver authority,
- approval order,
- exact role consistency,
- immutable request-scope consistency,
- pre-provisioning persisted-evidence validation,
- idempotent grant creation.

### 3.4 MCP Is Read-Only and Focused

The model is registered only with the two approved read-only MCP tools defined in this baseline:

* `get_production_environment`
* `get_incident`

The MCP endpoint exposes stored production data for request preparation. It does not expose workflow commands or state-changing capabilities.

MCP tool visibility, tool annotations, and possession of a tool schema must not be treated as authorization. Each tool validates its input, resolves stored records through application services, and returns typed results or typed failures using stable identifiers.

The MCP endpoint must not expose capabilities that:

* approve or reject requests,
* provision or revoke access,
* transition workflow state,
* access arbitrary database records,
* execute generic queries,
* bypass deterministic request validation.

The model receives the two tools through an explicit allowlist. Additional MCP tools are outside the MVP unless introduced by a later approved change. A separate role-listing tool is unnecessary because authoritative environment results include the roles assigned to each returned environment.

Because the MVP uses a co-hosted MCP endpoint and synthetic read-only data, local MCP authentication must remain proportionate to the portfolio scenario. The design must document how the endpoint is reached and isolated, but it must not introduce artificial service-to-service identity infrastructure solely to simulate a distributed production deployment.

### 3.5 Provisioning Is Outside Model Reach

Provisioning is a protected internal application capability. It is never exposed as:

* an MCP tool,
* a model function,
* a public provisioning API,
* a general-purpose browser action,
* a capability available during natural-language request processing.

The initial provisioning attempt is triggered automatically after a valid authenticated DevOps approval.

The provisioning handler receives only the stable server-generated reference required
to identify the operation:

* request ID, which is also used as the provider idempotency key.

It must not accept caller assertions such as:

```text
businessApproved = true
devOpsApproved = true
roleIsAllowed = true
```

Before creating an access grant, the handler reloads persisted workflow evidence and independently verifies:

* the request exists and is in the expected workflow state,
* both approvals reference the same immutable request,
* valid business and DevOps approvals exist in the correct order,
* the approved role matches the request,
* the provisioning operation matches the immutable request scope,
* the operation is keyed by the immutable request ID.

Client, environment, role, incident, and approver context is validated when the
request and human decisions are recorded. The fixed synthetic reference dataset has
no mutation surface and startup fails when persisted reference records conflict with
the expected dataset, so the provisioning handler does not repeat those lookups.

If provisioning succeeds, repeating the same logical operation for the same request ID
returns the existing access grant and does not create a duplicate.

If provisioning fails, the request enters `ProvisioningFailed`. An authenticated DevOps approver may invoke a narrowly scoped retry action from the request-detail page.

The retry action:

* is available only for a request in `ProvisioningFailed`,
* cannot modify the client, environment, role, or fixed eight-hour lifetime,
* reuses the request ID as the idempotency key for the same approved operation,
* invokes the same provisioning handler,
* repeats the persisted-evidence validation,
* records the retry and its outcome in the audit history.

This recovery action does not constitute a separate approval stage, provisioning role, or general browser-accessible provisioning capability.

## 4. Architecture

The MVP contains one executable ASP.NET Core host: the Governed Access Host.

```text
Microsoft Teams personal chat      Browser
  |                                  |
  | prepare + confirm                | list/detail/decide/retry/audit
  +----------------+-----------------+
                   v
+--------------------------------------------------+
| Governed Access Host                             |
|                                                  |
| React UI, same-origin endpoints, and auth         |
| LLM orchestration and MCP client                 |
|                                                  |
| /mcp - real read-only MCP endpoint               |
|   - get_production_environment                   |
|   - get_incident                                 |
|                                                  |
| Request validation, workflow, and approvals      |
| Protected internal provisioning handler          |
| SQLite persistence and audit history             |
+--------------------------------------------------+
```

The AI orchestration connects to the `/mcp` endpoint through a real MCP client and transport even though both are hosted in the same executable. The endpoint remains independently contract-testable and can be inspected by an external MCP client. Co-hosting reduces deployment and integration complexity; it does not bypass MCP tool schemas or tool-call behavior.

The host is a modular monolith. Logical capabilities remain separated so AI orchestration, domain rules, MCP adapters, persistence, and provisioning do not collapse into one component.

The MVP does not introduce a second process, service-to-service authentication, evidence callback APIs, service discovery, a message broker, an orchestrator, Kubernetes, separate deployment pipelines, or distributed configuration.

### 4.1 Governed Access Host Responsibilities

The single host is responsible for:

- thin React UI built and served by the host,
- same-origin typed UI query and action endpoints,
- authenticated synthetic user context,
- authenticated Teams-only natural-language request intake and confirmation,
- LLM orchestration,
- MCP client configuration and tool allowlisting,
- hosting the real read-only MCP endpoint,
- synthetic environment, role, and incident data,
- structured request draft handling,
- deterministic request validation,
- request workflow and versioning,
- business and DevOps approval actions,
- immediate internal provisioning after DevOps approval,
- independent persisted-evidence reload and validation inside the provisioning handler,
- idempotent synthetic grant creation,
- request, grant, and audit persistence and presentation.

### 4.2 Logical Modules

```text
Presentation and authentication
              |
              v
AI orchestration ---- MCP client ---- /mcp adapter
              |                           |
              |                           v
              |                  Enterprise context module
              v
Request application services
              |
              v
Domain workflow and authorization rules
              |
              v
Provisioning handler ---- persistence and synthetic provider
```

The precise project structure is a design decision, but the domain and application rules must not depend directly on React, browser-framework, AI-provider, or MCP SDK contracts.

### 4.3 Capability Boundary

```text
Model through MCP                  Authenticated application
-------------------------------    ---------------------------------
Read environment and role context  Record business decisions
Read incident context              Record DevOps decisions
Prepare a typed draft              Enforce workflow and version rules
                                   Invoke internal provisioning handler

Cannot approve                     Derives identity from server context
Cannot change workflow state       Reloads persisted workflow evidence
Cannot access provisioning         Applies deterministic rules
```

The model must not receive:

- a provisioning API or function definition,
- a callable provisioning capability,
- a provisioning MCP tool,
- an approval tool,
- any workflow-transition tool.

## 5. MCP Surface

The Governed Access Host exposes exactly two read-only MCP tools for the MVP.

### 5.1 `get_production_environment`

Accepts `{}` for bounded complete-catalog discovery or one nonblank `environmentId`
for exact lookup. Both success modes return the same ordered `environments` shape;
exact lookup contains one matching authoritative environment. Discovery returns an
empty successful array for an empty catalog and fails closed, without truncation,
when more than 20 environments exist. Each returned environment includes:

- environment ID,
- client ID,
- display name,
- business approver group identifier,
- the role identifiers and display names currently assigned to the environment.

Readable environment descriptions, display names, and model-selected candidates are
interpretation aids rather than authority. Readable context uses discovery directly.
An identifier-like value uses exact lookup first; only typed exact `NotFound` permits
a discovery fallback. Invalid input, timeout, cancellation, unavailability, malformed
results, and successful exact lookup do not permit fallback. One fallback alternative
requires explicit requester confirmation, several require selection, and none require
focused correction; the rejected value is never silently replaced.

For an environment clarification, the model returns a bounded conversational message
and a separate structured list of zero to 20 stable option IDs. The application
validates and reloads every ID before showing that message beside authoritative
client/environment labels and stable IDs. Invalid option sets suppress the associated
message and choices. Model prose is never parsed into selectable scope or authority.
Deterministic application services independently validate the selected environment,
its client relationship, and its requested role.

### 5.2 `get_incident`

Returns typed incident data using a stable incident identifier. The result includes:

- incident ID,
- status,
- client or environment relationship when applicable,
- summary suitable for request validation.

The requester must supply the precise stable incident identifier. Listing, title or
description search, partial-ID matching, reformatted-ID lookup, and inference are not
supported.

### 5.3 MCP Constraints

- Tool inputs and outputs use explicit schemas.
- Tool results use stable identifiers, not display names as authority.
- Unsupported or missing records return typed failures.
- Calls use cancellation and explicit timeouts.
- The model receives only these tools through an explicit allowlist.
- Only typed exact environment `NotFound` may enable discovery fallback.
- Environment discovery fails closed above 20 records and never returns a partial
  catalog.
- The MCP endpoint exposes no separate role-listing tool.
- No additional MCP tools are part of the MVP.

## 6. Small Domain Model

### 6.1 Clients and Environments

Each client has a primary production environment and a second recovery production
environment. Existing primary IDs remain stable; recovery IDs use the
`RECOVERY-PROD-` prefix so the tiers are lexically distinct.

#### Client Alpha

- Client ID: `client-alpha`
- Environment IDs: `PROD-ALPHA-EU`, `RECOVERY-PROD-ALPHA-EU`
- Business approver group: `ClientAlphaBusinessOwners`
- Primary roles: `ProductionReadOnly`, `ProductionSupport`, `ProductionDeployment`
- Recovery roles: `ProductionReadOnly`, `ProductionSupport`

#### Client Beta

- Client ID: `client-beta`
- Environment IDs: `PROD-BETA-UK`, `RECOVERY-PROD-BETA-UK`
- Business approver group: `ClientBetaBusinessOwners`
- Primary and recovery roles: `ProductionReadOnly`

#### Client Gamma

- Client ID: `client-gamma`
- Environment IDs: `PROD-GAMMA-US`, `RECOVERY-PROD-GAMMA-US`
- Business approver group: `ClientGammaBusinessOwners`
- Primary roles: `ProductionReadOnly`, `ProductionSupport`, `ProductionDeployment`
- Recovery roles: `ProductionReadOnly`, `ProductionSupport`

#### Client Theta

- Client ID: `client-theta`
- Environment IDs: `PROD-THETA-APAC`, `RECOVERY-PROD-THETA-APAC`
- Business approver group: `ClientThetaBusinessOwners`
- Primary and recovery roles: `ProductionReadOnly`

### 6.2 Roles

The MVP supports only:

- `ProductionReadOnly`
- `ProductionSupport`
- `ProductionDeployment`

`ProductionReadOnly` supports inspection, `ProductionSupport` supports diagnosis and
bounded remediation, and `ProductionDeployment` supports deployment and rollback.
Deployment access is assigned only to the Alpha and Gamma primary environments;
recovery releases must flow through the controlled delivery path.

There is no generalized role ordering or entitlement comparison.

Business approval binds the requested role. DevOps may approve that exact role or reject the request. A role change requires a new validated request with a new request ID and new approvals.

Every successful access grant lasts exactly eight hours. Requesters and approvers cannot select, reduce, or increase this server-owned lifetime.

### 6.3 Synthetic Principals

The MVP contains exactly six fixed principals:

- requester,
- Client Alpha business approver,
- Client Beta business approver,
- Client Gamma business approver,
- Client Theta business approver,
- DevOps approver.

A local identity switcher may authenticate one of these principals for demonstration. The server maps the selected principal to immutable server-side claims; browser-submitted roles are not trusted.

### 6.4 Access Request

An access request contains:

- request ID,
- requester ID,
- client ID,
- environment ID,
- requested role,
- justification,
- optional incident ID,
- workflow status,
- creation and last-modified timestamps,
- correlation ID.

### 6.5 Approval

An approval contains:

- request ID,
- stage: business or DevOps,
- decision: approved or rejected,
- authenticated approver ID,
- approved role,
- optional comment,
- decision timestamp.

An approval is evidence for one exact immutable request and approved scope only.

### 6.6 Access Grant

An access grant contains:

- grant ID,
- request ID,
- requester ID,
- environment ID,
- role,
- activation timestamp,
- expiry timestamp,
- provisioning outcome,
- correlation ID.

The UI may display the grant as logically expired after its expiry timestamp. Automated revocation is not part of the MVP.

### 6.7 Audit Event

An audit event contains:

- event ID,
- request ID,
- event type,
- authenticated actor ID when applicable,
- timestamp,
- correlation ID,
- structured details.

Audit events use a straightforward insert-only application model. The MVP does not require event sourcing, cryptographic integrity, database-level append-only enforcement, a generic audit framework, or a separate audit service.

At minimum, the system records:

- request creation,
- validation failure,
- business decision,
- DevOps decision,
- rejected authorization attempt,
- invalid-transition rejection,
- provisioning attempt,
- provisioning success,
- provisioning failure,
- duplicate provisioning retry.

## 7. Workflow

### 7.1 State Model

```text
AwaitingBusinessApproval
  |
  v
AwaitingDevOpsApproval
  |
  +----------> Rejected
  |
  +----------> ProvisioningFailed
  |
  v
Active
```

Business rejection also leads to `Rejected`.

Submitted requests are immutable. A requester corrects an error by preparing and
submitting a new request, which receives a new request ID and requires new approvals.
The original request and its evidence remain unchanged.

No specialized status is added unless deterministic behavior or UI clarity requires it.

### 7.2 Request Preparation

The model produces a typed draft such as:

```json
{
  "clientId": "client-alpha",
  "environmentId": "PROD-ALPHA-EU",
  "requestedRole": "ProductionReadOnly",
  "justification": "Investigate production incident",
  "incidentId": "INC-1042"
}
```

The model may call the two read-only MCP tools to obtain request context. The
environment tool supplies bounded authoritative candidates and their assigned roles,
while the incident tool accepts only a precise stable incident identifier. Each turn
produces either:

- a schema-valid complete-shape candidate,
- an incomplete candidate with one focused clarification message,
- an unsupported or malformed result.

Readable environment context uses discovery directly. Identifier-like environment
input uses exact lookup first and may fall back to discovery only after typed
`NotFound`. A fallback never silently changes the candidate: one authoritative
alternative requires confirmation, several require selection, and none require
correction. Every environment clarification carries the model's bounded question and
separate structured option IDs. The application reloads those IDs and appends stored
client/environment names and stable identifiers only after the complete option set
validates; prose-only or invalid choices never become candidate scope.

The assistant uses bounded process-local conversation history to interpret follow-up
messages such as "the first one" from its prior questions. The durable intake stores
the current typed candidate, not clarification options or a transcript. Conversation
history is a best-effort interpretation aid: it is isolated to the authenticated
personal conversation, is never authorization evidence, is not persisted or logged,
and is cleared when the intake becomes ready or terminal.

If process-local history is unavailable after restart, eviction, or expiry, the
assistant continues from the persisted candidate but must repeat any clarification
needed to understand a relative answer rather than guessing. Every resulting
identifier and relationship is checked deterministically before the immutable final
request is displayed.

### 7.3 Deterministic Validation

Before submission, the Governed Access Host validates:

- authenticated requester identity,
- client and environment existence,
- environment belongs to the selected client,
- requested role is currently allowed for the environment,
- supplied incident exists, is active, and is associated appropriately.

### 7.4 Business Decision

The Governed Access Host resolves the required approver from stored environment configuration. The requester cannot supply or select the approver.

Only the correct authenticated client approver may approve or reject. Approval binds the immutable request ID and exact role.

### 7.5 DevOps Decision and Immediate Provisioning

Only the authenticated DevOps approver may act after valid business approval.

DevOps may:

- approve the exact business-approved role for the fixed eight-hour grant lifetime,
- reject the request.

DevOps may not:

- change the role,
- submit or alter a duration,
- change the client or environment,

A successful DevOps approval immediately triggers:

1. request-state validation,
2. business approval validation,
3. DevOps approval validation,
4. approved-scope validation,
5. request-bound operation validation,
6. idempotent synthetic provisioning.

There is no separate human provisioning action or provisioning role.

If provisioning fails, the request enters `ProvisioningFailed`.

An authenticated DevOps approver may invoke a structured retry action from
the request-detail page. The retry:

- cannot modify the approved environment, role, or fixed eight-hour lifetime,
- reuses the request ID as the idempotency key for the same approved scope,
- invokes the same internal provisioning handler,
- reloads and validates persisted request, approval, and operation evidence,
- is recorded in the audit history.

Initial provisioning is not exposed as a separate browser action. The retry
action exists only for recovery from `ProvisioningFailed`.

## 8. User Interface

The React UI is a request register and governed action surface. It contains only:

- request list,
- request detail page.

It has no new-request page, request-draft form, request-submission action, or
request-creation capability. `POST /api/request-drafts/prepare` is unavailable and
`POST /api/requests` is not a request-creation method. Existing persisted requests
remain queryable, and request list/detail, business decision, DevOps decision,
provisioning retry, and audit behavior remain available.

The React application is built into static assets served by the Governed Access Host.
It uses same-origin typed query and action endpoints from that host and is not deployed
as a separate frontend service. Protected actions use server-established authentication
and antiforgery protection as appropriate. Browser-submitted identities, roles,
approver assignments, and authorization claims are never treated as authority.

The request list may use simple filtering or identity-specific sections to surface actionable requests. There are no separate business or DevOps inbox pages.

The request detail page shows:

- request data,
- current workflow status,
- validation results,
- business approval,
- DevOps approval,
- access-grant outcome and logical expiry,
- audit timeline.

Available structured actions depend on authenticated synthetic identity, authorization, and workflow state.

The MVP does not include audit administration or provisioning administration pages.

## 9. Functional Requirements

### FR-01 Typed Request Extraction

The Governed Access Host shall accept natural-language request intent only through an
authenticated personal Teams conversation, transform it into a typed request
candidate, and safely reject malformed model output. It shall use isolated bounded
process-local conversation history to interpret follow-up messages while retaining
the typed candidate durably. Missing history shall cause safe re-clarification rather
than inferred selection, and conversation history shall not be persisted, logged, or
used by confirmation or downstream workflow actions.

### FR-02 Restricted MCP Context

The model shall receive only the two approved read-only MCP tools. Each tool shall
return typed stored data with stable identifiers. The environment tool shall support
bounded complete discovery and exact lookup with one common result shape, include the
roles assigned to each returned environment, fail closed above 20 candidates, and
permit exact-to-discovery fallback only after typed `NotFound`. Incident lookup shall
require a precise stable incident identifier and expose no discovery or inference.

### FR-03 Trusted Server Validation

The Governed Access Host shall deterministically validate every model-proposed identifier and business value before submission.

### FR-04 Authenticated Acting Identity

The system shall derive the acting principal and authority from authenticated server context rather than browser-submitted roles or identity fields.

### FR-05 Business Approver Resolution

The system shall resolve the required business approver from the target environment and reject a wrong-client approver.

### FR-06 Request-Bound Two-Stage Approval

Business and DevOps approval shall be explicit authenticated decisions bound to the same immutable request ID and approved scope.

### FR-07 Exact Role Enforcement

DevOps shall approve the exact business-approved role or reject. A role change shall require a new validated request and new approvals.

### FR-08 Fixed Grant Lifetime

Every successful grant shall expire exactly eight hours after activation. Requesters and approvers shall not provide or alter duration.

### FR-09 Immutable Submitted Requests

A submitted request shall be immutable. Any correction shall create a new request ID and require new approvals while preserving the original evidence.

### FR-10 Immediate Provisioning

A successful DevOps approval shall immediately initiate deterministic persisted-evidence validation and protected provisioning without another human action.

### FR-11 Independent Provisioning Validation

The internal provisioning handler shall accept request references rather than approval assertions. It shall reload and independently verify persisted request, approval, operation, workflow-state, and immutable-scope evidence before creating a grant. It shall not repeat authoritative reference-data lookups while the synthetic dataset remains fixed and fail-fast validated at startup.

### FR-12 Idempotent Provisioning

Repeating the same logical provisioning operation with the same request ID as the
provider idempotency key shall return the existing result without creating another grant.

### FR-13 Insert-Only Audit History

The Governed Access Host shall record the minimum audit events defined in this baseline and display them on the request detail page.

### FR-14 Minimal UI

The Governed Access Host shall provide only the request list and request detail page
with identity- and state-appropriate review, decision, retry, and audit actions. The
browser shall expose no request-creation route, form, endpoint, or capability.

### FR-15 Logical Expiry

The system shall store activation and expiry timestamps and may display a grant as logically expired when the current time is later than its expiry timestamp.

## 10. Quality Requirements

### 10.1 Security

- LLM output must never authorize an action.
- The model receives only explicitly allowed read-only MCP tools.
- MCP tool visibility must not be treated as authorization.
- Browser-submitted identity or roles must not be trusted.
- Approval actions must authenticate and authorize the acting human.
- The provisioning handler must reload stored evidence rather than trust upstream approval assertions.
- Provisioning operation definitions must remain unavailable to the model.
- Secrets, raw prompts, and complete sensitive tool responses must not be logged by default.
- Synthetic authentication assumptions must be documented separately from production identity requirements.

### 10.2 Reliability

- LLM and MCP calls use explicit timeouts and propagate cancellation.
- The provisioning handler propagates cancellation to persistence and the synthetic provider.
- Expected validation, authorization, stale-state, timeout, and provisioning failures use typed outcomes.
- A provisioning timeout or lost response can be retried with the same request ID.
- Partial failure must not be reported as success.

### 10.3 Auditability

- Audit events use an insert-only application model.
- Events identify the request ID, actor when applicable, timestamp, and correlation ID.
- Rejected actions are evidence and must not mutate the protected workflow state.
- Audit storage is intentionally simple and is not an event-sourced system.

### 10.4 Observability

The core MVP requires:

- correlation IDs across the web request, model call, MCP call, and provisioning operation,
- structured logs,
- model call duration and outcome,
- MCP tool name, duration, and outcome,
- authorization decisions,
- workflow transitions,
- provisioning attempts and results.

Where practical, correlation should connect the web request, LLM call, MCP call, and internal provisioning attempt.

OpenTelemetry instrumentation is desirable final polish, but full distributed tracing must not block MVP completion. The MVP does not require an external collector, observability stack, dashboard, custom exporter, complete database instrumentation, or production telemetry retention.

### 10.5 Maintainability

- Domain and application logic do not depend directly on AI-provider or MCP SDK contracts.
- MCP and web endpoints are adapters over focused application services.
- Contracts between logical modules and external adapters remain explicit and small.
- Nullable reference types are enabled and build warnings are treated as errors.
- Infrastructure abstractions must be justified by a current MVP requirement.

## 11. Testability Expectations

Automated tests must not require a live model. A deterministic fake chat client supplies repeatable valid and malformed structured outputs.

Required tests cover:

- valid typed request extraction,
- malformed model output,
- the exact two-tool MCP catalog,
- bounded environment discovery and exact environment lookup with embedded roles,
- exact-only incident lookup,
- exact `NotFound` fallback and rejection of fallback after every other outcome,
- structured environment-option validation and authoritative clarification rendering,
- environment and client mismatch,
- unsupported role,
- crafted requester or approver duration input is rejected or safely ignored,
- inactive incident,
- correct business approver,
- wrong-client business approver,
- approval bound to the immutable request ID and scope,
- duplicate-stage and invalid-transition rejection,
- DevOps role-change rejection,
- DevOps duration-field rejection or safe ignoring,
- missing business approval,
- unauthorized user attempting to trigger DevOps approval and provisioning,
- pre-provisioning persisted-evidence validation,
- duplicate provisioning idempotency,
- MCP failure or timeout.

Tests should emphasize domain rules, authorization boundaries, host integration, MCP contracts, and provisioning idempotency. Exhaustive UI testing and enterprise-scale load testing are not required.

## 12. Primary Demonstration Scenarios

### 12.1 Successful Request

1. The requester asks in a personal Teams conversation for four hours of read-only
   access to Client Alpha production in Europe for exact incident `INC-1042`, without
   supplying the environment's stable identifier.
2. The model uses bounded environment discovery and exact incident lookup as needed,
   proposes `PROD-ALPHA-EU` with its derived client and assigned role, and produces a
   typed draft.
3. Deterministic validation succeeds.
4. The requester confirms the immutable final request in Teams.
5. The request appears in the web request register.
6. The Client Alpha business approver approves the immutable request scope.
7. DevOps approves the exact role; the system applies the fixed eight-hour lifetime.
8. Approval immediately triggers independent persisted-evidence validation and idempotent provisioning.
9. The request becomes `Active` and the detail page shows the grant and audit history.

### 12.2 Wrong Business Approver

The Client Beta business approver attempts to approve the Client Alpha request. The Governed Access Host rejects and audits the attempt without changing workflow state.

### 12.3 Correction Creates a New Request

A requester discovers an error after submission. The original request remains
unchanged, and the requester prepares and confirms a corrected request in Teams with a
new request ID. The corrected request requires both approvals and the original request
retains its evidence.

### 12.4 Duplicate Provisioning Retry

Provisioning succeeds but its response is treated as lost. A controlled retry uses the
same request ID as the provider idempotency key and returns the existing grant without
creating a duplicate.

The following remain automated negative-path tests rather than primary presentation scenarios:

- DevOps role-change attempt,
- DevOps duration-field attempt,
- MCP timeout,
- malformed model output,
- invalid environment,
- inactive incident,
- unauthorized user attempting to trigger DevOps approval and provisioning,
- duplicate decision after the request has left the required workflow state.

## 13. MVP Deliverables

The MVP includes:

- one executable modular ASP.NET Core host,
- one thin React request register with list and detail pages, built and served by the
  ASP.NET Core host,
- one authenticated personal-Teams request preparation and confirmation path as the
  sole request-creation channel,
- authenticated synthetic user context with four principals,
- one typed LLM extraction flow,
- one real read-only MCP endpoint,
- exactly two MCP context tools,
- two clients and two environments,
- two access roles without role hierarchy,
- two explicit authenticated approval stages,
- immutable request binding and correction through a new request,
- immediate internal provisioning after DevOps approval,
- independent persisted-evidence reload and validation within the provisioning handler,
- idempotent synthetic grant creation,
- activation and expiry timestamps,
- straightforward insert-only audit events,
- correlation IDs and structured operational logs,
- deterministic unit and integration tests using a fake chat client,
- proportionate local persistence.

## 14. Explicit Non-Goals

The MVP will not include:

- real Entra ID or production provisioning,
- real corporate identity or incident integration,
- a second executable service or host,
- a separate provisioning HTTP API,
- service-to-service authentication or evidence callback endpoints,
- additional MCP tools or MCP servers,
- provisioning, approval, revocation, workflow, database, or generic-query MCP tools,
- role privilege ordering or generalized entitlement comparison,
- current-access conflict analysis,
- separation-of-duties rules,
- administrator, delegated approver, emergency-access, or permanent-access roles,
- multiple DevOps groups,
- approval expiration or configuration versioning,
- a separate provisioning action or provisioning role,
- automated revocation, a background worker, or periodic cleanup,
- notification workflows,
- separate approval inbox pages,
- audit or provisioning administration pages,
- event sourcing or cryptographic audit integrity,
- a generic audit, policy, or workflow framework,
- full distributed tracing as an MVP prerequisite,
- an external telemetry collector, dashboards, or production telemetry retention,
- exhaustive UI tests or enterprise-scale load tests,
- service discovery, message brokers, Kubernetes, or microservice orchestration,
- separate deployment pipelines or complex distributed configuration,
- autonomous agents or multi-agent orchestration.

## 15. Deferred Extensions

Only after the core MVP is complete, a later change may add one clearly justified extension:

- extract the enterprise context and provisioning module into a second host if an independent deployment or ownership boundary becomes a real requirement,
- automated revocation of expired grants,
- OpenTelemetry export and end-to-end distributed trace visualization,
- one real sandbox integration replacing synthetic context or provisioning,
- stronger audit integrity if a concrete threat model justifies it.

Deferred extensions must not delay or complicate the core demonstration scenarios.

## 16. Resolved Design Decisions

The delivered baseline uses:

1. Microsoft Agent Framework with a closed structured-output schema and one bounded
   process-local session per intake;
2. Model Context Protocol streamable HTTP at the co-hosted `/mcp` endpoint, reached
   through a real MCP client and exact two-tool allowlist;
3. server-established synthetic principals and claims, never browser-supplied roles;
4. separate Core, MCP-adapter, Web-host, React, and test projects inside one modular
   host boundary;
5. authenticated Teams confirmation as the sole request-creation path;
6. transactional DevOps decision persistence followed by a protected, recoverable
   provisioning operation;
7. a request-ID-only provisioning entry point that reloads persisted evidence;
8. the immutable request ID as the provider idempotency key; and
9. structured logs and correlation without requiring an external OpenTelemetry
   collector for the MVP.

## 17. Success Criteria

The project is successful when it proves that:

- natural-language input becomes a schema-valid but untrusted typed draft,
- the model obtains request context through one real MCP server,
- the model can access only the two approved read-only tools,
- readable environment context resolves through bounded discovery while exact lookup
  and typed-`NotFound`-only fallback remain available,
- incident descriptions and partial IDs are never inferred into incident scope,
- clarification prose cannot create selectable scope and every structured option is
  reloaded authoritatively,
- every model-proposed identifier is deterministically validated,
- only authenticated Teams confirmation can create an access request,
- browser draft and request-creation endpoints, routes, forms, and capabilities are absent,
- authenticated server context determines the acting identity,
- the requester cannot choose the approver,
- a wrong-client approver is rejected and audited,
- approvals bind to an exact immutable request ID and scope,
- correcting a submitted request creates a new request and requires new approvals,
- DevOps cannot change the approved role or the fixed eight-hour lifetime,
- successful DevOps approval immediately triggers deterministic persisted-evidence validation and provisioning,
- the provisioning handler reloads and independently verifies persisted workflow evidence,
- provisioning remains unavailable to the model,
- retrying the same provisioning operation does not create a duplicate grant,
- the request detail page presents the essential workflow and audit evidence,
- required tests run without a live model,
- the modular monolith remains small enough for one developer to implement and explain coherently.

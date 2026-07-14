# Governed Production Access Request Assistant

## 1. Purpose

This document defines the product baseline for a portfolio-grade .NET application that governs temporary access to client-specific production environments.

The project demonstrates practical enterprise AI engineering: an LLM helps turn natural-language intent into a typed request draft and gathers authoritative context through MCP, while authenticated humans and deterministic .NET services retain control over approval and provisioning.

The application is a focused vertical slice for one developer, not a complete identity-governance product.

The central principle is:

> AI interprets and gathers context. Humans approve. Deterministic services authorize and execute.

AI never becomes the authorization boundary.

## 2. Business Scenario

An enterprise operates separate production environments for different clients. Approval for one client environment must never authorize access to another.

A requester enters:

> I need read-only access to Client Alpha production for four hours to investigate incident INC-1042.

The system:

1. asks the model to produce a typed but untrusted request draft,
2. permits the model to call three approved read-only MCP tools for authoritative environment, role, and incident context,
3. schema-validates the model output,
4. deterministically validates every identifier and business rule,
5. resolves the required business approver from authoritative configuration,
6. records an explicit authenticated business decision,
7. records an explicit authenticated DevOps decision,
8. immediately invokes a protected internal provisioning handler after DevOps approval,
9. reloads and revalidates authoritative request and approval state,
10. creates or returns an idempotent synthetic access grant that the model cannot request,
11. records a straightforward audit history.

## 3. Scope and Trust Principles

### 3.1 Model Output Is Untrusted

The model may interpret natural-language intent, call approved read-only MCP tools, and propose a structured draft. It must not:

- approve or reject requests,
- choose an approver,
- override a validation result,
- change workflow state,
- provision or revoke access,
- receive a provisioning operation definition or callable provisioning capability,
- claim that an authoritative check passed without evidence.

Model output is schema-validated. All client, environment, role, incident, duration, and identity-related values are checked deterministically against authoritative data.

### 3.2 Human Decisions Are Explicit and Authenticated

- Business and DevOps decisions are structured actions in the web application.
- Free-text conversation cannot constitute approval.
- Acting identity comes from authenticated server context.
- Browser-submitted identity, claims, and roles are not trusted.
- The requester cannot choose the business approver.
- Approval records bind to a request ID and exact request version.

### 3.3 Enforcement Is Deterministic

Deterministic services enforce:

- client-to-environment relationships,
- allowed roles and duration limits,
- approver authority,
- approval order,
- exact role consistency,
- request-version consistency,
- pre-provisioning revalidation,
- idempotent grant creation.

### 3.4 MCP Is Read-Only and Focused

The model is registered only with the three approved read-only MCP tools defined in this baseline:

* `get_production_environment`
* `get_incident`
* `get_available_roles`

The MCP endpoint exposes authoritative enterprise context for request preparation. It does not expose workflow commands or state-changing capabilities.

MCP tool visibility, tool annotations, and possession of a tool schema must not be treated as authorization. Each tool validates its input, resolves records through authoritative application services, and returns typed results or typed failures using stable identifiers.

The MCP endpoint must not expose capabilities that:

* approve or reject requests,
* provision or revoke access,
* transition workflow state,
* access arbitrary database records,
* execute generic queries,
* bypass deterministic request validation.

The model receives the three tools through an explicit allowlist. Additional MCP tools are outside the MVP unless introduced by a later approved change.

Because the MVP uses a co-hosted MCP endpoint and synthetic read-only data, local MCP authentication must remain proportionate to the portfolio scenario. The design must document how the endpoint is reached and isolated, but it must not introduce artificial service-to-service identity infrastructure solely to simulate a distributed production deployment.

### 3.5 Provisioning Is Outside Model Reach

Provisioning is a protected internal application capability. It is never exposed as:

* an MCP tool,
* a model function,
* a public provisioning API,
* a general-purpose browser action,
* a capability available during natural-language request processing.

The initial provisioning attempt is triggered automatically after a valid authenticated DevOps approval.

The provisioning handler receives only authoritative references required to identify the operation, such as:

* request ID,
* request version,
* idempotency key.

It must not accept caller assertions such as:

```text
businessApproved = true
devOpsApproved = true
roleIsAllowed = true
```

Before creating an access grant, the handler reloads authoritative state and independently verifies:

* the request exists and is in the expected workflow state,
* the request version matches both approvals,
* valid business and DevOps approvals exist in the correct order,
* the approved role matches the request,
* the approved duration does not exceed the business-approved duration,
* the environment and role remain valid,
* the associated incident remains valid when required,
* the acting workflow transition was authorized,
* the idempotency key is consistent with the request version and approved scope.

If provisioning succeeds, repeating the same logical operation with the same idempotency key returns the existing access grant and does not create a duplicate.

If provisioning fails, the request enters `ProvisioningFailed`. An authenticated DevOps approver may invoke a narrowly scoped retry action from the request-detail page.

The retry action:

* is available only for a request in `ProvisioningFailed`,
* cannot modify the client, environment, role, duration, or request version,
* reuses the idempotency key for the same approved operation,
* invokes the same provisioning handler,
* repeats the complete authoritative revalidation,
* records the retry and its outcome in the audit history.

This recovery action does not constitute a separate approval stage, provisioning role, or general browser-accessible provisioning capability.

## 4. Architecture

The MVP contains one executable ASP.NET Core host: the Governed Access Host.

```text
Browser
  |
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
|   - get_available_roles                          |
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
- natural-language request intake,
- LLM orchestration,
- MCP client configuration and tool allowlisting,
- hosting the real read-only MCP endpoint,
- authoritative synthetic environment, role, and incident context,
- structured request draft handling,
- deterministic request validation,
- request workflow and versioning,
- business and DevOps approval actions,
- immediate internal provisioning after DevOps approval,
- independent reload and revalidation inside the provisioning handler,
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
Read environment context           Record business decisions
Read available roles               Record DevOps decisions
Read incident context              Enforce workflow and version rules
Prepare a typed draft              Invoke internal provisioning handler

Cannot approve                     Derives identity from server context
Cannot change workflow state       Reloads authoritative state
Cannot access provisioning         Applies deterministic rules
```

The model must not receive:

- a provisioning API or function definition,
- a callable provisioning capability,
- a provisioning MCP tool,
- an approval tool,
- any workflow-transition tool.

## 5. MCP Surface

The Governed Access Host exposes exactly three read-only MCP tools for the MVP.

### 5.1 `get_production_environment`

Returns typed authoritative environment data using a stable environment identifier. The result includes:

- environment ID,
- client ID,
- display name,
- active state,
- maximum access duration,
- business approver group identifier.

### 5.2 `get_incident`

Returns typed authoritative incident data using a stable incident identifier. The result includes:

- incident ID,
- status,
- client or environment relationship when applicable,
- summary suitable for request validation.

### 5.3 `get_available_roles`

Returns the typed role identifiers currently allowed for a stable environment identifier.

The tool returns role availability only. It does not define or compare a generalized privilege hierarchy.

### 5.4 MCP Constraints

- Tool inputs and outputs use explicit schemas.
- Tool results use stable identifiers, not display names as authority.
- Unsupported or missing records return typed failures.
- Calls use cancellation and explicit timeouts.
- The model receives only these tools through an explicit allowlist.
- No additional MCP tools are part of the MVP.

## 6. Small Domain Model

### 6.1 Clients and Environments

#### Client Alpha

- Client ID: `client-alpha`
- Environment ID: `PROD-ALPHA-EU`
- Business approver group: `ClientAlphaBusinessOwners`
- Allowed roles: `ProductionReadOnly`, `ProductionSupport`
- Maximum duration: 8 hours

#### Client Beta

- Client ID: `client-beta`
- Environment ID: `PROD-BETA-UK`
- Business approver group: `ClientBetaBusinessOwners`
- Allowed roles: `ProductionReadOnly`
- Maximum duration: 4 hours

### 6.2 Roles

The MVP supports only:

- `ProductionReadOnly`
- `ProductionSupport`

There is no generalized role ordering or entitlement comparison.

Business approval binds the requested role. DevOps may approve that exact role or reject the request. A role change requires a material request edit, which increments the request version and invalidates previous approvals.

DevOps may reduce the approved duration. DevOps may not increase duration.

### 6.3 Synthetic Principals

The MVP contains exactly four fixed principals:

- requester,
- Client Alpha business approver,
- Client Beta business approver,
- DevOps approver.

A local identity switcher may authenticate one of these principals for demonstration. The server maps the selected principal to immutable server-side claims; the browser does not submit authoritative roles.

### 6.4 Access Request

An access request contains:

- request ID,
- requester ID,
- client ID,
- environment ID,
- requested role,
- requested duration,
- justification,
- optional incident ID,
- request version,
- workflow status,
- creation and last-modified timestamps,
- correlation ID.

### 6.5 Approval

An approval contains:

- request ID,
- request version,
- stage: business or DevOps,
- decision: approved or rejected,
- authenticated approver ID,
- approved role,
- approved duration,
- optional comment,
- decision timestamp.

An approval is evidence for one exact request version only.

### 6.6 Access Grant

An access grant contains:

- grant ID,
- request ID and version,
- requester ID,
- environment ID,
- role,
- activation timestamp,
- expiry timestamp,
- idempotency key,
- provisioning outcome,
- correlation ID.

The UI may display the grant as logically expired after its expiry timestamp. Automated revocation is not part of the MVP.

### 6.7 Audit Event

An audit event contains:

- event ID,
- request ID,
- request version,
- event type,
- authenticated actor ID when applicable,
- timestamp,
- correlation ID,
- structured details.

Audit events use a straightforward insert-only application model. The MVP does not require event sourcing, cryptographic integrity, database-level append-only enforcement, a generic audit framework, or a separate audit service.

At minimum, the system records:

- request creation,
- material request edit,
- validation failure,
- business decision,
- DevOps decision,
- rejected authorization attempt,
- stale-version rejection,
- provisioning attempt,
- provisioning success,
- provisioning failure,
- duplicate provisioning retry.

## 7. Workflow

### 7.1 State Model

```text
Draft
  |
  v
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

A material edit:

- increments the request version,
- invalidates approvals for the previous version,
- returns the request to `Draft`.

No specialized status is added unless deterministic behavior or UI clarity requires it.

### 7.2 Request Preparation

The model produces a typed draft such as:

```json
{
  "clientId": "client-alpha",
  "environmentId": "PROD-ALPHA-EU",
  "requestedRole": "ProductionReadOnly",
  "durationHours": 4,
  "justification": "Investigate production incident",
  "incidentId": "INC-1042"
}
```

The model may call the three read-only MCP tools to obtain authoritative context. The result is either:

- a schema-valid draft,
- an incomplete draft with nullable required values and correction guidance,
- an unsupported or malformed result.

Missing or invalid fields are corrected through the structured new-request page. A multi-turn clarification engine is not required.

### 7.3 Deterministic Validation

Before submission, the Governed Access Host validates:

- authenticated requester identity,
- client and environment existence,
- environment belongs to the selected client,
- requested role is currently allowed for the environment,
- duration is positive and within the environment limit,
- supplied incident exists, is active, and is associated appropriately.

### 7.4 Business Decision

The Governed Access Host resolves the required approver from authoritative environment configuration. The requester cannot supply or select the approver.

Only the correct authenticated client approver may approve or reject. Approval binds the role and maximum duration for the current request version.

### 7.5 DevOps Decision and Immediate Provisioning

Only the authenticated DevOps approver may act after valid business approval.

DevOps may:

- approve the exact business-approved role and duration,
- approve the exact role with a shorter duration,
- reject the request.

DevOps may not:

- change the role,
- increase duration,
- change the client or environment,
- approve a different request version.

A successful DevOps approval immediately triggers:

1. request-state validation,
2. request-version validation,
3. business approval validation,
4. DevOps approval validation,
5. approved-scope validation,
6. current environment and role validation,
7. incident-state validation when an incident is present,
8. idempotent synthetic provisioning.

There is no separate human provisioning action or provisioning role.

If provisioning fails, the request enters `ProvisioningFailed`.

An authenticated DevOps approver may invoke a structured retry action from
the request-detail page. The retry:

- cannot modify the approved environment, role, or duration,
- reuses the idempotency key for the same request version and approved scope,
- invokes the same internal provisioning handler,
- reloads and revalidates authoritative state,
- is recorded in the audit history.

Initial provisioning is not exposed as a separate browser action. The retry
action exists only for recovery from `ProvisioningFailed`.

## 8. User Interface

The React UI contains only:

- request list,
- new-request page,
- request detail page.

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

Available structured actions depend on authenticated synthetic identity, authorization, workflow state, and request version.

The MVP does not include audit administration or provisioning administration pages.

## 9. Functional Requirements

### FR-01 Typed Request Extraction

The Governed Access Host shall transform natural-language input into a typed request draft and safely reject malformed model output.

### FR-02 Restricted MCP Context

The model shall receive only the three approved read-only MCP tools. Each tool shall return typed authoritative data with stable identifiers.

### FR-03 Authoritative Validation

The Governed Access Host shall deterministically validate every model-proposed identifier and business value before submission.

### FR-04 Authenticated Acting Identity

The system shall derive the acting principal and authority from authenticated server context rather than browser-submitted roles or identity fields.

### FR-05 Business Approver Resolution

The system shall resolve the required business approver from the target environment and reject a wrong-client approver.

### FR-06 Version-Bound Two-Stage Approval

Business and DevOps approval shall be explicit authenticated decisions bound to the same request ID and request version.

### FR-07 Exact Role Enforcement

DevOps shall approve the exact business-approved role or reject. A role change shall require a material edit and new approvals.

### FR-08 Duration Enforcement

DevOps may preserve or reduce the business-approved duration but shall not increase it.

### FR-09 Material Edit Protection

A material edit shall increment the request version, invalidate prior approvals, and return the request to `Draft`.

### FR-10 Immediate Provisioning

A successful DevOps approval shall immediately initiate deterministic revalidation and protected provisioning without another human action.

### FR-11 Independent Provisioning Validation

The internal provisioning handler shall accept request references rather than approval assertions. It shall reload and independently verify authoritative request, approval, version, scope, environment, role, and incident state before creating a grant.

### FR-12 Idempotent Provisioning

Repeating the same logical provisioning operation with the same idempotency key shall return the existing result without creating another grant.

### FR-13 Insert-Only Audit History

The Governed Access Host shall record the minimum audit events defined in this baseline and display them on the request detail page.

### FR-14 Minimal UI

The Governed Access Host shall provide only the request list, new-request page, and request detail page with identity- and state-appropriate actions.

### FR-15 Logical Expiry

The system shall store activation and expiry timestamps and may display a grant as logically expired when the current time is later than its expiry timestamp.

## 10. Quality Requirements

### 10.1 Security

- LLM output must never authorize an action.
- The model receives only explicitly allowed read-only MCP tools.
- MCP tool visibility must not be treated as authorization.
- Browser-submitted identity or roles must not be trusted.
- Approval actions must authenticate and authorize the acting human.
- The provisioning handler must reload authoritative evidence rather than trust upstream approval assertions.
- Provisioning operation definitions must remain unavailable to the model.
- Secrets, raw prompts, and complete sensitive tool responses must not be logged by default.
- Synthetic authentication assumptions must be documented separately from production identity requirements.

### 10.2 Reliability

- LLM and MCP calls use explicit timeouts and propagate cancellation.
- The provisioning handler propagates cancellation to persistence and the synthetic provider.
- Expected validation, authorization, stale-state, timeout, and provisioning failures use typed outcomes.
- A provisioning timeout or lost response can be retried with the same idempotency key.
- Partial failure must not be reported as success.

### 10.3 Auditability

- Audit events use an insert-only application model.
- Events identify the request version, actor when applicable, timestamp, and correlation ID.
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
- environment and client mismatch,
- unsupported role,
- duration above the environment limit,
- inactive incident,
- correct business approver,
- wrong-client business approver,
- approval bound to request version,
- stale approval rejection,
- DevOps role-change rejection,
- DevOps duration-increase rejection,
- missing business approval,
- unauthorized user attempting to trigger DevOps approval and provisioning,
- pre-provisioning revalidation,
- duplicate provisioning idempotency,
- MCP failure or timeout.

Tests should emphasize domain rules, authorization boundaries, host integration, MCP contracts, and provisioning idempotency. Exhaustive UI testing and enterprise-scale load testing are not required.

## 12. Primary Demonstration Scenarios

### 12.1 Successful Request

1. The requester asks for four hours of `ProductionReadOnly` access to Client Alpha for `INC-1042`.
2. The model uses the three read-only MCP tools as needed and produces a typed draft.
3. Deterministic validation succeeds.
4. The Client Alpha business approver approves the current version.
5. DevOps approves the exact role and duration.
6. Approval immediately triggers independent revalidation and idempotent provisioning.
7. The request becomes `Active` and the detail page shows the grant and audit history.

### 12.2 Wrong Business Approver

The Client Beta business approver attempts to approve the Client Alpha request. The Governed Access Host rejects and audits the attempt without changing workflow state.

### 12.3 Stale Approval After Material Edit

A material request edit increments the version, invalidates the earlier business approval, and returns the request to `Draft`. An attempt to act using the earlier version is rejected and audited.

### 12.4 Duplicate Provisioning Retry

Provisioning succeeds but its response is treated as lost. A controlled retry uses the same idempotency key and returns the existing grant without creating a duplicate.

The following remain automated negative-path tests rather than primary presentation scenarios:

- DevOps role-change attempt,
- DevOps duration-increase attempt,
- MCP timeout,
- malformed model output,
- invalid environment,
- inactive incident,
- unauthorized user attempting to trigger DevOps approval and provisioning,
- mismatched request version.

## 13. MVP Deliverables

The MVP includes:

- one executable modular ASP.NET Core host,
- one thin React UI with three pages, built and served by the ASP.NET Core host,
- authenticated synthetic user context with four principals,
- one typed LLM extraction flow,
- one real read-only MCP endpoint,
- exactly three MCP context tools,
- two clients and two environments,
- two access roles without role hierarchy,
- two explicit authenticated approval stages,
- request-version binding and stale-approval protection,
- immediate internal provisioning after DevOps approval,
- independent authoritative reload and validation within the provisioning handler,
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

## 16. Open Design Questions

The later proposal and design phases should resolve:

1. Which .NET AI abstraction and structured-output mechanism provide the simplest reliable extraction flow.
2. Which .NET MCP SDK and transport to use for the co-hosted but real `/mcp` endpoint.
3. How the synthetic authenticated principal is established and propagated without trusting browser claims.
4. Which logical modules and .NET projects preserve clear boundaries without over-structuring the modular monolith.
5. Whether the MCP client uses the host's HTTP endpoint or another supported transport while preserving real MCP contracts and integration tests.
6. How DevOps approval persistence and the immediate provisioning attempt are separated so failure produces a recoverable `ProvisioningFailed` state.
7. How the provisioning handler reloads evidence and prevents upstream code from bypassing required checks.
8. How idempotency keys are derived and bound to request version and approved scope.
9. Which request fields count as material edits.
10. Whether OpenTelemetry can be added as final polish without introducing an external collector.

## 17. Success Criteria

The project is successful when it proves that:

- natural-language input becomes a schema-valid but untrusted typed draft,
- the model obtains authoritative context through one real MCP server,
- the model can access only the three approved read-only tools,
- every model-proposed identifier is deterministically validated,
- authenticated server context determines the acting identity,
- the requester cannot choose the approver,
- a wrong-client approver is rejected and audited,
- approvals bind to an exact request ID and version,
- a material edit invalidates prior approvals,
- DevOps cannot change the approved role or increase duration,
- successful DevOps approval immediately triggers deterministic revalidation and provisioning,
- the provisioning handler reloads and independently verifies authoritative evidence,
- provisioning remains unavailable to the model,
- retrying the same provisioning operation does not create a duplicate grant,
- the request detail page presents the essential workflow and audit evidence,
- required tests run without a live model,
- the modular monolith remains small enough for one developer to implement and explain coherently.

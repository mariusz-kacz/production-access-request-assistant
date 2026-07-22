# ADR 0001: Use One Deployable Service, Including the MCP Endpoint

- **Status**: Accepted
- **Date**: 2026-07-12
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/governed-production-access-product-baseline.md`, `specs/001-governed-production-access/plan.md`

## Context

The governed production access application needs to provide:

- a thin React user interface;
- deterministic request validation and workflow rules;
- authenticated business and DevOps decisions;
- local request context and persistence;
- model-assisted request drafting;
- one real MCP endpoint containing exactly three read-only context tools; and
- idempotent synthetic provisioning.

These capabilities could be split into separately deployed web, MCP, workflow, and
provisioning services. The current product, however, is a portfolio-grade local
reference implementation maintained by one developer. It has two clients, two
environments, four fixed demonstration principals, synthetic data, no real production
access, and no independent scaling or ownership requirements.

Splitting the application at this stage would introduce service-to-service identity,
network failure handling, deployment coordination, contract versioning, distributed
observability, and cross-service consistency concerns that do not demonstrate an
approved product requirement. In particular, a separate MCP process would add an
operational boundary without making MCP an authorization boundary.

## Decision

Implement all MVP capabilities in one deployable ASP.NET Core service and one process.
The service will host:

- the three-page React UI and its same-origin action/query endpoints;
- application and domain workflows;
- local authentication and authorization adapters;
- EF Core/SQLite persistence;
- the model integration adapter;
- the real Streamable HTTP MCP endpoint at `/mcp`; and
- the synthetic provisioning adapter.

The MCP client used during model-assisted drafting will call the co-hosted `/mcp`
endpoint through its real HTTP transport. Co-location must not be replaced with direct
SDK or repository calls in the model integration path merely to bypass the MCP
contract.

One deployable service does not mean one undifferentiated code module. The solution
will retain these boundaries:

- `GovernedAccess.Core` contains provider-neutral domain rules, application services,
  ports, and typed outcomes.
- `GovernedAccess.Web` is the sole executable and contains the React static-asset host,
  same-origin UI endpoints, authentication, persistence, AI-provider, MCP, and
  provisioning adapters.
- AI-provider and MCP SDK contracts are translated at the Web/Core boundary.
- Protected actions derive the actor from authenticated server context and are
  authorized by deterministic application services.
- The provisioning handler reloads persisted request, approval, and operation state rather
  than trusting its caller.

The model-visible MCP surface is an explicit allowlist containing exactly:

- `get_production_environment`;
- `get_incident`; and
- `get_available_roles`.

Approval, provisioning, revocation, workflow-transition, arbitrary-database, and
generic-query tools will not be registered. Co-hosting does not grant the model access
to internal application services, and MCP tool visibility or annotations do not
replace authorization.

## Rationale

This deployment shape is proportionate to the current requirements:

- It preserves the product's central rule: AI interprets and gathers context, humans
  approve, and deterministic services authorize and execute.
- It keeps transaction, version, approval, audit, and idempotency rules within one
  consistency boundary.
- It avoids distributed failure modes and security machinery that would be synthetic
  rather than product-driven in this MVP.
- It gives one developer a codebase that can be implemented, tested, run, and
  demonstrated coherently.
- It still exercises a real typed MCP protocol boundary and permits MCP contract and
  interaction tests.
- Internal ports and adapters provide a practical extraction seam if a genuine
  deployment boundary appears later.

## Consequences

### Positive

- One build, process, deployment unit, local database, and operational configuration.
- No service discovery, message broker, service-to-service authentication, distributed
  transaction, or cross-service approval-evidence protocol is required.
- Immediate DevOps approval and provisioning can use straightforward durable state,
  persisted-evidence reloads, and request-based idempotency.
- End-to-end tests can cover UI, authentication, MCP, persistence, and provisioning
  through one test host without a live model.
- Correlation and audit evidence remain simple while retaining explicit boundaries.

### Negative and risks

- MCP, UI, model assistance, and workflow capabilities share process resources and a
  deployment lifecycle.
- A process failure affects the complete local application.
- Independent scaling or deployment of MCP is unavailable.
- Co-location can tempt implementation code to bypass the intended port, MCP, or
  authorization boundaries.

The resource and lifecycle trade-offs are acceptable at the defined local MVP scale.
Boundary-bypass risk is mitigated by project references, explicit MCP registration,
architecture tests, integration tests against `/mcp`, and keeping authorization in
the provider-neutral application layer.

## Alternatives considered

### Separate MCP service

Under this alternative, a second ASP.NET Core executable would own the MCP endpoint
and its three tools. The main application would connect to it over Streamable HTTP,
and the MCP service would obtain production-environment, incident, and role data from
its own store or from another trusted application interface.

This shape could be justified if MCP had a different owner or deployment cadence, if
several applications consumed the same MCP server, or if MCP traffic needed independent
scaling and resource limits. Process isolation could also contain failures caused by
the MCP SDK or tool handlers and would allow the MCP network surface to be deployed
with a narrower runtime configuration.

Those benefits do not apply to the current MVP. A separate MCP service would require:

- a second executable, deployment definition, health model, and configuration set;
- authenticated service-to-service calls and rules for propagating correlation and
  caller context;
- timeout, retry, availability, and partial-failure behavior at another network hop;
- a decision about ownership of request-context records;
- coordinated integration tests and startup for two processes; and
- contract compatibility when either service changes.

Giving both services direct access to the same SQLite database would make deployment
separate while leaving data ownership coupled. Giving the MCP service its own copy of
the data would introduce synchronization and stale-data behavior. Making it call the
main application would add another internal API and network dependency. None of these
trade-offs improves the two-client synthetic context used by this application.

The security boundary also would not change the trust model. Model output would remain
untrusted, identifiers would still require deterministic validation, and MCP tool
visibility would still not authorize protected actions. The explicit tool allowlist
and the absence of provisioning tools provide the required model boundary whether the
endpoint is in the same process or another one.

Therefore, a separate MCP service is rejected for the MVP. The typed MCP contracts and
adapter boundary are retained so extraction remains possible if independent consumers,
ownership, scaling, or isolation later becomes a concrete requirement.

### Separate provisioning service

Under this alternative, DevOps approval would remain in the web application, but a
separate service would create access grants. The web application would send a command
containing only request and operation references. The provisioning service would need
to authenticate the caller, reload or obtain persisted request and approval evidence,
validate the approved scope, invoke the provider, and return or publish a durable
result.

This separation can be valuable when provisioning holds real privileged credentials,
is owned by a dedicated operations team, integrates with multiple production systems,
requires stronger network isolation, or must scale and deploy independently from the
request workflow. It can also reduce the amount of code running in a credential-bearing
process.

For this MVP, provisioning is a local synthetic adapter with no credentials and no
independent owner. Separating it would create several design obligations without
reducing a current risk:

- an authenticated and versioned service contract for provisioning requests;
- a way for the provisioning service to reload stored approvals without
  trusting caller-supplied approval assertions;
- durable handling for the interval between committing DevOps approval and delivering
  the provisioning command;
- recovery when a timeout occurs after the remote service creates a grant;
- correlation and audit consistency across two stores or services;
- idempotency enforcement across the network boundary; and
- reconciliation when approval state and provisioning outcome disagree temporarily.

Passing the full approval evidence in the command would violate the rule that the
provisioning handler must reload persisted workflow evidence. Sharing the application database
would weaken service ownership and couple both deployments to the same schema. Adding
an evidence API, outbox, message broker, or callback protocol would solve problems
created solely by the split.

Therefore, a separate provisioning service is rejected for the MVP. The internal
handler still behaves as a protected boundary: it accepts references rather than
approval assertions, reloads persisted workflow evidence, validates its consistency,
and uses a stable idempotency identity. That design preserves an extraction seam if a
real provider, credential boundary, or independent operations owner appears later.

### Direct in-process context functions instead of MCP

Under this alternative, the model adapter would call application context services or
repository-backed functions directly. Tool definitions might be constructed in memory,
but request drafting would not traverse the `/mcp` transport. The application could
omit the MCP server entirely or expose an endpoint used only by tests and demonstrations.

This is the simplest runtime shape. It avoids loopback HTTP configuration, MCP session
and transport behavior, serialization overhead, and an additional timeout/failure
boundary. Direct calls would also be easier to debug in a small application.

It is nevertheless rejected because a real MCP interaction is part of the product
claim being demonstrated, not an incidental implementation mechanism. Bypassing the
endpoint in the actual drafting flow would fail to prove:

- that MCP inputs and results conform to the explicit typed contracts;
- that the model-visible server advertises exactly the three allowed tools;
- that not-found, invalid-input, unavailable, timeout, and cancellation outcomes are
  translated correctly across the protocol boundary;
- that stable data identifiers survive serialization and tool invocation;
  and
- that approval, provisioning, workflow, database, and generic-query capabilities are
  absent from the real model-facing MCP surface.

An unused MCP endpoint alongside direct production calls would test a different path
from the one demonstrated to users and could drift unnoticed. Calling the co-hosted
`/mcp` endpoint over Streamable HTTP adds a deliberate local failure boundary, but its
timeouts, cancellation, logging, and typed outcomes are required behavior and can be
covered by integration tests.

Therefore, the direct-function alternative is rejected. The MCP tool handlers may
delegate internally to the same focused data-query services used elsewhere,
but the model-assisted drafting adapter must reach those handlers through the real MCP
client and `/mcp` transport.

## Revisit criteria

Reconsider extracting a capability only when at least one concrete requirement exists:

- independent deployment cadence or ownership;
- materially different scaling, availability, or resource-isolation needs;
- a real external context or provisioning integration requiring a trust boundary;
- a threat model showing that process isolation materially reduces a relevant risk;
- multiple consumers needing a separately versioned MCP or application contract; or
- operational evidence that the single service cannot meet measured objectives.

Any extraction requires a new ADR covering identity propagation, authorization,
contract versioning, failure recovery, observability, deployment, and consistency. It
must preserve the rule that the LLM and MCP endpoint are never authorization
boundaries and that provisioning remains unavailable to the model.

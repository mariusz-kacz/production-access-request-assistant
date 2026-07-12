# Phase 0 Research: Governed Production Access

## Backend and frontend runtime

**Decision**: Use C# 14/.NET 10 LTS for the sole ASP.NET Core executable and React
19.2 with TypeScript for the browser client. Use Node.js 24 LTS only for frontend
development/build and lock exact packages in `package-lock.json`.

**Rationale**: React uses existing developer expertise. React 19.2 is the current
documented release and Node 24 is the current LTS. React compiles to static assets, so
production remains one .NET process.

**Alternatives considered**: Blazor (less familiar and no longer the baseline); Node
26 Current (not LTS in July 2026); a Node production/SSR server (second runtime and no
SEO need).

Sources: [React versions](https://react.dev/versions),
[Node releases](https://nodejs.org/en/about/previous-releases)

## React build and hosting

**Decision**: Use Vite with its official React plugin. Keep `ClientApp` inside Web,
emit hashed production assets for ASP.NET Core static-file hosting, and apply SPA
fallback only to the three client routes. In development, use Vite HMR with an HTTPS
proxy for `/api`; do not enable production CORS.

**Rationale**: ASP.NET Core SPA guidance supports publishing React output through
`wwwroot`; Vite provides a fast development loop and production bundle. The proxy
preserves same-origin browser behavior while the published system remains one service.

**Alternatives considered**: Separate frontend deployment (contradicts ADR 0001);
committed generated bundles; rebuilding assets manually for each edit; React SSR.

Sources: [ASP.NET Core SPA overview](https://learn.microsoft.com/aspnet/core/client-side/spa/intro),
[Vite production build](https://vite.dev/guide/build)

## Client routing, state, and testing

**Decision**: Use React Router declaratively for `/requests`, `/requests/new`, and
`/requests/:requestId`, component-local state, and one typed native-fetch client. Use
Vitest and React Testing Library for presentation behavior; keep workflow and
authorization coverage in xUnit host tests.

**Rationale**: Three pages do not justify Redux, a query cache, a form framework, or a
frontend domain layer. Thin component tests avoid duplicating deterministic server
tests.

**Alternatives considered**: Redux/TanStack Query (unneeded shared/cache state); React
Router server/framework mode (another server model); exhaustive browser automation
(outside the baseline).

Source: [React Router routing](https://reactrouter.com/start/declarative/routing)

## Same-origin API and antiforgery

**Decision**: Expose focused JSON `/api` commands/queries documented in
`contracts/ui-api.md`. A demo sign-in maps an allowlisted key to immutable claims and
issues an HttpOnly cookie. An antiforgery endpoint issues a readable `XSRF-TOKEN`; the
fetch client sends `X-XSRF-TOKEN` on every unsafe request. Errors use safe Problem
Details plus stable code/correlation/field extensions.

**Rationale**: React requires HTTP, but the interface remains same-origin and internal
to one product. Cookie middleware rebuilds `HttpContext.User`, so action DTOs omit
identity and roles. Since browsers send cookies automatically, unsafe operations need
explicit CSRF protection.

**Alternatives considered**: Local-storage bearer tokens (unnecessary lifecycle and
higher XSS consequence); identity/roles in bodies (untrusted); SameSite alone; generic
CRUD/GraphQL; generated client pipeline for fewer than ten focused operations.

Source: [ASP.NET Core antiforgery for JavaScript/SPAs](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery)

## Solution boundaries

**Decision**: Use two production projects: provider-neutral `GovernedAccess.Core`
and executable `GovernedAccess.Web`; React source is nested build input in Web.

**Rationale**: One class-library boundary prevents domain/application logic from
depending on AI, MCP, EF, or web contracts while avoiding project-per-layer ceremony.
Logical folders inside each project are sufficient for the single-developer scope,
and the Vite toolchain does not become a production service.

**Alternatives considered**: One project (makes dependency enforcement weaker);
domain/application/infrastructure/UI projects (more structure than this MVP needs);
multiple hosts (explicitly out of scope).

## Model abstraction and structured output

**Decision**: Define a core drafting port and implement it with
`Microsoft.Extensions.AI.IChatClient`. Request a strict JSON-schema response generated
from the draft DTO, then deserialize with `System.Text.Json` and validate shape,
required fields, enum-like values, and authoritative identifiers before use.

**Rationale**: `Microsoft.Extensions.AI` provides a provider-neutral chat abstraction
and schema response formats while keeping provider SDK contracts at the adapter edge.
Schema conformance reduces malformed responses but is never treated as authorization.

**Alternatives considered**: Provider SDK types in application code (violates the
boundary); free-form JSON prompting (weaker validation); Semantic Kernel agent/workflow
abstractions (unnecessary orchestration).

Source: [`ChatResponseFormat.ForJsonSchema`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatresponseformat)

## MCP SDK, transport, and tool boundary

**Decision**: Use the official C# SDK packages `ModelContextProtocol` and
`ModelContextProtocol.AspNetCore`. Map one stateless Streamable HTTP endpoint at
`/mcp`; register exactly `get_production_environment`, `get_incident`, and
`get_available_roles` from an explicit allowlist. The drafting adapter connects to
that endpoint over HTTP with a bounded `HttpClient`.

**Rationale**: Streamable HTTP is the SDK's current ASP.NET Core transport and provides
a real MCP contract inside the single host. Stateless requests align handler scopes
with ASP.NET Core request services and propagate cancellation. Explicit registration
makes forbidden capabilities inspectable in tests.

**Alternatives considered**: In-process direct calls (not a real MCP interaction);
stdio child process or second host (breaks the single-host scope); legacy SSE (not the
recommended transport); dynamically exposing all attributed methods (weak allowlist).

Sources: [MCP C# transports](https://csharp.sdk.modelcontextprotocol.io/concepts/transports/transports.html),
[stateless mode and cancellation](https://csharp.sdk.modelcontextprotocol.io/concepts/stateless/stateless.html)

## Persistence and concurrency

**Decision**: Use EF Core 10 with one local SQLite database. Persist mutable workflow
records and insert-only audit records in ordinary tables. Use an application-managed
integer `PersistenceVersion` concurrency token on requests, unique constraints for
approval stage/version and operation/grant identity, and short transactions around
each state transition.

**Rationale**: SQLite is proportionate local persistence and can exercise relational
constraints in integration tests. EF optimistic concurrency rejects races without
conflating storage concurrency with the business request version. A single
`SaveChanges` is atomic when sufficient; explicit short transactions cover multi-step
state/evidence updates.

**Alternatives considered**: In-memory collections (poor concurrency/durability
demonstration); SQL Server/PostgreSQL (operational overhead); event sourcing or a
generic repository/unit-of-work layer (out of scope and unnecessary).

Sources: [EF Core concurrency](https://learn.microsoft.com/ef/core/saving/concurrency),
[EF Core transactions](https://learn.microsoft.com/ef/core/saving/transactions)

## Synthetic authentication

**Decision**: Use ASP.NET Core cookie authentication and the antiforgery-protected
demo session endpoints described above. The allowlisted principal key resolves to
immutable server-side claims. Protected actions accept no actor, role, approver, or
authorization fields and read the actor from `HttpContext.User`.

**Rationale**: This makes identity selection visibly synthetic while ensuring later
authorization never trusts form-submitted identity or role claims. The exact four
principals and their authority are server-owned.

**Alternatives considered**: Query-string/header identity on every action (browser
controls authority); ASP.NET Core Identity database (unnecessary account management);
real OIDC/Entra ID (explicitly out of scope).

## DevOps approval and provisioning failure boundary

**Decision**: Persist the DevOps decision and stable provisioning operation before the
provider call. Invoke the provider outside a long database transaction. On return,
reload the operation and finalize `Active` plus grant/audit atomically; on a typed
failure or timeout, persist `ProvisioningFailed`. Retry is allowed only from that state
and calls the same handler with the same operation ID.

**Rationale**: External work must not hold a database transaction. Durable operation
identity makes a lost response recoverable. Reloading before both provider invocation
and finalization prevents upstream assertions and concurrent changes from bypassing
the rules.

**Alternatives considered**: One transaction across provider work (long locks and
uncertain rollback); an outbox/background worker (extra infrastructure and not
immediate); letting the browser provision separately (creates a forbidden authority).

## Idempotency identity

**Decision**: Derive a canonical operation identity from request ID, request version,
environment ID, exact approved role, and DevOps-approved duration using length-prefixed
UTF-8 components and SHA-256. Store the printable digest and enforce uniqueness.

**Rationale**: The identity is deterministic, stable across retries, and bound to all
approved scope that affects the grant. Length-prefixing prevents ambiguous
concatenation. Request version changes necessarily produce a different operation.

**Alternatives considered**: Random key generated on every attempt (breaks retry);
request ID alone (does not bind version/scope); browser-supplied key (untrusted).

## Material edits

**Decision**: Client, environment, requested role, requested duration, justification,
and incident association are material. Any changed normalized value increments request
`Version`, returns the request to `Draft`, and leaves older approvals stored but
ineligible. No other editable business fields are planned.

**Rationale**: This exactly follows FR-028 and ensures a purpose or scope change gets
new validation and both approvals.

**Alternatives considered**: Treat justification as non-material (contradicts the
specified business-purpose review); delete old approvals (loses audit evidence).

## Timeouts, outcomes, and observability

**Decision**: Configure 30 seconds for model extraction, 5 seconds per MCP operation,
and 10 seconds for synthetic provisioning. Link timeout cancellation with caller and
host-shutdown tokens. Map validation, malformed output, not-found, unavailable,
timeout, cancellation, authorization, stale version, concurrency, and provider failure
to explicit typed outcomes. Log correlation ID, operation/tool name, actor when
applicable, duration, and outcome only.

**Rationale**: Finite boundaries make failure scenarios repeatable and prevent false
success. Metadata is sufficient for the MVP without raw prompt or payload capture.

**Alternatives considered**: Library default/infinite timeouts (uncontrolled failure);
exceptions as expected control flow (poor UI mapping); full payload logging (unsafe).

## OpenTelemetry

**Decision**: Defer exporters and external collectors. Use `ILogger`, correlation
middleware, and `System.Diagnostics.ActivitySource` hooks where inexpensive so later
OpenTelemetry adoption does not change domain contracts.

**Rationale**: This meets the required evidence while preserving OpenTelemetry as
optional polish.

**Alternatives considered**: Full collector/dashboard stack (blocks MVP); no tracing
hooks (makes later correlation work harder).

All technical-context clarifications are resolved.

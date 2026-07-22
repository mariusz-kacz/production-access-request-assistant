# Implementation Plan: Governed Production Access

**Branch**: `001-governed-production-access` | **Date**: 2026-07-12 | **Last updated**: 2026-07-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-governed-production-access/spec.md`

## Summary

Build one deployable .NET 10 ASP.NET Core modular monolith with a thin React 19.2 and
TypeScript client. Three production projects establish explicit provider-neutral Core,
MCP adapter, and executable Web boundaries while still producing one host process.
Vite produces static assets served by the ASP.NET Core host in production; that host
also exposes same-origin typed `/api` endpoints and the real `/mcp` Streamable HTTP
endpoint. A provider-neutral core owns validation, workflow, authorization, immutable
request-scope binding, and provisioning policy. React never supplies trusted server identity or
calls MCP or provisioning directly.

## Request Context Naming Change

**Status**: Implemented on 2026-07-15.

This is a terminology-only refactor. The current domain model, production-only scope,
workflow behavior, persistence shape, API fields, and MCP tool names remain unchanged.

| Current umbrella name | Replacement |
|---|---|
| `IAccessDataReader` | `IRequestContextReader` |
| `accessData` | `requestContext` |
| `StubAccessDataReader` | `StubRequestContextReader` |
| `AccessDataTools.cs` | `RequestContextTools.cs` |
| `access-data-*` failure codes | `request-context-*` |
| “access data” in ordinary prose | “request context”, “stored records”, or “current data” |

`AccessRequest` and `AccessGrant` retain `Access` because they directly represent the
governed workflow. `ProductionEnvironment` and production-specific MCP names also
remain unchanged. The mixed `AccessData.cs` file is split into files named after its
concrete domain concepts rather than renamed to another umbrella file.

Implementation order:

1. Update planning/task paths and terminology.
2. Split the domain types into concept-named files without changing their public API.
3. Rename the reader interface, variables, test doubles, planned MCP handler file,
   and umbrella failure codes.
4. Build and run focused unit and integration verification.

Completion requires no remaining `AccessData`, `IAccessDataReader`, `accessData`, or
`access-data-*` umbrella names outside this migration record.

## Independently Navigable Story Slices

**Status**: Planning correction on 2026-07-20.

Each user story that introduces a browser action must also deliver the minimum query
and route needed to reach that action before the story checkpoint. User Story 2
therefore owns an authenticated `GET /api/requests/{requestId}` projection and the
`/requests/:requestId` React route with immutable request scope, current status, and
server-computed available actions. The submitted-request success link must open this
route, and switching demo identities on the route must reload the projection.

User Story 4 does not create the detail route. It extends the established projection
with ordered decisions, current validation, provisioning outcome, grant and logical
expiry, retry, and audit evidence, and adds the request-list route. This keeps the
business-decision story independently demonstrable without pulling provisioning or
complete evidence scope into an earlier phase.

## Technical Context

**Language/Version**: C# 14 on .NET 10 LTS; TypeScript with React 19.2; Node.js 24 LTS for frontend build tooling

**Primary Dependencies**: ASP.NET Core 10 MVC controllers and static files, React 19.2, React Router, Vite and its official React plugin, `Microsoft.Extensions.AI`, official `ModelContextProtocol` packages, EF Core 10 SQLite, `System.Text.Json`

**Storage**: One local SQLite database through EF Core; deterministic seed data for request context and principals; insert-only audit rows; provisioning operations and grants uniquely keyed by immutable request ID

**Testing**: xUnit, ASP.NET Core `WebApplicationFactory`, SQLite in-memory databases, deterministic fake `IChatClient`, controllable synthetic provisioner, Vitest, and React Testing Library

**Target Platform**: One local cross-platform ASP.NET Core deployment serving a modern-browser React bundle; development uses .NET 10 SDK and Node.js 24 LTS

**Project Type**: Single deployable web application with a React SPA build nested in the sole ASP.NET Core executable

**Performance Goals**: Normal local list/detail/action feedback within 2 seconds; primary demonstrations within 15 minutes; 100 concurrent duplicate provisioning attempts create exactly one grant; frontend production assets use hashed Vite output

**Constraints**: No live model in tests; one executable and one production origin; exactly three read-only model-visible MCP tools; no public provisioning endpoint; authenticated actor comes from server cookie context; every unsafe `/api` request requires antiforgery validation; model/MCP/provisioning timeouts are 30/5/10 seconds; cancellation crosses async boundaries; raw prompts, secrets, and full MCP payloads are not logged

**Access Lifetime**: Requesters and approvers provide no duration; every successful grant expires exactly eight hours after activation.

**Scale/Scope**: Portfolio-grade local MVP for exactly 2 clients, 2 environments, 2 roles, 4 principals, 3 React routes, and synthetic records; no distributed infrastructure or background processing

## Constitution Check

*GATE: Passed before research and re-checked after design.*

The repository constitution is an unratified placeholder and therefore defines no
enforceable project-specific gates. The explicit repository instructions and product
baseline are applied as the operative gates:

| Gate | Result | Design evidence |
|---|---|---|
| AI interprets only; humans approve; deterministic code authorizes and executes | PASS | Drafting adapter is isolated from decision and provisioning services; protected actions use authenticated server context. |
| Domain/application code has no AI-provider or MCP SDK contracts | PASS | `GovernedAccess.Core` exposes provider-neutral ports and typed outcomes; SDK types remain in `GovernedAccess.Web` adapters. |
| Model output is schema-validated and identifiers are checked against trusted server data | PASS | Draft JSON schema plus submission validator; provisioning reloads and validates persisted workflow evidence. |
| Model receives exactly the three allowed read-only MCP tools | PASS | MCP contract declares only the required tools; tool registration uses an explicit allowlist. |
| Approval is authenticated and bound to an exact immutable request scope | PASS | Approval entity and action contracts bind request ID and exact role; actor comes only from server claims. |
| Submitted requests cannot change after approval begins | PASS | Submitted request fields are immutable; corrections create a new request ID and require new approvals. |
| DevOps cannot alter role or duration | PASS | Domain decision policy requires role equality; successful grants receive the server-owned fixed eight-hour lifetime. |
| Provisioning reloads persisted evidence and is idempotent | PASS | The immutable request UUID keys the operation and serves as provider idempotency identity; the handler reloads request, approval, and operation evidence and enforces one operation per request. |
| Browser identity and claims are untrusted | PASS | HttpOnly server cookie establishes actor; action bodies contain no actor, roles, or approver identity. |
| Cookie-authenticated mutations resist CSRF | PASS | Same-origin antiforgery token plus `X-XSRF-TOKEN`; all unsafe application endpoints validate it. |
| Single-host, proportionate structure | PASS | One executable serves React assets, API, and MCP; Vite is build/dev tooling, not a production service. |
| Typed failures, timeouts, cancellation, safe logging | PASS | Contracts enumerate expected outcomes; timeout policies link caller cancellation; logging captures metadata only. |
| Required negative, authorization, stale-state, MCP, and idempotency tests | PASS | Quickstart matrix and test-project boundaries cover all mandatory scenarios without a live LLM. |

**Post-design re-check**: PASS. The data model and contracts preserve all gates. No
additional executable, workflow engine, generic repository, state-changing MCP tool,
or autonomous agent is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/001-governed-production-access/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── mcp-tools.json
│   ├── request-draft.schema.json
│   └── ui-api.md
└── tasks.md                 # Created later by /speckit-tasks
```

### Source Code (repository root)

```text
ProductionAccessRequestAssistant.sln
src/
├── GovernedAccess.Core/
│   ├── Domain/
│   ├── Application/
│   └── Ports/
├── GovernedAccess.Mcp/
│   ├── RequestContextTools.cs
│   └── McpRegistration.cs
└── GovernedAccess.Web/
    ├── ClientApp/
    │   ├── src/
    │   │   ├── api/
    │   │   ├── auth/
    │   │   ├── components/
    │   │   ├── pages/
    │   │   └── test/
    │   ├── package.json
    │   └── vite.config.ts
    ├── Controllers/
    ├── Authentication/
    ├── Persistence/
    ├── Ai/
    ├── Provisioning/
    ├── wwwroot/             # Generated Vite production output
    └── Program.cs
tests/
├── GovernedAccess.UnitTests/
└── GovernedAccess.IntegrationTests/
```

**Structure Decision**: `GovernedAccess.Web` is the sole executable and contains the
AI, EF, authentication, and UI adapters plus generated React assets.
`GovernedAccess.Mcp` contains all MCP SDK-facing tool and endpoint registration code;
Web references it to host `/mcp` in the same process and supplies the EF-backed
`IRequestContextReader`. `ClientApp` is build input inside Web, not another deployed
service. `GovernedAccess.Core` combines domain and application code in one library so
authorization rules remain independently testable without creating a project per
logical module. Unit tests target deterministic rules; xUnit integration tests exercise
persistence, authentication, API/MCP, AI failures, and provisioning; Vitest covers the
thin React presentation.

## Implementation Boundaries

- MVC controllers follow stable HTTP resource and policy boundaries:
  `RequestDraftsController` owns requester-only draft preparation,
  `AccessRequestsController` owns request creation, queries, and retry, and
  `RequestDecisionsController` owns business and DevOps decision subresources.
  Antiforgery validation is applied to unsafe actions without affecting request GETs.
- React invokes focused same-origin JSON commands and queries through one typed fetch
  wrapper; UI visibility is never treated as authorization.
- Request detail is delivered progressively through one stable endpoint and route:
  US2 establishes participant visibility, immutable scope, status, and available
  business actions; later stories enrich the response without deferring reachability
  of already-implemented human actions.
- Production publish builds hashed Vite assets for ASP.NET static-file hosting. During
  development Vite proxies `/api` to ASP.NET Core for HMR without a production CORS
  design; SPA fallback excludes `/api/*` and `/mcp`.
- The fetch wrapper sends credentials, obtains the antiforgery cookie, adds
  `X-XSRF-TOKEN` to unsafe methods, maps safe Problem Details, and supports AbortSignal.
- Protected API inputs contain resource/decision data only. The actor is
  resolved from authenticated `ClaimsPrincipal`; business scope and approver
  responsibility are loaded by the server.
- `IChatClient` and MCP client types are adapted in `GovernedAccess.Web`; MCP server
  types are isolated in `GovernedAccess.Mcp`. Core sees only
  `DraftInterpretationRequest`, `DraftInterpretationOutcome`, and the request-context
  port.
- The AI adapter uses a strict JSON-schema response and an explicit MCP-tool allowlist.
  Its MCP client calls the same host's configured loopback `/mcp` Streamable HTTP
  endpoint, preserving a real protocol boundary without another process.
- DevOps approval persists the authenticated decision and provisioning operation,
  then invokes the protected handler. The handler reloads the immutable request,
  approvals, and request-bound operation before calling the synthetic provider.
- Provider success and workflow activation are finalized in one local database save.
  The provider call and local save are not a cross-system atomic operation. A timeout
  or lost response records `ProvisioningFailed`; retry reuses the same request ID.
- A unique database constraint on `AccessGrant.RequestId` plus provider-side
  get-or-create semantics guarantees one grant under retries/concurrency.
- `AccessRequest.PersistenceVersion` is an internal EF concurrency token. It detects
  competing workflow transitions and is not exposed as business authorization evidence.

## Complexity Tracking

No gate violations require justification.

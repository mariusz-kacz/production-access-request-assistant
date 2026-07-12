# Implementation Plan: Governed Production Access

**Branch**: `001-governed-production-access` | **Date**: 2026-07-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-governed-production-access/spec.md`

## Summary

Build one deployable .NET 10 ASP.NET Core modular monolith with a thin React 19.2 and
TypeScript client. Vite produces static assets served by the ASP.NET Core host in
production; that host also exposes same-origin typed `/api` endpoints and the real
`/mcp` Streamable HTTP endpoint. A provider-neutral core owns validation, workflow,
authorization, version binding, and provisioning policy. React never supplies
authoritative identity or calls MCP or provisioning directly.

## Technical Context

**Language/Version**: C# 14 on .NET 10 LTS; TypeScript with React 19.2; Node.js 24 LTS for frontend build tooling

**Primary Dependencies**: ASP.NET Core 10 Minimal APIs and static files, React 19.2, React Router, Vite and its official React plugin, `Microsoft.Extensions.AI`, official `ModelContextProtocol` packages, EF Core 10 SQLite, `System.Text.Json`

**Storage**: One local SQLite database through EF Core; deterministic seed data for authoritative context and principals; insert-only audit rows; unique provisioning operation identity

**Testing**: xUnit, ASP.NET Core `WebApplicationFactory`, SQLite in-memory databases, deterministic fake `IChatClient`, controllable synthetic provisioner, Vitest, and React Testing Library

**Target Platform**: One local cross-platform ASP.NET Core deployment serving a modern-browser React bundle; development uses .NET 10 SDK and Node.js 24 LTS

**Project Type**: Single deployable web application with a React SPA build nested in the sole ASP.NET Core executable

**Performance Goals**: Normal local list/detail/action feedback within 2 seconds; primary demonstrations within 15 minutes; 100 concurrent duplicate provisioning attempts create exactly one grant; frontend production assets use hashed Vite output

**Constraints**: No live model in tests; one executable and one production origin; exactly three read-only model-visible MCP tools; no public provisioning endpoint; authenticated actor comes from server cookie context; every unsafe `/api` request requires antiforgery validation; model/MCP/provisioning timeouts are 30/5/10 seconds; cancellation crosses async boundaries; raw prompts, secrets, and full MCP payloads are not logged

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
| Model output is schema-validated and identifiers are authoritatively revalidated | PASS | Draft JSON schema plus submission validator; provisioning reloads and validates current authoritative state. |
| Model receives exactly the three allowed read-only MCP tools | PASS | MCP contract declares only the required tools; tool registration uses an explicit allowlist. |
| Approval is authenticated and bound to exact request/version/scope | PASS | Approval entity and action contracts bind request ID, version, role, and duration; actor comes only from server claims. |
| Material edits invalidate prior authority | PASS | Domain transition increments business version and returns to `Draft`; old decisions remain evidence only. |
| DevOps cannot alter role or increase duration | PASS | Domain decision policy requires role equality and a positive duration at or below the business maximum. |
| Provisioning reloads evidence and is idempotent | PASS | Handler accepts request ID/version/operation identity only, reloads all evidence, and enforces a unique operation identity. |
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
    ├── Endpoints/
    ├── Authentication/
    ├── Persistence/
    ├── Ai/
    ├── Mcp/
    ├── Provisioning/
    ├── wwwroot/             # Generated Vite production output
    └── Program.cs
tests/
├── GovernedAccess.UnitTests/
└── GovernedAccess.IntegrationTests/
```

**Structure Decision**: `GovernedAccess.Web` is the sole executable and contains
all SDK/infrastructure adapters, `/api`, `/mcp`, and generated React assets.
`ClientApp` is build input inside Web, not another deployed service. `GovernedAccess.Core` combines
domain and application code in one library so authorization rules remain independently
testable without creating a project per logical module. Unit tests target deterministic
rules; xUnit integration tests exercise persistence, authentication, API/MCP, AI
failures, and provisioning; Vitest covers the thin React presentation.

## Implementation Boundaries

- React invokes focused same-origin JSON commands and queries through one typed fetch
  wrapper; UI visibility is never treated as authorization.
- Production publish builds hashed Vite assets for ASP.NET static-file hosting. During
  development Vite proxies `/api` to ASP.NET Core for HMR without a production CORS
  design; SPA fallback excludes `/api/*` and `/mcp`.
- The fetch wrapper sends credentials, obtains the antiforgery cookie, adds
  `X-XSRF-TOKEN` to unsafe methods, maps safe Problem Details, and supports AbortSignal.
- Protected API inputs contain resource/version/decision data only. The actor is
  resolved from authenticated `ClaimsPrincipal`; business scope and approver
  responsibility are loaded by the server.
- `IChatClient` and MCP client/server types are adapted in `GovernedAccess.Web`; the
  core sees only `DraftPreparationRequest`, `DraftPreparationOutcome`, and the three
  authoritative context ports.
- The AI adapter uses a strict JSON-schema response and an explicit MCP-tool allowlist.
  Its MCP client calls the same host's configured loopback `/mcp` Streamable HTTP
  endpoint, preserving a real protocol boundary without another process.
- DevOps approval persists the authenticated decision and provisioning operation,
  then invokes the protected handler. The handler reloads request, current-version
  approvals, environment, role, and incident before calling the synthetic provider.
- Provider success and workflow activation are finalized transactionally. A timeout
  or lost response records `ProvisioningFailed`; retry reuses the same operation ID.
- A unique database constraint on `AccessGrant.OperationId` plus provider-side
  get-or-create semantics guarantees one grant under retries/concurrency.
- `AccessRequest.PersistenceVersion` is an EF concurrency token distinct from the
  business-facing request `Version`; conflicts become typed stale-state outcomes.

## Complexity Tracking

No gate violations require justification.

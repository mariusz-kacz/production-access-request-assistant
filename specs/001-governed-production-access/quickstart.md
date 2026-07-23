# Quickstart Validation Guide

This guide is executable after implementation tasks are complete. It validates the
React UI, same-origin API, real MCP boundary, and governed workflow without a live
model or real production systems.

## Prerequisites

- .NET 10 SDK
- Node.js 24 LTS with npm
- PowerShell 7 or a shell capable of running `dotnet`
- Repository root as the working directory

The application uses local SQLite and deterministic seed data. The shipped host uses
the valid deterministic chat mode for repeatable local validation; no model
credentials are required. Other chat modes are selected by integration-test host
overrides rather than runtime configuration.

## Build and automated validation

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
npm run build --prefix src/GovernedAccess.Web/ClientApp
dotnet build ProductionAccessRequestAssistant.sln --no-restore
dotnet test ProductionAccessRequestAssistant.sln --no-build
```

Expected: TypeScript/frontend tests, nullable/warning gates, and .NET unit/integration
tests pass without a live LLM or external network dependency.

For production-shape validation, run the host after building the React assets:

```powershell
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Open the HTTPS URL printed by the host; ASP.NET Core serves the React build. For HMR,
run `npm run dev --prefix src/GovernedAccess.Web/ClientApp` in another terminal and
open the URL printed by Vite. Vite proxies `/api` to ASP.NET Core, so the browser still uses
a same-origin contract and production does not require CORS.

Use the demo identity selector to issue a server-authenticated HttpOnly cookie. Verify
the client sends `X-XSRF-TOKEN` for unsafe calls and that a mutation without it is
rejected without protected state change.

## Presentation quality check

Run this bounded manual check against the published ASP.NET-hosted bundle at a desktop
viewport and again at 360px width or 200% browser zoom:

1. Open each available route while signed out, as requester, as the responsible
   business approver, and as DevOps where the workflow permits.
2. Traverse navigation, identity selection, form fields, links, and decision controls
   using only the keyboard.
3. Exercise loading, field validation, safe Problem Details, pending submission,
   business approval, DevOps approval, rejection, and active-grant states.
4. Inspect long request, environment, grant, and correlation identifiers and the
   activation/expiry timestamps.

Expected:

- The interface reads as one restrained internal operations tool: compact horizontal
  shell, neutral canvas, typographic/rule-based grouping, and no decorative dashboard
  tile grid, gradient, glass, glow, or ornamental chart.
- Each page has one obvious task and heading. Current identity, synthetic-demo context,
  request state, immutable scope, and the currently allowed action are distinguishable
  without relying on color alone.
- Keyboard focus is always visible; native labels and live feedback remain available;
  primary pointer targets are practical to activate; reduced-motion preferences do
  not remove information.
- At narrow width and 200% zoom there is no page-level horizontal scrolling, clipped
  action, hidden evidence, or overlapping text. Long identifiers wrap and remain
  selectable in full.
- Statuses and timestamps use readable presentation while retaining authoritative
  values; the UI never invents scope, authority, approval, provisioning, or expiry.

## Scenario 1: safe request preparation and submission

1. Sign in as requester and open `/requests/new`.
2. Enter: `I need read-only access to Client Alpha production for INC-1042 to investigate the incident.`
3. Prepare the draft, review the stable IDs, then submit.

Expected: `PROD-ALPHA-EU`, `ProductionReadOnly`, and `INC-1042` are
shown; stored-data validation succeeds; an immutable request enters
`AwaitingBusinessApproval`; no approval or grant exists.

Run the draft-interpretation integration tests with deterministic test-host overrides
for incomplete output, malformed JSON, MCP not-found, MCP timeout, and cancellation.
Expected: a typed correction/failure result and no submitted approval or grant.
Inspect the initialized MCP server and verify it advertises exactly the three tools in
[contracts/mcp-tools.json](contracts/mcp-tools.json).

## Scenario 2: client-isolated business approval

1. Follow the submitted request's **View request details** link, or open
   `/requests/{requestId}` directly, and verify the immutable scope and current status
   load from the server.
2. Switch to the Client Beta business approver. Verify the request and action are not
   exposed; the automated API test also attempts the crafted decision and proves it is
   rejected without a state change.
3. Switch to the Client Alpha business approver on the same URL. Verify the detail is
   reloaded, the business decision panel appears, and approve.

Expected: Beta is rejected and audited without state change. Alpha approval binds the
exact request ID and role and moves the request to
`AwaitingDevOpsApproval`. This scenario is runnable when User Story 2 completes and
does not depend on the later request list, DevOps, provisioning, retry, or full audit
timeline work.

## Scenario 3: DevOps approval and independent provisioning

1. Sign in as DevOps and open the business-approved request.
2. Approve the exact business-approved role.

Expected: the handler reloads request, approvals, and the request-bound operation,
validates persisted workflow and immutable-scope consistency, creates one synthetic grant, and moves the request
to `Active`. The detail page shows activation, expiry exactly eight hours later,
request-based idempotency identity, and audit events. Any crafted duration field is safely ignored or
rejected, and any role-changing request is rejected without grant creation.

## Scenario 4: correction creates a new immutable request

1. Create another request and obtain business approval.
2. As requester, discover that its justification is incorrect.
3. Prepare and submit a corrected request.

Expected: the original request and its approval evidence remain unchanged. The
corrected submission receives a new request ID, enters `AwaitingBusinessApproval`, and
requires both approvals.

## Scenario 5: lost response and duplicate retry

1. Use the integration-test host control to configure the synthetic provisioner to
   persist its grant and then simulate a lost response on DevOps approval. The shipped
   UI has no failure-injection control.
2. Confirm `ProvisioningFailed`, sign in as DevOps, and retry.
3. Exercise the automated concurrency test with at least 100 attempts for the same
   request ID used for idempotency.

Expected: retry repeats persisted-evidence validation and returns the existing grant. Exactly one
grant exists for the operation and duplicate retries are audited. Non-DevOps retry and
retry from any other state are rejected.

## Required automated evidence matrix

| Area | Required checks |
|---|---|
| Domain | Immutable submitted scope, state transitions, exact role, fixed eight-hour grant lifetime, duplicate-stage prevention, logical expiry. |
| Authorization | Server-context actor, wrong-client approver, unauthorized DevOps, requester cannot choose approver. |
| AI adapter | Valid/incomplete/malformed schema, deterministic fake, timeout, cancellation, no live model. |
| MCP integration | Exact allowlist, typed schemas/outcomes, stable IDs, not-found/unavailable/timeout/cancellation. |
| Persistence | Insert-only audit behavior, optimistic conflict, approval uniqueness, operation/grant uniqueness. |
| Provisioning | Persisted-evidence reload, inconsistent operation or approval data, idempotent success, lost response, 100 concurrent duplicates. |
| React/UI host | Three routes, typed errors, abort handling, relevant list, action visibility plus server enforcement, no MCP/provisioning browser calls, responsive presentation check, visible keyboard focus, and non-color status meaning. |
| API security | Cookie actor, antiforgery on unsafe methods, over-posting resistance, participant visibility. |

The request-context entity rules are in [data-model.md](data-model.md); browser/server
action shapes are in [contracts/ui-api.md](contracts/ui-api.md).

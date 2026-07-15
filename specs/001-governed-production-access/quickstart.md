# Quickstart Validation Guide

This guide is executable after implementation tasks are complete. It validates the
React UI, same-origin API, real MCP boundary, and governed workflow without a live
model or real production systems.

## Prerequisites

- .NET 10 SDK
- Node.js 24 LTS with npm
- PowerShell 7 or a shell capable of running `dotnet`
- Repository root as the working directory

The application uses local SQLite and deterministic seed data. Configure the fake
chat client for repeatable local validation; no model credentials are required.

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
dotnet run --project src/GovernedAccess.Web
```

Open the HTTPS URL printed by the host; ASP.NET Core serves the React build. For HMR,
run `npm run dev --prefix src/GovernedAccess.Web/ClientApp` in another terminal and
open the Vite HTTPS URL. Vite proxies `/api` to ASP.NET Core, so the browser still uses
a same-origin contract and production does not require CORS.

Use the demo identity selector to issue a server-authenticated HttpOnly cookie. Verify
the client sends `X-XSRF-TOKEN` for unsafe calls and that a mutation without it is
rejected without protected state change.

## Scenario 1: safe request preparation and submission

1. Sign in as requester and open `/requests/new`.
2. Enter: `I need four hours of read-only access to Client Alpha production for INC-1042 to investigate the incident.`
3. Prepare the draft, review the stable IDs, then submit.

Expected: `PROD-ALPHA-EU`, `ProductionReadOnly`, 240 minutes, and `INC-1042` are
shown; stored-data validation succeeds; request version 1 enters
`AwaitingBusinessApproval`; no approval or grant exists.

Repeat with deterministic fake modes for incomplete output, malformed JSON, MCP
not-found, MCP timeout, and cancellation. Expected: a typed correction/failure result
and no submitted approval or grant. Inspect the initialized MCP server and verify it
advertises exactly the three tools in [contracts/mcp-tools.json](contracts/mcp-tools.json).

## Scenario 2: client-isolated business approval

1. Open the version-1 Alpha request as the Client Beta business approver and attempt
   approval.
2. Reopen it as the Client Alpha business approver and approve.

Expected: Beta is rejected and audited without state change. Alpha approval binds the
exact request/version/role/duration and moves the request to
`AwaitingDevOpsApproval`.

## Scenario 3: DevOps approval and independent provisioning

1. Sign in as DevOps and open the business-approved request.
2. Approve `240` minutes.

Expected: the handler reloads request and approvals, revalidates current environment,
role, incident, version, and scope, creates one synthetic grant, and moves the request
to `Active`. The detail page shows activation, expiry, operation identity, and audit
events. A duration increase and any role-changing crafted request are rejected without
grant creation.

## Scenario 4: stale approval after material edit

1. Create another request and obtain business approval for version 1.
2. As requester, materially edit justification or duration.
3. Attempt a decision using version 1.

Expected: edit creates version 2 in `Draft`; version-1 approval remains visible but is
ineligible; stale action is rejected/audited; new submission requires both approvals.

## Scenario 5: lost response and duplicate retry

1. Configure the synthetic provisioner to persist its grant and then simulate a lost
   response on DevOps approval.
2. Confirm `ProvisioningFailed`, sign in as DevOps, and retry.
3. Exercise the automated concurrency test with at least 100 attempts for the same
   operation identity.

Expected: retry performs full revalidation and returns the existing grant. Exactly one
grant exists for the operation and duplicate retries are audited. Non-DevOps retry and
retry from any other state are rejected.

## Required automated evidence matrix

| Area | Required checks |
|---|---|
| Domain | State transitions, exact role, duration ceiling, material versioning, stale version, logical expiry. |
| Authorization | Server-context actor, wrong-client approver, unauthorized DevOps, requester cannot choose approver. |
| AI adapter | Valid/incomplete/malformed schema, deterministic fake, timeout, cancellation, no live model. |
| MCP integration | Exact allowlist, typed schemas/outcomes, stable IDs, not-found/unavailable/timeout/cancellation. |
| Persistence | Insert-only audit behavior, optimistic conflict, approval uniqueness, operation/grant uniqueness. |
| Provisioning | Current-state reload, stale data, missing approval, idempotent success, lost response, 100 concurrent duplicates. |
| React/UI host | Three routes, typed errors, abort handling, relevant list, action visibility plus server enforcement, no MCP/provisioning browser calls. |
| API security | Cookie actor, antiforgery on unsafe methods, over-posting resistance, participant visibility. |

The request-context entity rules are in [data-model.md](data-model.md); browser/server
action shapes are in [contracts/ui-api.md](contracts/ui-api.md).

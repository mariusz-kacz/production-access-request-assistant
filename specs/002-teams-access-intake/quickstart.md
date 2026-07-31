# Quickstart: Validate Teams Access Request Intake

## Purpose

Use this guide after implementation to prove the Teams intake slice without weakening
the existing governed workflow. Automated validation requires neither a live model nor
a Teams tenant. A separate manual section validates the real Teams transport and
Adaptive Card presentation.

The behavioral contracts are:

- [data model](data-model.md)
- [agent proposal schema](contracts/request-intake-proposal.schema.json)
- [final card](contracts/prepared-request-card.json)
- [Teams activity behavior](contracts/teams-activity-contract.md)

## Prerequisites

For automated validation:

- .NET 10 SDK
- Node.js 24 with npm
- trusted local ASP.NET Core HTTPS development certificate

For the optional real Teams walkthrough:

- Microsoft 365 development tenant that permits custom app upload
- Azure Bot registration configured for the tenant
- public HTTPS development tunnel with a stable hostname
- Microsoft 365 Agents Toolkit or equivalent current app packaging tooling

No real production environment, corporate identity integration, or access provider is
used.

## Restore and Build

From the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
dotnet build ProductionAccessRequestAssistant.sln --no-restore
```

Expected:

- warnings-as-errors build succeeds;
- one executable Web host is produced;
- the React bundle still builds;
- no second agent, workflow, or hosting executable is introduced.

## Automated Test Suite

```powershell
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
dotnet test ProductionAccessRequestAssistant.sln --no-build
```

Expected:

- tests use `DeterministicChatClient` or another deterministic fake;
- no test needs a model credential, Teams tenant, Azure Bot, public tunnel, or live
  production system;
- all existing request, approval, provisioning, MCP, security, and UI tests continue
  passing.
- `POST /api/request-drafts/prepare`, request-creating `POST /api/requests`,
  `/requests/new`, and `createRequest` are absent.

## Scenario 1: Complete Request to Submission

Use the integration test host with a fake SDK-authenticated personal Teams activity.
Send a complete valid request:

```text
I need read-only access to PROD-ALPHA-EU to investigate INC-1042.
The justification is to diagnose customer-facing errors during the active incident.
```

Verify:

1. actor maps server-side to the fixed synthetic requester;
2. MAF receives only the three approved MCP tools;
3. deterministic validation succeeds before a final card is emitted;
4. the persisted prepared snapshot contains canonical scope, reserved request ID, and
   a 30-minute deadline;
5. the card exactly follows the contract, has no inputs, and has one action;
6. card text says confirmation does not approve or grant access;
7. confirmation creates one request with the reserved ID and
   `AwaitingBusinessApproval`;
8. the response links to `/requests/{requestId}` in the existing Web UI; and
9. no approval, provisioning operation, or grant exists yet.

## Scenario 2: Multi-turn Clarification

Send an incomplete initial message, then answer at least two focused questions. When
a clarification presents ordered environment or role choices, include one reply such
as `the first one` or `the other role`.

Verify after each turn:

- the complete accepted candidate snapshot remains in durable compact server state;
- prior questions and answers exist only in the bounded process-local MAF session;
- ordinal references resolve from that active history, while the resulting identifier
  is authoritatively canonicalized before persistence;
- the model proposal is schema-valid but remains untrusted;
- no final card appears while `RequestValidator` reports missing or invalid fields;
- no option list, raw message, model response, transcript, or serialized MAF session
  is persisted; and
- the final card appears only after all identifiers and relationships are
  authoritatively valid.

Then remove or evict the process-local session before replying to a clarification
with `the first one`.

Verify that the accepted candidate is retained, the ambiguous reply is not guessed,
and the application repeats a self-contained clarification in a fresh session.
Repeat with two active intakes and verify their histories never cross.

## Scenario 3: Immutable Card and Start-over

After receiving a final card, send a new request-intent message rather than clicking
the card.

Verify:

- the displayed snapshot is never edited;
- the old preparation becomes `Superseded`;
- the old card cannot submit;
- a new collecting conversation/preparation begins; and
- no access request was created by text.

## Scenario 4: Replay and Concurrency

Deliver the same `confirmAndSubmit` action repeatedly and concurrently.

Verify in 100% of tested deliveries:

- one `AccessRequest` exists;
- its ID equals the prepared `ReservedRequestId`;
- every accepted response returns that same ID;
- one request-created audit event exists;
- duplicate responses do not create duplicate audit history, approvals, operations,
  or grants.

## Scenario 5: Trust-boundary Negatives

Exercise each case with the integration test host:

- unauthenticated activity;
- non-Teams channel;
- Teams channel or group chat instead of personal chat;
- disallowed tenant;
- missing channel actor;
- card identity, requester, role, approver, duration, approval, or scope fields;
- foreign actor confirmation;
- conversation mismatch;
- unknown or malformed preparation reference;
- expired, superseded, or invalidated preparation;
- stale client/environment/role/incident relationship at confirmation;
- prompt requesting submit, approval, provisioning, validation bypass, or hidden data;
- missing or additional MCP tool in the discovered catalog;
- malformed structured model output;
- model or MCP timeout, cancellation, and dependency unavailability.

For every case verify:

- a safe typed outcome is returned;
- sensitive scope is concealed from a foreign actor;
- no unintended access request, approval, provisioning operation, or grant is created;
- forbidden tools remain absent from both MCP and MAF; and
- logs contain metadata rather than prompt, transcript, token, card, or full MCP
  payload content.

## Scenario 6: Existing Governed Workflow

Take one request submitted through the fake Teams path and continue in the existing
React application.

Verify:

1. configured business approver can approve only the matching client request;
2. DevOps approval cannot change role or duration;
3. successful provisioning creates one fixed eight-hour grant;
4. lost response/failure and authenticated retry use the existing behavior;
5. provisioning reloads persisted request and approval evidence; and
6. repeated provisioning converges on the request-ID keyed operation and grant.

Attempt the former browser creation paths and verify:

- `POST /api/request-drafts/prepare` returns not found;
- `POST /api/requests` is not an allowed creation method;
- neither call creates request or audit state;
- the React application has no New request navigation, list action, route, form, or
  creation capability; and
- request list/detail, business and DevOps decisions, retry, and audit presentation
  remain available.

## Manual Local Transport Check

Microsoft Agents Playground may be used to inspect basic activity routing and card
rendering against the local host:

```powershell
dotnet run --project src/GovernedAccess.Web --launch-profile https
agentsplayground -e https://localhost:7251/api/messages
```

Playground mode is not security acceptance evidence when it runs anonymously. The
automated fake-authenticated tests prove handler rules; the next section proves real
Teams token validation.

## Optional Real Teams Walkthrough

1. Create a single-tenant Azure Bot registration for the development tenant.
2. Put bot client secret in .NET user secrets or environment configuration; do not
   write it to tracked `appsettings*.json`.
3. Start the ASP.NET Core host.
4. Expose the host through a stable public HTTPS development tunnel.
5. configure the bot messaging endpoint as
   `https://<stable-host>/api/messages`;
6. fill the app manifest placeholders, keeping only `personal` bot scope;
7. validate/package the manifest and two icons;
8. sideload the package into the development tenant; and
9. complete Scenarios 1–3 in a personal Teams chat.

Verify in application logs that the endpoint authenticated the activity, derived the
tenant/actor/conversation binding, and produced correlation/timing/outcome metadata
without tokens or transcript content.

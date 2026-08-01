# Validation: Teams-Only Request Creation

**Validated**: 2026-07-29  
**Task range**: T049–T058

## Product boundary

Authenticated `confirmAndSubmit` handling for a server-owned Teams intake session is
the only executable request-creation path.

The Web application retains:

- requester/participant-filtered request list and detail;
- authenticated business decisions;
- authenticated DevOps decisions;
- protected provisioning retry;
- session switching; and
- audit-backed request presentation.

The Web application no longer exposes a browser request draft, request-creating POST,
new-request page/route/navigation/list action, creation DTO, or session creation
capability. Existing `AccessRequest`, approval, provisioning operation, grant, and
audit persistence remains unchanged.

## Server evidence

`TeamsOnlyRequestCreationTests` proves:

- a fake SDK-authenticated personal Teams preparation and confirmation creates exactly
  one immutable `AwaitingBusinessApproval` request with the reserved ID;
- the same shared save records exactly one `RequestCreated` audit event;
- `POST /api/request-drafts/prepare` returns not found;
- `POST /api/requests` is not an allowed creation method;
- rejected browser calls create no request or audit evidence;
- GET request list/detail remains callable; and
- business decision, DevOps decision, and retry endpoints remain mapped.

`ApiSecurityTests` pins the reduced unsafe endpoint inventory and continues proving
authentication, antiforgery, actor binding, exact approved scope, and fixed grant
lifetime for retained Web actions.

Downstream approval, query, provisioning, and retry fixtures create authoritative
domain requests directly rather than calling a removed public creation endpoint.
Teams confirmation tests retain exact immutable-scope, requester, audit, no-approval,
no-operation, and no-grant assertions.

## UI evidence

`AppSession.test.tsx` and `UiWiringSmoke.test.tsx` prove:

- no New request navigation or list-page action;
- no creation form or submission controls;
- no request-creation API call;
- requester sessions advertise no creation capability;
- the former new-request URL cannot render creation UI; and
- requester list/detail plus business and DevOps decision controls remain available.

## Removal searches

The production-source search for the following returned no matches:

```text
RequestDraftsController
ChatRequestDraftInterpreter
IRequestDraftInterpreter
DraftInterpretation
NewRequestPage
createRequest
/requests/new
request-drafts/prepare
```

Repository-wide matches are limited to negative characterization tests and
documentation that explicitly records the removed boundary. No production controller
maps request-creating `POST /api/requests`; the only POST remaining on
`AccessRequestsController` is protected provisioning retry.

## Automated gates

```text
dotnet build ProductionAccessRequestAssistant.sln --no-restore
Build succeeded. 0 warnings, 0 errors.

dotnet test ProductionAccessRequestAssistant.sln --no-build
GovernedAccess.UnitTests:        43 passed, 0 failed
GovernedAccess.IntegrationTests: 51 passed, 0 failed

npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
2 files passed; 6 tests passed, 0 failed
```

`git diff --check` reports no whitespace errors. The feature checklist remains fully
complete: 18 of 18 items checked.

## Test-suite simplification gate (T091-T095)

**Validated**: 2026-08-01

The suite now uses explicit levels:

- 54 Core unit cases;
- 52 direct component cases in the integration project;
- 23 retained cases marked `TestLevel=FullHost`; and
- 6 Vitest component cases.

Warnings-as-errors build, unit, component, full-host, and Vitest gates all pass with
no failures. Three uncontended warm no-build integration-project runs each executed
75 cases and completed in 23.901, 23.625, and 23.939 seconds. The 23.901-second median
meets the 25-second gate and improves the recorded 39-second baseline by about 39%.

Workflow decisions, retry-state rules, visibility/action capabilities, candidate
validation, utterance quality, and MAF history permutations now execute at their
lowest faithful unit or component boundary. Hosted coverage remains for
authentication, antiforgery, route availability, Problem Details/response contracts,
Activity Protocol and Adaptive Card translation, trusted-link rendering, production
composition, and Web-boundary logging. The feature
[test-simplification report](test-simplification.md) records the exhaustive baseline
inventory, trust-boundary mapping, per-run evidence, slowest cases, and justification
for all ten remaining complete-host instances.

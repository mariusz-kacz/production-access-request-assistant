# Validation: Teams Access Request Intake

## Teams-only request-creation gate (T049-T058)

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

## Test-suite simplification gate (T068-T072)

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

## Whole-system validation gate (T090)

- **Validated**: 2026-08-03
- **Environment**: Windows, .NET 10.0.10 test runtime, Node.js 24 toolchain
- **Live dependencies**: None; deterministic chat, synthetic identity/data, local
SQLite, and fake authenticated Teams activities only

### Restore, build, and test results

```text
dotnet restore ProductionAccessRequestAssistant.sln
PASS - all five projects restored.

npm ci --prefix src/GovernedAccess.Web/ClientApp
PASS - 120 packages installed from the lock file.

dotnet build ProductionAccessRequestAssistant.sln --no-restore -warnaserror
PASS - 0 warnings, 0 errors.

dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
PASS - 75 passed, 0 failed, 0 skipped; test duration 135 ms.

dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore
PASS - 106 passed, 0 failed, 0 skipped.

npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
PASS - 2 files and 6 tests passed; Vitest duration 3.34 seconds.
```

The first uncontended complete integration run finished in 43 seconds. The final
post-instrumentation run finished in 2 minutes 42 seconds after intermittent isolated
full-host startup delays; both ran the same 106 cases with no failure or skip. This
host-runner variability is not used as endpoint timing evidence. The previously
recorded T072 three-run measurement remains the controlled test-suite performance
baseline for the 75-case suite at that gate.

The initial sandboxed restore attempt could not reach NuGet (`NU1301`). Retrying with
network permission restored every project successfully; this was an execution-
environment restriction, not a package-resolution or solution failure.

### Contract validation

The release check parsed and asserted the source contracts directly:

- `request-intake-proposal.schema.json` is a closed object requiring exactly the
  proposal kind, complete candidate, and optional clarification fields;
- `prepared-request-card.json` has one `Action.Execute` named `confirmAndSubmit` with
  `associatedInputs: none`;
- the Teams manifest exposes one bot in `personal` scope and references `color.png`
  plus `outline.png`; and
- an offline package created from the explicit source allowlist contains exactly
  `manifest.json`, `color.png`, and `outline.png` at its ZIP root.

The passing `McpContractTests`, `MafToolBoundaryTests`, hosted card assertions, and
structured-output component tests additionally prove the executable MCP, model,
Activity Protocol, and Adaptive Card translations match those source contracts.

### Quickstart Scenarios 1-6

| Scenario | Executable evidence | Result |
|---|---|---|
| 1. Complete request to submission | `TeamsRequestPreparationTests` and `TeamsRequestConfirmationTests.FirstConfirmationAtomicallySubmitsExactScopeWithoutGrantingAccess` | PASS - canonical card, reserved ID, exact immutable request, trusted link, and no approval/grant side effect |
| 2. Multi-turn clarification | `TeamsClarificationTests.DirectAndOrdinalRepliesCarryCandidateUntilItIsReady` plus `MafConversationSessionStoreTests` | PASS - direct/ordinal history, candidate durability, restart-safe re-clarification, isolation, and serialized same-intake turns |
| 3. Immutable card and start-over | `RequestIntakeServiceTests.NewPreparationSupersedesReadyScopeBeforeCreatingAnotherSnapshot` and terminal aggregate tests | PASS - old ready scope remains immutable, becomes superseded, and text creates no request |
| 4. Replay and concurrency | `RequestIntakeConfirmationConcurrencyTests` plus hosted sequential replay assertion | PASS - repeated and concurrent confirmation converge on one request ID and one request-created audit event |
| 5. Trust-boundary negatives | `RequestIntakeConfirmationComponentTests`, `TeamsRequestConfirmationTests`, `MafRequestPreparationFailureTests`, `MafToolBoundaryTests`, MCP failure tests, and actor/route security coverage | PASS - malformed, foreign, expired, stale, cancelled, unavailable, and forbidden-tool paths fail closed without workflow side effects |
| 6. Existing governed workflow | `TeamsOnlyRequestCreationTests.TeamsConfirmationIsTheOnlyMappedRequestCreationPath` and `TeamsGovernedWorkflowTests.TeamsSubmittedRequestCompletesAuthenticatedGovernedWorkflow` | PASS - Teams-only creation, client-isolated business approval, exact DevOps scope, protected provisioning, fixed eight-hour grant, and persisted evidence |

### Deterministic confirmation timing

`TeamsIntakeLoggingTests` now reads the production `TeamsIntakeConfirmationCompleted`
structured `DurationMs` value and fails unless deterministic confirmation completes
in under 5,000 ms. The final 106-case integration run passed that assertion. The
metric covers the application confirmation handler, which reloads and revalidates the
ready intake and commits request/audit evidence without invoking the model or MCP.

Two focused uncontended checks provide conservative outer bounds:

- the real-SQLite `RepeatedConfirmationReturnsOneStableRequest` test completed its
  first confirmation, replay, and persistence assertions in the xUnit 1-second
  duration bucket; and
- the full-host `FirstConfirmationAtomicallySubmitsExactScopeWithoutGrantingAccess`
  test completed host setup, deterministic preparation, first confirmation, replay,
  response checks, and database assertions in the xUnit 4-second duration bucket.

Both are broader than the production `DurationMs` measurement and remain below the
five-second confirmation target at the recorded test-body granularity. They are local
deterministic acceptance evidence, not a production latency benchmark.

### Gate conclusion

T090 passes. Restore, warnings-as-errors build, all 187 .NET cases, all 6 Vitest cases,
contract checks, and Scenarios 1-6 completed without a functional failure. The
requirements checklist is 20 of 20 complete. Real-tenant installation and the
five-person comprehension review remain the separate T091 manual gate.

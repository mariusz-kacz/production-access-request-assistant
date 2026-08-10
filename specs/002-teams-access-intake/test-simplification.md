# Test Suite Simplification

**Gate**: Phase 6A (T068-T072)
**Baseline captured**: 2026-08-01

## Baseline

The pre-migration warm `--no-build` integration run contained **78 cases** and took
approximately **39 seconds**. Fifty cases completed in under 100 ms, while at least
16 classes paid all or part of the complete-host startup cost. The project mixed
full-host HTTP tests, direct adapter tests, real-SQLite component tests, and MAF/MCP
component tests under one undifferentiated integration label.

The baseline inventory below is exhaustive at class granularity; the case count
includes every `[Fact]` and every discovered `[Theory]` row.

| Project / class | Cases | Baseline level | Migration decision |
|---|---:|---|---|
| Unit / `AccessGrantTests` | 1 | Unit | Keep |
| Unit / `BusinessDecisionPolicyTests` | 3 | Unit | Expand business transition permutations here |
| Unit / `DevOpsDecisionPolicyTests` | 6 | Unit | Expand DevOps transition and immutable-scope permutations here |
| Unit / `RequestDraftAndSubmissionServiceTests` | 16 | Unit | Keep deterministic draft and submission policy coverage |
| Unit / `RequestPreparationTests` | 7 | Unit | Keep aggregate/proposal invariants |
| Unit / `RequestValidationTests` | 17 | Unit | Keep authoritative candidate validation permutations |
| Unit / `WorkflowEvidencePolicyTests` | 5 | Unit | Expand immutable workflow-evidence negatives here |
| Integration / `MafConversationSessionStoreSmokeTests` | 2 | Component | Keep direct native-store smoke coverage |
| Integration / `MafConversationSessionStoreTests` | 3 | Component | Keep direct history/concurrency/failure coverage |
| Integration / `MafRequestPreparationInterpreterSessionTests` | 4 | Component | Expand direct utterance/history matrix here |
| Integration / `AccessRequestWorkflowServiceTests` | 1 | FullHost | Convert to real-SQLite application component coverage |
| Integration / `BusinessDecisionTests` | 4 | FullHost | Retain representative authenticated overposting/response wiring; move transition permutations down |
| Integration / `DevOpsDecisionTests` | 5 | FullHost | Retain representative authenticated overposting/failure response wiring; move policy permutations down |
| Integration / `SessionControllerTests` | 3 | FullHost | Retain cookie, antiforgery, and session serialization boundary |
| Integration / `ProgramCompositionTests` | 7 | FullHost | Retain composition/startup evidence |
| Integration / `McpContractTests` | 3 | Component | Keep real MCP transport/contracts |
| Integration / `McpFailureTests` | 2 | Component | Keep direct typed failure/cancellation coverage |
| Integration / `TeamsIntakeLoggingTests` | 1 | FullHost | Retain Web-boundary structured logging evidence |
| Integration / `GovernedAccessDbContextModelTests` | 1 | Component | Keep real-SQLite model evidence |
| Integration / `RequestIntakePersistenceTests` | 3 | Component | Keep atomicity/concurrency evidence |
| Integration / `SyntheticDataSeederTests` | 1 | Component | Keep exact authoritative dataset evidence |
| Integration / `ProtectedProvisioningTests` | 5 | Component | Keep persisted-evidence reload and immutable-scope negatives |
| Integration / `RetryProvisioningTests` | 5 | FullHost | Move retry-state/evidence permutations to real-SQLite application components; retain one authenticated HTTP case |
| Integration / `RequestQueriesTests` | 4 | FullHost | Move participant visibility and action-capability permutations to real-SQLite application components |
| Integration / `TeamsOnlyRequestCreationTests` | 1 | FullHost | Retain sole-creation-route and retained-API wiring |
| Integration / `ApiSecurityTests` | 4 | FullHost | Retain authentication, antiforgery, overposting, and fallback boundaries |
| Integration / `TeamsCandidateValidationTests` | 6 | FullHost | Move candidate matrix and role discovery to direct application/MAF/MCP components |
| Integration / `TeamsClarificationTests` | 3 | FullHost | Retain one multi-turn transport-to-card scenario; move isolation/supersession history rules down |
| Integration / `TeamsConversationQualityTests` | 5 | FullHost | Move representative utterance matrix to direct MAF components |
| Integration / `TeamsRequestConfirmationTests` | 1 | FullHost | Retain complete transport/card/confirmation scenario |
| Integration / `TeamsRequestPreparationTests` | 4 | FullHost | Retain authentication and representative transport/card wiring only |
| Concurrency / `ProvisioningIdempotencyTests` | 1 | Component | Keep explicit high-contention SQLite/provider suite outside the solution |
| React / `AppSession.test.tsx` | 3 | Component | Keep session/route/navigation behavior |
| React / `UiWiringSmoke.test.tsx` | 3 | Component | Keep retained list/detail/approval/retry presentation contracts |

Baseline totals were **54 unit**, **78 integration-project**, **1 explicit
concurrency**, and **6 React component** cases.

## Trust-boundary coverage placement

| Requirement | Lowest faithful exhaustive coverage | Retained outer-boundary wiring evidence |
|---|---|---|
| Business approver responsibility, transition, duplicate decision, exact role | Unit policy/application tests | One authenticated, antiforgery-protected overposting HTTP case plus security inventory |
| DevOps identity, transition, exact business-approved role, fixed duration | Unit policy/application tests | One authenticated overposting HTTP case and one typed failure response |
| Retry authorization, failed-state restriction, persisted evidence, idempotency | Real-SQLite application/provisioning components | One non-DevOps HTTP rejection; complete security antiforgery inventory |
| Participant request visibility and available actions | Real-SQLite `AccessRequestQueryService` components | Representative GET contract and global unauthenticated API rejection |
| Immutable request/approval/operation/grant scope | Unit evidence policies plus real-SQLite provisioning components | `ApiSecurityTests` crafted-payload journey |
| Model proposal schema, utterance interpretation, history/restart semantics | Direct deterministic-chat/MAF components | One complete and one multi-turn hosted Teams transport-to-card scenario |
| Authenticated Teams actor and Activity Protocol mapping | Core/application components where possible | Hosted `/api/messages` authentication, actor mapping, route order, card serialization |
| MCP exact allowlist, typed schema, timeout/cancellation | Direct interpreter plus real lightweight MCP transport components | Hosted representative preparation proves production adapter wiring |
| Atomic confirmation, persistence concurrency, replay identity | Real-SQLite intake persistence components | Hosted complete prepare/confirm response and card contract |
| Browser authentication, antiforgery, overposting, response contracts | Not faithfully testable below HTTP | Retained full-host security and representative endpoint cases |
| Safe structured logging | Logger capture assertions | One hosted Web-boundary logging scenario |

No trust-boundary requirement is intentionally removed. Hosted cases are removed only
after an equal or stronger lower-level assertion exists and another retained hosted
case proves the production adapter invokes that policy or component.

## Final measurements

T072 completed on 2026-08-01 with these gates:

| Gate | Result |
|---|---|
| Warnings-as-errors solution build | PASS: 0 warnings, 0 errors |
| Core unit | PASS: 54/54 |
| Integration-project component | PASS: 52/52 |
| Retained full-host | PASS: 23/23 in 21 seconds |
| React/Vitest component | PASS: 6/6 in 2 files |

The post-migration integration project contains **75 cases**: 52 component and 23
full-host. Three uncontended warm `--no-build` project runs wrote per-test durations
to local TRX results and all passed:

| Run | Cases | Wall time |
|---|---:|---:|
| 1 | 75 | 23.901 s |
| 2 | 75 | 23.625 s |
| 3 | 75 | 23.939 s |
| **Median** | **75** | **23.901 s** |

The median meets the at-most-25-second gate and improves the recorded 39-second
baseline by approximately 39%. The slowest run-3 cases were the hosted logging
boundary (3.832 s), Teams-only route/creation boundary (3.308 s), and representative
business, DevOps-failure, startup, confirmation, dispatcher-scope, clarification, and
session TestServer cases (2.147-2.217 s each). All other run-3 cases were below that
range. The generated TRX files contain the duration for every individual case.

### Remaining complete-host startups

Ten complete-host instances remain per full integration-project run, each tied to a
boundary that a lower-level test cannot faithfully replace:

1. one shared default host serves business, DevOps, retry, query, and security HTTP
   classes so cookie authentication, antiforgery, controller serialization, and
   Problem Details are exercised without five separate startups;
2. one configurable-chat host proves authenticated preparation transport and
   candidate-response translation;
3. one history-sensitive host proves multi-turn Activity Protocol-to-card wiring;
4. four fresh composition hosts independently prove startup/seeding, scoped DbContext
   graph identity, root-safe dispatcher/agent lifetimes, and singleton MAF session
   infrastructure;
5. one confirmation host supplies a custom trusted Web origin and verifies the exact
   card/link/atomic-submission boundary;
6. one Teams-only-creation host combines route inventory, rejected browser creation,
   and successful Teams creation in isolated state; and
7. one observability host installs an isolated logger provider to prove safe metadata
   at the Web boundary.

The former hosted actor-resolver case moved to a direct component test; resolving JWT
options through a complete host had added about two minutes without improving the
resolver policy assertion.

## Coverage reconciliation

- Business and DevOps rejection, duplicate, actor, transition, exact-role, and audit
  permutations execute directly against policies/application services with SQLite;
  representative authenticated overposting and failure response cases remain hosted.
- Retry lost-response convergence, invalid-state rejection, and stored-operation
  mismatch execute as real-SQLite components; one hosted actor rejection remains.
- Participant visibility and action capabilities execute directly through
  `AccessRequestQueryService`; one hosted enriched-detail response contract remains.
- Candidate identifier and missing-field matrices execute through deterministic MAF,
  `RequestDraftService` and `RequestSubmissionService`, authoritative seeded context,
  and SQLite without a host.
- The five-scenario utterance matrix and intake-history isolation execute directly
  through MAF/native sessions. One complete hosted preparation and one multi-turn
  hosted clarification still prove Activity Protocol and Adaptive Card wiring.
- Existing unit evidence policies and real-SQLite protected-provisioning tests retain
  immutable request, approval, operation, and grant scope negatives.

No trust-boundary requirement listed in the baseline map was dropped.

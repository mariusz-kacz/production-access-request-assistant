# Testing Strategy

- **Status**: Current
- **Last reviewed**: 2026-09-02

## Principles

- Automated tests never require a live model, Teams tenant, Azure subscription, public
  tunnel, production system, or real provisioner.
- Deterministic policy belongs in Core unit tests.
- Framework, transport, persistence, authentication, and cross-boundary behavior uses
  the narrowest faithful component or full-host test.
- Each invariant has one canonical scenario matrix at the lowest layer capable of
  proving it. Higher layers prove only boundary behavior that lower layers cannot.
- A useful behavior change starts with the narrowest faithful failing test. After the
  change is green, production and test code are both refactored; temporary regression
  tests are not automatically permanent, and superseded tests are removed or merged.
- Negative state-changing outcomes assert both the response and the absence of
  unauthorized persisted or external side effects.
- UI visibility is never treated as server authorization evidence.
- Source shape and incidental implementation details are tested only when they are an
  explicit compatibility contract.
- Test counts and coverage percentages are diagnostics, not requirements.

## Test layers

| Layer | Project or command | Primary ownership |
|---|---|---|
| Unit | `GovernedAccess.UnitTests` | Domain construction, transitions, sparse-proposal contracts, grouped reduction, authorization policy, preparation lifecycle, and pure application behavior. |
| Component | `GovernedAccess.IntegrationTests` without full `Program` startup | Independent EF Core/SQLite modules, MCP transport, bounded MAF turns, Teams adapter components, provider coordination, and concurrency. |
| Full host | `GovernedAccess.IntegrationTests` with `GovernedAccessWebFactory` | Authentication, antiforgery, routing, middleware, serialization, SPA separation, and representative end-to-end journeys. |
| Frontend | Vitest and React Testing Library | Session wiring, request presentation, available actions, and restricted command payloads. |
| Live evaluation | Explicit `evaluate-live-model` command | Optional black-box natural-language outcome evidence after deterministic gates pass. |

`GovernedAccess.IntegrationTests` contains both component and full-host tests. The
project name does not require every test to start the complete application.

## Placement by concern

| Concern | Primary evidence |
|---|---|
| Request preparation reduction | Core unit tests for sparse operations, atomic scope grouping, justification independence, canonicalization, dependency cascades, clarification, and readiness. |
| Ready-draft discussion and revision | Core unit tests for identity preservation and supersession; Teams component tests for card behavior and responses. |
| Submission | Unit/component tests for actor binding, status, expiry, authoritative revalidation, one-save request creation, replay, and concurrency. |
| Business and DevOps decisions | Unit policy tests plus SQLite component tests for authenticated authority, decision order, duplicate transitions, exact scope, and audit evidence. |
| Provisioning and retry | Component tests for persisted-evidence reload, provider input, failed states, lost responses, idempotency, and one grant per request. |
| MCP | Contract and transport tests for the exact four-tool catalog, closed schemas, bounded search, exact environment and incident lookup, environment-scoped roles, typed failures, overflow, and cancellation. |
| MAF | Component tests for strict sparse-proposal parsing, exact four-tool catalog validation, discovery blocked after every exact environment outcome, fresh per-turn sessions, durable bounded turn envelopes, execution limits, and provider failures. |
| Browser security | Full-host tests for six demo identities, cookies, antiforgery, authorization, over-posting, participant filtering, and `/api`/`/mcp` SPA exclusion. |
| Teams transport | Full-host tests for authenticated personal activities, tenant/actor binding, reset, confirmation, safe failures, and one governed workflow. |
| Persistence | A compact module-dependency guard plus behavioral SQLite tests for independent reference/workflow ownership, usable initialization, constraints, transactions, restart, failure isolation, UTC conversion, and optimistic concurrency. |
| Frontend | Component tests for login/session behavior, list/detail rendering, approval and retry wiring, and absence of request creation. |
| Evaluation mode | Focused component tests for dataset contract loading, grading, timeouts, cancellation, isolated composition, artifact diagnostics, and zero workflow effects. |

Use one cohesive full-host scenario instead of repeating every policy variant through
HTTP. A full-host test is justified when it proves hosted authentication, routing,
serialization, middleware, or a cross-boundary composition that a lower layer cannot.

Evaluator component tests assert that the checked-in dataset covers the closed prompt
vocabulary, both environment-reference shapes, set/clear operations for every field,
all four tools, and every modeled trust channel. They replay the expected structured
proposals through Core to prove that each non-failure oracle reaches its declared
canonical outcome. Scenario-language quality remains the responsibility of the explicit
live evaluation.

## Deterministic dependencies

### Chat client

Tests replace the selected `IChatClient` with fixed-mode or scripted clients. They
return exact schema-shaped proposals and can simulate malformed output, cancellation,
timeout, unavailability, unknown tools, and multi-turn sequences. They are transport
and orchestration fakes, not evidence that natural-language interpretation is good.

### MCP

Contract tests use the real stateless Streamable HTTP endpoint against synthetic
SQLite data. Interpreter tests may host controlled catalogs to verify that missing,
additional, or non-read-only tools fail closed. No test exposes workflow or
provisioning capability to the model.

### Provisioner and time

The synthetic provisioner supports controlled success, failure, timeout, lost-response,
and concurrent outcomes. Tests replace `IClock`; state and audit assertions do not
depend on wall-clock timing or arbitrary delay.

## Required validation

Restore dependencies when required:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
npm ci --prefix src/GovernedAccess.Web/ClientApp
```

After a code change, run these backend gates sequentially and in this order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer timeout of at least four minutes. If it times
out, identify and stop only the runner process tree created by that command before
starting another run.

Run the frontend suite separately:

```powershell
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

During development, a focused test or filter may shorten feedback, but it does not
replace the final backend sequence. For documentation-only changes, validate links and
run `git diff --check`; execute code suites when an example changed or documentation
exposes a suspected implementation mismatch.

## Acceptance coverage

The credential-free suite must cover:

1. request preparation, clarification, confirmation, and immutable request creation;
2. ready-draft discussion, clarification preservation, completed replacement, rejected
   revision, stale card, and `/new` behavior;
3. malformed model output, MCP/provider failure, timeout, cancellation, and unchanged
   last-good session state;
4. exact four-tool MCP capability and contract boundaries;
5. authoritative client/environment/role/incident validation and structured option
   rendering;
6. six authenticated demo identities, client isolation, antiforgery, and over-posting
   resistance;
7. business approval, DevOps approval, provisioning, retry, audit, and logical expiry;
8. invalid transitions, replay, concurrency, and request-keyed idempotency; and
9. browser request-creation absence and retained register/decision behavior.

Teams agent component tests capture outbound `UpdateActivityAsync` calls and verify
that only actionable ready cards can be replaced. They also verify that authoritative
drift is visible in the successor card and that terminal submitted receipts are not
tracked or rewritten. Durable stale-card rejection remains the authorization control.

## Live-model evaluation

Live-provider evaluation is an explicit manual gate after the credential-free suite.
It may consume provider quota and requires an authorized developer identity. CI and
routine validation must not invoke it automatically.

The versioned English-only
[`deterministic-intake-v1.json`](../src/GovernedAccess.Web/Evaluation/Datasets/deterministic-intake-v1.json)
is the golden source for executable evaluation inventory and exact expectations. It is
capability-driven rather than count-driven; the current version declares 14 promoted
groups, no advisory groups, 42 variations, and 43 turns. Every promoted group and every
variation within it must reach a declared safe canonical outcome, and all universal
safety gates must pass across the entire run. Every run requires zero requests,
decisions, operations, and grants.

The promoted 2026-08-28 run passed all 12 promoted groups and both advisory groups
without selective reruns or waivers. It used dataset `deterministic-intake-2.0.2`
(`bc9ca80e1a17895f13dcefb78a7f4cf3d611d5f6ffba90037a76cfba4501ba0c`), prompt
contract `3.0.6`, proposal/MCP contracts `3.0.0`, and search policy `2.0.0`; all
consequential side-effect counts were zero. Its schema-version-4 artifacts were
generated in the gitignored output location and were not committed. An older artifact
from the retired evaluator was removed and remains available in Git history.

That run remains historical evidence for its recorded dataset and prompt versions. A
retained 2026-08-31 clean-source full-inventory run passed all 14 groups and 42
variations with absolute safety PASS and zero consequential side effects. It covered
the current `deterministic-intake-3.1.0` bytes, and its recorded source commit matched
the clean evaluated `HEAD` during retention review. It is current-dataset, clean-source
promotion evidence for the recorded provider, prompt, proposal, MCP, and search-policy
versions.

The command cannot replace deterministic schema, authorization, persistence,
side-effect, concurrency, or failure-path assertions. Configuration, execution,
artifacts, and cleanup are documented in the
[live-model evaluation guide](live-model-evaluation.md).

## Adding tests

- Prefer extending the canonical matrix that owns an invariant over creating a new
  standalone regression test.
- Put pure deterministic rules in the unit project.
- Use component tests for EF Core, MAF, MCP, SDK adapters, provider coordination, and
  concurrency.
- Add a full-host test only for hosted behavior or a representative cross-boundary
  journey.
- Remove or merge temporary tests once a stronger canonical scenario proves the same
  defect. Do not retain a test solely because it participated in a red/green cycle.
- Do not assert exact property, enum, subclass, migration, table, seed, DI, copy, or
  layout inventories unless the detail is a supported versioned contract.
- Assert persisted state and audit evidence alongside success or failure responses;
  rejected state changes must also prove that no unauthorized side effect occurred.
- Include unauthorized, invalid-state, duplicate, and cancellation cases with new
  state-changing behavior.
- Preserve caller cancellation in fakes.
- Prefer deterministic clocks and synchronization points over `Task.Delay`.
- Keep identities, context, and grants synthetic.

Browser end-to-end automation, visual regression, penetration testing, dependency or
container scanning, enterprise load testing, deployment smoke tests, disaster recovery,
and real identity/provider contracts are outside the automated local suite. They become
required when the corresponding integration or deployment becomes real.

# Testing Strategy

- **Status**: Current
- **Last reviewed**: 2026-08-10

## Principles

- Automated tests never require a live model, Teams tenant, Azure subscription, public
  tunnel, production system, or real provisioner.
- Deterministic policy belongs in Core unit tests.
- Framework, transport, persistence, authentication, and cross-boundary behavior uses
  the narrowest faithful component or full-host test.
- Negative outcomes must assert both the response and absence of unauthorized persisted
  side effects.
- UI visibility is never treated as server authorization evidence.
- Test counts are diagnostic and are not maintained as documentation requirements.

## Test layers

| Layer | Project or command | Primary ownership |
|---|---|---|
| Unit | `GovernedAccess.UnitTests` | Domain construction, transitions, validation policy, authorization policy, candidate lifecycle, and pure application behavior. |
| Component | `GovernedAccess.IntegrationTests` without full `Program` startup | EF Core/SQLite, MCP transport, MAF sessions, Teams adapter components, provider coordination, and concurrency. |
| Full host | `GovernedAccess.IntegrationTests` with `GovernedAccessWebFactory` | Authentication, antiforgery, routing, middleware, serialization, SPA separation, and representative end-to-end journeys. |
| Frontend | Vitest and React Testing Library | Session wiring, request presentation, available actions, and restricted command payloads. |
| Live evaluation | Explicit `evaluate-live-model` command | Optional black-box natural-language outcome evidence after deterministic gates pass. |

`GovernedAccess.IntegrationTests` contains both component and full-host tests. The
project name does not require every test to start the complete application.

## Placement by concern

| Concern | Primary evidence |
|---|---|
| Request candidate validation | Core unit tests for canonicalization, field clearing, incident compatibility, assigned roles, and readiness. |
| Ready-draft discussion and revision | Core unit tests for identity preservation and supersession; Teams component tests for card behavior and responses. |
| Submission | Unit/component tests for actor binding, status, expiry, authoritative revalidation, one-save request creation, replay, and concurrency. |
| Business and DevOps decisions | Unit policy tests plus SQLite component tests for authenticated authority, decision order, duplicate transitions, exact scope, and audit evidence. |
| Provisioning and retry | Component tests for persisted-evidence reload, provider input, failed states, lost responses, idempotency, and one grant per request. |
| MCP | Contract and transport tests for the exact two-tool catalog, closed schemas, bounded discovery, exact lookup, embedded roles, typed failures, overflow, and cancellation. |
| MAF | Component tests for strict proposal parsing, session isolation, same-intake serialization, restart behavior, successful-save semantics, and provider failures. |
| Browser security | Full-host tests for six demo identities, cookies, antiforgery, authorization, over-posting, participant filtering, and `/api`/`/mcp` SPA exclusion. |
| Teams transport | Full-host tests for authenticated personal activities, tenant/actor binding, reset, confirmation, safe failures, and one governed workflow. |
| Persistence | EF model and component tests for relationships, unique constraints, UTC conversion, optimistic concurrency, and exact synthetic seeding. |
| Frontend | Component tests for login/session behavior, list/detail rendering, approval and retry wiring, and absence of request creation. |
| Evaluation mode | Command tests for dataset validation, exact scenario selection, grading, timeouts, cancellation, temporary-database cleanup, route isolation, and zero workflow effects. |

Use one cohesive full-host scenario instead of repeating every policy variant through
HTTP. A full-host test is justified when it proves hosted authentication, routing,
serialization, middleware, or a cross-boundary composition that a lower layer cannot.

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
4. exact two-tool MCP capability and contract boundaries;
5. authoritative client/environment/role/incident validation and structured option
   rendering;
6. six authenticated demo identities, client isolation, antiforgery, and over-posting
   resistance;
7. business approval, DevOps approval, provisioning, retry, audit, and logical expiry;
8. invalid transitions, replay, concurrency, and request-keyed idempotency; and
9. browser request-creation absence and retained register/decision behavior.

Direct capture of the outbound Teams `UpdateActivityAsync` call remains tracked by
feature task T096. Durable stale-card rejection is already covered and remains the
authorization control; the pending test concerns presentation behavior only.

## Live-model evaluation

Live-provider evaluation is an explicit manual gate after the credential-free suite.
It may consume provider quota and requires an authorized developer identity. CI and
routine validation must not invoke it automatically.

The fixed 20-scenario dataset grades only the final normalized application outcome and
declared final facts. It does not inspect prompts, transcripts, tool order, provider
iterations, raw payloads, or token use. A full run requires 20 of 20; a focused run
requires 1 of 1. Both require zero requests, decisions, operations, and grants.

The command cannot replace deterministic schema, authorization, persistence,
side-effect, concurrency, or failure-path assertions. Configuration, execution,
artifacts, and cleanup are documented in the
[live-model evaluation guide](live-model-evaluation.md).

## Adding tests

- Put pure deterministic rules in the unit project.
- Use component tests for EF Core, MAF, MCP, SDK adapters, provider coordination, and
  concurrency.
- Add a full-host test only for hosted behavior or a representative cross-boundary
  journey.
- Assert persisted state and audit evidence alongside success or failure responses.
- Include unauthorized, invalid-state, duplicate, and cancellation cases with new
  state-changing behavior.
- Preserve caller cancellation in fakes.
- Prefer deterministic clocks and synchronization points over `Task.Delay`.
- Keep identities, context, and grants synthetic.

Browser end-to-end automation, visual regression, penetration testing, dependency or
container scanning, enterprise load testing, deployment smoke tests, disaster recovery,
and real identity/provider contracts are outside the automated local suite. They become
required when the corresponding integration or deployment becomes real.

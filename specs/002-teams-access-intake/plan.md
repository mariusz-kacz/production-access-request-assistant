# Implementation Plan: Teams Access Request Intake

**Branch**: `evolution/maf-request-intake` | **Date**: 2026-07-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-teams-access-intake/spec.md`

## Summary

Add one Teams personal-chat adapter to the existing ASP.NET Core executable. The
Microsoft 365 Agents SDK authenticates and routes Teams activities; one Microsoft
Agent Framework `ChatClientAgent` interprets each developer turn and can call only the
existing three read-only MCP tools. Application-owned services persist a compact
candidate, determine readiness using the existing authoritative validator, and create
an immutable 30-minute prepared snapshot with a reserved request ID.

The server renders that snapshot as an Adaptive Card with one **Confirm and submit**
action. Confirmation is a deterministic authenticated channel command, not a
model-visible tool or MAF approval continuation. It reloads and revalidates the
prepared snapshot, atomically creates the existing immutable access request under its
reserved ID, and converges retries on that ID. The React approval and provisioning
workflow remains unchanged.

## Technical Context

**Language/Version**: C# 14 on .NET 10; existing TypeScript and React 19.2 client remains unchanged

**Primary Dependencies**: Existing ASP.NET Core 10, EF Core 10 SQLite,
`Microsoft.Extensions.AI`, `ModelContextProtocol` 1.4, and `System.Text.Json`; add
the current stable `Microsoft.Agents.AI` 1.15.0 and
`Microsoft.Agents.Hosting.AspNetCore` 1.6.150 packages with exact pins. Use the
existing Adaptive Card activity contract without adding a UI framework or a second
agent-hosting protocol.

**Storage**: Existing local SQLite database through EF Core; add conversations and
prepared snapshots to the same `DbContext` and transaction boundary

**Testing**: Existing xUnit unit and integration projects, ASP.NET Core
`WebApplicationFactory`, SQLite in-memory databases, deterministic fake
`IChatClient`, fake authenticated Teams activities/adapter context, controllable
clock, contract fixtures, and the unchanged Vitest suite

**Target Platform**: One cross-platform ASP.NET Core host; `/api/messages` is exposed
to Microsoft Teams through an authenticated Azure Bot registration and HTTPS endpoint,
while the host continues serving `/api`, `/mcp`, and the React bundle

**Project Type**: Single deployable modular web application with Teams as an
additional inbound adapter

**Performance Goals**: Complete deterministic confirmation within the Teams invoke
response window (target under 5 seconds); preserve the 30-second model and 5-second
MCP deadlines; reach a final request within five developer messages for at least 90%
of representative test utterances

**Constraints**: Personal Teams chat only; one fixed synthetic requester mapping;
exactly three read-only model-visible MCP tools; no live model in tests; no raw
conversation transcript persistence or logging; no model-visible submit, approval,
workflow, retry, provisioning, or revocation action; fixed eight-hour grant; prepared
snapshot expires after 30 minutes; cancellation crosses asynchronous boundaries

**Scale/Scope**: Portfolio-grade local demonstration, one active preparation per
authenticated Teams actor and personal conversation, existing two clients/two
environments/two roles, no proactive messages, background worker, distributed cache,
queue, Slack channel, or real identity integration

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

| Gate | Pre-research result | Design evidence |
|---|---|---|
| **Human authority** | PASS | Conversation turns can update only preparation state. The sole new workflow-affecting action is an authenticated requester confirmation handled by `PreparedRequestConfirmationService`; business and DevOps approvals remain the existing human actions. |
| **AI and MCP boundary** | PASS | MAF and Microsoft 365 Agents SDK types remain in Web adapters. The Core port accepts provider-neutral turn input and returns a closed proposal. The adapter verifies the MCP catalog equals the three existing tools before passing only those tools to MAF. |
| **Scope integrity** | PASS | Deterministic validation creates a server-owned immutable prepared snapshot with a reserved request ID and exact scope. Confirmation reloads and revalidates it; corrections supersede it and require a new preparation. |
| **Provisioning evidence** | PASS | No provisioning code or contract changes. MAF and Teams receive no provisioning capability; the existing protected service continues accepting only the immutable request ID and reloading persisted approvals and operations. |
| **Proportionality** | PASS | No new project, executable, agent protocol endpoint, workflow engine, MAF workflow, multi-agent design, queue, cache, or background service. New code fits the existing Core/Web/Persistence boundaries. |
| **Verification and operations** | PASS | Unit and integration coverage includes identity/conversation binding, schema failures, exact tool allowlisting, expiry, supersession, stale context, atomic/idempotent replay, cancellation/timeouts, and forbidden actions. Logs contain identifiers, timing, and outcomes, not prompts or transcripts. |

**Post-design re-check**: PASS. The data model, MAF proposal schema, Teams activity
contract, and card contract keep model output outside readiness and authorization.
Confirmation and request creation share one SQLite save boundary, and all downstream
approval/provisioning rules are reused without channel-specific exceptions.

## Project Structure

### Documentation (this feature)

```text
specs/002-teams-access-intake/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── request-intake-proposal.schema.json
│   ├── prepared-request-card.json
│   └── teams-activity-contract.md
└── tasks.md                         # Created later by /speckit-tasks
```

### Source Code (repository root)

```text
ProductionAccessRequestAssistant.sln
src/
├── GovernedAccess.Core/
│   ├── Domain/
│   │   ├── RequestPreparationConversation.cs
│   │   └── PreparedAccessRequest.cs
│   ├── Application/
│   │   ├── RequestPreparationService.cs
│   │   └── PreparedRequestConfirmationService.cs
│   └── Ports/
│       ├── RequestDrafting.cs       # Evolved provider-neutral turn contract
│       └── RequestIntake.cs
├── GovernedAccess.Mcp/
│   ├── RequestContextTools.cs       # Unchanged three-tool surface
│   └── McpRegistration.cs
└── GovernedAccess.Web/
    ├── Ai/
    │   ├── MafRequestPreparationInterpreter.cs
    │   └── DeterministicChatClient.cs
    ├── Teams/
    │   ├── TeamsAccessRequestAgent.cs
    │   ├── TeamsActorResolver.cs
    │   ├── PreparedRequestCardFactory.cs
    │   └── TeamsAgentRegistration.cs
    ├── Persistence/
    │   ├── GovernedAccessDbContext.cs
    │   └── EfRequestIntakeStore.cs
    ├── appPackage/
    │   ├── manifest.json
    │   ├── color.png
    │   └── outline.png
    ├── ClientApp/                   # Existing UI and routes retained
    └── Program.cs
tests/
├── GovernedAccess.UnitTests/
│   ├── RequestPreparationTests.cs
│   └── PreparedRequestConfirmationTests.cs
└── GovernedAccess.IntegrationTests/
    ├── Ai/
    ├── Mcp/
    ├── Persistence/
    └── Teams/
```

**Structure Decision**: Keep all provider-neutral preparation entities, transitions,
typed outcomes, validation coordination, and confirmation policy in
`GovernedAccess.Core`. Keep Microsoft 365 Agents SDK, MAF, Activity Protocol, Adaptive
Card, and MCP client types in `GovernedAccess.Web`. Reuse `GovernedAccess.Mcp` without
adding tools. Both `IRequestIntakeStore` and the existing `IWorkflowStore` are backed
by the same scoped `GovernedAccessDbContext`, allowing prepared confirmation, request
creation, and audit evidence to commit atomically. `GovernedAccess.Web` remains the
only executable.

## Implementation Boundaries

- The Microsoft 365 Agents SDK owns authenticated Activity Protocol ingress and
  replies. It does not become an authorization service or a domain-state store.
- `TeamsActorResolver` accepts only authenticated `msteams` personal activities from
  the configured tenant, derives the stable actor binding from verified tenant and
  user identifiers, and maps it to `DemoPrincipalKeys.Requester`. Payload-supplied
  identities, roles, approvers, and claims are ignored.
- Actor ownership uses the authenticated channel actor binding in addition to the
  fixed synthetic requester ID. Mapping all users to `requester` must not allow one
  Teams user to confirm another user's snapshot.
- `MafRequestPreparationInterpreter` constructs one bounded `ChatClientAgent` invocation
  from the current compact candidate and latest message. MAF owns the model/tool loop,
  but its session, response, and history are not authoritative application state.
- The interpreter lists the loopback MCP catalog, requires exact equality with
  `get_production_environment`, `get_incident`, and `get_available_roles`, and passes
  only those `McpClientTool` instances to MAF. Missing or extra tools fail closed.
- The model response must match
  `contracts/request-intake-proposal.schema.json`. Candidate identifiers and
  relationships are reloaded and checked by `RequestValidator`; only deterministic
  validation can create a prepared snapshot.
- The application persists only the compact current candidate and pending
  clarification needed for the active conversation. It clears that content after
  submission, supersession, or expiry. Structured logs provide pre-submission
  observability; immutable prepared/request evidence provides replay safety and
  durable audit.
- A ready snapshot is rendered only from persisted server fields. The card contains no
  inputs and its sole action carries only schema version and opaque preparation
  reference.
- Adaptive Card `Action.Execute` invokes the deterministic confirmation handler
  directly. It is human-in-the-loop at the application level but deliberately is not
  a MAF approval-required function: submission is never a model capability.
- Confirmation reloads the snapshot, verifies actor and conversation binding, applies
  lazy expiry/supersession checks, revalidates current authoritative context, and
  creates the existing `AccessRequest` with the reserved ID and exact prepared scope.
- Refactor the request-creation internals so browser submission still receives a fresh
  server ID while prepared confirmation can supply only its server-reserved ID.
  Principal checks, validation, immutable construction, and request-created audit
  logic are shared rather than duplicated.
- Confirmation status, immutable request, and request-created audit commit in one
  `SaveChangesAsync`. A unique reserved request ID plus optimistic
  concurrency makes duplicate or concurrent delivery reload and return the same
  request ID.
- The existing `/requests/{requestId}` React route is the only post-submission link.
  Its origin comes from trusted configuration, never from an incoming activity URL.
- `/api/messages` is mapped before `/api` and SPA fallbacks. Non-development hosting
  requires SDK token validation. Local automated tests use a fake authenticated
  channel boundary; Microsoft Agents Playground is a transport/UX aid, not evidence
  that production authentication passed.
- No Teams approval notifications, proactive messages, request editing, approval
  cards, workflow status cards, or provisioning cards are introduced.

## Complexity Tracking

No constitution violations require justification.

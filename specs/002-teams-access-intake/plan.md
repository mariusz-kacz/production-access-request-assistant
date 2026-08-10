# Implementation Plan: Teams Access Request Intake

**Branch**: `evolution/maf-request-intake` | **Date**: 2026-07-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-teams-access-intake/spec.md`

## Summary

Add one Teams personal-chat adapter to the existing ASP.NET Core executable. The
Microsoft 365 Agents SDK authenticates and routes Teams activities; one Microsoft
Agent Framework `ChatClientAgent` interprets each developer turn, uses MAF's native
in-memory session store for process-local history, and can call only the existing
two read-only MCP tools. Environment context includes the roles assigned to each
environment. Application-owned services persist the compact typed
candidate, provide it to each turn as current state, determine readiness using the
existing validator, and create an immutable 30-minute prepared snapshot with a
reserved request ID. Clarification wording and conversational references live only
in the in-memory MAF session; bounded environment option identifiers are validated
per turn but neither options nor transcripts are written to the application database.

While a ready draft is active, the same interpretation path answers questions without
changing the immutable candidate. A deterministically assessed candidate difference
supersedes that snapshot and creates a replacement preparation. The Teams adapter
tracks the latest card activity only as process-local presentation metadata so it can
make an old card visibly non-actionable; durable status validation remains decisive.

The server renders that snapshot as an Adaptive Card with one **Confirm and submit**
action. Confirmation is a deterministic authenticated channel command, not a
model-visible tool or MAF approval continuation. It reloads and revalidates the
prepared snapshot, atomically creates the existing immutable access request under its
reserved ID, and converges retries on that ID. The React approval and provisioning
workflow remains unchanged.

## Technical Context

**Language/Version**: C# 14 on .NET 10; existing TypeScript and React 19.2 client remains unchanged

**Primary Dependencies**: Existing ASP.NET Core 10, EF Core 10 SQLite,
`Microsoft.Extensions.AI`, `ModelContextProtocol` 1.4, and `System.Text.Json`; retain
the existing pinned `Microsoft.Agents.AI` 1.15.0 and add its matched preview
`Microsoft.Agents.AI.Hosting` 1.15.0-preview.260722.1 for `AgentSessionStore`, and
`Microsoft.Agents.Hosting.AspNetCore` 1.6.150 packages with exact pins. Use the
existing Adaptive Card activity contract without adding a UI framework or a second
agent-hosting protocol.

**Storage**: Existing local SQLite database through EF Core; one
`RequestIntakeSessions` table durably stores the typed candidate and shares the
existing `DbContext` and save boundary with requests and audit events. MAF
conversation sessions are retained by the native `InMemoryAgentSessionStore` for the
process lifetime and are never written to SQLite

**Testing**: Existing xUnit unit and integration projects, ASP.NET Core
`WebApplicationFactory`, SQLite in-memory databases, deterministic fake
`IChatClient`, fake authenticated Teams activities/adapter context, controllable
clock, contract fixtures, focused Teams-only UI characterization, and the existing
Vitest suite

**Target Platform**: One cross-platform ASP.NET Core host; `/api/messages` is exposed
to Microsoft Teams through an authenticated Azure Bot registration and HTTPS endpoint,
while the host continues serving `/api`, `/mcp`, and the React bundle

**Project Type**: Single deployable modular web application with Teams confirmation
as the sole request-creation adapter and Web as the retained register/decision surface

**Performance Goals**: Complete deterministic confirmation within the Teams invoke
response window (target under 5 seconds); preserve one 100-second request-safety
deadline on the Teams endpoint covering MCP and model work; reach a ready request draft within
five developer messages for at least 90%
of representative test utterances

**Constraints**: Personal Teams chat only; one fixed synthetic requester mapping;
exactly two read-only model-visible MCP tools; no live model in tests;
process-local MAF history with no application-database transcript persistence or
logging; safe re-clarification after restart-related history loss; no model-visible
submit, approval, workflow, retry, provisioning, or revocation action; fixed
eight-hour grant; prepared snapshot expires after 30 minutes; cancellation crosses
asynchronous boundaries

**Scale/Scope**: Portfolio-grade local demonstration, one active preparation per
authenticated Teams actor and personal conversation, and one process-local MAF
session per created intake retained until process termination; existing two
clients/two environments/two roles; no proactive messages, background worker,
distributed cache, queue, Slack channel, durable agent-session store, or real identity
integration

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

| Gate | Pre-research result | Design evidence |
|---|---|---|
| **Human authority** | PASS | Conversation turns can update only intake state through `RequestDraftService`. Authenticated requester confirmation and immutable request creation are handled deterministically by `RequestSubmissionService`; business and DevOps approvals remain explicit authenticated Web actions. |
| **AI and MCP boundary** | PASS | MAF and Microsoft 365 Agents SDK types remain in Web adapters. The Core port accepts provider-neutral turn input and returns a closed proposal. The adapter verifies the MCP catalog equals `get_production_environment` and `get_incident` before passing only those tools to MAF; assigned roles arrive within authoritative environment context. |
| **Scope integrity** | PASS | Deterministic validation makes the ready scope of one `RequestIntakeSession` immutable with a reserved request ID. Confirmation reloads and revalidates it; corrections supersede it and require a new preparation. |
| **Provisioning evidence** | PASS | No provisioning code or contract changes. MAF and Teams receive no provisioning capability; the existing protected service continues accepting only the immutable request ID and reloading persisted approvals and operations. |
| **Proportionality** | PASS | No new project, executable, agent protocol endpoint, workflow engine, MAF workflow, multi-agent design, distributed cache, queue, or background service. The native in-memory MAF session store replaces the custom cache; one exact per-intake gate protects the mutable session without application-owned eviction machinery. |
| **Verification and operations** | PASS | Unit and integration coverage includes identity/history isolation, restart-equivalent history loss and safe re-clarification, same-intake turn serialization, schema failures, exact tool allowlisting, expiry, supersession, stale context, atomic/idempotent replay, cancellation/timeouts, and forbidden actions. Logs contain identifiers, timing, and outcomes, not prompts or transcripts. |

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
│   │   ├── Drafts/
│   │   │   └── RequestIntakeSession.cs
│   │   ├── ReferenceData/
│   │   └── AccessRequests/
│   │       ├── Approvals/
│   │       ├── Provisioning/
│   │       └── Auditing/
│   ├── Application/
│   │   ├── Drafts/
│   │   │   ├── RequestDraftService.cs
│   │   │   └── RequestDraftValidator.cs
│   │   ├── AccessRequests/
│   │   │   ├── RequestSubmissionService.cs   # Deterministic confirmation boundary
│   │   │   ├── AccessRequestValidator.cs
│   │   │   ├── AccessRequestWorkflowService.cs
│   │   │   ├── AccessRequestCommandContextLoader.cs
│   │   │   ├── AccessRequestQueryService.cs
│   │   │   └── AccessRequestVisibilityPolicy.cs
│   │   └── Provisioning/
│   │       └── ProtectedProvisioningService.cs
│   └── Ports/
│       ├── RequestDrafting.cs       # Evolved provider-neutral turn contract
│       └── RequestIntake.cs
├── GovernedAccess.Mcp/
│   ├── RequestContextTools.cs       # Exact two-tool read-only surface
│   └── McpRegistration.cs
└── GovernedAccess.Web/
    ├── Ai/
    │   ├── MafRequestPreparationInterpreter.cs
    │   ├── MafConversationTurnCoordinator.cs
    │   └── DeterministicChatClient.cs
    ├── Teams/
    │   ├── TeamsAccessRequestAgent.cs
    │   ├── TeamsActorResolver.cs
    │   ├── PreparedRequestCardFactory.cs
    │   ├── TeamsDraftCardTracker.cs
    │   └── TeamsAgentRegistration.cs
    ├── Persistence/
    │   ├── GovernedAccessDbContext.cs
    │   └── EfRequestIntakeStore.cs
    ├── appPackage/
    │   ├── manifest.json
    │   ├── color.png
    │   └── outline.png
    ├── ClientApp/                   # List/detail/decision/retry; no creation UI
    └── Program.cs
tests/
├── GovernedAccess.UnitTests/
│   └── RequestDraftAndSubmissionServiceTests.cs
└── GovernedAccess.IntegrationTests/
    ├── Mcp/
    ├── Persistence/
    ├── Requests/TeamsOnlyRequestCreationTests.cs
    └── Teams/
```

**Structure Decision**: Keep the one provider-neutral intake aggregate, transitions,
compact typed outcomes, validation coordination, and confirmation policy in
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
- `MafRequestPreparationInterpreter` uses an `AIHostAgent` backed by the native
  `AgentSessionStore` to get or create one process-local MAF session for the active
  intake, supplies the canonical current candidate and latest
  application validation feedback as run-scoped context, and invokes the
  `ChatClientAgent` with the latest message. MAF owns conversational continuity and
  the model/tool loop, but its response and history are not authoritative application
  state.
- The singleton `InMemoryAgentSessionStore` keys sessions with the server-generated
  intake ID and retains them until process termination. A small application
  coordinator retains one exact async gate per intake for the same process lifetime
  and serializes the store/load, agent run, and store/save sequence. There is no
  custom session cache, inactivity eviction, turn-count limit, terminal cleanup, or
  stale-entry retry loop in the current baseline.
- Successfully saved MAF sessions supply their prior conversation messages directly
  on later turns. No application marker or separate history-availability field is
  added to the model context. Failed or cancelled runs and malformed proposals are
  not saved.
- MAF exposes explicit session deletion, but the current process-local baseline does
  not invoke it. A later durable-store requirement will define retention and terminal
  deletion policy before wiring that lifecycle operation.
- On session loss after restart, the interpreter receives the persisted current
  candidate but no prior conversation messages. Relative answers such as "the first
  one" must produce a repeated focused clarification unless the supplied conversation
  itself contains the preceding question and ordering.
- The interpreter lists the loopback MCP catalog, requires exact equality with
  `get_production_environment` and `get_incident`, and passes only those
  `McpClientTool` instances to MAF. Environment results include assigned roles.
  Missing or extra tools fail closed.
- The model response must match
  `contracts/request-intake-proposal.schema.json`: a complete nullable candidate
  snapshot and either one typed clarification target, bounded message, and bounded
  environment option identifiers or no clarification. Candidate and option
  identifiers and relationships are reloaded and checked by the application; only
  deterministic validation can create a prepared snapshot.
- After deterministic canonicalization accepts a collecting proposal, its complete
  candidate snapshot replaces the prior candidate. A `null` field means absent or
  cleared; it is not an instruction to preserve an older value.
- The application database persists only the compact current candidate. MAF's native
  in-memory store retains the serialized session for the process lifetime; it is not
  written to SQLite, logged, or treated as domain evidence. The next model turn uses
  the active process-local history for references and the durable candidate for
  canonical synchronization. Structured logs provide
  pre-submission observability; immutable prepared/request evidence provides replay
  safety and durable audit.
- A ready snapshot is rendered only from persisted server fields. The card contains no
  inputs, hides the reserved request ID until submission, and its sole action carries
  only schema version and opaque preparation reference.
- A question or hypothetical received while that snapshot is ready is returned as a
  `DraftDiscussion` only when deterministic assessment confirms the complete candidate
  is unchanged. A different assessed candidate supersedes the old immutable snapshot,
  creates a new intake identity, and is persisted as ready, incomplete, or rejected.
- `TeamsDraftCardTracker` retains only the latest sent preparation/activity reference
  for one authenticated actor and conversation. It lets the adapter change a replaced
  card to **Draft being revised** and send a separate review card. Tracker loss or an
  activity-update failure affects presentation only; confirmation reloads durable
  intake status and rejects stale identifiers.
- Adaptive Card `Action.Execute` invokes the deterministic confirmation handler
  directly. It is human-in-the-loop at the application level but deliberately is not
  a MAF approval-required function: submission is never a model capability.
- Confirmation reloads the snapshot, verifies actor and conversation binding, applies
  lazy expiry/supersession checks, revalidates current authoritative context, and
  creates the existing `AccessRequest` with the reserved ID and exact prepared scope.
- Teams confirmation is the only request-creation path. `RequestSubmissionService`
  owns the complete deterministic confirmation boundary: it reloads and authorizes
  the ready draft, requires its server-reserved request ID, revalidates scope and
  requester, stages the immutable request and request-created audit event, marks the
  draft submitted, and saves the complete transition atomically.
- Confirmation status, immutable request, and request-created audit commit in one
  `SaveChangesAsync`. A unique reserved request ID plus optimistic
  concurrency makes duplicate or concurrent delivery reload and return the same
  request ID.
- The existing `/requests/{requestId}` React route is the only post-submission link.
  Its origin comes from trusted configuration, never from an incoming activity URL.
- The Web host maps GET list/detail, business decision, DevOps decision, retry,
  session, and audit-backed detail behavior. It does not map
  `POST /api/request-drafts/prepare` or request-creating `POST /api/requests`, and the
  React application has no new-request route, navigation, form, DTO, or capability.
- `/api/messages` is mapped before `/api` and SPA fallbacks. Non-development hosting
  requires SDK token validation. Local automated tests use a fake authenticated
  channel boundary; Microsoft Agents Playground is a transport/UX aid, not evidence
  that production authentication passed.
- No Teams approval notifications, proactive messages, editing of a ready snapshot or
  submitted request, approval cards, workflow status cards, or provisioning cards are
  introduced. Pre-submission correction always creates a replacement preparation.

### Browser-creation removal inventory

- Removed `RequestDraftsController`, `ChatRequestDraftInterpreter`, and the stateless
  `IRequestDraftInterpreter`/`DraftInterpretation*` contracts.
- Removed request-creating `POST /api/requests` and its DTOs; GET list/detail and
  protected retry remain on `AccessRequestsController`.
- Removed `NewRequestPage`, `/requests/new`, New request navigation/list actions,
  request-creation client DTOs, `createRequest`, and creation-only styles/tests.
- Existing `AccessRequest`, approval, provisioning operation, grant, and audit rows
  are unchanged; no migration or cleanup is required.

## Complexity Tracking

No constitution violations require justification. `Microsoft.Agents.AI.Hosting` is
preview-only at the selected compatible MAF version, but it supplies the concrete
native session-store boundary requested by the current design and replaces more
complex custom cache, eviction, and cleanup code. No MAF workflow or additional
hosting protocol is used.

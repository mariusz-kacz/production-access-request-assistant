# Implementation Plan: Exercise the Real Conversational Model

**Branch**: `[003-exercise-real-model]` | **Date**: 2026-08-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-exercise-real-model/spec.md`

## Summary

Add one process-wide, server-selected request-preparation model profile behind the
existing provider-neutral `IChatClient` boundary. The checked-in/default profile
remains deterministic. An explicitly selected `FoundryResponses` profile uses one
approved Foundry model deployment through the OpenAI Responses API and Microsoft
Entra authentication, the existing MAF
conversation/session flow, the exact three read-only loopback MCP tools, and the
existing closed JSON-schema proposal. Invalid or unavailable real profiles resolve to
a fail-closed unavailable client and never fall back to the deterministic client.

The existing ASP.NET Core 100-second Teams endpoint request timeout remains the single
overall deadline. Its cancellation token continues through model, MCP, parsing, and
session operations; no second interpreter timer is added.

Add one exact authenticated Teams lifecycle command, `/new`, for abandoning the
caller's active unsubmitted preparation. The Teams adapter recognizes the exact
trimmed command before model invocation and calls a provider-neutral Core operation.
Core reloads only the active intake for the authenticated actor and conversation,
applies the existing `Collecting|Ready -> Superseded` transition (or expires a ready
preparation that reached its deadline), and saves the existing lifecycle evidence.
No new preparation is created until the next ordinary message; its new intake ID
naturally isolates it from the old MAF session. Reset is idempotent when no active
intake exists and never changes a submitted request.

No new model tool, domain status, database table, external endpoint, React surface,
approval/provisioning path, or persisted model provenance is added.

## Technical Context

**Language/Version**: C# 14 on .NET 10; TypeScript 7/React 19 remain unchanged

**Primary Dependencies**: Existing Microsoft Agent Framework 1.15.0,
`Microsoft.Agents.AI.Hosting` 1.15.0-preview.260722.1, Microsoft 365 Agents SDK
1.6.150, `Microsoft.Extensions.AI` 10.7.0, and MCP SDK; add
`Microsoft.Extensions.AI.OpenAI` 10.7.0, `OpenAI` 2.11.0, and `Azure.Identity`
1.21.0 for the single Foundry Responses adapter

**Storage**: Existing SQLite workflow/intake data and process-local MAF session store;
execution profile, endpoint, and deployment name are configuration only and are not
persisted; Entra credentials remain outside application configuration. Explicit reset
reuses the existing `Superseded` status, candidate clearing, lifecycle record, and
active-intake uniqueness filter. The old process-local model session becomes
unreachable because the next preparation receives a new intake ID

**Testing**: xUnit v3 unit/component/full-host tests, real SQLite where persistence is
asserted, `WebApplicationFactory`, deterministic `IChatClient` substitutes, loopback
MCP test host, and existing Vitest suite; no automated live-model calls

**Target Platform**: The existing single ASP.NET Core host on local Windows developer
workstations, exercised through an authenticated personal Teams conversation and an
Azure AI Foundry project inference endpoint reachable over HTTPS

**Project Type**: Single-host modular web application with a thin React UI and
personal Teams transport

**Performance Goals**: Every Teams preparation request completes or is terminated by
the existing 100-second endpoint deadline; a complete valid request reaches
confirmation within five requester messages

**Constraints**: Warnings as errors; nullable enabled; one selected profile per host;
explicit real-profile opt-in; no automatic fallback; Microsoft Entra authentication
only; exact three-tool read-only MCP catalog; closed JSON-schema output; provider and
MCP contracts remain outside Core; no secrets, prompts, transcripts, response bodies,
or complete MCP payloads in logs; tests remain credential- and network-free; `/new`
is an exact reserved lifecycle command rather than an LLM intent

**Scale/Scope**: One developer/reviewer, exactly `Deterministic` and
`FoundryResponses` profiles, and one approved Foundry deployment at a time, local
synthetic data, personal Teams conversations, and the
existing single-host portfolio demo; no router, provider marketplace, multi-tenant
model policy, production rollout, RAG, or distributed coordination

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

| Gate | Pre-design | Post-design evidence |
|------|------------|----------------------|
| **Human authority** | PASS | Model use ends at an untrusted proposal. Authenticated requester confirmation, business approval, DevOps approval, and deterministic provisioning remain the only state-changing path. |
| **AI and MCP boundary** | PASS | The existing closed proposal schema and authoritative `RequestDraftValidator` remain unchanged. The interpreter still requires exact equality with `get_production_environment` and `get_incident`; Azure/OpenAI SDK types stay in Web. |
| **Scope integrity** | PASS | A real-model proposal enters the same immutable ready snapshot, reserved request ID, actor/conversation ownership, revalidation, fixed eight-hour scope, and correction-by-new-request rules. |
| **Provisioning evidence** | PASS | No model-visible state-changing tool is added. Provisioning still receives only request ID, reloads persisted evidence, and uses that ID for idempotency. |
| **Proportionality** | PASS | One options/configuration surface, one concrete registration/adapter boundary, and one unavailable client are added to the existing executable. There is no new project, endpoint, process, database entity, router, or generic provider abstraction. |
| **Verification and operations** | PASS | Automated tests use deterministic provider substitutes and cover profile selection, no fallback, provider translation, native request-timeout cancellation, exact tools, schema rejection, unchanged state, and safe logs. One manual guide covers the approved live-model exercise. |
| **Explicit reset lifecycle** | PASS | `/new` is authenticated and conversation-bound, invokes no AI or MCP, reuses the existing terminal supersession transition and audit metadata, leaves submitted requests immutable, and starts no workflow state. |

No constitution violation or amendment is required.

## Project Structure

### Documentation (this feature)

```text
specs/003-exercise-real-model/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   |-- model-execution-profile.schema.json
|   |-- real-model-turn-contract.md
|   `-- teams-reset-command.md
|-- checklists/
|   `-- requirements.md
`-- tasks.md                              # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
|-- GovernedAccess.Core/                  # Provider-neutral reset lifecycle
|   |-- Application/Drafts/RequestDraftService.cs # Reset active draft operation
|   |-- Domain/Drafts/RequestIntakeSession.cs     # Reuse terminal supersession
|   |-- Ports/RequestDrafting.cs
|   `-- Ports/RequestIntake.cs                     # Reset command/outcome contract
|-- GovernedAccess.Mcp/                   # Same exact three read-only tools
|   `-- RequestContextTools.cs
`-- GovernedAccess.Web/
    |-- Ai/
    |   |-- RequestPreparationModelOptions.cs       # New closed profile/config validation
    |   |-- RequestPreparationChatRegistration.cs   # New exact client selection
    |   |-- ProviderFailureMappingChatClient.cs     # New SDK failure normalization/logging
    |   |-- UnavailableChatClient.cs                # New fail-closed invalid profile
    |   |-- MafRequestPreparationInterpreter.cs     # Native request cancellation propagation
    |   `-- DeterministicChatClient.cs               # Existing default/test client
    |-- Teams/
    |   `-- TeamsAccessRequestAgent.cs               # Exact /new and safe operation metadata
    |-- Program.cs                                   # Use profile registration extension
    |-- appsettings.json                             # Nonsecret deterministic defaults
    |-- appsettings.Development.json                 # Nonsecret local placeholders
    `-- GovernedAccess.Web.csproj                    # Pinned provider packages

tests/
|-- GovernedAccess.UnitTests/
|   `-- RequestDraftAndSubmissionServiceTests.cs    # Draft/reset and confirmation lifecycle transitions
`-- GovernedAccess.IntegrationTests/
    |-- Ai/
    |   |-- RequestPreparationChatRegistrationTests.cs
    |   |-- RealModelDeadlineTests.cs
    |   `-- MafRequestPreparationFailureTests.cs
    |-- Hosting/ProgramCompositionTests.cs
    |-- Infrastructure/GovernedAccessWebFactory.cs
    |-- Mcp/MafToolBoundaryTests.cs
    |-- Observability/TeamsIntakeLoggingTests.cs
    |-- Teams/TeamsRequestPreparationTests.cs
    `-- Teams/TeamsConversationResetTests.cs

docs/
├── architecture.md
├── local-development.md
├── security-model.md
├── teams-quickstart.md
├── teams-advanced-reference.md
└── testing-strategy.md
```

**Structure Decision**: Keep the existing provider-neutral Core and MAF interpreter
contracts. Add a Web-host configuration/registration boundary that selects one
`IChatClient`, plus a provider-failure wrapper with a concrete need to normalize
provider exceptions and log safe operation metadata. Put exact `/new` recognition in
the Teams adapter and the actor/conversation-bound lifecycle operation in the existing
Core intake service. Do not encode reset in the model prompt or MCP surface. The React
UI, MCP project, database model, confirmation service, approval services, and
provisioning services remain unchanged.

## Design Flow

```text
server configuration
        │
        ├── Deterministic ───────────────► DeterministicChatClient
        │
        ├── FoundryResponses + valid config ─► Responses IChatClient
        │                                  + failure/logging wrapper
        │
        └── missing/invalid/unknown ─────► UnavailableChatClient
                                           (never deterministic fallback)
                                                    │
authenticated Teams message ─► ASP.NET request timeout + existing MAF interpreter
                                                    │
                         exact three read-only MCP tools + closed proposal schema
                                                    │
                         existing deterministic authoritative RequestDraftValidator
                                                    │
                    clarification / rejection / immutable confirmation card
                                                    │
                    existing human confirmation and governed workflow
```

The Teams adapter branches before the interpreter for the one reserved lifecycle
command:

```text
authenticated Teams text
        |
        |-- exact trimmed /new --> Core reset active intake
        |                          |-- Collecting/Ready --> Superseded
        |                          |-- expired Ready ----> Expired
        |                          `-- no active --------> idempotent success
        |                               (no model, MCP, or request creation)
        |
        `-- every other message --> existing preparation flow
```

## Complexity Tracking

No constitution violations or exceptional complexity are introduced.

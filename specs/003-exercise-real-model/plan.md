# Implementation Plan: Exercise the Real Conversational Model

**Branch**: `[003-exercise-real-model]` | **Date**: 2026-08-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-exercise-real-model/spec.md`

## Summary

Add one process-wide, server-selected request-preparation model profile behind the
existing provider-neutral `IChatClient` boundary. The checked-in/default profile
remains deterministic. An explicitly selected Azure OpenAI profile uses one approved
chat-completions deployment through Microsoft Entra authentication, the existing MAF
conversation/session flow, the exact three read-only loopback MCP tools, and the
existing closed JSON-schema proposal. Invalid or unavailable real profiles resolve to
a fail-closed unavailable client and never fall back to the deterministic client.

The model/MCP loop receives one cumulative 90-second inner deadline, leaving ten
seconds inside the existing 100-second Teams endpoint deadline to translate timeout
and return safe guidance. No Core contract, domain rule, request/approval/provisioning
path, external endpoint, React surface, database table, or persisted model provenance
is added.

## Technical Context

**Language/Version**: C# 14 on .NET 10; TypeScript 7/React 19 remain unchanged

**Primary Dependencies**: Existing Microsoft Agent Framework 1.15.0,
`Microsoft.Agents.AI.Hosting` 1.15.0-preview.260722.1, Microsoft 365 Agents SDK
1.6.150, `Microsoft.Extensions.AI` 10.7.0, and MCP SDK; add
`Microsoft.Extensions.AI.OpenAI` 10.7.0, `Azure.AI.OpenAI` 2.1.0, and
`Azure.Identity` 1.21.0 for the single Azure OpenAI adapter

**Storage**: Existing SQLite workflow/intake data and process-local MAF session store;
execution profile and provider credentials are configuration only and are not
persisted

**Testing**: xUnit v3 unit/component/full-host tests, real SQLite where persistence is
asserted, `WebApplicationFactory`, deterministic `IChatClient` substitutes, loopback
MCP test host, and existing Vitest suite; no automated live-model calls

**Target Platform**: The existing single ASP.NET Core host on local Windows developer
workstations, exercised through an authenticated personal Teams conversation and an
Azure OpenAI resource reachable over HTTPS

**Project Type**: Single-host modular web application with a thin React UI and
personal Teams transport

**Performance Goals**: Every model/MCP interpretation turn completes or returns a
typed timeout within 90 seconds; the Teams endpoint retains its 100-second outer
deadline; a complete valid request reaches confirmation within five requester
messages; ten controlled complete conversations prepare correctly

**Constraints**: Warnings as errors; nullable enabled; one selected profile per host;
explicit real-profile opt-in; no automatic fallback; Microsoft Entra authentication
only; exact three-tool read-only MCP catalog; closed JSON-schema output; provider and
MCP contracts remain outside Core; no secrets, prompts, transcripts, response bodies,
or complete MCP payloads in logs; tests remain credential- and network-free

**Scale/Scope**: One developer/reviewer, one approved Azure OpenAI profile and
deployment at a time, local synthetic data, personal Teams conversations, and the
existing single-host portfolio demo; no router, provider marketplace, multi-tenant
model policy, production rollout, RAG, or distributed coordination

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

| Gate | Pre-design | Post-design evidence |
|------|------------|----------------------|
| **Human authority** | PASS | Model use ends at an untrusted proposal. Authenticated requester confirmation, business approval, DevOps approval, and deterministic provisioning remain the only state-changing path. |
| **AI and MCP boundary** | PASS | The existing closed proposal schema and authoritative `RequestValidator` remain unchanged. The interpreter still requires exact equality with `get_production_environment`, `get_incident`, and `get_available_roles`; Azure/OpenAI SDK types stay in Web. |
| **Scope integrity** | PASS | A real-model proposal enters the same immutable ready snapshot, reserved request ID, actor/conversation ownership, revalidation, fixed eight-hour scope, and correction-by-new-request rules. |
| **Provisioning evidence** | PASS | No model-visible state-changing tool is added. Provisioning still receives only request ID, reloads persisted evidence, and uses that ID for idempotency. |
| **Proportionality** | PASS | One options/configuration surface, one concrete registration/adapter boundary, and one unavailable client are added to the existing executable. There is no new project, endpoint, process, database entity, router, or generic provider abstraction. |
| **Verification and operations** | PASS | Automated tests use deterministic provider substitutes and cover profile selection, no fallback, provider translation, cumulative deadline, exact tools, schema rejection, unchanged state, and safe logs. One manual guide covers the approved live-model exercise. |

No constitution violation or amendment is required.

## Project Structure

### Documentation (this feature)

```text
specs/003-exercise-real-model/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── model-execution-profile.schema.json
│   └── real-model-turn-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md                              # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── GovernedAccess.Core/                  # No provider-specific changes
│   ├── Application/RequestIntakeService.cs
│   └── Ports/RequestDrafting.cs
├── GovernedAccess.Mcp/                   # Same exact three read-only tools
│   └── RequestContextTools.cs
└── GovernedAccess.Web/
    ├── Ai/
    │   ├── RequestPreparationModelOptions.cs       # New closed profile/config validation
    │   ├── RequestPreparationChatRegistration.cs   # New exact client selection
    │   ├── ProviderFailureMappingChatClient.cs     # New SDK failure normalization/logging
    │   ├── UnavailableChatClient.cs                # New fail-closed invalid profile
    │   ├── MafRequestPreparationInterpreter.cs     # Cumulative inner turn deadline
    │   └── DeterministicChatClient.cs               # Existing default/test client
    ├── Teams/
    │   └── TeamsAccessRequestAgent.cs               # Safe profile/model operation metadata
    ├── Program.cs                                   # Use profile registration extension
    ├── appsettings.json                             # Nonsecret deterministic defaults
    ├── appsettings.Development.json                 # Nonsecret local placeholders
    └── GovernedAccess.Web.csproj                    # Pinned provider packages

tests/
└── GovernedAccess.IntegrationTests/
    ├── Ai/
    │   ├── RequestPreparationChatRegistrationTests.cs
    │   ├── RealModelDeadlineTests.cs
    │   └── MafRequestPreparationFailureTests.cs
    ├── Hosting/ProgramCompositionTests.cs
    ├── Infrastructure/GovernedAccessWebFactory.cs
    ├── Mcp/MafToolBoundaryTests.cs
    ├── Observability/TeamsIntakeLoggingTests.cs
    └── Teams/TeamsRequestPreparationTests.cs

docs/
├── architecture.md
├── local-development.md
├── security-model.md
├── teams-demo.md
└── testing-strategy.md
```

**Structure Decision**: Keep the existing provider-neutral Core and MAF interpreter
contracts. Add only a Web-host configuration/registration boundary that selects one
`IChatClient`, plus a provider-failure wrapper with a concrete need to normalize Azure
SDK exceptions and log safe operation metadata. The React UI, MCP project, database
model, confirmation service, approval services, and provisioning services remain
unchanged.

## Design Flow

```text
server configuration
        │
        ├── Deterministic ───────────────► DeterministicChatClient
        │
        ├── AzureOpenAI + valid config ─► Azure OpenAI IChatClient
        │                                  + failure/logging wrapper
        │
        └── missing/invalid/unknown ─────► UnavailableChatClient
                                           (never deterministic fallback)
                                                    │
authenticated Teams message ─► existing MAF interpreter + 90s cumulative deadline
                                                    │
                         exact three read-only MCP tools + closed proposal schema
                                                    │
                         existing deterministic authoritative RequestValidator
                                                    │
                    clarification / rejection / immutable confirmation card
                                                    │
                    existing human confirmation and governed workflow
```

## Complexity Tracking

No constitution violations or exceptional complexity are introduced.

# Implementation Plan: Natural-Language Environment Resolution

**Branch**: `004-resolve-context-identifiers` | **Date**: 2026-08-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from
`specs/004-resolve-context-identifiers/spec.md`

## Summary

Allow the request-preparation model to resolve a readable production-environment
description without requiring the requester to know its stable identifier. The
existing `get_production_environment` MCP tool will support a bounded complete-catalog
discovery call as well as exact lookup, returning each environment's authoritative
client and assigned roles. The model will interpret the readable description against
that catalog, while existing application services independently validate the proposed
environment, client, role, and optional exact incident identifier.

When the model interprets a value as a possible environment identifier, it first
uses exact lookup. A typed `NotFound` then permits bounded discovery so the model can
show authoritative plausible alternatives. Even one alternative requires developer
confirmation; other exact-lookup failures never trigger this fallback.

The model-visible MCP catalog shrinks from three tools to exactly two:
`get_production_environment` and exact-only `get_incident`. The separate
`get_available_roles` tool is removed. No workflow state, approval behavior,
provisioning behavior, persistence schema, or browser UI changes. The proposal
candidate fields stay unchanged, while the clarification-target schema removes
`clientId` because the client is always derived from the selected environment and
adds structured `environmentOptionIds`. The model owns the bounded conversational
clarification message and semantic shortlist; the application validates and reloads
the option IDs, presents that message as non-authoritative text, and appends
authoritative choice labels and stable IDs without interpreting facts from prose.

## Technical Context

**Language/Version**: C# 14 on .NET 10; existing TypeScript/React client is unaffected

**Primary Dependencies**: ASP.NET Core; Microsoft Agent Framework 1.15;
Microsoft.Extensions.AI 10.7; ModelContextProtocol 1.4.1; EF Core SQLite 10.0

**Storage**: Existing SQLite `Clients`, `ProductionEnvironments`, and
`EnvironmentRoles` tables; no new table, column, or migration

**Testing**: xUnit v3 unit tests and ASP.NET Core integration/contract tests using
deterministic chat clients and test-hosted Streamable HTTP MCP

**Target Platform**: One executable ASP.NET Core host with authenticated personal
Teams intake and same-origin browser request register

**Project Type**: Modular monolith web application with Core, MCP adapter, Web host,
and test projects

**Performance Goals**: Return the complete fixed environment catalog within the
existing request-preparation timeout; keep one unambiguous request within a single
developer turn and one focused clarification for ambiguity or a rejected potential
identifier

**Constraints**: Exactly two read-only model-visible tools; maximum 20 environment
candidates controlled by the server; no partial/truncated catalog; exact-only incident
lookup; assigned roles embedded in environment results; all model-proposed values
authoritatively revalidated; no live LLM in automated tests; cancellation and typed
failures across every async boundary; exact-to-discovery fallback only for typed
`NotFound`; no silent identifier correction; no raw prompts or full MCP payload
logging; environment clarification messages are bounded and shown only after their
structured option set passes validation; selectable values never come from prose

**Scale/Scope**: Fixed synthetic catalog with two clients, two production
environments, two supported role identifiers, and no mutable reference-data surface;
the cap of 20 is a fail-closed guard rather than a pagination contract

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

### Pre-design gate

| Gate | Status | Evidence |
|------|--------|----------|
| Human authority | PASS | Discovery and lookup are read-only. Request creation still requires authenticated Teams confirmation; business and DevOps decisions remain authenticated structured actions. |
| AI and MCP boundary | PASS | The design uses exactly `get_production_environment` and `get_incident`, keeps SDK contracts in adapters, schema-validates output, independently reloads environment-role evidence, and treats rendered model clarification text as informational rather than choice or scope data. |
| Scope integrity | PASS | Client is derived from the environment; conflicting values are rejected; a rejected potential identifier cannot be silently substituted; submitted requests and approval scope remain immutable. |
| Provisioning evidence | PASS | Provisioning is untouched, model-inaccessible, request-ID-bound, and independently reloads persisted evidence. |
| Proportionality | PASS | Existing projects, tables, endpoint, and orchestration are reused. One focused provider-neutral read projection is justified by the combined environment/client/role result. |
| Verification and operations | PASS | Contract, orchestration, persistence, failure, cancellation, timeout, prompt-injection, and deterministic validation tests are planned without a live LLM. |

No pre-design gate violation requires Complexity Tracking.

### Post-design re-check

| Gate | Status | Design confirmation |
|------|--------|---------------------|
| Human authority | PASS | [data-model.md](data-model.md) introduces no state-changing entity or transition, and [contracts/mcp-tools.json](contracts/mcp-tools.json) exposes read-only context only. |
| AI and MCP boundary | PASS | The contract advertises exactly two closed-schema tools. Environment discovery returns bounded authoritative context; fallback suggestions are catalog members and remain untrusted; the bounded model message is rendered only after option validation and is never parsed as authority; incident lookup remains exact-only. |
| Scope integrity | PASS | Environment candidates carry one authoritative client and assigned role set; final candidate and confirmation validation remain unchanged. |
| Provisioning evidence | PASS | No provisioning contract, input, handler, operation, or retry path is changed. |
| Proportionality | PASS | The design adds a non-persistent read projection and reader operations, removes the redundant role tool, and requires no new service, package, table, UI, or retrieval subsystem. |
| Verification and operations | PASS | [quickstart.md](quickstart.md) covers the required sequential validation gates and feature-specific positive and negative scenarios. |

No post-design gate violation requires Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/004-resolve-context-identifiers/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   |-- mcp-tools.json
|   |-- environment-resolution-turn-contract.md
|   `-- request-intake-proposal.schema.json
|-- checklists/
|   `-- requirements.md
`-- spec.md
```

### Source Code (repository root)

```text
src/
|-- GovernedAccess.Core/
|   |-- Application/RequestIntakeService.cs # authoritative option validation
|   |-- Ports/CorePorts.cs                   # enriched exact/list reader operations
|   `-- Ports/RequestDrafting.cs             # structured environment option IDs
|-- GovernedAccess.Mcp/
|   |-- RequestContextTools.cs       # two tools and combined environment/role result
|   `-- McpRegistration.cs           # exact two-tool server registration
`-- GovernedAccess.Web/
    |-- Ai/MafRequestPreparationInterpreter.cs # proposal schema and tool loop
    |-- Teams/TeamsAccessRequestAgent.cs         # model message + authoritative choices
    `-- Persistence/EfRequestContextReader.cs

tests/
|-- GovernedAccess.UnitTests/
|   |-- RequestPreparationTests.cs
|   `-- RequestValidationTests.cs
`-- GovernedAccess.IntegrationTests/
    |-- Ai/
    |-- Mcp/
    |-- Persistence/
    `-- Teams/

docs/                                  # as-built/runtime guidance synchronized on delivery
```

**Structure Decision**: Retain the existing modular monolith. Add one Core-owned,
non-persistent production-environment context projection and focused context-reader
operations so the MCP adapter does not depend on persistence or issue N+1 reads.
Keep `GetEnvironmentRoleAsync` as the independent deterministic validator. Remove the
list-only role reader operation if no non-MCP caller remains after migration.

## Design and Implementation Sequence

1. Add the provider-neutral enriched environment context projection and exact/bounded
   reader operations. Implement stable, no-tracking EF reads over existing reference
   tables and fail closed above the 20-record limit.
2. Replace the environment MCP contract with discovery/exact modes and a common
   `environments` result, embed stable ordered roles, remove the role tool, and enforce
   the exact two-tool registration.
3. Update model instructions, proposal-schema validation, and catalog validation.
   Natural-language environment text triggers discovery; identifier-like values
   trigger exact lookup first; only typed `NotFound` permits a discovery fallback;
   fallback alternatives are catalog-validated and always require developer
   confirmation or selection. The model returns alternative IDs in a structured
   field; the application rejects unknown, duplicate, or excessive values, reloads
   the referenced contexts, sorts them, renders the bounded model message as
   informational text, and appends authoritative option display values. Invalid
   option sets suppress the associated message, and identifiers appearing only in
   prose are ignored. Incident text never triggers discovery, role choices come only
   from the selected environment, and `clientId` is never a clarification target.
4. Update deterministic chat substitutes and the focused contract, persistence, MAF
   boundary, Core validation, and retained Teams clarification tests. Cover exact
   `NotFound` followed by discovery, valid and invalid structured alternatives,
   preservation of the model message beside authoritative choices, explicit
   confirmation, conflicts, and absence of fallback for every other failure. Do not
   recreate session, candidate-validation, incident, logging, or workflow scenarios
   already owned by narrower existing suites.
5. Supersede the old MCP contract with the feature 004 contract and synchronize
   README, architecture, orchestration, security, testing, Teams, and current ADR
   guidance when the runtime implementation changes.
6. Run the repository's mandatory build, unit, and unified integration validation in
   the exact order documented in [quickstart.md](quickstart.md).

## Complexity Tracking

No constitution violations or complexity exceptions are required.

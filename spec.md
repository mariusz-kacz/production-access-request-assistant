# Common Project Specification

- **Status**: Current context index
- **Last verified**: 2026-08-20
- **Scope**: Repository-wide, as-built local synthetic implementation

## Purpose

This file is the common starting point for work in this repository. It identifies
which artifacts are authoritative, where each concern lives, and what context to load
for a task. It does not replace the product baseline, architecture, security model,
contracts, or tests.

The product rule is:

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

## Authority and conflict handling

Use the narrowest authoritative artifact that covers the decision:

| Concern | Authority |
|---|---|
| Governing principles and delivery constraints | [Project constitution](docs/constitution.md) |
| Current product behavior, fixed data, requirements, and exclusions | [Product baseline](docs/governed-production-access-product-baseline.md) |
| As-built modules, runtime flows, persistence, and interfaces | [Architecture](docs/architecture.md) |
| Trust boundaries, controls, residual risks, and review triggers | [Security model](docs/security-model.md) |
| Intake algorithm, candidate handling, tool policy, and conversation memory | [Request-intake orchestration](docs/request-intake-orchestration.md) |
| Accepted architectural decisions and revisit criteria | [ADR index](docs/adr/README.md) |
| MCP wire surface | [Current MCP contract](docs/contracts/mcp-tools.json) |
| Test ownership and required validation | [Testing strategy](docs/testing-strategy.md) |
| Setup, configuration, run commands, and troubleshooting | [Local development](docs/local-development.md) |
| Operator-only live model checks | [Live-model evaluation](docs/live-model-evaluation.md) |

The [roadmap](docs/roadmap.md) is explicitly proposed and non-authoritative. Retired
SDLC artifacts remain available in Git history but are not current project context.

If guidance conflicts, do not silently choose the most convenient version. Follow the
constitution and current product baseline, confirm the as-built state in source and
tests, and surface any remaining mismatch before changing behavior.

## Current product boundary

- This is a portfolio-grade local implementation using only synthetic identity, data,
  provisioning, and grants. It grants no real production access.
- One executable `GovernedAccess.Web` host serves the Teams endpoint, same-origin API,
  React assets, read-only MCP endpoint, SQLite persistence, and synthetic provisioner.
- Teams card confirmation is the only request-creation path. The browser is a request
  register and authenticated business/DevOps decision surface.
- The model prepares an untrusted proposal. Core validates authoritative context and
  readiness; authenticated humans decide; deterministic services transition and
  provision.
- Submitted request scope is immutable. The role cannot be changed during approval,
  and every successful grant lasts exactly eight hours.
- MCP exposes exactly `get_production_environment` and `get_incident`. Neither the
  model nor MCP can submit, approve, provision, retry, revoke, or mutate workflow
  state.
- The solution deliberately excludes real identity and access providers, mutable
  enterprise reference systems, generic workflow engines, large RAG, multi-agent
  orchestration, distributed infrastructure, and separate deployable services.

Detailed requirements and acceptance cases remain in the
[product baseline](docs/governed-production-access-product-baseline.md).

## Repository map

| Path | Responsibility | Start with |
|---|---|---|
| `src/GovernedAccess.Core` | Provider- and protocol-independent domain rules, application services, typed outcomes, and ports | `Domain/`, `Application/`, `Ports/` |
| `src/GovernedAccess.Mcp` | Translation from the two typed read-only MCP tools to Core context ports | `McpRegistration.cs`, `RequestContextTools.cs` |
| `src/GovernedAccess.Web` | Composition root, controllers, Teams and AI adapters, EF Core, authentication, observability, evaluation, and synthetic provisioning | `Program.cs`, `appsettings.json` |
| `src/GovernedAccess.Web/ClientApp` | Thin React request register and decision UI built into Web `wwwroot` | `src/App.tsx`, `src/api/` |
| `tests/GovernedAccess.UnitTests` | Deterministic domain and pure application behavior | Tests matching the Core type being changed |
| `tests/GovernedAccess.IntegrationTests` | SQLite, MCP, MAF, Teams, HTTP, security, concurrency, and provisioning boundaries | Concern-named subdirectory plus `Infrastructure/` |
| `docs/` | Current product, architecture, security, operations, testing, and ADR guidance | The authority table above |

Project references enforce `Web -> Core + Mcp` and `Mcp -> Core`; Core has no project
reference to either outer layer. Shared .NET settings are in
[`Directory.Build.props`](Directory.Build.props). Frontend scripts and the supported
Node range are in [`package.json`](src/GovernedAccess.Web/ClientApp/package.json).

## Context to load by task

- Product or workflow behavior: product baseline, architecture, relevant ADRs, Core
  service/entity, and its unit and integration tests.
- AI or MCP: product baseline authority rules, request-intake orchestration, current
  MCP contract, `GovernedAccess.Mcp`, Web `Ai/`, and `tests/.../Mcp` plus `tests/.../Ai`.
- Identity, approval, provisioning, persistence, or public endpoints: security model,
  architecture, relevant ADRs, boundary implementation, and negative-path integration
  tests.
- Teams: request-intake orchestration, Teams runbooks, Web `Teams/`, and
  `tests/.../Teams`.
- React: current controller response types, `ClientApp/src/api/contracts.ts`, affected
  component/page, and `ClientApp/src/test`.
- Evaluation: live-model evaluation guide, Web `Evaluation/`, its contracts and
  dataset, and evaluation integration tests.

Before editing, read the target, its closest tests, and one existing analogous pattern.
No repository-specific SDLC scaffolding is currently selected; add replacement
workflow files only when that SDLC is chosen.

## Verified working conventions

- .NET targets `net10.0`; nullable references, analyzers, code style, and
  warnings-as-errors are configured centrally.
- The React client requires Node `>=24 <25`; its build is `tsc -b && vite build` and
  its test runner is Vitest.
- Automated suites use deterministic model clients. Live-model evaluation is an
  explicit, credentialed operator action and is not a routine test gate.
- Required restore, build, test, run, and timeout commands are maintained in
  [local development](docs/local-development.md) and
  [testing strategy](docs/testing-strategy.md). `AGENTS.md` carries the concise
  execution rules that must remain visible in every coding session.

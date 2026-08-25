# ADR 0008: Separate Read-Only Context Capabilities by Authoritative Source

- **Status**: Accepted
- **Date**: 2026-08-22
- **Clarified**: 2026-08-24
- **Refined by**: ADR 0010 for exact environment IDs resolved through model-side search
- **Implemented structurally by**: ADR 0011 for the co-hosted reference-authority module and database
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `docs/constitution-amendment-3.0.0.md`, `docs/contracts/deterministic-request-intake-mcp-contract.md`

## Context

The delivered baseline exposes two model-visible tools. One environment tool combines bounded discovery, exact lookup, client ownership, and assigned roles. That is compact, but it collapses capabilities that commonly belong to different enterprise systems:

- a service catalog or CMDB search projection supports human-readable discovery;
- an environment registry owns exact environment identity and client ownership;
- IAM or an entitlement catalog owns roles currently assignable in one environment;
- ITSM owns incident state and affected environment.

Those facts may have different owners, permissions, freshness, latency, and failure behavior. Combining roles with environment metadata makes the environment source look authoritative for entitlements and hides a meaningful partial-failure boundary.

The feature must remain bounded. More tools are justified only when they preserve real capability and trust differences, not merely to create more observable model choreography.

The model-visible environment search and Core's authoritative re-search must also avoid policy drift. Two separately implemented matchers could return different result sets for the same structured query and undermine the intended trust boundary.

## Decision

Expose exactly four model-visible, typed, read-only MCP tools:

1. `search_production_environments(query)` for deterministic human-readable discovery;
2. `get_production_environment(environmentId)` for exact identity and owning client of an active production environment eligible for access-request intake;
3. `get_environment_roles(environmentId)` for current environment-scoped assignable roles; and
4. `get_incident(incidentId)` for exact incident state and affected environment.

The synthetic implementation may use the same SQLite database, but adapters and contracts preserve the conceptual authority boundaries. Exact environment lookup does not embed roles. Role lookup does not search across environments.

Core independently reloads every proposed exact identifier and relationship. Model-visible tool output is interpretive context only.

### Shared environment-search policy

The MCP search tool and Core environment-search port must call one shared, versioned deterministic search-policy implementation, or one common service containing that policy. They may use different transport adapters, but they must not duplicate matching logic.

The policy searches only active, production, access-request-eligible environments. It uses the same normalization, approved fields, stable ordering, 20-result hard cap, and overflow behavior on both surfaces.

The raw agent-proposed query is untrusted and is not logged. Safe diagnostics may record query length/category, policy version, outcome count/classification, duration, and correlation ID.

Tool count does not imply a mandatory ceremonial sequence. The model is instructed to gather exact environment and role context when needed, but the application does not reject a safe outcome solely because a redundant lookup was omitted or a different valid read-only order was used. Core's authoritative result, not tool-call order, determines correctness.

A unique deterministic search result may become canonical after Core executes the shared policy and exact-reloads the environment. Two-to-five matches require application-rendered selection. Six-to-twenty matches require a more specific query and are not truncated into choices. More than twenty returns a typed too-broad outcome.

## Rationale

The four capabilities model enterprise ownership and failure boundaries without exposing generic access. They demonstrate federated context gathering while retaining least privilege:

- search input is readable but bounded;
- exact lookup establishes stable identity and eligibility;
- entitlement lookup is scoped to one resolved environment;
- incident lookup is exact and cannot discover incidents.

Independent Core validation protects correctness even when sources are stale, the model omits a call, or tool output differs from current authoritative data.

Using one shared search-policy implementation prevents the agent's visible catalog and Core's authority path from disagreeing because of code drift. Core still performs its own call/read at the business boundary; “shared policy” does not mean trusting the model-visible response.

## Consequences

### Positive

- Environment metadata and entitlement assignment have explicit, credible authority boundaries.
- Independent source outage and freshness behavior can be represented and tested.
- Search and exact lookup have clearer contracts and trust semantics.
- The model receives no generic enterprise-search or arbitrary-query capability.
- Core can revalidate mutable role assignments before readiness and confirmation.
- Environment eligibility is explicit rather than inferred from existence.
- One search policy version governs both the MCP projection and authoritative Core result.
- The project provides a stronger enterprise GenAI architecture narrative without adding deployable services or real integrations.

### Negative and risks

- The catalog grows from two to four tools.
- More calls can increase model latency, provider iterations, and failure probability.
- The prompt and contract tests must prevent cross-environment role discovery.
- Synthetic shared storage may make the source separation look artificial unless the documentation clearly describes the production-shaped authority boundary.
- The shared search-policy component becomes a governed dependency for two adapters and needs explicit versioning.
- Exact tool-sequence assertions can become brittle if treated as correctness rather than diagnostics.

## Alternatives considered

### Keep the two-tool catalog and embed roles in environment results

Rejected for the proposed feature because it makes environment metadata authoritative for entitlement assignment and cannot express independent role-source failure or freshness.

### Use three tools and return roles from exact environment lookup

Rejected for the same ownership reason. Search and exact lookup would be separated, but environment and entitlement authority would still be conflated.

### Add a generic enterprise search tool

Rejected because it would widen disclosure, prompt-injection, query, ranking, and validation risks and would weaken the exact bounded contract.

### Implement separate search matchers for MCP and Core

Rejected because policy drift could produce different candidates and break the intended “Core repeats the search” safety story. Transport wrappers may differ; matching policy may not.

### Require exact environment lookup immediately before every role lookup

Rejected as a universal runtime invariant. It is a useful normal interaction for a new environment, but redundant for an unchanged canonical environment and unnecessary for Core safety. Exact source revalidation remains mandatory in the application.

### Require requester selection even for one authoritative search match

Rejected because deterministic Core can run the shared policy, observe uniqueness, exact-reload the entity, and still expose the final exact scope on the mandatory review card. Requiring another turn would add ceremony without moving authority away from the model.

### Return the first five matches from a larger result

Rejected because truncation silently changes the candidate universe and lets ordering become an accidental selection policy. Larger sets require a more specific requester query.

## Revisit criteria

Revisit this decision if:

- the real target enterprise source owns environment metadata and role assignments as one indivisible contract with identical access and freshness semantics;
- measured latency or reliability shows the split harms the bounded workflow without providing useful source distinction;
- role assignment becomes policy-derived rather than source-retrieved;
- the shared search-policy boundary becomes impossible because authoritative sources provide incompatible search semantics; or
- the catalog needs a fifth capability.

Any fifth tool requires a separate specification, threat review, contract, ADR update, and deterministic validation design.

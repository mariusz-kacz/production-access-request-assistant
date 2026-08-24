# Architecture Decision Records

- **Status**: Current decision index
- **Last reviewed**: 2026-08-22

The [architecture](../architecture.md) describes the system now. An ADR records why a
durable architectural choice was made, its trade-offs, and when it should be
reconsidered. Target-feature ADRs do not redefine the as-built runtime until their
implementation and required evidence are complete, even after the decisions are
accepted.

## Decisions

| ADR | Status | Decision | Consequence |
|---|---|---|---|
| [0001: One deployable service, including MCP](0001-use-one-deployable-service-including-mcp.md) | Accepted | Keep React, APIs, AI orchestration, MCP, persistence, workflow, and synthetic provisioning in one ASP.NET Core executable. | One deployment while retaining logical and protocol boundaries. |
| [0002: Validate persisted evidence at provisioning](0002-validate-persisted-workflow-evidence-at-provisioning.md) | Accepted | Reload the request, approvals, operation, and grant before provider execution. | Provisioning distrusts caller assertions without repeating fixed startup-validated reference lookups. |
| [0003: Provider execution and persistence are not atomic](0003-do-not-model-provider-and-workflow-persistence-as-atomic.md) | Accepted | Treat the provider call and SQLite commits as separate consistency boundaries. | Partial outcomes are recovered through idempotency and scoped retry rather than a false distributed transaction. |
| [0004: Request ID is the provisioning identity](0004-use-request-id-as-provisioning-idempotency-identity.md) | Accepted | Use the immutable request UUID as the operation and provider idempotency identity. | One request maps to one operation and at most one grant. |
| [0005: Retain terminal intake tombstones](0005-retain-terminal-request-intake-tombstones.md) | Accepted | Keep terminal intake rows while clearing obsolete candidate content. | Stale cards and replay remain deterministic; metadata accumulates until a retention policy exists. |
| [0006: Persist canonical intake, not conversation history](0006-persist-canonical-intake-state-not-conversation-history.md) | Superseded by 0009 | Keep the sanitized candidate durable and MAF conversation/presentation state process-local. | Candidate progress survives restart; conversational nuance and card tracking do not. |
| [0007: Sparse model patches and deterministic reducer](0007-use-sparse-model-patches-and-a-deterministic-reducer.md) | Accepted | The model proposes only explicit changed fields; Core owns merge, authority, validation, readiness, and outcomes. | Model omission or context loss cannot replace canonical state. |
| [0008: Context capabilities follow authoritative sources](0008-separate-read-only-context-capabilities-by-authoritative-source.md) | Accepted | Expose four narrow read-only tools for discovery, exact environment metadata, environment-scoped roles, and exact incident context. | Enterprise authority and failure boundaries remain visible without generic query access. |
| [0009: Persist canonical intake and bounded clarification context](0009-persist-canonical-intake-and-bounded-clarification-context.md) | Accepted | Persist one candidate plus version-bound ordered choice IDs; material ready revisions immediately supersede the old snapshot. | Ordinal replies survive restart without raw transcripts or dual pending-revision state. |

Each ADR contains its authoritative alternatives, consequences, and revisit criteria.

## When an ADR is warranted

Write an ADR for a change to:

- deployment, process, ownership, scaling, or availability boundaries;
- trust, identity, authorization, credential, or network boundaries;
- AI or MCP capability exposure;
- persistence ownership, consistency, reconciliation, or idempotency;
- a foundational runtime, protocol, database, or framework; or
- an established architectural rule.

Routine refactoring, formatting, temporary experiments, and feature requirements do
not need an ADR.

## Format and lifecycle

Use the next four-digit number and a short kebab-case title, for example:

```text
docs/adr/0010-short-decision-title.md
```

```markdown
# ADR 0010: Decision Title

- **Status**: Proposed
- **Date**: YYYY-MM-DD
- **Decision owners**: Project maintainer
- **Related artifacts**: ...

## Context
## Decision
## Rationale
## Consequences
### Positive
### Negative and risks
## Alternatives considered
## Revisit criteria
```

Statuses are `Proposed`, `Accepted`, `Superseded`, or `Deprecated`. Do not rewrite an
accepted decision's history. Add a dated clarification when the decision is unchanged,
or create a superseding ADR when it changes.

When accepting or superseding an ADR, update this index and any affected architecture,
security, contract, and test documentation in the same change.

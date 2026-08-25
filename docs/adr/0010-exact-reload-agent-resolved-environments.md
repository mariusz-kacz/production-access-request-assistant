# ADR 0010: Exact-Reload Agent-Resolved Environments

- **Status**: Accepted
- **Date**: 2026-08-25
- **Decision owners**: Project maintainer
- **Refines**: ADR 0008 for the unique model-side environment-search path
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `docs/adr/0007-use-sparse-model-patches-and-a-deterministic-reducer.md`, `docs/contracts/deterministic-request-intake-mcp-contract.md`

## Context

The agent can use `search_production_environments` to map readable requester wording to
current enterprise identifiers and then gather roles for one exact environment in the
same turn. The original target design could be read as requiring Core to repeat that
same search even when the final structured proposal already contains the single exact
environment ID returned by the tool.

Repeating search in that path validates neither the agent's translation of requester
intent nor the semantic correctness of its exact selection. It adds latency and an
additional remote failure opportunity, especially if MCP context moves to a remote
gateway backed by real services. Current environment eligibility, client ownership,
role assignment, and incident relationships still require deterministic authoritative
validation.

The closed proposal already distinguishes `exactEnvironmentId` from `searchQuery`.
Those forms should describe different application work rather than two inputs that both
trigger search.

## Decision

Use mutually exclusive environment-resolution paths:

1. For `exactEnvironmentId`, Core calls only the exact environment authority. It
   verifies that the environment currently exists, is active production, is eligible
   for intake, and owns the returned client. Core does not reconstruct or replay a
   model-side search query.
2. For `searchQuery`, Core calls the shared deterministic search authority because no
   exact environment has been resolved. Zero, unique, two-to-five, six-to-twenty, and
   overflow outcomes follow the normative reducer matrix. A unique result receives one
   subsequent exact reload before it becomes canonical.
3. When model-side MCP search returns exactly one result and requester intent uniquely
   justifies it, the agent may propose that `exactEnvironmentId`. Two or more results
   must not be collapsed into an unprompted exact proposal.
4. Model-visible MCP output remains interpretation context. Exact reload, not the tool
   observation, establishes current enterprise facts. Prominent ready-card review and
   human approval remain the controls for semantic misinterpretation that deterministic
   authority lookup cannot detect.

The shared search-policy rule continues to apply wherever search executes: the MCP
search surface and Core's `searchQuery` path use one implementation or one common
service and one policy version.

## Rationale

This preserves the constitutional distinction between model interpretation and
deterministic authority while avoiding a redundant operation. It also gives model-side
search a concrete purpose: the agent can resolve an environment ID, retrieve its roles,
and prepare a complete one-turn proposal. Core independently validates every proposed
exact fact without pretending that search replay proves natural-language intent.

The decision is transport-neutral. Local synthetic adapters may share an in-process
policy. A future remote context gateway may serve model-facing MCP and a separately
authenticated Core authority surface backed by one search service.

## Consequences

### Positive

- A uniquely resolved environment can reach the ready card with one fewer search call.
- Remote latency and failure exposure are lower on the exact-ID path.
- `exactEnvironmentId` and `searchQuery` have simple, non-overlapping semantics.
- Core still validates current eligibility, client ownership, roles, and incidents.
- Model-side MCP search can support one-turn environment and role resolution.

### Negative and risks

- Core does not prove that an agent-proposed exact ID came from a unique search result.
- Exact authoritative validation cannot detect a valid but semantically mistaken
  environment choice.
- Ambiguous-scope restraint therefore needs explicit prompt-contract and live-model
  evaluation coverage.
- Ready-card presentation must keep client, environment, role, and justification
  prominent so the requester can detect interpretation errors before request creation.

## Alternatives considered

### Always repeat model-side search in Core

Rejected because it adds latency and a second failure boundary without validating the
agent's translation of requester wording. It remains required only when the final
proposal contains `searchQuery` and Core must resolve cardinality itself.

### Trust an MCP result without authoritative exact reload

Rejected because model-visible output may be stale, malformed, omitted, or altered by
the model. It cannot establish current eligibility or client ownership.

### Remove model-visible environment search

Rejected for the target because it prevents the agent from grounding readable
enterprise terminology, obtaining stable IDs, and chaining environment discovery into
role lookup in one turn. Tool use remains optional when the agent can safely produce a
structured query or exact reference without it.

### Bind exact proposals to signed tool-result evidence

Deferred because it introduces query/result provenance, freshness, signing, and replay
contracts that are not justified for the current bounded flow. Reconsider it if
semantic misselection remains material after evaluation and human review.

## Revisit criteria

Revisit this decision if:

- live evaluation shows the agent collapses ambiguous results into exact IDs;
- requester review frequently misses semantically incorrect but eligible environments;
- a remote authority can provide cheap, tamper-evident query-bound evidence;
- enterprise search and exact lookup have materially incompatible freshness guarantees;
  or
- product policy requires every exact environment choice to originate from an explicit
  requester selection.

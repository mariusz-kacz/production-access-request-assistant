# Constitution Amendment Proposal: Govern the MCP Catalog Without Hard-Coding One Product Increment

- **Status:** Proposed with `deterministic-request-intake`
- **Date:** 2026-08-22
- **Target constitution version:** `3.0.0`
- **Affected principle:** II. Untrusted AI, Bounded MCP
- **Related artifacts:** `SPEC-deterministic-request-intake.md`, `docs/adr/0008-separate-read-only-context-capabilities-by-authoritative-source.md`, `docs/contracts/deterministic-request-intake-mcp-contract.md`

## Motivation

Constitution version 2.0.1 hard-codes the current two-tool product catalog and
explicitly forbids a separate role capability. That was appropriate for the delivered
baseline, but it makes a product-level capability change impossible without rewriting
a core principle even when the security properties remain unchanged.

The deterministic request-intake feature introduces four narrow read-only
capabilities to represent different enterprise responsibilities:

- deterministic environment discovery;
- exact environment metadata;
- environment-scoped entitlement assignments; and
- exact incident context.

The change does not permit generic query, state mutation, submission, approval, or
provisioning. It preserves the governing security rule that model-visible context is
untrusted and must be independently revalidated by deterministic application code.

## Amendment

Replace the hard-coded tool-name clauses in Principle II and Product and Technical
Constraints with governance rules that require:

1. one exact allowlist defined by the active product baseline and machine-readable
   contract;
2. narrow, typed, read-only capabilities associated with named authoritative
   responsibilities;
3. no generic query or state-changing model capability;
4. independent deterministic reproduction/reload of every fact before it becomes
   canonical;
5. an approved specification, ADR, threat review, contract, negative tests, and
   synchronized documentation for every catalog change; and
6. cancellation, timeouts, and typed failures across model, MCP, and authoritative
   source boundaries.

The proposed constitution text is supplied as `docs/constitution.md` in this bundle.

## Compatibility impact

This is a MAJOR constitutional change because it redefines where the exact tool names
are governed. It does **not** immediately change the as-built runtime:

- until the feature is implemented, the active product baseline and current MCP
  contract still define the exact two-tool catalog;
- after implementation and evidence, those artifacts change together to the exact
  four-tool catalog;
- any extra fifth tool remains prohibited unless another governed change updates the
  active catalog.

The amendment therefore makes the constitution more stable while keeping the runtime
catalog exact and reviewable.

## Security impact

The amendment does not widen the class of permitted tool behavior. All model-visible
tools remain read-only and untrusted. The proposed four-tool design adds independent
failure and freshness boundaries, but Core remains responsible for canonical search,
exact reload, relationship validation, readiness, confirmation, and every side effect.

The primary new risks are:

- a larger model-visible attack and failure surface;
- stale or inconsistent facts across environment, entitlement, and incident sources;
- additional latency and provider iterations; and
- accidental introduction of generic discovery through a poorly bounded search tool.

The specification and ADR 0008 mitigate these risks with an exact catalog, closed
schemas, one-call bounds, deterministic search, independent Core authority, and typed
source failures.

## Migration work

1. Approve this amendment and the related ADRs before implementing the conflicting
   four-tool catalog.
2. Keep the current product baseline and machine-readable MCP contract unchanged until
   the new implementation passes its deterministic gates.
3. Implement the four-tool contract and Core revalidation boundaries.
4. Update the product baseline, architecture, security model, current MCP contract,
   request-intake orchestration, testing strategy, operator guidance, and README in
   the evidence-backed reconciliation task.
5. Record constitution version `3.0.0` in the final documentation set.

## Rejected alternatives

### Keep exact tool names in the constitution

Rejected because every bounded capability evolution would require another
constitutional redefinition even when the governing trust properties remain stable.
The exact names remain controlled by the active baseline and contract instead.

### Permit any read-only tool dynamically

Rejected because read-only annotations alone do not bound disclosure, query scope,
latency, or prompt-injection risk. The catalog must remain exact and pre-approved.

### Keep the two-tool catalog and embed roles in environment lookup

Rejected for the proposed feature because environment identity and entitlement
assignment represent different authoritative responsibilities with different
freshness and failure semantics.

## Approval record

Approval of this proposal authorizes only the governance change and the specified
synthetic four-tool target. It does not authorize real enterprise data, credentials,
production access, additional tools, or any model-visible state-changing capability.

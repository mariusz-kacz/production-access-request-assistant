# Constitution Amendment 3.0.0: Govern the MCP Catalog Without Hard-Coding One Product Increment

- **Status:** Promoted on 2026-08-27; the four-tool runtime is the current product baseline
- **Date:** 2026-08-22
- **Last reconciled:** 2026-08-28
- **Target constitution version:** `3.0.0`
- **Affected principle:** II. Untrusted AI, Bounded MCP
- **Related artifacts:** `SPEC-deterministic-request-intake.md`, `docs/adr/0008-separate-read-only-context-capabilities-by-authoritative-source.md`, `docs/contracts/deterministic-request-intake-mcp-contract.md`

## Motivation

Constitution version 2.0.1 hard-coded the then-current two-tool product catalog and
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

The ratified constitution text is recorded in `docs/constitution.md`.

## Compatibility impact

This is a MAJOR constitutional change because it redefines where the exact tool names
are governed. At amendment acceptance it did **not** immediately change the as-built
runtime:

- before the feature was implemented, the active product baseline and MCP contract
  defined the exact two-tool catalog;
- after implementation and evidence, those artifacts changed together to the exact
  four-tool catalog;
- any extra fifth tool remains prohibited unless another governed change updates the
  active catalog.

The amendment therefore makes the constitution more stable while keeping the runtime
catalog exact and reviewable.

The implementation was promoted on 2026-08-27. The active product baseline and
canonical machine-readable contract now define the exact four-tool catalog.

## Security impact

The amendment does not widen the class of permitted tool behavior. All model-visible
tools remain read-only and untrusted. The accepted four-tool target adds independent
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

## Migration record

1. The amendment and ADR 0008 governed the target four-tool catalog.
2. The implementation passed its deterministic gates before production cutover.
3. The four-tool contract and independent Core revalidation boundaries were promoted.
4. Current product, architecture, security, testing, operator, and contract
   documentation was reconciled with the promoted catalog.
5. Constitution version `3.0.0` is recorded in the final documentation set.

## Rejected alternatives

### Keep exact tool names in the constitution

Rejected because every bounded capability evolution would require another
constitutional redefinition even when the governing trust properties remain stable.
The exact names remain controlled by the active baseline and contract instead.

### Permit any read-only tool dynamically

Rejected because read-only annotations alone do not bound disclosure, query scope,
latency, or prompt-injection risk. The catalog must remain exact and pre-approved.

### Keep the two-tool catalog and embed roles in environment lookup

Rejected for the target feature because environment identity and entitlement
assignment represent different authoritative responsibilities with different
freshness and failure semantics.

## Approval record

Acceptance of this amendment authorizes only the governance change and the specified
synthetic four-tool target. It does not authorize real enterprise data, credentials,
production access, additional tools, or any model-visible state-changing capability.

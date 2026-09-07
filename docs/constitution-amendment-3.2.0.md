# Constitution Amendment 3.2.0: Refine the Router/Policy Target

- **Status:** Approved by the project maintainer on 2026-09-07; target only
- **Target constitution version:** `3.2.0`
- **Affected provisions:** Principle II; Product and Technical Constraints; Change
  Design and Delivery
- **Refines:** [amendment 3.1.0](constitution-amendment-3.1.0.md)
- **Related artifacts:** [constitution](constitution.md),
  [target specification](../SPEC-router-policy-evolution.md),
  [ADR 0015](adr/0015-refine-router-policy-target-contracts.md),
  [plan](../tasks/plan.md), and [tasks](../tasks/todo.md)

## Motivation

The maintainer authorized four documentation-only target amendments: remove the
mandatory prose contradiction parser, evaluate router outcomes directly, define
complete-pair history ordering/overflow, and remove stale fixture chunks on rebuild.
These decisions reduce unnecessary semantic machinery and make the two bounded data
lifecycle contracts explicit. They do not authorize application implementation in
this documentation change.

## Amendment

1. Keep the small free-form `PolicyAdvisorResult`, authoritative snapshot precedence,
   closed schema and answer/outcome compatibility, current-invocation citations,
   bounded answers, safe rendering, read-only capabilities, and deterministic access
   authorization. Remove the mandatory runtime contradiction parser and its grammar
   and test requirements without replacing it with another model/parser, mandatory
   structured claims, or a templating subsystem. Runtime validation does not prove
   arbitrary prose semantically correct or consistent with every policy fact.
   Offline groundedness/relevance evaluation measures risk, not live-answer certainty.
2. Compare exact expected router route/context directly in Microsoft evaluation and
   reporting. Remove router model-judge grading, synthetic function projections,
   equivalence/compatibility gates, and the associated promotion threshold. Keep all
   v1 case IDs, categories, expected outcomes, the 95% exact threshold, mandatory
   exact safety-sensitive outcomes, three repetitions, provenance, multi-turn checks,
   and 100% safety/isolation gates. Policy Retrieval, Groundedness, and Relevance
   remain applicable as declared; no replacement mandatory router metric is added.
3. Give completed executable requester/assistant pairs explicit persisted order within
   their authenticated binding, requester first, without interleaving on reads or
   pruning. Timestamp plus arbitrary GUID sorting is not conversational order.
   Append/prune atomically to at most six complete pairs (12 messages). Keep each
   message's 2,000-character storage limit; overflow of either omits the whole pair,
   without semantic truncation, valid-input rejection, Access-limit changes, or
   authoritative rollback/replay. Select chronological newest contiguous suffixes of
   eligible complete pairs within existing four-message and 600/800 approximate-token
   caps, policy-filtering first and stopping at the first non-fitting pair. Empty
   windows are valid. Preserve `/new` and retained policy history; no semantic memory,
   summaries, retention workflow, or general orchestration is introduced.
4. Make indexing an explicit operator rebuild of only the configured synthetic fixture
   index from current checked-in corpus. Success leaves exactly its current chunk IDs;
   removed documents/chunks and obsolete IDs are absent and unsearchable. Preserve
   schema, embeddings, metadata, filters, authentication, cancellation, timeout, and
   failure controls. Failed/partial uploads cannot report success. Do not add
   incremental synchronization, background ingestion, aliases, multiple indexes, or
   an ingestion platform.

## Compatibility, security, and delivery

This is a MINOR target-governance revision: it expands explicit history/rebuild
obligations and refines unimplemented validation/evaluation requirements without
removing a core principle or changing current behavior. Human approval, deterministic
authorization, immutable scope, fixed eight-hour access, one host, synthetic data,
the exact four read-only MCP tools, and no real production access remain unchanged.

No runtime or database migration occurs here. Tasks 1 and 2 remain complete; Task 3
and all later implementation tasks remain unstarted. Task 2's historical router-judge
compatibility probe is no longer required; its scoped removal belongs to Task 12,
not this documentation change. New history and rebuild verification belongs to the
existing Tasks 6, 7, and 9. The approved target, ADR summaries, dependencies, and
estimates are updated together. Existing runtime promotion requirements are unchanged.

The accepted residual prose risk must not be presented as runtime semantic proof.
History omission may reduce continuity but cannot undo an access result; safe metadata
may record omission without message content. Fixture rebuild failure may interrupt
fixture search and must remain visible as failure, not partial success.

## Approval record

The maintainer's 2026-09-07 request explicitly approves these four target-design
amendments. No additional approval is required merely because they revise earlier
target decisions. This record does not approve runtime promotion, broader product
scope, deployment, or an implementation-task completion.

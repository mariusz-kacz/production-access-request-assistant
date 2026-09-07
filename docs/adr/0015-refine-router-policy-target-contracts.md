# ADR 0015: Refine Router/Policy Validation, History, and Fixture Rebuilds

- **Status**: Accepted
- **Date**: 2026-09-07
- **Runtime status**: Target amendment; not implementation or promotion
- **Decision owners**: Project maintainer
- **Partially supersedes**: [ADR 0012](0012-router-and-context-isolation.md) for
  history ordering/windows/overflow, [ADR 0013](0013-policy-grounding.md) for prose
  validation and fixture indexing, and
  [ADR 0014](0014-routed-evaluation-and-observability.md) for router metrics
- **Related artifacts**: [target specification](../../SPEC-router-policy-evolution.md),
  [amendment 3.2.0](../constitution-amendment-3.2.0.md),
  [plan](../../tasks/plan.md), and [tasks](../../tasks/todo.md)

## Context

The original 2026-09-04 target required a finite prose contradiction guard and router
Intent Resolution grading through evaluation-only function declarations. It also
left conversational pair order/overflow and removal of stale fixture IDs underspecified.
The maintainer explicitly authorized these four target amendments on 2026-09-07.
This ADR records the supersession; the affected earlier ADR sections summarize the
revised target and retain their original approval dates and unchanged decisions.

Task 2 has already delivered SDK/configuration foundations, including an initial
router-judge probe. Routing, reusable history, Policy Advisor execution, and fixture
indexing are not implemented by that foundation or by these documentation changes.

## Decision

1. Remove the mandatory prose contradiction parser and its grammar/test requirements.
   Keep `PolicyAdvisorResult` small and free-form, the authoritative policy snapshot
   ahead of retrieved explanation, closed output and outcome compatibility,
   current-invocation citation membership, bounded safe application rendering, and
   offline policy Groundedness/Relevance evaluation. Runtime validation does not
   prove arbitrary prose semantically correct or consistent with every policy fact;
   offline evaluation measures risk without guaranteeing each live answer. Do not
   replace the parser with another model/parser, structured claims, or templates.
2. Use direct exact expected route/context checks within Microsoft evaluation/reporting
   for all router cases, without a model judge, synthetic functions, decision-to-call
   adapter, projection-equivalence tests, related compatibility gate, or router
   Intent Resolution threshold. Preserve v1 IDs/categories/expected outcomes and the
   95% exact threshold plus exact safety-sensitive outcomes. Keep applicable policy
   Retrieval/Groundedness/Relevance, repetitions, provenance, complete multi-turn
   checks, and exact safety/isolation gates. No replacement router metric/framework.
3. Persist completed executable turns as complete requester/assistant pairs with
   explicit order per authenticated binding, requester first, never interleaved.
   Timestamp/GUID sorting is not that order. Append/prune atomically to six pairs;
   omit the entire pair if either message exceeds 2,000 characters. Do not truncate
   semantic content, reject valid input, change the Access input limit, or roll
   back/replay authoritative state. Build chronological newest-contiguous-suffix
   windows of complete eligible pairs under the existing four-message and 600/800
   approximate-token caps. Filter policy first; stop at the first non-fitting pair,
   including returning empty when the newest cannot fit. Preserve `/new` and its
   policy-history retention decision; add no memory or retention subsystem.
4. Rebuild only the configured synthetic fixture index through an explicit operator
   command. Success means exactly current checked-in fixture chunk IDs, with removed
   document/chunk and obsolete IDs absent and unsearchable. Preserve schema,
   embeddings, metadata, filtering, authentication, cancellation, timeout, and typed
   failures. Failed/partial uploads or unverified contents cannot report success.
   No incremental sync, background ingestion, aliases, multiple indexes, or platform.

## Rationale

A free-form answer does not become semantically proven through a limited prose parser.
Structural validation and snapshot precedence have clear enforceable boundaries;
offline semantic evaluation measures the remaining risk. Router output already has
an exact route/context oracle, so a function-call projection and additional judge do
not protect a distinct required boundary. Complete pairs preserve chronological
context without partial exchanges, and rebuilding the bounded fixture prevents stale
evidence without introducing a synchronization lifecycle.

## Consequences

### Positive

- Read-only policy capabilities and deterministic access authorization are unchanged.
- Router promotion uses its direct oracle and existing Microsoft reporting.
- History order, concurrency, omission, pruning, selection, and restart are testable.
- Fixture success has a precise current-ID-set invariant.

### Negative and risks

- A structurally valid cited answer can still misstate policy; evaluation is not a
  guarantee for each live answer.
- Oversized pairs and token caps can lose continuity, without affecting access state.
- A failed explicit rebuild can interrupt fixture search; no atomic availability or
  alternate-index mechanism is promised.

## Verification and migration

Task 6 owns equal timestamps, requester-first pair order, concurrent append/read,
whole-pair pruning, oversized-message omission, and restart integration cases. Task 7
owns complete-pair count/token and policy-filtered contiguous-suffix selection cases.
Task 9 owns index -> remove document/chunk -> rebuild -> absent/unretrievable IDs,
plus failed/partial upload cases. Task 10 retains structural, citation, rendering,
snapshot-precedence, and read-only evidence without parser tests. Task 12 owns exact
router reporting and removes the obsolete Task 2 router-judge probe, preserving the
policy-evaluator/reporting probes. These are future acceptance criteria, not tests
added or implementation performed by this amendment. Completed task status is unchanged.

## Alternatives considered

Replacement runtime semantic models/parsers, mandatory structured claims/templates,
an alternative mandatory router metric/framework, semantic memory/summaries, and an
ingestion platform are excluded by the approved amendment. The existing bounded
contracts remain sufficient for this target.

## Revisit criteria

Revisit only with an explicit new requirement for stronger semantic guarantees,
conversation memory/retention, or ingestion/availability beyond the synthetic fixture.
No such expansion is authorized here.

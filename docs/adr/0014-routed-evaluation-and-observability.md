# ADR 0014: Evaluate and Observe Routed Components Separately

- **Status**: Accepted
- **Date**: 2026-09-04
- **Runtime status**: Target decision; not yet implemented or promoted
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-router-policy-evolution.md`,
  `docs/constitution-amendment-3.1.0.md`, ADR 0012, and ADR 0013

## Context

An ordinary routed text turn adds a classifier before zero or one specialist and may
add policy retrieval. An end-to-end success or latency number cannot show whether a
failure or regression belongs to routing, history selection, retrieval, the Access
Request specialist, or the Policy Advisor. The target also needs evidence that context
and capabilities remain isolated even when model-graded quality is acceptable.

The repository already has an isolated live-model evaluation command, versioned
datasets, retained provenance, deterministic product checks, and safe model-call
logging. Building a second generic evaluation platform or recording raw content in
production telemetry would duplicate infrastructure and weaken the current privacy
boundary.

Microsoft's .NET evaluation libraries supply quality evaluators and reporting. The
quality evaluators used by this target return probabilistic model-graded scores; they
cannot know product invariants such as zero workflow mutation, current citation
membership, exact tool isolation, or retired-policy exclusion.

## Decision

Extend the existing isolated evaluation command and hosting discipline. Use
`Microsoft.Extensions.AI.Evaluation` abstractions, the Quality evaluators, and
Microsoft reporting/storage for router, policy, and multi-turn datasets. Keep custom
evaluation logic only for exact product invariants that a generic evaluator cannot
know.

The approved v1 inventory contains:

- 12 router cases for exact route/context selection plus Intent Resolution over the
  evaluation-only projection defined below;
- 10 Policy Advisor cases for retrieval, groundedness, relevance, citations, and
  current/retired evidence; and
- four multi-turn conversations for Access -> Policy -> Access, policy continuation,
  route switching, and ambiguous reference restatement.

Exact route checks consume the validated `RouterDecision`. For Intent Resolution only,
a deterministic evaluation adapter maps that decision one-to-one to exactly one
`FunctionCallContent` using five evaluation-only `AIFunctionDeclaration` definitions:
`route_access_request`, `route_policy_guidance`, `route_mixed`, `route_unclear`, and
`route_unsupported`. The call carries `schemaVersion` and `contextReference`; the
function descriptions reproduce the approved route semantics. The evaluator receives
the complete sanitized runtime router envelope: normalized current query, every
selected route-tagged history message, and the exact minimal active-access context
(`HasActivePreparation`, clarification target, and safe choice labels), plus those
definitions. An application-owned evaluation serializer may change representation but
cannot omit, add, summarize, or infer semantic fields. A capture-based test compares
the runtime input and evaluator projection field for field. The adapter performs no
reclassification.

The evaluation-only declarations are never runtime tools or provider capabilities.
Tests must prove the projection is one-to-one and that no declaration reaches the
runtime router's empty tool collection. Task 2 must stop for an explicit specification
amendment if its pinned package cannot evaluate the documented
`AIFunctionDeclaration` input shape; raw `RouterDecision` JSON must not be graded as
requester-visible prose.

Deterministic automated tests run without live model, Azure Search, or judge
credentials. Every live router case, every live Policy Advisor case, and every complete
live multi-turn conversation runs exactly three independent repetitions in the
credentialed retained evaluation. Independence requires fresh uncached calls only for
components applicable to the case's expected route and declared metrics, plus fresh
conversation state. A repetition must not invoke retrieval or a specialist forbidden
by its route. Cached responses, retrieval results, or judge scores cannot satisfy an
applicable call. Each versioned dataset
declares one immutable case ID per scenario and metric applicability per case before
execution. The approved specification's v1 manifest fixes 12 router, 10 policy, and
four multi-turn IDs; cases cannot be merged, duplicated, relabelled, omitted from an
applicable metric, or added to a v1 denominator. The report records source revision,
dataset and hash, metric-applicability map, repetition plan, package/evaluator versions,
router/policy/judge deployments, prompt/schema versions, corpus/index version, exact
outcomes, tokens, component latencies, thresholds, and promotion eligibility.

Use the approved specification as the single source for pre-results numeric
thresholds. Exact safety/isolation checks remain blocking independently of aggregate
quality scores. A missing, invalid, or evaluator-diagnostic score fails the applicable
promotion gate rather than being excluded from the denominator.

The metric definitions and 1-5 scale are grounded in Microsoft's official
[evaluation library inventory](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries)
and API documentation for
[Intent Resolution](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.quality.intentresolutionevaluator),
[Retrieval](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.quality.retrievalevaluator),
[Groundedness](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.quality.groundednessevaluator),
and
[Relevance](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.evaluation.quality.relevanceevaluator).
Task 2 must still prove and pin one compatible package set before implementation.

### Exact product gates

Custom deterministic/evaluation checks must prove 100% of the following for every
applicable case and repetition:

- expected route and context-reference compatibility for safety-critical `Mixed`,
  `Unclear`, `Unsupported`, and submitted-status cases;
- zero unauthorized preparation, request, approval, provisioning-operation, grant, or
  external-provider side effects;
- Access Request receives no routed policy history or retrieval evidence;
- Policy Advisor receives no Access Request tools or mutation ports;
- router receives no tools and at most one specialist is invoked;
- every cited ID belongs to current invocation evidence;
- retired policy is excluded before model invocation;
- routing, retrieval, provider, timeout, and malformed-output failures do not fall
  through to another route; and
- all three repetitions of each approved multi-turn case pass every exact per-turn
  route/context, requester-visible outcome, final-state, isolation, and zero-side-effect
  expectation.

### Observability

Create one application parent activity per routed turn and retain standard MAF/model
spans with sensitive-data capture disabled. Record safe component dimensions:

- route and context reference;
- model/deployment and prompt/schema version;
- router and selected-specialist input/output tokens and duration;
- retrieval duration and chunk count;
- selected history-message count;
- end-to-end duration, safe outcome, failure classification, and correlation ID; and
- timeout, throttling, provider, and retrieval-failure counters.

Do not record raw prompts, requester/assistant messages, answers, search queries,
retrieved chunks, justification, reasoning, complete MCP payloads, credentials, or
provider objects in default logs, spans, or metrics. Retained evaluation artifacts may
contain reviewed synthetic inputs and validated typed outcomes under the repository's
existing generated-evidence rules; they are not production telemetry or workflow
evidence.

Do not claim that routing improves latency or token cost. Report router, retrieval,
specialist, and end-to-end measurements separately and draw a comparison only from
retained evidence with matching versions and datasets.

## Rationale

Microsoft's evaluation abstractions and reporting supply the general quality and
evidence pipeline while small exact checks preserve product-specific safety boundaries.
Separating components makes the cost and failure impact of routing observable and
prevents a good aggregate result from hiding context leakage or a consequential side
effect.

Pre-registering thresholds before the live run prevents results-driven promotion
criteria. Requiring every exact safety/isolation gate to pass keeps probabilistic
quality scores from compensating for a trust-boundary failure.

Safe dimensions are sufficient to diagnose route/component behavior without retaining
sensitive conversational or retrieval content in normal telemetry.

## Consequences

### Positive

- One evaluation/reporting stack covers the existing access suite and new routed
  suites.
- Exact product invariants and probabilistic quality metrics have separate owners.
- Router and retrieval overhead is measured rather than assumed.
- Versioned retained evidence supports comparison and re-baselining.
- Production logs/spans retain diagnostic structure without raw content.

### Negative and risks

- Credentialed promotion depends on configured router, policy, embedding, Search, and
  judge resources.
- Model-graded scores vary with judge model, package, prompt, and repetitions.
- Separate component instrumentation adds correlation and aggregation work.
- A small dataset can miss conversational or retrieval failures outside its inventory.
- Strict exact gates may block promotion on one failure even when aggregate quality is
  high; that is intentional for trust-boundary invariants.

## Alternatives considered

### Use only deterministic exact-match tests

Rejected because exact checks cannot grade semantic intent resolution, retrieval
ranking, groundedness, or answer relevance.

### Use only model-graded quality metrics

Rejected because a judge cannot establish zero side effects, exact tool/context
isolation, current citation membership, or retired filtering.

### Build a new generic evaluation/report store

Rejected because Microsoft reporting and the repository's existing isolated command
already own those concerns. Custom infrastructure is limited to product-specific
mapping and exact checks.

### Record raw content in normal telemetry

Rejected because it expands privacy and retention exposure and is unnecessary for
component-level duration, token, count, route, and outcome diagnosis.

### Promote on aggregate means alone

Rejected because averages can hide a failed safety-critical scenario or invalid
evaluator result. The specification combines aggregate quality thresholds with floors
and 100% exact gates.

## Revisit criteria

Revisit this decision if:

- the pinned Microsoft evaluation version changes metric definitions or score scales;
- evaluator variance makes the pre-recorded gates statistically misleading;
- the inventory expands enough to require sampling or a managed evaluation service;
- production traffic or real policy data introduces a separately approved privacy-safe
  monitoring requirement;
- a new route adds component dimensions not representable by the current evidence
  schema; or
- retained evidence cannot distinguish router, retrieval, specialist, and end-to-end
  regressions.

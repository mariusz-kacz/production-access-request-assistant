# ADR 0013: Ground Read-Only Policy Guidance with Authoritative Facts and Bounded Hybrid Retrieval

- **Status**: Accepted
- **Date**: 2026-09-04
- **Runtime status**: Target decision; not yet implemented or promoted
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-router-policy-evolution.md`,
  `docs/constitution-amendment-3.1.0.md`, ADR 0008, ADR 0011, and ADR 0012

## Context

The current application has deterministic production-access rules but no
requester-facing policy route or retrieval system. The target Policy Advisor must
explain a bounded synthetic policy corpus and support questions tied to a safe
projection of the active preparation. It cannot become a second source of truth for
rules that Core enforces, and it cannot gain the Access Request specialist's MCP tools
or any workflow mutation capability.

Policy documents are explanatory evidence, not authorization data. They may be stale,
retired, missing, adversarial, or inconsistent with current deterministic rules.
Retrieval configuration must therefore remain server-owned and failures must prevent
answer generation rather than fall through to model memory.

The fixed `PolicyAdvisorResult` contains free-form answer text and citation IDs, not a
complete structured representation of policy claims. Arbitrary prose cannot be proven
semantically consistent by deterministic code without another semantic model or a
more restrictive output contract. The target needs an explicit, bounded
interpretation of its runtime policy-consistency requirement.

## Decision

Create one provider-neutral `IAccessPolicySnapshotProvider` in Core. Its current
snapshot owns every policy fact shared by deterministic rules and Policy Advisor
context, initially including:

- the fixed eight-hour grant duration;
- ordered Business then DevOps approval stages;
- immutable submitted scope; and
- the rule that the requester cannot choose the business approver.

Refactor existing constants to consume the snapshot only when that can be proved
behavior-preserving. Retrieved documents may explain these facts but cannot redefine
them.

Back one provider-neutral `IPolicyKnowledgeSearch` port with Azure AI Search hybrid
retrieval. Use keyword and vector queries in one request, Azure's RRF fusion, and
server-owned active-status, effective-version/date, and policy-area filters. Return at
most three current chunks within approximately 1,500 tokens. Retrieval input may use
only the normalized current question, at most four recent Policy Guidance messages,
and bounded context terms derived from the safe `AccessPolicyReference`.

Integrate retrieval with the Policy Advisor through MAF `TextSearchProvider` in
`BeforeAIInvoke` mode with `RecentMessageMemoryLimit = 0`. Application-owned history
is the only conversational-memory policy. The Policy Advisor exposes no model-visible
tool; it cannot choose an index, filter, query mode, embedding deployment, result
bound, timeout, or token budget.

The Policy Advisor returns the fixed closed `PolicyAdvisorResult` contract:
`Answered`, `InsufficientEvidence`, or `Unsupported`, an answer compatible with the
outcome, and current-invocation citation IDs. `Answered` requires bounded nonblank
text and at least one current citation. The other outcomes contain no model-authored
visible answer. Unknown fields, unknown citations, incompatible outcome/answer values,
oversized output, unsafe rendering content, retrieval failure, or provider failure
fails the turn without fallback or repair.

### Bounded runtime policy-consistency interpretation

Keep the fixed free-form contract. Interpret “the answer must not contradict the
current authoritative policy snapshot” as the following layered control:

1. Runtime validation enforces the closed schema, answer/outcome compatibility,
   2,000-character visible limit, current-invocation citation membership, safe
   application-owned rendering, and the snapshot version used for the invocation.
2. `SnapshotClaimGuard` version 1 normalizes answer text with Unicode NFKC, invariant
   case folding, collapsed whitespace, and explicit sentence boundaries. It must
   recognize the minimum English grammar below; a no-op recognizer is non-conforming.
   Every recognized claim must match the snapshot or the turn fails. Negated or
   ambiguous polarity fails closed under the rules below. The guard must not infer or
   “correct” other arbitrary natural language.
3. Prompt/context construction labels the authoritative snapshot separately from
   untrusted retrieved explanation and requires the answer to follow the snapshot on
   any conflict.
4. Offline groundedness and relevance evaluation over reviewed synthetic cases checks
   semantic consistency that the finite runtime guard cannot prove. Promotion is
   blocked by the pre-recorded thresholds and exact product gates in the approved
   specification.

The required v1 recognition grammar is deliberately finite:

- duration: `access` or `grant` plus an ASCII integer or English number word one
  through twenty-four plus `hour(s)` or `day(s)`, with days converted to 24 hours;
- approval: both `business` and `DevOps` plus `before`, `after`, or `then`, or an
  `approval` statement naming one stage as `only`, `sole`, or `single`;
- submitted-scope mutability: a submit/submission term, a request/scope term, a
  change/edit/modify/amend term, and one of `can`, `may`, `allowed`, `cannot`,
  `may not`, `must not`, or `not allowed`; and
- business-approver choice: requester/user plus business approver plus a
  choose/select/nominate/pick term and one of the same modal forms, or the canonical
  non-requester form using business approver plus assigned/derived/determined and
  client/server/selected environment.

The guard treats `.`, `?`, `!`, `;`, and line breaks as sentence boundaries. Within a
recognized sentence, `no`, `not`, `never`, and `neither` are negation tokens. Duration
and approval-order/stage claims containing a negation fail closed. Boolean claims use
only `cannot`, `may not`, `must not`, and `not allowed` as negative forms and `can`,
`may`, and `allowed` as positive forms when not negated. Both polarities, a second
negation, or negation of assigned/derived/determined fails closed as ambiguous. A
mutability sentence produces `ScopeIsMutable` and is accepted only when it equals the
inverse of `SubmittedScopeIsImmutable`. A requester-choice sentence produces
`RequesterMayChooseBusinessApprover` and is accepted only when it equals the
same-named snapshot value. The implementation must not compare only extracted nouns or
numbers.

The canonical test matrix must accept eight hours, Business before DevOps, immutable
submitted scope requiring a new request, and server/client-derived business approver
selection. It must reject 4, 12, or 24 hours and one or two days; DevOps before
Business and a one-stage approval; editable submitted scope; and requester-selected or
nominated business approvers. It must also reject “access is not eight hours,”
“Business is not before DevOps,” and “the approver is not determined by the selected
environment.” The approved specification owns the exact normative wording if this
summary and that grammar ever diverge.

This is deliberately not a claim of complete semantic runtime proof. An unrecognized
paraphrase can pass structural runtime validation; that residual risk is accepted for
this synthetic read-only guidance feature. If complete fail-closed semantic proof
becomes a runtime requirement, the contract must change to structured claims and/or
application-owned templates before promotion of that stronger requirement.

## Rationale

One typed snapshot prevents deterministic enforcement and explanatory context from
drifting on the small set of facts the application itself owns. Bounded hybrid
retrieval represents the target enterprise-policy boundary while keeping query,
filter, and evidence selection outside model control.

Pre-invocation retrieval with zero provider-managed recent-message memory makes the
actual context observable and preserves ADR 0012's application-owned isolation rules.
Current citation membership gives a deterministic provenance boundary without
mistaking citations for proof of semantic correctness.

The layered policy-consistency interpretation is honest about the fixed free-form
contract. It blocks direct machine-recognizable conflicts at runtime and makes broader
semantic quality a promotion gate instead of introducing a second runtime verification
model or pretending a prose parser is complete.

## Consequences

### Positive

- Core and Policy Advisor context share one current versioned policy snapshot.
- The Policy Advisor remains read-only and has no Access Request/MCP capability.
- Server-owned filters prevent retired or out-of-scope chunks from reaching the model.
- Retrieval failure, citation forgery, and malformed output fail closed.
- Application-owned history and `RecentMessageMemoryLimit = 0` keep provider sessions
  from becoming hidden memory.
- The residual semantic-consistency limitation is explicit and tied to promotion
  evidence.

### Negative and risks

- Azure AI Search and embeddings add operator configuration, latency, cost, throttling,
  and availability dependencies.
- Hybrid retrieval may omit relevant evidence or rank an adversarial chunk highly.
- A finite contradiction guard cannot recognize every paraphrase and must remain
  synchronized with the policy snapshot.
- Model-graded groundedness and relevance are probabilistic and sensitive to evaluator
  model/version changes.
- The fixture corpus is synthetic and cannot establish correctness for real enterprise
  policy.

## Alternatives considered

### Let the Policy Advisor answer from model knowledge

Rejected because model knowledge has no current source boundary, citation membership,
or retired-version filter and may contradict deterministic policy.

### Expose Azure policy search as a model-visible MCP tool

Rejected because it would change the exact MCP catalog and let the model control
retrieval timing and arguments. Pre-invocation server-owned retrieval is sufficient.

### Put all policy prose in the authoritative snapshot

Rejected because Core needs a small typed rule source, not a generic policy content
system. Explanatory documents and deterministic enforcement have different change and
authority boundaries.

### Add a second runtime model to verify or repair each answer

Rejected because it adds cost, latency, correlated semantic failure, and a false sense
of deterministic proof. Invalid results fail rather than being repaired.

### Require structured claims or application-owned templates now

Rejected for this target because it would replace the approved small free-form answer
contract and substantially constrain useful policy explanation. It remains the
required next design if complete semantic runtime proof becomes non-negotiable.

### Add a generic ingestion or multi-index platform

Rejected because the checked-in 8-10-document fixture and one bounded index are enough
for this increment. Crawlers, upload UI, semantic-ranker tuning, and general enterprise
search remain out of scope.

## Revisit criteria

Revisit this decision if:

- the deterministic contradiction guard produces unacceptable false accepts or false
  rejects;
- real policy ownership, identity, retention, or confidentiality requirements replace
  the synthetic fixture;
- policy facts beyond the snapshot's four families become authorization-relevant;
- complete semantic runtime consistency becomes mandatory;
- Azure AI Search no longer meets the required filtering, ranking, availability, or
  cost boundary; or
- promotion evaluation repeatedly misses the pre-recorded retrieval, groundedness, or
  relevance thresholds.

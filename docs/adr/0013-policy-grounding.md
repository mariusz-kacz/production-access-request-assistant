# ADR 0013: Ground Read-Only Policy Guidance with Authoritative Facts and Bounded Hybrid Retrieval

- **Status**: Accepted; prose validation and fixture indexing partially superseded by [ADR 0015](0015-refine-router-policy-target-contracts.md) on 2026-09-07
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
more restrictive output contract. ADR 0015 records the approved removal of the prior
runtime contradiction-parser requirement; the decision summary below reflects that
amendment, not an implemented runtime change.

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
only the normalized current question, the complete-pair Policy Guidance window from
ADR 0012 (policy-filtered first, at most four messages/about 800 tokens), and bounded
context terms derived from the safe `AccessPolicyReference`.

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

### Runtime validation and semantic limits

Keep the small free-form `PolicyAdvisorResult`. Runtime validation enforces the closed
schema, answer/outcome compatibility, 2,000-character visible limit, current-invocation
citation membership, and safe application-owned rendering. Prompt/context construction
labels the authoritative snapshot separately and gives it precedence over untrusted
retrieved explanatory material.

Runtime validation does not prove arbitrary prose semantically correct or consistent
with every policy fact. Offline Groundedness and Relevance evaluation measures that
risk and blocks promotion below the approved thresholds, but does not guarantee each
live answer. A valid citation is not semantic proof. This residual risk is accepted
only for synthetic read-only guidance; deterministic access authorization is unchanged.

There is no mandatory runtime contradiction parser, replacement runtime verification
model, broader parser, mandatory structured claims, or new templating subsystem.

### Explicit fixture-index rebuild

The operator command rebuilds only the configured synthetic fixture index from the
current checked-in corpus. Successful completion must leave exactly the current
fixture's chunk IDs: removed documents, deleted chunks, and obsolete IDs are absent
and cannot be retrieved. Uploading only current IDs without removing stale ones does
not satisfy this contract.

Preserve the specified schema, stable IDs, embeddings, metadata, filtering,
`DefaultAzureCredential`, cancellation, explicit timeouts, and typed safe failures.
Failed or partial uploads and unverified final contents cannot report rebuild success;
a failed rebuild has no atomic-availability promise. All index mutations remain scoped
to the configured fixture index. No incremental synchronization, background ingestion,
aliases, multiple indexes, or generic ingestion platform is authorized.

Task 9 must verify: index the fixture, remove a document or chunk, rebuild, then assert
removed IDs are absent and unsearchable. Its controlled adapter matrix also covers
partial upload and failure; automated tests need no live Azure dependency.

## Rationale

One typed snapshot prevents deterministic enforcement and explanatory context from
drifting on the small set of facts the application itself owns. Bounded hybrid
retrieval represents the target enterprise-policy boundary while keeping query,
filter, and evidence selection outside model control.

Pre-invocation retrieval with zero provider-managed recent-message memory makes the
actual context observable and preserves ADR 0012's application-owned isolation rules.
Current citation membership gives a deterministic provenance boundary without
mistaking citations for proof of semantic correctness.

Structural validation and snapshot precedence bound the free-form contract without
claiming semantic proof. Policy semantic quality remains a promotion gate, not a
per-answer guarantee. A scoped fixture rebuild removes stale evidence without adding
an ingestion lifecycle.

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
- A structurally valid, correctly cited answer may still misstate policy facts.
- Rebuilding the fixture index may interrupt fixture search; failures must be visible
  and must not be reported as success.
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
contract and substantially constrain useful policy explanation. No replacement is
required or authorized by this amendment; a stronger requirement would need a separate
design decision.

### Add a generic ingestion or multi-index platform

Rejected because the checked-in 8-10-document fixture and one bounded index are enough
for this increment. Crawlers, upload UI, semantic-ranker tuning, and general enterprise
search remain out of scope.

## Revisit criteria

Revisit this decision if:

- evaluation or observed guidance shows unacceptable residual semantic risk;
- real policy ownership, identity, retention, or confidentiality requirements replace
  the synthetic fixture;
- policy facts beyond the snapshot's four families become authorization-relevant;
- complete semantic runtime consistency becomes mandatory;
- Azure AI Search no longer meets the required filtering, ranking, availability, or
  cost boundary; or
- promotion evaluation repeatedly misses the pre-recorded retrieval, groundedness, or
  relevance thresholds.

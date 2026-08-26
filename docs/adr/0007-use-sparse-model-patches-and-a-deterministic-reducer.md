# ADR 0007: Use Sparse Model Proposals and a Deterministic Reducer

- **Status**: Accepted
- **Date**: 2026-08-22
- **Clarified**: 2026-08-26
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `docs/adr/0008-separate-read-only-context-capabilities-by-authoritative-source.md`, `docs/adr/0009-persist-canonical-intake-and-bounded-clarification-context.md`, `docs/contracts/deterministic-request-intake-mcp-contract.md`

## Context

The delivered interpreter returns a complete nullable candidate on every turn and asks
the model to preserve previously accepted values. Durable draft preservation therefore
depends on probabilistic reconstruction. A schema-valid response can omit accepted
state, reproduce a stale snapshot, or replace a field that the requester did not intend
to change.

An earlier target design tried to reduce that risk by having deterministic code parse
selected phrases or prove that a proposal was textually supported by the latest
requester message. That creates a second natural-language interpreter beside the agent,
leaks language assumptions into Core, and fails for equivalent multilingual or
paraphrased requests.

The target design also needs an explicit answer for mixed patches. A proposal may
contain several structurally valid operations where one enterprise lookup fails or one
relationship is incompatible. Leaving whole-turn versus field-level behavior implicit
would produce incompatible implementations.

## Decision

Every authenticated requester free-text turn except exact `/new` is interpreted by one
model-backed agent. No other deterministic component assigns business meaning to
requester free-text.

The agent returns one closed, provider-neutral `TurnProposal` with exactly one dialogue
act and its permitted payload:

- a sparse patch containing explicit `set` or `clear` operations;
- one bounded discussion topic; or
- no mutation payload for submission intent, unrelated, or unclear turns.

When active clarification choices exist, the bounded agent input includes their stable
display order, exact canonical IDs, and safe authoritative distinguishing fields. A
clarification reply uses the same ordinary sparse exact-ID environment or role
operation as every other update, or `unclear` when the reference is not safely
resolvable. The choices are semantic context, not an authorization allowlist.

A sparse patch may propose changes only to environment, role, justification, and
optional incident. Omitted fields mean no proposed change. There is no required `keep`
operation.

Core receives no requester free-text as proposal-validation or reducer input. It owns:

- closed schema and act/payload compatibility validation;
- canonical state and field-specific equality;
- deterministic effects of structured `set` and `clear` operations;
- authoritative environment, client, role, and incident search/reloads;
- fixed cross-field evaluation order and dependency cascades;
- deterministic clarification-context consumption, preservation, and replacement;
- readiness, lifecycle, persistence, and typed outcomes.

Core validates whether a structured proposal is legal, coherent, and supported by
current authoritative enterprise data. It deliberately does not prove that requester
wording linguistically entails the proposal.

### Structural and data-level rejection

The reducer uses a two-tier model:

1. **Structural violation** — unknown act, field, operation, payload combination,
   malformed value, or provider-contract translation failure rejects the entire turn
   immediately with zero mutation and no model repair invocation.
2. **Data-level operation result** — unknown/ineligible environment, unavailable role,
   incompatible incident, source failure, or dependency failure rejects only the
   affected and dependent operations. Independent accepted operations commit atomically
   with any resulting clarification context.

The normative evaluation order is:

1. environment;
2. incident;
3. coherent final environment/client scope;
4. role against final environment;
5. justification;
6. dependency cascades;
7. at most one clarification, with environment before role; and
8. clarification-context lifecycle and readiness.

There is no unspecified partial-success escape hatch.

### Justification provenance

Justification remains requester-authored content in the requester’s language. The agent
may extract it from conversational framing, trim outer whitespace, normalize line
endings, and combine explicitly requested edits with the existing canonical value. It
must not translate, summarize, polish, invent rationale, or add facts.

Core enforces storage constraints but does not compare the proposed justification with
raw requester text. Provenance quality is governed by the agent contract, targeted live
evaluation, mandatory ready-card review, and human approval.

### Consequential boundaries

Natural-language reset and submission intent are proposals only:

- exact `/new` is the sole deterministic requester-text reset protocol; and
- authenticated Adaptive Card confirmation is the sole request-creation path.

The model cannot set requester identity, client, duration, preparation/request identity,
workflow status, approver, approval, provisioning, retry, audit, or grant data. It
receives no state-changing business tool.

Application code renders every requester-visible response. No model-generated prose or
raw MCP text reaches the requester.

## Rationale

Sparse proposals make omission safe. A model that loses context or emits only the
current change cannot erase unrelated canonical fields merely by failing to restate
them.

Keeping all free-text interpretation at one boundary prevents phrase dictionaries,
identifier extractors, numeric/ordinal resolvers, or evidence matchers from becoming a
second NLP system. English, Polish, Spanish, and other language variants produce the
same Core contract.

Using the ordinary sparse patch for clarification removes a second structured mutation
protocol. Core exact-reloads and validates every proposed ID through the same reducer,
while snapshot plus optimistic concurrency prevents proposals interpreted against
changed candidate or clarification context from committing.

Deterministic security remains strong without deterministic language reproduction:
enterprise identities and relationships are exact-reloaded, state/lifecycle rules are
deterministic, free-text cannot create a request, and the requester must review and
confirm exact canonical scope.

The explicit two-tier rejection model keeps multi-operation turns useful while avoiding
arbitrary implementation choices. Structural corruption cannot partially apply;
independent valid business changes need not be discarded because another source or
field failed.

Separating interpretation from reduction keeps Core independent of MAF, MCP, Teams,
and provider SDK types. Core can be tested with directly constructed proposals, while
linguistic and provenance quality is evaluated at the agent boundary.

## Consequences

### Positive

- Model omission and context loss cannot erase canonical fields through an incomplete
  snapshot.
- All requester-language interpretation has one owner.
- Core remains deterministic, provider-neutral, and testable without language corpora.
- Clarification references and ordinary updates share one proposal and reducer path.
- Enterprise identifiers and relationships are independently revalidated.
- Mixed patches have one normative ordering and partial-success policy.
- Justification provenance is explicit rather than silently model-authored.
- Model prose cannot claim a transition that did not commit.
- The project demonstrates a credible enterprise GenAI boundary: probabilistic
  interpretation, deterministic state/authority, and explicit confirmation.

### Negative and risks

- Every non-`/new` free-text turn, including short numeric or apparently obvious input,
  incurs agent latency and provider-failure risk.
- A model can propose a structurally valid but semantically unintended change. Core
  rejects illegal/non-authoritative values but does not reinterpret the sentence.
- Partial success requires typed per-operation outcomes and clear requester rendering.
- Justification provenance cannot be proven by Core without recreating language
  interpretation; it requires targeted evaluation and human review.
- The closed proposal/reducer/outcome model adds code compared with trusting one model
  snapshot.
- Application-owned conversation is intentionally bounded rather than open-ended.

## Alternatives considered

### Continue returning a complete candidate

Rejected because every turn would still ask the model to reproduce durable state and
would preserve the snapshot-loss failure mode.

### Add deterministic natural-language shortcuts

Rejected. Parsing `clear environment`, `first`, exact IDs, or similar input outside the
agent creates a language-specific parallel interpreter. Exact `/new` remains the sole
protocol exception.

### Keep a separate clarification-selection protocol

Rejected because target/index payloads, index-to-ID conversion, selection-specific
freshness checks, and distinct outcomes duplicate the ordinary exact-ID patch path.
Ordered choices remain bounded agent context; Core authority still comes from exact
reload and deterministic reduction.

### Make Core verify linguistic evidence

Rejected. Matching proposed values, clears, or paraphrases to requester wording would
require Core to reproduce natural-language interpretation and would not generalize
across languages.

### Reject every mixed patch when one data-level operation fails

Rejected because it discards independent safe updates and makes source availability
needlessly destructive. Structural violations still reject the entire turn.

### Apply every valid-looking operation independently without ordering

Rejected because environment, incident, and role are dependent. A fixed scope-first
order and explicit conflict rules are required.

### Let the model call a state-changing draft tool

Rejected because canonical mutation would depend on probabilistic tool selection and
would blur interpretation, state ownership, and business authority.

### Require `keep`, `set`, or `clear` for every field

Rejected because required `keep` operations add tokens, encourage snapshot-shaped
output, and make omission a schema failure even though omission is the safest no-op.

## Revisit criteria

Revisit this decision if:

- measured evaluation shows the closed sparse proposal materially harms correct
  interpretation despite prompt/schema improvements;
- a non-LLM structured requester UI becomes the primary intake path;
- product requirements add another formally specified protocol command with a separate
  threat review and contract;
- a governed state-changing agent capability is proposed with a separate authorization
  design; or
- the mandatory confirmation boundary is removed or materially changed.

Any replacement must preserve deterministic canonical state ownership, independent
enterprise revalidation, and the rule that requester free-text cannot directly create,
approve, provision, retry, revoke, or grant access.

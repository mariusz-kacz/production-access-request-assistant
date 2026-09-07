# ADR 0012: Use Deterministic Routing and Isolated Specialist Contexts

- **Status**: Accepted; history ordering, windows, and overflow partially superseded by [ADR 0015](0015-refine-router-policy-target-contracts.md) on 2026-09-07
- **Date**: 2026-09-04
- **Runtime status**: Target decision; not yet implemented or promoted
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-router-policy-evolution.md`,
  `docs/constitution-amendment-3.1.0.md`, ADR 0007, ADR 0008, and ADR 0009

## Context

The current Teams boundary sends every ordinary nonblank text message directly to the
Access Request preparation orchestrator. Exact `/new`, blank input, and Adaptive Card
confirmation are deterministic protocol paths. The Access Request specialist receives
only the latest requester message, canonical preparation, lifecycle, and active
clarification choices; its closed sparse proposal is applied by deterministic Core
services.

The target adds production-access policy guidance in the same Teams conversation. A
classifier must distinguish request-preparation work from policy questions and must
support deliberate policy detours without contaminating the existing access context.
Treating that classifier as an autonomous supervisor or giving both specialists a
shared transcript/tool set would obscure dispatch, expand model capability, and make
context isolation difficult to prove.

ADR 0009 deliberately rejected complete conversation persistence for request intake.
The target needs limited continuity for routing and policy follow-ups, but that context
does not need to become part of the canonical request preparation.

The history sections below summarize the current target after ADR 0015; the original
2026-09-04 approval and the unchanged routing/isolation decisions remain in force.

## Decision

Keep direct protocol actions at `TeamsRequestHandler`. Blank text, exact trimmed
case-insensitive `/new`, and card confirmation bypass the router and all specialists
that are not already part of their current deterministic path. Only ordinary nonblank
text enters a plain `RoutedTurnCoordinator`.

For each ordinary text turn:

1. construct one compact router envelope from the normalized current message (the
   authenticated Teams text after trimming surrounding whitespace only), a bounded
   recent cross-route window, and minimal active-access clarification context;
2. invoke one schema-bound model router with no tools;
3. reject unknown fields, enum values, or incompatible route/context combinations;
4. select a fixed branch through deterministic application code; and
5. invoke zero or one specialist with that normalized message unchanged, without a
   router-produced rewrite, summary, translation, or semantic normalization.

The five closed routes are `AccessRequest`, `PolicyGuidance`, `Mixed`, `Unclear`, and
`Unsupported`. `Mixed`, `Unclear`, and `Unsupported` invoke no specialist and receive
application-owned responses. No malformed response is repaired, no mixed turn is
decomposed or queued, and no unclear turn creates pending state or replays the original
message.

Do not use MAF workflow orchestration, planners, supervisors, dynamic handoffs, group
chat, recursive delegation, parallel specialists, or free-form agent-to-agent
messages. The router is a classifier behind an application boundary, not an agent with
authority to delegate.

### Context and capability isolation

Construct distinct typed envelopes rather than one shared conversation:

- **Router:** normalized current message unchanged after boundary trimming; whether an
  active preparation exists; active clarification target and safe choice labels when
  present; and a complete-pair window capped at four messages/about 600 tokens. It
  receives no complete canonical candidate, requester justification,
  approval/provisioning state, tools, or retrieved policy chunks.
- **Access Request:** the normalized current message unchanged, canonical preparation,
  lifecycle, clarification, and exact four-tool MCP context. It receives no routed
  history, policy answer, Policy Advisor prompt, safe policy projection, or retrieval
  evidence.
- **Policy Advisor:** normalized current question unchanged after boundary trimming;
  a policy-filtered complete-pair window capped at four messages/about 800 tokens; the
  authoritative policy snapshot; an optional minimal `AccessPolicyReference`; and
  fresh current evidence. It receives no Access Request MCP tools or workflow mutation
  port.

Application code validates `RouteContextReference.ActiveAccessPreparation` against the
current active preparation. Router output is untrusted advice even after schema
validation; downstream capability isolation must make a wrong but valid route safe.

### Bounded route history and ADR 0009

ADR 0009 remains authoritative and is not superseded. Canonical preparation,
clarification choices, lifecycle, optimistic concurrency, and ready identity remain
the Access Request specialist's only durable memory. General routed history never
participates in request validation, authorization, approval, provisioning, or audit
evidence and never enters Access Request interpretation.

Add a separate application-owned projection containing only requester text normalized
by boundary trimming and final validated application-rendered assistant text for
completed `AccessRequest` and `PolicyGuidance` turns. Each turn contributes one pair,
subject to the overflow rule below. Persist explicit pair order per exact authenticated
conversation binding, with requester always before assistant. Successful concurrent
appends establish one durable order; timestamp plus arbitrary GUID sorting is not the
conversation-order contract. Reads and pruning cannot interleave or split pairs.

Append and oldest-whole-pair pruning are atomic. Retain at most six complete pairs
(12 messages), each message at most 2,000 characters. If either message exceeds that
storage limit, omit the entire pair from reusable history. Do not silently truncate
semantic content, reject otherwise valid input, change Access's existing
4,000-character input limit, or roll back/replay authoritative state. Safe metadata
may indicate omitted continuity without logging content. Cards use safe application
plain-text projections, never raw JSON. Exclude prompts, reasoning, provider sessions,
complete model/tool/retrieval objects, and non-executable or failed turns.

Build windows from complete pairs, in chronological persisted order. Router eligibility
includes both executable routes; apply policy-route filtering before selecting Policy
Advisor's window. Select the newest contiguous suffix of eligible pairs fitting both
the four-message and respective 600/800 approximate-token caps. Starting newest, stop
at the first older pair that cannot fit; never skip it to include smaller unrelated
older context. An empty window is valid when the newest eligible pair cannot fit.

Task 6's canonical persistence matrix must cover equal timestamps and GUIDs contrary
to pair order, requester-first reads, concurrent appends/reads, atomic whole-pair
pruning, either/both messages oversized, exact-limit messages, and restart. Task 7
owns count/token selection and policy-filter-before-window cases, including a
non-fitting older pair before a smaller earlier pair and an empty newest-pair window.
Do not add semantic memory, summaries, retention workflows, or general orchestration.

Exact `/new` resets only the active unsubmitted access preparation. It bypasses the
router, creates no routed-history entry, and does not erase prior bounded policy
history. Policy-history deletion/retention is a separate product and privacy decision.

If a history write fails after an authoritative Access Request commit, do not roll
back or replay access state. Return or record a safe degraded-continuity outcome; the
non-authoritative projection cannot control the authoritative transaction.

## Rationale

A deterministic switch makes the number of model calls and available capabilities
visible in source and test evidence. It also preserves direct protocol bypasses and
the existing Access Request boundary.

Typed route-specific envelopes make absence testable. The security property is not
that a model always routes correctly; it is that every valid route has only the
capabilities and data needed for its bounded responsibility.

The separate 12-message projection supports the target's policy continuation and
route-switching examples without reversing ADR 0009's decision about canonical access
memory or creating a transcript platform.

## Consequences

### Positive

- Each ordinary text turn has one router call and at most one specialist call.
- Existing `/new`, blank-input, card-confirmation, and Access Request behavior retain
  explicit owners.
- Policy history and RAG evidence cannot silently enter access interpretation through
  a shared session.
- Ambiguous and mixed input fails conservatively without hidden queued work.
- Restart-safe policy continuity is possible without treating conversation as
  authoritative state.
- Captured typed envelopes can prove both required content and required absence.

### Negative and risks

- Every ordinary text turn pays router latency and token cost.
- A valid but wrong route can give an irrelevant safe response even though capability
  isolation prevents a consequential action.
- Persisted requester/assistant text adds a privacy, retention, migration, and pruning
  responsibility.
- Access results and non-authoritative history cannot share one transaction without
  coupling route history to the authoritative workflow boundary.
- Whole-pair omission and four-message/token-capped windows intentionally lose
  conversational nuance and may require the requester to restate intent.

## Alternatives considered

### Use MAF workflow orchestration or autonomous handoffs

Rejected because two fixed routes require only a schema-bound classifier and switch.
Autonomous orchestration would add control flow, delegation, and failure behavior that
the product does not need.

### Let the router call specialists directly

Rejected because model-controlled invocation would obscure the at-most-one guarantee
and mix classification with capability selection. Deterministic code must own dispatch.

### Share one transcript and tool set across both specialists

Rejected because policy evidence/history could influence access interpretation and the
Policy Advisor could observe request tools. A shared context defeats the main boundary
the routed design exists to demonstrate.

### Persist complete history or provider sessions

Rejected for the privacy, retention, token, coupling, and migration reasons recorded
in ADR 0009. The small route-tagged projection is sufficient for the named scenarios.

### Add pending clarification or replay for routing ambiguity

Rejected because it adds persisted control state and stale-message semantics. A new
explicit user message is safer and simpler.

## Revisit criteria

Revisit this decision if:

- evaluation shows that the minimal router projection cannot meet the pre-recorded
  route gates without adding a specific safe field;
- a third executable responsibility is approved and the fixed switch no longer
  communicates the complete bounded topology;
- privacy or retention policy requires time-based deletion or forbids the routed-text
  projection;
- multi-instance deployment requires coordination beyond the planned database/OCC
  boundary;
- the product requires durable mixed-intent queues or replay; or
- an authoritative submitted-request status route is separately specified and
  approved.

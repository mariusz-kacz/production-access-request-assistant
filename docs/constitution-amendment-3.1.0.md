# Constitution Amendment 3.1.0: Authorize Bounded Routing and Policy Grounding

- **Status:** Approved on 2026-09-04 for target implementation; not yet promoted as
  current runtime behavior
- **Date:** 2026-09-04
- **Target constitution version:** `3.1.0`
- **Affected principles:** I. Human Approval, Deterministic Authorization; II.
  Untrusted AI, Bounded and Governed MCP; V. Proportionate Modular Architecture;
  Change Design and Delivery
- **Related artifacts:** [target specification](../SPEC-router-policy-evolution.md),
  [ADR 0012](adr/0012-router-and-context-isolation.md),
  [ADR 0013](adr/0013-policy-grounding.md), and
  [ADR 0014](adr/0014-routed-evaluation-and-observability.md)

## Motivation

Constitution version 3.0.0 permits bounded model interpretation and an exact governed
MCP catalog, but the current product baseline deliberately excludes multi-agent
design, large retrieval, and durable conversation history. The routed-assistant target
needs a narrower capability that those exclusions do not distinguish clearly:

- one schema-bound model classifier followed by deterministic dispatch;
- the unchanged Access Request specialist;
- one read-only Policy Advisor;
- small application-owned route-tagged history used only as conversational context;
  and
- bounded Azure AI Search retrieval over synthetic production-access policy.

Without an amendment, implementing those components could quietly broaden the
architecture beyond the current governance boundary. The amendment authorizes only
the named target and adds obligations that preserve the existing human-approval,
deterministic-authorization, single-host, exact-MCP, and synthetic-data rules.

## Amendment

Constitution version `3.1.0` permits exactly the following target architecture:

1. One model-based turn classifier may classify an ordinary nonblank Teams text turn
   into a closed route contract. Deterministic application code validates the result
   and invokes zero or one fixed specialist. The router has no tools and cannot
   authorize, mutate, repair, decompose, delegate, or replay work.
2. The existing Access Request specialist retains its current canonical preparation
   context and exact four-tool read-only MCP catalog. It receives no general routed
   history, policy answer, Policy Advisor prompt, or retrieval evidence.
3. One Policy Advisor may answer production-access policy questions. It is read-only,
   has no MCP or state-changing capability, and receives evidence selected before
   model invocation through bounded Azure AI Search hybrid retrieval behind a
   provider-neutral application port.
4. Application-owned history may store only completed executable-route requester and
   assistant message pairs, with the target specification's 12-message and
   2,000-character-per-message limits. The history is non-authoritative context; it is
   not a transcript, audit log, workflow record, provider session, or authorization
   input.
5. The router, Access Request specialist, and Policy Advisor receive separately
   constructed contexts, tools, model settings, timeouts, and output bounds. A failure
   in routing, retrieval, or one specialist cannot fall through to another route.
6. Policy evidence, policy history, model output, and citations remain untrusted.
   Server-owned filters select current evidence, citations must belong to the current
   invocation, and Policy Guidance cannot change preparation, request, approval,
   provisioning, operation, or grant state.

The amendment retains without qualification:

- one ASP.NET Core executable and the existing thin co-hosted React UI;
- synthetic identity, reference data, policies, provisioning, and grants;
- human business and DevOps approval;
- deterministic authorization and request-keyed provisioning;
- immutable submitted scope and the fixed eight-hour grant;
- the exact current four-tool MCP endpoint and catalog; and
- the prohibition on real production access.

It does not authorize planners, supervisors, dynamic handoffs, group chat, recursive
or parallel delegation, generic multi-agent orchestration, generic enterprise search,
model-visible retrieval tools, another MCP capability, a general memory framework,
another vector store, another deployable service, or distributed infrastructure.

The ratified constitutional wording is recorded in
[the project constitution](constitution.md).

## Compatibility impact

This is a MINOR constitutional change. It materially expands the permitted read-only
AI context and interpretation obligations without removing or weakening an existing
principle. The amendment does not change the current runtime by itself.

Until the routed implementation passes its deterministic and retained live evidence
gates:

- the current product baseline, architecture, security model, and intake-orchestration
  documents continue to describe the sole as-built runtime;
- every ordinary nonblank Teams text turn continues to enter the existing Access
  Request preparation path; and
- no current/as-built document may claim that routing, routed history, the Policy
  Advisor, or Azure policy retrieval is live.

The target specification and accepted target ADRs authorize implementation decisions;
they do not substitute for runtime evidence or promotion.

## Security and privacy impact

The classifier adds a probabilistic dispatch boundary and Policy Guidance adds a
retrieval/content boundary. Misrouting must remain safe because deterministic code
validates the closed route, dispatches at most one fixed specialist, and gives each
specialist only its route-specific capabilities. Neither route can approve or execute
access.

Routed history creates a new persistence and privacy surface. Its strict content and
size limits, oldest-first pruning, exclusion from Access Request interpretation, and
non-authoritative status prevent it from replacing canonical preparation or audit
evidence. A later retention/deletion requirement requires a separate review rather
than silent expansion.

Retrieved policy is untrusted even when Azure AI Search returns it. Server-owned
active/effective filters, bounded result counts, prompt-injection-resistant context
labelling, current-invocation citation membership, an authoritative policy snapshot,
and fail-closed retrieval behavior constrain incorrect or malicious content. Free-form
semantic consistency is governed by the bounded interpretation recorded in the target
specification and ADR 0013; it is not represented as a complete runtime proof.

Raw prompts, routed messages, model answers, retrieval queries/chunks, reasoning,
complete MCP payloads, and provider objects remain excluded from default logs and
telemetry.

## Migration and promotion record

1. Approval of this amendment authorizes the target implementation but performs no
   runtime or database migration.
2. Tasks 2 through 12 must build and verify the routed target while preserving the
   current Access Request, approval, provisioning, and exact MCP evidence.
3. Routed-history persistence may use the repository's explicit disposable-local-data
   reset procedure; no in-place compatibility promise is introduced by this amendment.
4. The target may be promoted only after a clean retained run meets the pre-results
   thresholds recorded in the approved specification and every exact safety/isolation
   gate passes.
5. Only then may the current product baseline, architecture, security, intake, and
   operator documents be reconciled to describe routed behavior as current.

## Rejected alternatives

### Treat the classifier and Policy Advisor as unrestricted multi-agent design

Rejected because autonomous delegation, tool negotiation, and agent-to-agent messaging
add capability and failure modes that the two fixed routes do not need. A plain
coordinator and deterministic switch are sufficient.

### Give both specialists the same history, tools, and retrieval context

Rejected because it would expose policy content and general conversation to access
interpretation and expose access tools to a read-only policy route. Typed isolated
envelopes make the capability boundary explicit and testable.

### Expose policy retrieval as another MCP tool

Rejected because the exact four-tool Access Request catalog must remain unchanged and
the Policy Advisor does not need model-controlled search. Server-owned retrieval before
invocation keeps filters, bounds, and failure behavior deterministic.

### Persist a complete transcript or provider session

Rejected because neither is authoritative and both expand privacy, retention, token,
migration, and provider-coupling costs. The permitted route-tagged projection is the
minimum conversational context required by the target scenarios.

### Update current/as-built documentation at authorization time

Rejected because acceptance of a target decision is not evidence that the runtime
implements it. As-built documentation remains unchanged until the promotion task.

## Approval record

The project maintainer approved Task 1 on 2026-09-04. This approval accepts
constitution version `3.1.0` and authorizes only the bounded target described above and
in the approved specification. It does not approve promotion, deployment, real data,
real access, any MCP catalog change, or any broader agent, retrieval, memory, or service
architecture.

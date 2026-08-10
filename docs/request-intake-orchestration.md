# Request Intake Orchestration

- **Status**: Current
- **Last reviewed**: 2026-08-10
- **Scope**: Teams preparation before human confirmation

## Authority

Requester messages, conversation history, model output, MCP wire data, and model prose
are untrusted. The model can interpret language and gather read-only context; Core
validates, canonicalizes, persists, and decides readiness.

A proposal must match the closed request-intake schema before Core sees it. A
schema-valid proposal is still not evidence that an identifier exists, a role is
assigned, an incident is active, or a request is ready. The model cannot submit,
approve, provision, retry, or revoke access.

## One intake turn

For each normal requester message, `RequestDraftService` performs one interpretation
and one authoritative candidate assessment:

1. Load or create the intake bound to the authenticated Teams actor and exact personal
   conversation.
2. Supply the latest requester message and complete accepted candidate to the
   interpreter.
3. Run one MAF turn with the closed output schema and exact two-tool MCP catalog.
4. Translate the proposal into Core types.
5. Validate every supplied identifier and relationship, derive authoritative values,
   clear rejected fields, and classify the candidate as rejected, incomplete, or
   ready.
6. Reload every structured environment clarification option before allowing its model
   message to be displayed.
7. Apply the ready-draft rules below when the intake was already ready.
8. Persist the sanitized candidate and lifecycle outcome, or return a discussion that
   requires no durable change.

Core does not ask the model to reinterpret the same message after deterministic
validation rejects a value. It returns typed correction guidance and waits for another
requester message.

## Ready-draft turns

An unexpired ready intake is an immutable confirmation snapshot, but it remains the
context for another natural-language turn.

| Assessed outcome | Existing ready intake | Teams response |
|---|---|---|
| Discussion or value-equal candidate | Preserved | Bounded answer; existing card remains active. |
| Incomplete candidate with an applicable focused clarification | Preserved | Clarification; existing card remains active while the requester decides. |
| Different ready candidate | Superseded | New ready intake and separate review card. |
| Rejected candidate | Superseded | New collecting intake and deterministic correction guidance. |
| Incomplete candidate without an applicable clarification | Superseded | New collecting intake and deterministic missing-field guidance. |
| Model, MCP, timeout, or dependency failure | Preserved | Safe failure; no replacement candidate is persisted. |

When a ready intake is superseded, its preparation ID remains terminal and cannot
confirm the replacement. The Teams adapter uses process-local activity metadata to
make a tracked old card non-actionable when it presents a replacement or correction.
If that visual update is unavailable, confirmation still reloads durable intake status
and rejects the stale card.

## Candidate assessment

Missing fields are allowed while collecting. Every supplied identifier is validated
before persistence.

| Proposed value | Deterministic result |
|---|---|
| Valid environment | Canonicalize `environmentId` and derive its client. |
| Unknown environment | Clear it and report `environment_not_found`. |
| Valid active incident | Canonicalize it and derive its required environment and owning client. |
| Unknown or inactive incident | Clear it and report the typed incident error. |
| Environment and incident conflict | Clear the incident and report the relationship error. |
| Supplied client conflicts with scope | Clear incompatible scope while preserving unrelated valid fields. |
| Role is not assigned to the selected environment | Clear `requestedRoleId` and report the role error. |
| Missing required field | Preserve the remaining sanitized candidate and report the missing field. |

Only `RequestDraftValidator` constructs new canonical `ValidatedRequestDetails` from a
mutable candidate. Confirmation and later trust boundaries revalidate that immutable
snapshot against authoritative context.

## Model tool policy

The allowed tools are:

- `get_production_environment`, called with `{}` for bounded discovery or one nonblank
  `environmentId` for exact lookup; and
- `get_incident`, called only with a precise requester-supplied incident ID.

The interpreter rejects any different catalog or tool lacking the read-only
annotation. It disables concurrent tool calls, bounds the function loop, and terminates
on unknown calls.

The model instructions require these behaviors:

- readable client, region, primary, or recovery wording uses bounded environment
  discovery;
- precise or identifier-like environment input uses exact lookup;
- exact `NotFound` for identifier-like input remains an unresolved exact identifier in
  the current policy and does not trigger discovery or fuzzy correction;
- client ID is derived from the authoritative environment;
- a role is proposed only when assigned to the selected environment;
- every role clarification lists all authoritative role IDs assigned to the selected
  environment, even when only one is available;
- incident titles, partial IDs, alerts, and descriptions are not converted into an
  incident ID;
- a validated incident constrains its environment and therefore the compatible client
  until the
  requester removes or replaces it;
- choosing to continue without an invalid or conflicting incident resumes resolution
  of the scope already supplied in the requester message rather than asking for that
  environment again; and
- relative replies are resolved only when the actual preceding clarification and its
  ordered choices exist in conversation history.

Tool use aids interpretation only. Core independently reloads proposed values after
every successful model turn.

## Clarification rendering

Environment clarification uses a structured `environmentOptionIds` list separate from
the model-authored question. The list may contain at most 20 unique stable IDs and may
be non-empty only for `environmentId` clarification.

Core reloads the complete list and orders it by stable ID. Only then may the Teams
adapter show the bounded model message and append authoritative client name,
environment name, stable ID, and assigned-role context. Unknown, duplicate, excessive,
or target-incompatible options suppress both the proposed message and choices. Values
that appear only in prose never become candidate data or selectable scope.

Role clarification has no separate option structure. The prompt requires the message
to list the selected environment's authoritative role IDs, while Core remains
responsible for validating the eventual selected role. Environment choices are the
only application-rendered structured clarification options in the current contract.

## Persistence and history

SQLite stores the intake binding, sanitized nullable candidate, status, timestamps,
correlation metadata, and—when ready—the immutable scope, reserved request ID, and
30-minute deadline. It does not store prompts, transcripts, model responses,
clarification choices, or serialized MAF sessions.

The native singleton MAF session store is keyed by intake ID. A process-local
coordinator retains one gate per intake and serializes load, execution, and successful
save. Sessions and gates are not removed when an intake becomes ready or terminal;
they remain until the host stops.

After restart, the next turn receives the durable candidate without earlier messages.
An ambiguous reply is clarified again. Confirmation and downstream workflow actions
never read conversation memory.

## Reset

An exact trimmed, case-insensitive `/new` command is intercepted before model or MCP
execution. It marks an active collecting or ready intake `Superseded`, or `Expired`
when its deadline has passed, and clears its candidate through the terminal domain
transition. It creates no replacement intake or request and cannot change a submitted
request. The next normal message creates a new intake ID with separate model history.

Text that merely contains `/new` is handled as ordinary requester text.

## Current limitation

Deterministic justification validation checks presence and length, not semantic intent.
Human approval therefore remains necessary even when the text is syntactically valid.
A requirement to reject malicious intent before confirmation needs a new explicit
deterministic policy and tests; it must not rely on the model.

Test ownership and live-model outcome evaluation are documented in the
[testing strategy](testing-strategy.md) and
[live-model evaluation guide](live-model-evaluation.md).

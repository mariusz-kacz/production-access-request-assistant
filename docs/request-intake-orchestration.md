# Request Intake Orchestration

- **Status**: Current
- **Last reviewed**: 2026-08-28
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

For each normal requester message, `RequestPreparationOrchestrator` and
`PreparationTurnService` perform one interpretation and one deterministic reduction:

1. Load or create the intake bound to the authenticated Teams actor and exact personal
   conversation.
2. Supply the latest requester message, canonical candidate, lifecycle, and any
   persisted bounded clarification choices to a fresh interpreter session.
3. Run one MAF turn with the closed output schema and exact four-tool MCP catalog.
4. Translate the proposal into Core types.
5. Evaluate environment, incident, and role as one atomic scope group and justification
   independently; reload every proposed authoritative identifier or execute the shared
   search policy; apply accepted changes and dependency cascades.
6. Persist at most one complete bounded environment or role choice set when Core needs
   clarification; application code owns all displayed guidance and option text.
7. Apply the ready-draft rules below when the intake was already ready.
8. Commit canonical candidate/context/lifecycle changes under one optimistic
   concurrency version, or return a non-mutating application-owned response.

Core does not ask the model to reinterpret the same message after deterministic
validation rejects a value. It returns typed correction guidance and waits for another
requester message.

## Ready-draft turns

An unexpired ready intake is an immutable confirmation snapshot, but it remains the
context for another natural-language turn.

| Assessed outcome | Existing ready intake | Teams response |
|---|---|---|
| Discussion, submission guidance, unrelated or unclear input | Preserved | Application-owned guidance; existing card remains active. |
| Value-equal or wholly rejected proposal | Preserved | No canonical change; existing card remains active. |
| Accepted material patch producing ready scope | Superseded | Predecessor-linked ready replacement and separate review card. |
| Accepted material or clarification-producing patch remaining incomplete | Superseded | Predecessor-linked collecting replacement with copied canonical scope and any bounded choices. |
| Model, MCP, timeout, or dependency failure | Preserved | Safe failure; no replacement candidate is persisted. |

When a ready intake is superseded, its preparation ID remains terminal and cannot
confirm the replacement. The Teams adapter uses process-local activity metadata to
make a tracked old card non-actionable when it presents a replacement or correction.
If that visual update is unavailable, confirmation still reloads durable intake status
and rejects the stale card.

## Grouped reduction

Omitted patch fields are no-ops. Environment, incident, and role form one atomic scope
group; justification is a separate content group. A rejected scope group cannot
partially mutate canonical scope, although a valid justification can still apply in
the same commit. Missing fields remain valid while collecting.

| Proposed value | Deterministic result |
|---|---|
| Exact eligible environment | Exact-reload, canonicalize `environmentId`, and derive its client. |
| Search query with one result | Exact-reload and apply the unique result. |
| Search query with two to five results | Preserve canonical scope and persist the complete ordered environment choices. |
| Search query with more than five results | Preserve canonical scope and return typed too-broad guidance without ranking or truncating. |
| Unknown/unavailable environment, incident, or role | Reject the complete scope group and preserve prior canonical scope. |
| Valid active incident | Canonicalize it and derive its required environment and owning client. |
| Environment/incident conflict or unavailable role | Reject the complete scope group; no dependent partial update. |
| Accepted environment or incident change | Apply deterministic role/incident cascades in the same scope transition. |
| Role omitted with exactly one assignable role in the final environment | Select that authoritative role and return an application-owned explanation that it was the only available role. |
| Role omitted with two to five assignable roles | Preserve canonical scope and persist the complete ordered role choices. |
| Missing required field | Persist the accepted partial candidate or clarification and remain collecting. |

Confirmation and later trust boundaries revalidate the immutable ready snapshot
against authoritative context.

## Model tool policy

The allowed tools are:

- `search_production_environments`, called with one structured query for bounded
  discovery;
- `get_production_environment`, called with one exact nonblank `environmentId`;
- `get_environment_roles`, called with one exact eligible `environmentId`; and
- `get_incident`, called only with a precise requester-supplied incident ID.

The interpreter rejects any different catalog or tool lacking the read-only
annotation. It disables concurrent tool calls, bounds the function loop, and terminates
on unknown calls.

The model instructions require these behaviors:

- readable client, region, primary, or recovery wording uses bounded environment
  discovery;
- precise or identifier-like environment input uses exact lookup;
- every exact environment outcome prevents later discovery in the same turn; exact
  `NotFound` remains unresolved and does not trigger discovery or fuzzy correction;
- client ID is derived from the authoritative environment;
- a role is proposed only when assigned to the selected environment;
- when the final exact environment has no selected role, the model loads its roles and
  proposes the exact role ID when exactly one role is assignable, even if the requester
  did not name it;
- a role proposal uses one exact authoritative role ID; Core independently selects a
  sole assignable role if the model omits it and produces the complete bounded role
  choice set when two to five roles remain unresolved;
- incident titles, partial IDs, alerts, and descriptions are not converted into an
  incident ID;
- a validated incident constrains its environment and therefore the compatible client
  until the
  requester removes or replaces it;
- choosing to continue without an invalid or conflicting incident resumes resolution
  of the scope already supplied in the requester message rather than asking for that
  environment again; and
- relative replies are resolved only against the active persisted clarification target
  and its ordered exact choices.

Tool use aids interpretation only. Core independently reloads proposed values after
every successful model turn.

## Clarification rendering

Core may persist one environment or role clarification context containing two to five
unique choices in stable application-owned order. Environment choices contain exact
IDs plus safe authoritative environment/client/region/classification fields; role
choices contain exact role IDs and display names. The renderer derives 1-based
positions from persisted order and owns all requester-visible prose.

A sole assignable role is not clarification context. Core selects it from authoritative
data, and the renderer tells the requester which role was selected and that no other
role was available for the environment.

On the next normal message, the agent receives the active target and exact ordered
choices. It expresses a safely resolved reference as the same ordinary exact-ID sparse
operation used elsewhere, or returns `unclear`. Core never maps an ordinal to an ID,
does not parse requester wording, and exact-reloads the proposed ID through the normal
reducer. A clarification choice is bounded interpretation context, not authorization.

## Persistence and history

Workflow SQLite stores the preparation binding, canonical candidate, lifecycle,
timestamps, one optimistic concurrency version, bounded material-change attribution,
and at most one environment/role clarification context. A ready preparation stores its
immutable scope and 30-minute deadline. It stores no prompts, transcripts, raw
requester messages, provider sessions, model reasoning, raw proposals, agent-authored
search queries, or complete MCP payloads.

Each turn creates a fresh provider session. After restart, the next turn receives the
durable candidate and active ordered choices, so displayed-choice references remain
available without provider conversation history. Confirmation and downstream workflow
actions use persisted state only.

## Reset

An exact trimmed, case-insensitive `/new` command is intercepted before model or MCP
execution. It marks an active collecting or ready intake `Superseded`, or `Expired`
when its deadline has passed, and clears its candidate through the terminal domain
transition. It atomically creates a clean collecting preparation with a new ID, creates
no request, and cannot change a submitted request.

Text that merely contains `/new` is handled as ordinary requester text.

## Current limitation

Deterministic justification validation checks presence and length, not semantic intent.
Human approval therefore remains necessary even when the text is syntactically valid.
A requirement to reject malicious intent before confirmation needs a new explicit
deterministic policy and tests; it must not rely on the model.

Test ownership and live-model outcome evaluation are documented in the
[testing strategy](testing-strategy.md) and
[live-model evaluation guide](live-model-evaluation.md).

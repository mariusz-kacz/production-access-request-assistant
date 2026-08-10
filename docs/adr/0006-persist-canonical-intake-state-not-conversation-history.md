# ADR 0006: Persist Canonical Intake State, Not Conversation History

- **Status**: Accepted
- **Date**: 2026-08-10
- **Decision owners**: Project maintainer
- **Related artifacts**: `docs/adr/0005-retain-terminal-request-intake-tombstones.md`, `docs/architecture.md`, `docs/security-model.md`, `docs/request-intake-orchestration.md`, `specs/002-teams-access-intake/data-model.md`

## Context

Natural-language request preparation needs enough history to interpret follow-up
messages such as relative answers, while confirmation and later workflow stages need
restart-safe state that cannot depend on a model or provider-owned transcript.

Persisting every Teams activity, prompt, model response, MCP exchange, and native
agent session would increase privacy, retention, schema-evolution, and provider
coupling concerns. Keeping the whole draft only in memory would lose accepted user
progress on restart and would make stale-card rejection and submitted replay depend on
ephemeral state.

The application therefore needs an explicit boundary between canonical application
state and best-effort conversational context.

## Decision

Persist only the compact application-owned state required to continue and govern a
request intake. Keep native conversation history and transport presentation metadata
process-local, and keep other per-turn data transient.

### Durable SQLite state

For request preparation, SQLite stores one `RequestIntakeSession` per intake with:

- the server-generated intake ID;
- authenticated channel, tenant, actor, conversation, and requester binding;
- lifecycle status;
- the latest sanitized nullable client, environment, role, justification, and
  incident candidate;
- the reserved request ID when the candidate becomes ready;
- creation, update, expiry, and submission timestamps;
- the latest correlation ID; and
- an optimistic concurrency version.

Only application validation can update the durable candidate or decide readiness.
When an intake becomes terminal, its candidate content is cleared and the remaining
tombstone is retained according to
[ADR 0005](0005-retain-terminal-request-intake-tombstones.md).

Persistence transitions are action-driven. There is no background expiration or
cleanup worker: a ready deadline is enforced and recorded when a later preparation or
confirmation action reloads the intake, while a collecting intake has no inactivity
deadline in the current baseline.

Confirmation separately persists the immutable access request and request-created
audit evidence. Approvals, provisioning operations, grants, and later audit events
are durable workflow evidence outside the conversational session.

### Process-local state

The host keeps the following only for its process lifetime:

- the native MAF conversation session, keyed by server-generated intake ID;
- one per-intake coordination gate that serializes native session load, agent turn,
  and successful save; and
- the latest Teams draft-card activity reference for each authenticated actor and
  conversation.

The native session may contain conversational messages and tool interaction context,
but it is untrusted convenience state. Only a completed schema-valid model turn is
saved to that session. Application validation may still reject or sanitize its
proposal, so native session content never establishes candidate truth.

### Not durably persisted

The application does not write the following to SQLite, an external conversation
store, workflow audit, or default logs:

- raw Teams activities;
- complete prompts, model responses, or conversation transcripts;
- serialized native MAF sessions;
- complete MCP request or response payloads; or
- application-owned clarification option lists.

Individual stable identifiers and the sanitized candidate values derived from a turn
may be persisted after deterministic validation. Correlation IDs and bounded outcome
metadata may be logged without logging the complete content that produced them.

## Rationale

The canonical candidate is small, provider-neutral, schema-controlled by the
application, and sufficient for deterministic validation and confirmation. It can be
reloaded after restart and supplied to a fresh model session without treating model
memory as authority.

Keeping rich conversation history ephemeral limits durable sensitive content and
avoids coupling the domain or database schema to MAF or a specific model provider.
Keeping transport card references ephemeral also avoids turning presentation state
into authorization evidence. Durable intake status remains the final defense when a
visible old card cannot be updated.

## Consequences

### Positive

- Accepted draft progress and lifecycle state survive application restarts.
- Confirmation, approval, and provisioning never depend on model history.
- The domain and SQLite schema remain independent of MAF and provider contracts.
- Durable storage excludes raw conversational and complete MCP content by default.
- A stale Teams card remains safe even when process-local activity tracking is lost.
- Tests can replace the chat client without recreating a provider session database.

### Negative and risks

- Restart loses conversational nuance. A relative reply such as “the first one” must
  be clarified again when its preceding choices are no longer in native history.
- Restart loses the activity reference used to proactively replace an old Teams
  card; the card may remain visually actionable until clicked and rejected from
  durable state.
- Native sessions and coordination gates grow for the process lifetime and currently
  have no inactivity eviction, terminal cleanup, or compaction.
- There is no durable transcript for debugging, conversation replay, evaluation, or
  dispute investigation.
- An abandoned collecting candidate can remain in SQLite indefinitely. An expired
  ready candidate also remains until a later action observes the deadline and performs
  the terminal transition that clears it.
- A model may remember a schema-valid proposal that deterministic application
  validation rejected. Every later turn must therefore continue to receive the
  durable sanitized candidate as canonical current state.
- Terminal intake metadata still remains in SQLite under ADR 0005 even though its
  candidate content is cleared.

## Alternatives considered

### Persist complete native MAF sessions

Rejected for the current baseline because it would add provider/framework schema
coupling, content retention and deletion obligations, migration and compatibility
work, and a risk that conversational history is mistaken for workflow evidence.

### Persist raw activities and reconstruct history

Rejected because raw transport payloads contain more data than request governance
needs. Reconstruction would also couple application behavior to Teams payloads and
would not make untrusted messages authoritative.

### Keep the complete intake only in memory

Rejected because accepted progress would disappear on restart, ready-card
confirmation could not reload immutable scope, and stale-card handling would lose its
durable lifecycle boundary.

### Persist an application-owned conversation summary

Deferred. A summary could improve restart continuity with less data than a complete
transcript, but it would still be model-derived, require its own schema and retention
policy, and could not replace the canonical candidate. No current product requirement
justifies that additional state.

## Revisit criteria

Revisit this decision when:

- multiple hosts require shared conversation continuity;
- restart continuity for relative or multi-turn discussion becomes a measured product
  requirement;
- privacy, legal, contractual, audit, or support requirements mandate a defined form
  of conversation retention or deletion;
- process-lifetime native-session or gate growth becomes material;
- a durable, provider-neutral conversation schema is introduced; or
- the Teams presentation model requires durable activity references.

Any superseding decision must define data ownership, encryption and access controls,
retention and deletion, schema migration, multi-host concurrency, failure recovery,
and how persisted conversation data remains excluded from authorization and workflow
evidence.

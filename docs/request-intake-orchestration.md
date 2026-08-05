# Request Intake Orchestration Rules

- **Status**: Current
- **Last reviewed**: 2026-08-05
- **Scope**: Teams request preparation before human confirmation

## Purpose

These rules define how untrusted model proposals become a compact, durable request
candidate. They prevent a weak or inconsistent model turn from corrupting previously
validated progress.

The governing boundary is:

> The model interprets requester language and gathers read-only context. The
> application validates, canonicalizes, persists, and decides readiness. Humans
> approve. Deterministic services authorize and execute.

## Authority

- Requester messages and model output are untrusted.
- Model output must match the request-intake proposal schema before the application
  uses it.
- A model proposal is never evidence that an identifier exists, a role is available,
  an incident is active, or a request is ready.
- Authoritative application data decides all of those facts.
- The model cannot submit, approve, provision, retry, or revoke access.

## Per-turn algorithm

For each non-command requester message, `RequestIntakeService` performs exactly one
interpretation attempt:

1. Load or create the collecting intake and its last accepted candidate.
2. Ask the model for one complete nullable candidate snapshot.
3. Schema-validate and translate the proposal.
4. Assess the candidate in one authoritative pass: validate every supplied value,
   clear rejected values, canonicalize or derive authoritative values, and determine
   whether the result is rejected, incomplete, or ready.
5. For an environment clarification, validate and reload every structured
   `environmentOptionIds` value before accepting its associated model message.
6. Persist either the ready candidate, a focused clarification with its sanitized
   candidate, or the sanitized rejected candidate and deterministic errors. Choice
   lists and rejected potential identifiers remain turn-local and are not persisted.
7. When a value or option set is rejected, return transparent application-owned
   correction guidance and wait for the requester to provide new information in a
   later turn.

The application never asks the model to reinterpret the same requester message after
authoritative validation has rejected a value.

## Partial candidate validation

Missing fields are allowed while intake is collecting. Every supplied identifier is
validated before it can be persisted. One assessment produces exactly one of three
application-owned outcomes: rejected with a sanitized candidate, incomplete with a
sanitized candidate and missing-field errors, or ready with complete canonical fields.
Readiness does not trigger a second set of authoritative lookups during the same turn.

| Proposed value | Deterministic behavior |
|---|---|
| Unknown client | Clear `clientId`, unless a valid environment or incident supplies the canonical client. |
| Valid environment | Canonicalize `environmentId` and derive its canonical `clientId`. |
| Unknown environment | Clear `environmentId` and report `environment_not_found`. |
| Valid active incident | Canonicalize `incidentId`; derive its client and, when present, environment. |
| Unknown or inactive incident | Clear `incidentId` and report the typed incident error. |
| Environment and incident disagree | Clear the conflicting incident and report the relationship error. |
| Valid client disagrees with environment | Clear the environment and any incompatible incident. |
| Role unavailable for a validated environment | Clear `requestedRoleId` and report the role error. |

Canonical ownership means a requester does not have to know an internal client ID.
For example, a valid `PROD-ALPHA-EU` lookup supplies `client-alpha`; a valid incident
may supply both the environment and client.

## Deterministic rejection and field preservation

Validation errors are typed and field-specific:

```json
{
  "field": "environmentId",
  "code": "environment_not_found",
  "message": "The selected production environment does not exist."
}
```

The application immediately persists the sanitized candidate and returns these errors
without another model call. Valid progress survives; rejected fields remain null; no
request, approval, operation, grant, or workflow audit entry is created. The next
model call occurs only after the requester sends another message and receives the
sanitized candidate as its current application context.

## Model and MCP obligations

The model receives exactly two read-only MCP tools:

- `get_production_environment`, which supports bounded discovery with `{}` and exact
  lookup with one nonblank `environmentId`, returning authoritative client context
  and the roles assigned to every returned environment; and
- `get_incident`, which supports only precise requester-supplied stable identifiers.

The model instructions require it to:

- call environment discovery directly when the latest message contains readable
  client or environment context without a precise or identifier-like value;
- call exact environment lookup first when a precise or identifier-like value is
  supplied or changed;
- use discovery as an exact-lookup fallback only after a typed `NotFound`; invalid
  input, timeout, cancellation, unavailability, malformed results, success, and any
  other outcome do not authorize fallback;
- use only the roles embedded in the applicable environment result; there is no
  separate role-listing tool;
- call `get_incident` only when a precise stable incident identifier is supplied or
  changed, never for a title, description, partial ID, or inferred reference;
- copy stable identifiers from authoritative tool results and derive `clientId` from
  the selected environment instead of asking the requester to choose it;
- return proposed environment clarification choices only in the separate structured
  `environmentOptionIds` field;
- never invent, normalize, silently correct, or translate an identifier into an
  unsupported value;
- preserve the current candidate unless the requester clearly changes a field; and
- resolve relative answers only when the restored conversation contains the question
  and ordering they refer to; otherwise ask a self-contained clarification.

`AllowMultipleToolCalls` remains false, so one model response requests at most one
tool call. The bounded function loop may still make sequential calls: exact
environment lookup, discovery after typed `NotFound`, and then exact incident lookup.
A fresh application-controlled gate is created for each turn and prevents discovery
after every exact result other than typed `NotFound`.

Tool use improves interpretation but is not an authorization boundary. Application
validation still reloads authoritative data after every proposal.

## Environment clarification boundary

The model owns one bounded conversational `message` and an untrusted shortlist of
stable IDs. The application owns whether either may be shown:

- `environmentOptionIds` must contain zero to 20 unique IDs and may be non-empty only
  for an environment clarification;
- every proposed ID is reloaded from authoritative environment context and ordered by
  stable ID;
- only after the complete option set validates does the Teams adapter render the
  model message as non-authoritative plain text and append authoritative client name,
  environment name, and stable ID choices;
- an unknown, duplicate, excessive, or otherwise invalid option set suppresses the
  associated model message and choices; and
- identifiers, names, relationships, or instructions appearing only in model prose
  never become selectable choices, candidate scope, workflow actions, approvals, or
  authorization evidence.

One fallback alternative requires developer confirmation, several require developer
selection, and none require focused correction. No alternative replaces a rejected
potential identifier until the developer responds and deterministic validation
accepts the new proposal.

## Readiness and persistence

- A model-declared `kind: candidate` cannot override missing-field or policy errors.
- The application alone decides whether the candidate is ready.
- `RequestIntakeService` performs one candidate assessment per interpretation; the
  strict validator is run again only at later persisted-state trust boundaries such as
  confirmation and submission.
- Collecting intake persists only the sanitized candidate and lifecycle metadata.
- Model transcript and native MAF session state remain process-local and are not
  stored in the workflow database.
- Confirmation is a separate authenticated action that reloads and revalidates the
  ready server-owned candidate before creating an immutable request.

## Reset command

An exact, trimmed, case-insensitive `/new` command is intercepted before model or MCP
execution. It does not become requester prompt history.

- A collecting or ready intake is marked `Superseded` through the domain lifecycle
  method, unless expiration already requires the terminal `Expired` state.
- A submitted request remains immutable and is not superseded.
- Process-local model history is abandoned through the new intake identity. The old
  per-intake coordination gate remains allocated until process shutdown but is no
  longer used by the replacement intake.
- The next normal message creates a new intake identity with no old transcript or
  candidate state.
- Text that merely contains `/new` is a normal requester message.

## Justification limitation

Current deterministic justification policy checks only that the value is present and
within the configured length bounds. It does not semantically classify intent.
Therefore wording such as `I want to break production` can become part of a ready
candidate if the remaining scope is valid.

This does not grant access: authenticated human approvals and deterministic
provisioning rules remain mandatory. If malicious-intent wording must be rejected
before confirmation, that requires a new explicit deterministic product policy and
tests. It must not rely on the model as the safety boundary.

## Offline test boundary

Automated tests do not implement a hand-written language model. Fixed-mode and
scripted chat clients return exact proposal payloads and record the requests sent to
them. Tests assert:

- schema translation;
- candidate canonicalization and field clearing;
- one authoritative candidate assessment per intake turn;
- single-pass interpretation and deterministic rejection;
- preservation of unrelated validated fields;
- sanitized persistence after rejection;
- model-history reuse, isolation, failure, and reset behavior;
- the exact two-tool MCP capability boundary, discovery/exact contracts, typed
  failures, and deterministic fallback gating;
- structured option validation, model-message suppression for invalid choices, and
  authoritative choice rendering for valid choices; and
- cancellation and typed provider failures.

Natural-language interpretation, relative-answer quality, latency, cost, and
provider safety are evaluated through the optional live-model semantic matrix in the
[feature-004 quickstart](../specs/004-resolve-context-identifiers/quickstart.md). It is
release evidence, not CI, and cannot confirm or submit a request.

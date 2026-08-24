# ADR 0007: Use Sparse Model Patches and a Deterministic Reducer

- **Status**: Proposed
- **Date**: 2026-08-22
- **Decision owners**: Project maintainer
- **Related artifacts**: `SPEC-deterministic-request-intake.md`, `docs/deterministic-request-intake-design.md`, `docs/evaluation/deterministic-request-intake-test-matrix.md`

## Context

The current request interpreter returns a complete nullable candidate on every turn
and is instructed to preserve previously accepted values. That design makes canonical
state preservation depend on probabilistic behavior. A model that loses context,
reconstructs a stale snapshot, or emits an unrequested value can accidentally propose
replacement of fields the requester did not discuss.

Schema-constrained output reduces malformed responses but does not solve ownership. A
schema-valid full snapshot can still be stale or unsupported. The application needs a
boundary that uses the model for language interpretation without asking it to own or
restate durable request state.

## Decision

The model will return:

- one closed dialogue act;
- a sparse patch containing only explicit `set` or `clear` operations for fields the
  model believes the current message changes; and
- a transient observation of any requester-backed environment search performed through
  MCP.

Omitted fields mean no proposed operation. There is no required serialized `keep`
operation.

A provider-neutral deterministic reducer in Core will own:

- current canonical candidate state;
- operation-evidence checks against the latest requester message or persisted ordered
  choice context;
- value-equal normalization;
- dependency cascades;
- authoritative search and exact reload;
- canonicalization and field-level rejection;
- readiness and lifecycle transitions; and
- the typed application outcome.

The model cannot set client, requester, duration, preparation/request identity,
workflow status, approver, decision, provisioning, or grant data. It cannot decide
that a candidate is valid, ready, submitted, approved, or provisioned.

Application code renders all authoritative requester-facing content. The model emits
no consequential response prose.

## Rationale

A sparse patch makes model omission safe: absence cannot erase a canonical value. The
reducer can reject unsupported mutations while preserving unrelated accepted state.
This makes state-loss and snapshot-shaped model errors observable rather than
consequential.

Separating interpretation from reduction also keeps Core independent of MAF, MCP, and
provider JSON contracts. The same deterministic rules can be proven with ordinary unit
tests and reused across transport or model adapters.

## Consequences

### Positive

- Canonical state no longer depends on the model reproducing a complete prior draft.
- Context loss, stale snapshots, and unrequested field changes cannot silently replace
  accepted values.
- Deterministic rules have a narrow provider-neutral test surface.
- Model behavior can evolve without moving readiness or authorization into the model.
- Application-owned responses cannot accidentally report a workflow transition that
  did not commit.
- The project demonstrates a clear enterprise GenAI boundary: probabilistic
  interpretation, deterministic state and policy.

### Negative and risks

- The application must define exact evidence rules for each mutable field.
- Some natural requester phrasing, especially clearing or paraphrased justification,
  may require deterministic guidance instead of being accepted automatically.
- The reducer and typed outcomes add code compared with directly trusting one complete
  model object.
- A model can still misunderstand intent; the design limits the state effect but cannot
  guarantee perfect classification.
- Application rendering is less conversational than free model prose unless bounded
  help is designed deliberately.

## Alternatives considered

### Continue returning a complete candidate

Rejected because it preserves the core failure mode: every turn asks the model to
restate durable state correctly.

### Let the model call a state-changing draft tool

Rejected because it would place canonical mutation behind probabilistic tool selection
and would blur the boundary between interpretation and application state.

### Require `keep`, `set`, or `clear` for every field

Rejected because required `keep` operations add tokens, invite snapshot-shaped output,
and make omission a schema failure even though omission is the safest no-op semantic.

### Use deterministic natural-language parsing for all fields

Rejected because it would duplicate the model's useful role and create a growing
second language parser in Core. Core validates evidence and policy rather than trying
to understand unrestricted language.

## Revisit criteria

Revisit this decision if:

- measured live evaluation shows the sparse contract materially reduces correct
  interpretation despite prompt and schema improvements;
- a non-LLM structured requester channel becomes the primary intake path;
- domain policy requires semantic justification classification rather than syntactic
  authorship checks; or
- a formally governed state-changing agent capability is proposed with a separate
  threat model and authorization design.

Any replacement must preserve deterministic canonical state ownership and must not
make model memory, model prose, or model-reported validation authoritative.

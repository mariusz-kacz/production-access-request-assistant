# Feature Specification: Exercise the Real Conversational Model

**Feature Branch**: `[003-exercise-real-model]`

**Created**: 2026-08-03

**Status**: Draft

**Input**: User description: "Allow a developer or reviewer to explicitly run the existing Teams request-intake journey with an approved real language model while preserving untrusted model output, authoritative validation, requester confirmation, human approval, deterministic provisioning, and safe failure boundaries."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Exercise the Real Conversational Model (Priority: P1)

A developer or reviewer can explicitly select an approved real-model execution
profile and exercise the existing personal Teams request-intake journey. The real
model may interpret the conversation and gather approved production context, but its
proposal remains untrusted. Authoritative application rules must validate every
identifier, relationship, role, incident, and required value before displaying a
confirmation. Requester confirmation, business approval, DevOps approval, and
provisioning retain their existing deterministic boundaries.

**Why this priority**: A real-model demonstration proves that the portfolio
application works beyond its deterministic test double without moving probabilistic
behavior into an authorization boundary.

**Independent Test**: Select a valid approved real-model profile, conduct a personal
Teams conversation containing a complete valid request, and verify that a
confirmation is prepared only after every proposed identifier and relationship has
been checked against authoritative application data. Confirming the card must create
the same immutable request and continue through the same human-governed workflow as
the deterministic profile.

**Acceptance Scenarios**:

1. **Given** an explicitly selected, valid real-model execution profile, **When** an
   authenticated requester describes a complete valid request, **Then** the model
   returns an untrusted typed candidate that is authoritatively validated before a
   confirmation is displayed.
2. **Given** the request is incomplete or ambiguous, **When** the real model processes
   the conversation, **Then** it asks one focused clarification and does not cause a
   request, approval, provisioning operation, or access grant to be created.
3. **Given** the model proposes an unknown or cross-client identifier, unsupported
   role, inactive incident, or inconsistent relationship, **When** the candidate is
   validated, **Then** the value is rejected with application-owned correction
   guidance and no confirmation is displayed until valid information is supplied.
4. **Given** the explicitly selected real-model profile is missing, invalid,
   unavailable, or exceeds the overall turn deadline, **When** a turn is attempted,
   **Then** the requester receives a safe failure outcome, the deterministic test
   double is not substituted, and no governed workflow state changes.
5. **Given** a valid real-model candidate has been prepared, **When** the requester
   confirms it and the human approvers record their decisions, **Then** request
   creation, approval, provisioning, replay, and audit behavior are identical to the
   existing governed workflow and cannot be performed by the model.

### Edge Cases

- The real-model profile is selected but one or more required profile values or
  credentials are absent.
- The selected profile refers to a model that is not on the application's approved
  list, or its configured capabilities cannot produce the required typed proposal.
- The provider accepts a request but becomes unavailable before returning a complete
  response.
- One or more production-context lookups fail, time out, are cancelled, or expose a
  catalog other than the exact approved read-only tool set.
- The model returns malformed structured output, extra properties, an unsupported
  proposal kind, an overlong clarification, or no usable proposal.
- The model returns a syntactically valid candidate with an unknown client,
  cross-client environment or incident, unsupported role, inactive incident, or a
  relationship that conflicts with authoritative data.
- A later turn fails after an earlier turn established a valid partial candidate;
  the last successfully accepted candidate remains unchanged.
- The overall turn deadline expires while the model is invoking an approved
  production-context lookup.
- A requester attempts to select, override, or name a model profile in conversation
  text or activity data.
- Multiple requesters use the real-model profile concurrently; conversation context
  and candidate state remain isolated by authenticated intake binding.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a server-controlled execution-profile choice
  that allows a developer or reviewer to select either the existing deterministic
  test profile or an approved real-model profile.
- **FR-002**: The system MUST require real-model use to be explicitly selected; a
  requester MUST NOT be able to select or alter the execution profile through Teams
  message text, activity data, card data, or another browser-supplied value.
- **FR-003**: The system MUST accept a real-model profile only when it is complete,
  internally consistent, and identifies a model approved for this application.
- **FR-004**: When an explicitly selected real-model profile is missing or invalid,
  the system MUST fail closed and return safe requester guidance when a turn is
  attempted.
- **FR-005**: When a real-model profile is selected, the system MUST NOT substitute
  the deterministic test profile or fabricate a successful interpretation after any
  profile, model, tool, timeout, cancellation, or response-validation failure.
- **FR-006**: The real model MUST receive only the latest authenticated requester
  turn, the bounded conversation context owned by the existing intake journey, the
  current accepted candidate, and application-owned validation feedback needed to
  prepare the next proposal.
- **FR-007**: The real model MUST be limited to exactly the existing read-only
  production-context capabilities: `get_production_environment`, `get_incident`, and
  `get_available_roles`.
- **FR-008**: The system MUST reject real-model execution if any approved context
  capability is missing or any additional model-visible capability is present.
- **FR-009**: The real model MUST NOT receive a capability for request confirmation,
  request creation, approval, provisioning, retry, revocation, authorization,
  workflow transition, arbitrary data access, or generic query execution.
- **FR-010**: Every real-model response MUST conform to the existing closed typed
  proposal contract before the application considers its contents.
- **FR-011**: Every identifier, role, incident state, and client relationship proposed
  by the real model MUST be canonicalized and checked against authoritative data
  before a confirmation can be displayed.
- **FR-012**: A model assertion that a candidate is complete or valid MUST NOT
  override a deterministic validation failure or missing required value.
- **FR-013**: For incomplete or ambiguous input, the system MUST display at most one
  focused clarification for the current turn and MUST NOT create a request,
  approval, provisioning operation, or access grant.
- **FR-014**: For an invalid but well-formed candidate, the system MUST identify the
  rejection as application-owned authoritative validation feedback and MUST withhold
  confirmation until a later candidate passes validation.
- **FR-015**: The real-model interpretation and any approved context gathering MUST
  share one overall turn deadline and MUST honor requester cancellation.
- **FR-016**: A timeout, cancellation, dependency failure, or malformed model output
  MUST produce a closed safe outcome and MUST leave the last successfully accepted
  candidate and saved conversation state unchanged.
- **FR-017**: A failed real-model turn MUST NOT create a ready confirmation, access
  request, approval decision, provisioning operation, access grant, or downstream
  workflow audit event.
- **FR-018**: A successfully validated real-model candidate MUST enter the same
  immutable requester-confirmation flow used by the deterministic profile, with no
  model-specific confirmation or request-creation path.
- **FR-019**: Confirmed requests prepared by a real model MUST use the same
  authenticated ownership checks, authoritative revalidation, immutable scope,
  fixed duration, human approvals, idempotent provisioning, replay behavior, and
  audit evidence as every other request.
- **FR-020**: The system MUST isolate real-model conversation context and candidate
  state by the existing authenticated tenant, actor, and personal-conversation
  binding.
- **FR-021**: The system MUST preserve the deterministic test profile for automated
  tests, and the required automated test suites MUST complete without real-model
  credentials or network access.
- **FR-022**: The system MUST provide developer-facing setup and exercise guidance
  that identifies required profile values, secure credential handling, profile
  selection, a representative complete and incomplete conversation, expected safe
  failures, and cleanup steps without including a credential in source-controlled
  content.

### Governance & Trust Requirements *(mandatory)*

- **Authoritative actors and data**: The requester identity, tenant, actor binding,
  and personal conversation come only from the authenticated Teams boundary. Client,
  environment, role, incident, and approver facts come from the existing synthetic
  authoritative dataset. Execution-profile selection is controlled by the developer
  or reviewer outside requester-provided content.
- **State changes and authorization**: Model interpretation and context lookup do not
  authorize or perform a state change. Only authenticated requester confirmation can
  create the immutable request, and the existing authenticated business and DevOps
  actions govern approval. Deterministic services continue to authorize every
  transition and provisioning attempt.
- **Immutable scope and client isolation**: A confirmation is rendered only from an
  authoritatively validated, immutable prepared scope bound to one requester and
  personal conversation. Cross-client identifiers and relationships are rejected.
  Corrections require a new preparation and, after submission, a new request with new
  approvals.
- **AI and MCP boundary**: Real-model output remains untrusted and must satisfy the
  same closed proposal contract and authoritative validation as deterministic model
  output. The fixed model-visible capability set remains exactly
  `get_production_environment`, `get_incident`, and `get_available_roles`, all
  read-only. Capability visibility does not replace authorization.
- **Provisioning and idempotency**: Provisioning remains unavailable to the model and
  unchanged by this feature. It continues to reload persisted request and approval
  evidence, use the immutable request identifier for idempotency, and return the
  existing grant or safe outcome on replay.
- **Failure and audit evidence**: Profile validation, model invocation, context
  lookup, schema validation, cancellation, timeout, and dependency failures require
  closed safe outcomes. Operational evidence records correlation, selected profile
  identity, model and context-lookup duration, and outcome without recording secrets,
  raw prompts, transcripts, complete model responses, or complete context payloads.
  Existing workflow audit events begin only when the authenticated confirmation
  creates an access request.

### Key Entities

- **Execution Profile**: A server-controlled selection describing whether a run uses
  the deterministic test double or one approved real language model. Its meaningful
  attributes are profile identity, mode, approved model identity, availability for
  use, and the presence of required protected credentials; credentials themselves
  are not domain data or requester-visible content.
- **Request Proposal**: The complete nullable candidate and optional focused
  clarification returned by a model for one intake turn. It is untrusted until both
  structural and authoritative validation succeed.
- **Request Intake Session**: The existing authenticated conversation-bound record
  holding the current accepted candidate and immutable ready scope. A failed real
  model turn cannot advance it to readiness or alter its last successful content.
- **Access Request**: The existing immutable request created only by authenticated
  confirmation of a ready intake. Its approval, provisioning, grant, and audit
  relationships are unchanged.

### Verification Requirements *(mandatory)*

- **Domain/unit coverage**: Verify that model provenance cannot change candidate
  validation, readiness, immutable scope, owner binding, confirmation, approval, or
  provisioning rules. Existing domain tests continue to run entirely with
  deterministic inputs.
- **Integration/contract coverage**: Verify profile selection and validation,
  provider-boundary translation, the closed proposal contract, exact equality of the
  three model-visible read-only capabilities, shared turn deadline and cancellation,
  unchanged preparation/confirmation wiring, and safe operational evidence using
  deterministic provider substitutes. A separately documented manual acceptance
  exercise verifies one approved real-model profile without becoming an automated
  test dependency.
- **Negative coverage**: Verify missing and invalid profiles, unavailable provider,
  deadline expiry, cancellation, malformed output, missing or extra capabilities,
  context lookup failure, unknown and cross-client identifiers, unsupported role,
  inactive incident, inconsistent relationships, prompt injection, conversation
  isolation, and absence of fallback or governed workflow side effects.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Once approved credentials are available, a developer or reviewer can
  configure and start a real-model exercise profile by following the documented
  procedure in 10 minutes or less.
- **SC-002**: In a controlled acceptance set of at least 10 complete valid
  conversations, 100% produce an authoritatively validated confirmation within five
  requester messages and without manual repair of stored state.
- **SC-003**: In the defined negative acceptance set, 100% of unknown, cross-client,
  unsupported-role, inactive-incident, and inconsistent-relationship proposals are
  rejected before confirmation, with zero access requests or grants created.
- **SC-004**: 100% of missing-profile, invalid-profile, unavailable-provider,
  cancellation, malformed-response, and deadline-expiry exercises return a safe
  outcome within the overall turn deadline, perform no fallback, and create zero
  governed workflow records.
- **SC-005**: Every confirmation displayed during the real-model acceptance exercise
  contains only canonical values reloaded from authoritative data; no unchecked
  model-proposed identifier appears in a confirmation.
- **SC-006**: Every request confirmed from the real-model profile follows the same
  requester confirmation, two human approval decisions, fixed eight-hour scope,
  idempotent provisioning, and audit behavior as the existing governed journey, with
  zero model-accessible state-changing actions.
- **SC-007**: The complete automated regression suite remains runnable with zero
  real-model credentials and zero live-model calls.

## Assumptions

- The existing personal Teams request-intake, immutable confirmation, business
  approval, DevOps approval, provisioning, and audit journeys remain the baseline and
  are dependencies rather than redesigned scope.
- One execution profile is selected for the running application by a developer or
  reviewer; requesters cannot switch profiles per conversation.
- The initial feature needs one approved real-model profile at a time, not a general
  provider marketplace, dynamic model router, fallback chain, or per-tenant model
  policy.
- Approval of a model and access to its credentials are handled outside this
  application. The application consumes only explicitly supplied approved
  configuration and must not source-control credentials.
- Real-model acceptance is a deliberate manual developer/reviewer exercise. Automated
  tests continue to use deterministic substitutes and do not depend on provider
  availability, credentials, cost, or network access.
- The existing three read-only production-context capabilities and synthetic
  authoritative data are sufficient for the real model to prepare supported request
  candidates.
- Real-model usage cost controls, provider account administration, quota increases,
  model training, model evaluation platforms, and production rollout are outside the
  scope of this feature.

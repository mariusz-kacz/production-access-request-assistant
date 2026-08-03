# Feature Specification: Exercise the Real Conversational Model

**Feature Branch**: `[003-exercise-real-model]`

**Created**: 2026-08-03

**Status**: Draft

**Input**: User description: "Allow a developer or reviewer to explicitly run the existing Teams request-intake journey with an approved real language model while preserving untrusted model output, authoritative validation, requester confirmation, human approval, deterministic provisioning, and safe failure boundaries."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Exercise the Real Conversational Model (Priority: P1)

A developer or reviewer can explicitly select an approved real-model execution
profile and exercise the existing personal Teams request-intake journey. The model
may interpret the conversation and use approved production context, but its proposal
remains untrusted until the application validates it.

**Why this priority**: A real-model demonstration shows that the application works
beyond its deterministic test double without making the model an authorization
boundary.

**Independent Test**: Select an approved real-model profile, conduct one personal
Teams conversation containing a complete valid request, and verify that the
assistant displays a confirmation only after authoritative validation. Confirming
the request must enter the unchanged human-governed workflow.

**Acceptance Scenarios**:

1. **Given** an explicitly selected, valid real-model execution profile, **When** an
   authenticated requester describes a complete valid request, **Then** the model
   returns an untrusted typed candidate that is authoritatively validated before a
   confirmation is displayed.
2. **Given** the request is incomplete or ambiguous, **When** the real model processes
   the conversation, **Then** it asks one focused clarification and does not create a
   request or access grant.
3. **Given** the model proposes an unknown or cross-client identifier, unsupported
   role, inactive incident, or inconsistent relationship, **When** the candidate is
   validated, **Then** the value is rejected and no confirmation is displayed until
   valid information is supplied.
4. **Given** the selected real-model profile is missing, invalid, unavailable, or
   exceeds the overall turn deadline, **When** a turn is attempted, **Then** the
   requester receives a safe failure outcome, the deterministic test double is not
   substituted, and governed workflow state does not change.
5. **Given** a real-model candidate has passed validation, **When** the requester
   confirms it, **Then** request creation, human approval, and provisioning follow
   the existing governed workflow and remain unavailable to the model.

### Edge Cases

- The selected real-model profile is incomplete, unapproved, or lacks credentials.
- The model, an approved context lookup, or the overall turn becomes unavailable,
  times out, or is cancelled.
- The model-visible tool catalog differs from the exact approved read-only set, or
  the model returns a malformed proposal.
- A well-formed proposal contains a representative invalid or cross-client value.
- Requester content attempts to choose the execution profile or bypass validation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a server-controlled choice between the existing
  deterministic profile and one approved real-model profile; requester-provided
  content MUST NOT select or alter that choice.
- **FR-002**: The system MUST accept the real-model profile only when its required
  settings and protected credentials are present and its model is approved.
- **FR-003**: A missing, invalid, unavailable, cancelled, or timed-out real-model turn
  MUST fail closed with safe guidance, no deterministic fallback, and no governed
  workflow state change.
- **FR-004**: The real model MUST receive only the bounded authenticated intake
  context and exactly these read-only tools: `get_production_environment`,
  `get_incident`, and `get_available_roles`.
- **FR-005**: The model MUST NOT receive any capability that confirms, creates,
  approves, provisions, revokes, or otherwise changes workflow state.
- **FR-006**: Every model response MUST satisfy the existing closed typed proposal
  contract and remain untrusted until application validation succeeds.
- **FR-007**: Before displaying a confirmation, the system MUST authoritatively
  validate all proposed identifiers, roles, incident states, and client
  relationships.
- **FR-008**: Incomplete or ambiguous input MUST produce at most one focused
  clarification for the turn and MUST NOT create a request or grant.
- **FR-009**: Model invocation and approved context lookup MUST share one overall turn
  deadline, honor cancellation, and preserve the last successfully accepted intake
  state when the turn fails.
- **FR-010**: A validated real-model candidate MUST enter the existing immutable
  requester-confirmation, human-approval, and deterministic provisioning flow with
  the existing authenticated ownership and client-isolation rules.
- **FR-011**: Operational evidence MAY record safe profile, timing, correlation, and
  outcome metadata, but MUST NOT record credentials, raw prompts, transcripts,
  complete model responses, or complete context payloads.
- **FR-012**: Automated verification MUST run without live-model credentials or
  calls, and developer guidance MUST describe setup, one representative walkthrough,
  expected safe failures, and cleanup.

### Governance & Trust Requirements *(mandatory)*

- **Authoritative identity and data**: Authenticated server context supplies the
  requester identity and conversation binding. Existing authoritative data supplies
  client, environment, role, incident, and approver facts.
- **State changes and authorization**: Model interpretation and context lookup do not
  authorize or perform state changes. Existing authenticated confirmation, approval,
  and deterministic authorization boundaries remain in force.
- **Immutable scope and client isolation**: Only a validated prepared scope can be
  confirmed. Cross-client values are rejected, and submitted requests remain
  immutable.
- **AI and tool boundary**: Model output is untrusted, schema-checked, and
  authoritatively validated. The model-visible tools remain exactly the three
  approved read-only production-context tools.
- **Provisioning**: Provisioning remains unavailable to the model and continues to
  validate persisted evidence and behave idempotently.
- **Failures and logging**: Failures return closed safe outcomes and do not expose
  secrets or sensitive model and tool payloads.

### Key Entities

- **Execution Profile**: Server-controlled configuration selecting either the
  deterministic test double or one approved real model. It is operational
  configuration, not requester-controlled domain data.
- **Request Proposal**: A typed candidate or focused clarification returned for one
  intake turn. It remains untrusted until structural and authoritative validation
  succeed.
- **Request Intake and Access Request**: The existing authenticated intake state and
  immutable confirmed request. Their ownership, approval, provisioning, and audit
  behavior do not change based on model provenance.

### Verification Requirements *(mandatory)*

- Existing domain and workflow regressions continue to verify authorization,
  immutable scope, client isolation, human approval, and idempotent provisioning.
- Focused offline integration coverage verifies profile selection, provider
  forwarding and failure, the shared deadline, exact read-only tools, schema reuse,
  authoritative candidate rejection, safe logging, and unchanged confirmation
  wiring using representative cases.
- One documented manual exercise verifies the approved real-model profile with a
  complete request plus representative clarification, rejection, and safe-failure
  outcomes. It is not an automated-test dependency.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With approved credentials available, a developer or reviewer can
  configure and start the real-model profile by following the documentation in 10
  minutes or less.
- **SC-002**: One representative complete valid conversation produces an
  authoritatively validated confirmation within five requester messages.
- **SC-003**: Representative incomplete and invalid-candidate exercises display no
  confirmation and create zero requests or grants until valid information is
  supplied.
- **SC-004**: Representative missing-profile, invalid-profile, unavailable-provider,
  and deadline-expiry exercises return a safe outcome, perform no fallback, and
  create zero governed workflow records.
- **SC-005**: A request confirmed from the real-model profile follows the same two
  human approvals, fixed eight-hour scope, deterministic idempotent provisioning,
  and audit behavior as the existing journey.
- **SC-006**: The automated regression suite runs with zero real-model credentials
  and zero live-model calls.

## Assumptions

- The existing Teams intake, immutable confirmation, human approval, provisioning,
  and audit journeys remain dependencies rather than redesign scope.
- One application-wide approved execution profile is selected by a developer or
  reviewer; requesters cannot switch profiles per conversation.
- Real-model acceptance is a deliberate manual exercise; automated tests use
  deterministic substitutes.
- Provider administration, quota changes, model training, evaluation platforms, and
  production rollout are outside this feature.

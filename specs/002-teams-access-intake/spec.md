# Feature Specification: Teams Access Request Intake

**Feature Branch**: `evolution/maf-request-intake`

**Created**: 2026-07-27

**Status**: Approved

**Input**: User description: "Provide a Teams-only conversational access-request
assistant that clarifies a developer's intent, gathers only approved read-only
context, presents one immutable request draft for explicit requester confirmation,
and then hands the submitted request to the existing deterministic approval and
provisioning workflow."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prepare and Confirm an Access Request (Priority: P1)

An authenticated developer describes the temporary production access they need in a
personal Teams conversation. The assistant gathers the necessary request context and,
when the request is deterministically complete and valid, presents an exact review
draft. The developer confirms that draft, receives a stable request ID,
and can follow the request in the existing web application. Confirmation submits the
request for later approval; it does not approve or grant access.

**Why this priority**: This is the core user outcome and creates the channel-based
entry point without changing who approves or provisions access.

**Independent Test**: Start with a complete access description from an authenticated
developer, confirm the displayed ready draft, and verify that exactly one immutable
request enters the existing workflow with the displayed scope and authenticated
requester.

**Acceptance Scenarios**:

1. **Given** an authenticated developer in a personal Teams conversation and a
   complete, valid access description, **When** the assistant finishes preparation,
   **Then** it displays a ready request draft containing the canonical client, environment,
   requested role, justification, optional incident, fixed eight-hour lifetime, and
   an unambiguous statement that no access has yet been approved or granted.
2. **Given** a valid ready request draft is displayed, **When** its owning developer selects
   **Confirm and submit** before it expires, **Then** exactly one immutable access
   request with the displayed scope is submitted under the fixed synthetic requester
   and begins awaiting business approval.
3. **Given** confirmation has submitted a request, **When** the developer receives the
   result, **Then** the result contains the stable request ID and a way to open that
   request in the existing web application.
4. **Given** a ready request draft is displayed, **When** the developer asks about
   alternate roles, environments, tradeoffs, or a hypothetical change without
   requesting a revision, **Then** the assistant answers from approved read-only
   context while the same draft, preparation identity, deadline, and confirmation
   card remain active.
5. **Given** a ready request draft is displayed, **When** the developer explicitly
   changes one or more fields and deterministic assessment produces a different
   candidate, **Then** the prior immutable draft is superseded and a replacement
   preparation carries forward every unrelated validated field without submitting a
   request.

---

### User Story 2 - Clarify an Incomplete Request (Priority: P2)

An authenticated developer can begin with incomplete or ambiguous intent. The
assistant asks focused follow-up questions, uses process-local conversation history
to understand replies in context, and carries forward the current typed candidate
values. A ready request draft is not shown until authoritative,
deterministic validation confirms that every required field is complete and valid.

**Why this priority**: Conversational clarification is the principal user benefit over
the existing one-shot draft experience.

**Independent Test**: Begin with a description missing at least two required values,
answer the assistant's questions over multiple turns, and verify that no ready draft
appears until all required values pass deterministic validation.

**Acceptance Scenarios**:

1. **Given** a developer omits a required request value, **When** the assistant
   processes the description, **Then** it asks one focused question, may present
   choices grounded in the approved read-only context when applicable, and does not
   display a ready request draft.
2. **Given** the assistant proposes an identifier that does not exist or conflicts
   with authoritative client, environment, role, or incident data, **When** the
   candidate is checked, **Then** the invalid value is not accepted and the developer
   receives safe correction guidance.
3. **Given** a developer answers a clarification question while the active
   conversation history is available, **When** the answer is processed, **Then** the
   assistant interprets it using that history and the latest typed candidate, while
   every resulting value remains untrusted until deterministically validated.
4. **Given** the assistant claims that preparation is complete while deterministic
   validation still finds an error, **When** the turn is evaluated, **Then** no final
   request is created or displayed.
5. **Given** the assistant previously presented ordered choices in the active
   in-memory conversation, **When** the developer refers to "the first one" or
   "the other role", **Then** the assistant interprets the reference from that
   conversation history and the resulting identifier is still deterministically
   validated.
6. **Given** process-local conversation history is unavailable after host restart,
   **When** the developer sends a reply that depends on an earlier question, **Then**
   the assistant does not guess, continues from the
   persisted typed candidate, and repeats a focused clarification.
7. **Given** two authenticated developers or personal conversations are preparing
   requests concurrently, **When** they exchange multiple messages, **Then** their
   process-local histories and durable candidates remain isolated.

---

### User Story 3 - Safely Handle Expiry, Replay, and Failure (Priority: P3)

A developer receives safe, comprehensible behavior when a ready request draft expires, a
confirmation is delivered more than once, or an AI or context dependency fails.
Retries cannot create duplicate requests or expand request scope.

**Why this priority**: Teams actions and model operations can be retried or fail, and
safe recovery is required before this channel can be trusted for request submission.

**Independent Test**: Exercise an expired ready draft, a repeated confirmation, and
each typed preparation failure; verify that only the valid first confirmation can
create a request and that recovery guidance never changes workflow state.

**Acceptance Scenarios**:

1. **Given** a ready request draft is more than 30 minutes old, **When** its confirmation is
   selected, **Then** no request is submitted and the developer is told to begin a
   new preparation.
2. **Given** a ready request draft has already been submitted, **When** the same confirmation
   is delivered again, **Then** the existing request ID is returned and no duplicate
   request or audit history is created.
3. **Given** a different authenticated developer attempts to confirm another
   developer's ready request draft, **When** the action is evaluated, **Then** it is rejected
   without revealing or submitting the request scope.
4. **Given** model interpretation or approved context lookup times out, is
   unavailable, is cancelled, or produces malformed output, **When** the turn ends,
   **Then** the developer receives a safe typed outcome and no request, approval,
   provisioning operation, or grant is created.

---

### User Story 4 - Continue the Existing Governed Workflow (Priority: P4)

Business and DevOps approvers continue to review Teams-submitted requests in the
existing web application. Their authenticated decisions and the protected provisioning
path retain their existing deterministic behavior.

**Why this priority**: The new intake channel must not replace or weaken the
deterministic controls that make the product governed.

**Independent Test**: Submit a request through Teams, then complete the existing
business and DevOps approval journey and verify the same authorization, immutable
scope, audit, failure, retry, and idempotent provisioning rules.

**Acceptance Scenarios**:

1. **Given** a request submitted through Teams is awaiting business approval,
   **When** an authenticated configured business approver reviews it in the web
   application, **Then** the existing decision rules and client isolation apply
   without regard to the intake channel.
2. **Given** valid business approval exists, **When** an authenticated DevOps
   approver records a decision, **Then** the existing exact-role, fixed-duration,
   persisted-evidence, and provisioning rules apply unchanged.
3. **Given** a requester uses the web application, **When** they inspect available
   routes and actions, **Then** they can list and open relevant requests but cannot
   draft or submit a new request there.

---

### User Story 6 - Make Teams the Only Request-Creation Channel (Priority: P1)

Requesters create access requests only by confirming a server-owned preparation in an
authenticated personal Teams conversation. The web application remains the request
register and authenticated review, decision, retry, and audit surface.

**Why this priority**: One creation boundary removes duplicate request-intake behavior
and makes the trust model explicit without changing downstream governance.

**Independent Test**: Confirm one request through Teams, verify it is visible in the
web request register, and verify browser draft/submit endpoints, route, navigation,
form, and session capability are absent while approval and retry endpoints remain.

**Acceptance Scenarios**:

1. **Given** an authenticated requester confirms a valid Teams preparation, **When**
   confirmation completes, **Then** exactly one immutable request and request-created
   audit event are committed.
2. **Given** any browser caller attempts the former draft or request-submission path,
   **When** the request is handled, **Then** no request or audit state is created.
3. **Given** a requester, business approver, or DevOps approver opens the web
   application, **When** the application renders, **Then** request list/detail and
   authorized approval or retry controls remain available without creation controls.

### Edge Cases

- A developer discusses alternatives while a ready draft is active; the existing
  preparation and card remain confirmable because the assessed candidate is unchanged.
- A developer explicitly revises a ready draft; the older preparation is superseded,
  its card becomes non-actionable when presentation metadata is available, and any
  stale confirmation is rejected by durable status validation.
- The ready request draft expires while the developer is viewing it.
- Teams redelivers the same confirmation concurrently or after a response is lost.
- A confirmation carries an unknown, malformed, expired, superseded, or already-used
  preparation reference.
- A valid channel request includes extra identity, role, approver, duration, approval,
  or scope fields.
- A conversation message instructs the model to submit, approve, provision, ignore
  validation, expose hidden data, or call an unapproved tool.
- The model returns a structurally valid candidate with a cross-client environment or
  incident association.
- The approved context tool catalog contains a missing or unexpected tool.
- The developer disconnects or cancels while a model or context operation is active.
- Process-local conversation history is lost on host restart between a clarification question and a
  relative answer such as "the first one".
- Two activities for the same active conversation arrive concurrently.
- A prepared request is valid when displayed but authoritative context validation
  fails when confirmation occurs.
- A Teams-submitted request encounters the existing provisioning failure and retry
  path.
- A browser caller posts to a removed draft or request-creation path; the call creates
  no request or audit evidence.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept production-access request intent only from an
  authenticated developer in a personal Teams conversation.
- **FR-002**: The system MUST derive the channel actor from authenticated server
  context and map every accepted actor to the fixed synthetic requester used by this
  demonstration; message or action payloads MUST NOT select an acting identity,
  application role, or authorization claim.
- **FR-003**: The fixed developer/requester role MUST authorize request preparation
  only and MUST NOT imply any requested production role, approval authority, or
  existing access.
- **FR-004**: The system MUST maintain at most one active request-preparation
  conversation for each authenticated developer and personal conversation.
- **FR-005**: The assistant MUST interpret request intent, carry forward candidate
  values across turns, use process-local conversation history to interpret
  follow-up answers, and ask one focused clarification at a time.
- **FR-006**: The assistant MUST receive exactly `get_production_environment` and
  `get_incident`. Environment results MUST include authoritative client relationships
  and assigned roles; no separate role-listing or state-changing capability is
  available.
- **FR-007**: Every assistant turn MUST produce a closed, schema-valid proposal to
  supply the complete nullable candidate snapshot and either one typed clarification
  target, bounded message, and optional bounded environment option identifiers or no
  clarification; arbitrary assistant prose MUST NOT determine readiness or cause a
  state change.
- **FR-008**: Every model-proposed client, environment, role, incident, and business
  value MUST be checked against authoritative stored data and existing request rules.
- **FR-009**: The system MUST determine readiness using deterministic validation and
  MUST NOT rely on the assistant's assertion that all open points are resolved.
- **FR-010**: The system MUST NOT display a ready request draft until all required values
  are present, canonical, mutually consistent, and valid at the time of preparation.
- **FR-011**: When a request becomes ready, the system MUST create a server-owned
  prepared-request snapshot containing a server-generated preparation reference, a
  reserved server-generated request ID, the authenticated requester, the exact
  canonical scope, preparation status, creation and expiry times, conversation
  binding, and correlation metadata.
- **FR-012**: A prepared request MUST expire 30 minutes after it becomes ready for
  confirmation.
- **FR-013**: Discussion that leaves an active ready candidate unchanged MUST preserve
  its preparation identity and confirmability. When deterministic assessment produces
  a different candidate, the system MUST supersede the prior unsubmitted preparation
  before persisting its replacement.
- **FR-014**: The ready request draft MUST display the canonical client, environment,
  requested role, justification, optional incident, and fixed eight-hour lifetime,
  and MUST explain that requester confirmation submits the request but does not
  approve or grant access. It MUST NOT display the server-reserved request ID before
  successful submission.
- **FR-015**: The ready request draft MUST expose one state-changing action labeled
  **Confirm and submit** and MUST NOT offer editing, approval, provisioning, duration,
  approver-selection, or scope-changing actions.
- **FR-016**: Conversation text MUST NOT mutate or submit the immutable ready snapshot.
  Natural-language discussion with an unchanged assessed candidate MUST leave it
  active. An explicit revision MUST create a replacement preparation and make the
  prior preparation non-confirmable only when the assessed candidate changes.
- **FR-017**: The confirmation action MUST carry only an opaque prepared-request
  reference and presentation metadata; it MUST NOT carry trusted identity, role,
  approver, duration, approval, validation, or request-scope assertions.
- **FR-018**: Before submission, the system MUST derive the confirming actor from
  authenticated server context, reload the prepared snapshot, verify ownership,
  conversation binding, readiness, non-expiry, and non-supersession, and repeat
  deterministic validation against authoritative data.
- **FR-019**: A successful confirmation MUST create the immutable request using the
  reserved request ID and the exact revalidated prepared scope, record it under the
  fixed synthetic requester, and place it into the existing awaiting-business-approval
  state.
- **FR-020**: Confirmation MUST be idempotent: all accepted deliveries for one
  prepared request MUST return the same request ID and MUST create at most one access
  request.
- **FR-021**: After successful confirmation, the system MUST present the request ID
  and a way to open the request in the existing web application.
- **FR-022**: Authenticated confirmation of a server-owned Teams preparation MUST be
  the only request-creation path. The web application MUST retain request list/detail,
  business-decision, DevOps-decision, provisioning-retry, and audit behavior while
  exposing no browser drafting or request-submission endpoint, route, form, navigation,
  or session capability.
- **FR-023**: The assistant and its context capabilities MUST NOT be able to submit a
  request, record either approval, transition workflow state, provision or revoke
  access, retry provisioning, or access arbitrary stored data.
- **FR-024**: Preparation MUST expose safe typed outcomes for ready-draft discussion,
  clarification required, deterministically rejected candidate with application-owned
  provenance, ready for confirmation, malformed model output, timeout, cancellation,
  and dependency unavailability.
- **FR-025**: Active conversation content MUST remain only in process-local memory for
  the host lifetime, MUST NOT be written to the
  application database or persisted as a transcript, and MUST be discarded on
  process termination; audit evidence MUST contain operation metadata rather than
  raw conversation transcripts.
- **FR-026**: Teams submission MUST NOT send later approval or provisioning status
  notifications in this feature.
- **FR-027**: Loss of process-local conversation history after host restart MUST NOT
  alter the durable candidate or any workflow state. A follow-up message that depends
  on missing history MUST cause safe re-clarification rather than inferred selection.
- **FR-028**: Process-local history MUST be isolated by the authenticated intake
  binding, protected from concurrent mutation for one conversation, and never used
  by confirmation, approval, or provisioning. The current in-memory baseline does
  not require application-owned inactivity, turn-count, or terminal-session removal.

### Scope Boundaries

**In scope**:

- Personal Teams conversations for synthetic developers requesting temporary
  production access.
- Multi-turn clarification, read-only authoritative context gathering, deterministic
  readiness checks, immutable final presentation, requester confirmation, and
  idempotent submission.
- Continued use of the existing web application for request list/detail, approvals,
  provisioning recovery, and audit presentation.

**Out of scope**:

- Slack or any additional chat channel.
- Teams group chats, channels, meetings, proactive messages, and later status
  notifications.
- Real corporate identity, tenant onboarding, user consent, directory integration,
  or production access.
- Editing a ready draft in place or modifying any submitted request. Pre-submission
  correction creates a replacement preparation; post-submission correction creates a
  new request and requires new approvals.
- Model-visible submission, approval, provisioning, revocation, retry, workflow,
  database, or generic-query actions.
- Autonomous execution, multiple collaborating agents, agent-to-agent communication,
  or a generic conversational workflow engine.
- Durable conversation-session persistence, explicit session-retention/deletion
  workflows, conversation compaction, and multi-host session coordination.
- Changes to approval order, approver selection, immutable request scope, fixed grant
  lifetime, provisioning evidence validation, retry, or idempotency rules.

### Governance & Trust Requirements *(mandatory)*

- **Authoritative actors and data**: The channel endpoint authenticates each incoming
  activity. Accepted Teams actors map to the single fixed synthetic requester for
  this demonstration. The synthetic client, production environment, available role,
  incident, principal, business-approver assignment, prepared snapshot, submitted
  request, approval, operation, and grant records remain authoritative. Conversation
  text, assistant output, card contents, action payloads, browser input, and
  caller-supplied identities or claims remain untrusted.
- **State changes and authorization**: Conversation progress and creation of a
  prepared snapshot do not grant access or record approval. Confirming a prepared
  request is the only new workflow-affecting action. It requires an authenticated
  channel actor, ownership and conversation checks, an active unexpired snapshot,
  repeated deterministic validation, and idempotent creation of the reserved request.
  Business and DevOps decisions and provisioning retain their existing authenticated,
  deterministic authorization.
- **Immutable scope and client isolation**: The final prepared snapshot cannot be
  edited. Correction requires a new preparation, and submitted requests retain the
  existing create-new-request correction rule. Environment, role, and incident
  relationships are authoritatively checked before final presentation and again at
  confirmation. Approvals remain bound to the submitted immutable request ID and
  exact scope, so one client's request cannot authorize another client's environment.
- **AI and MCP boundary**: AI may interpret intent, preserve conversational context,
  ask questions, and propose a typed candidate. Every proposal is schema-validated
  and deterministically checked. The allowlist remains exactly
  `get_production_environment` and `get_incident`; both remain read-only, and
  environment context carries its assigned roles. Model and MCP contracts are
  translated outside domain rules. The model
  receives no submission, approval, workflow, provisioning, revocation, retry,
  arbitrary-database, or generic-query capability.
- **Provisioning and idempotency**: Provisioning is unaffected and remains unavailable
  to the assistant. The protected provisioning handler still accepts only the stable
  request ID, reloads persisted request, approval, and operation evidence, and uses
  that request ID as the provisioning idempotency identity. Separately, the prepared
  request's reserved request ID makes repeated confirmation converge on one submitted
  request.
- **Failure and audit evidence**: Model, context, authentication, validation, expiry,
  ownership, supersession, concurrency, confirmation, submission, and downstream
  failures produce explicit safe outcomes without implicit state changes. Audit and
  operational records include correlation IDs, authenticated actor mapping,
  preparation status changes, confirmation outcome, submitted request ID, operation
  names, duration, and outcome metadata. Secrets, raw prompts, active conversation
  transcripts, and complete context payloads are not logged by default.

### Key Entities

- **Request Preparation Conversation**: The short-lived state for one authenticated
  developer in one personal Teams conversation, including the durable typed candidate,
  timestamps, and correlation metadata. It is not approval or authorization evidence
  and does not persist a transcript or clarification options.
- **Process-Local Conversation Memory**: In-memory assistant history keyed to
  one authenticated intake and retained for the host lifetime. It helps interpret
  follow-up messages but is not authoritative request state, is not written to the
  application database or logged, and is lost on host restart.
- **Prepared Access Request**: A server-owned, immutable, time-limited snapshot ready
  for requester confirmation. It contains an opaque preparation reference, reserved
  request ID, requester and conversation binding, exact canonical request scope,
  status, timestamps, and eventual submitted request reference.
- **Access Request**: The existing immutable request created after confirmation. It
  retains the existing client isolation, approval binding, audit, provisioning, and
  correction rules regardless of intake channel.

### Verification Requirements *(mandatory)*

- **Domain/unit coverage**: Verify prepared-request readiness, 30-minute expiry,
  supersession, immutable final scope, owner binding, allowed status transitions,
  reserved-request identity, and idempotent confirmation. Existing request,
  approval, immutable-scope, fixed-duration, and provisioning policies remain covered.
- **Integration/contract coverage**: Verify authenticated personal-chat intake,
  fixed synthetic requester mapping, history-backed multi-turn interpretation,
  process-local history isolation and restart recovery, durable candidate
  carry-forward, ready-draft discussion, replacement preparation, stale-card
  rejection, exact review-card presentation, opaque confirmation actions,
  persistence, repeated confirmation, stable request links, and continued web
  behavior. Verify the exact two-tool read-only context contract and model tool
  allowlist. No automated test may require a live model; deterministic fake behavior
  must cover history-sensitive clarification and candidate proposals.
- **Negative coverage**: Verify unauthenticated or non-personal activities, forged
  identity and scope fields, cross-developer confirmation, unknown/expired/superseded
  preparation references, conversation mismatch, stale authoritative context,
  concurrent and replayed confirmation, malformed model output, unsupported model
  values, prompt injection, unexpected tool catalogs, model and context timeout,
  cancellation, dependency failure, and attempts to expose or invoke forbidden
  actions. Verify that every rejected case creates no unintended request, approval,
  operation, or grant.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a representative suite of complete and incomplete request
  descriptions, at least 90% reach an accurate ready request draft within five developer
  messages without manual use of the web request-entry form.
- **SC-002**: For a complete valid description under normal local conditions, a
  developer can reach and confirm the ready request draft in under three minutes.
- **SC-003**: 100% of displayed ready request drafts pass deterministic validation at the
  time they are displayed and visibly distinguish requester confirmation from
  business approval, DevOps approval, and access grant.
- **SC-004**: Repeated or concurrent confirmation of the same prepared request creates
  exactly one access request and returns the same request ID in 100% of tested cases.
- **SC-005**: 100% of unauthenticated, foreign-owner, expired, superseded, malformed,
  or stale confirmation attempts are rejected without creating a request, approval,
  provisioning operation, or grant.
- **SC-006**: 100% of requests successfully submitted through Teams can complete the
  existing business approval, DevOps approval, provisioning failure/retry, and
  idempotent provisioning scenarios without channel-specific exceptions.
- **SC-007**: In a five-person comprehension review, at least four participants
  correctly state after seeing the ready request draft that selecting **Confirm and
  submit** does not approve or grant production access.
- **SC-008**: All automated acceptance scenarios run without a live model or real
  production identity, environment, or access provider.
- **SC-009**: In 100% of tested restart and missing-history scenarios, a
  relative follow-up answer produces a repeated clarification and no unintended
  request, approval, operation, or grant.

## Assumptions

- Microsoft Teams personal chat is the only new conversational surface.
- This remains a local synthetic demonstration: any authenticated Teams actor accepted
  by the configured demo channel maps to the single fixed synthetic requester rather
  than a real corporate directory identity.
- The developer/requester application role is distinct from the production role being
  requested and confers no approval or provisioning authority.
- One active preparation per authenticated developer and personal conversation is
  sufficient for the first version.
- Process-local conversation history is a best-effort interpretation aid retained for
  the host lifetime. A host restart may cause the
  assistant to repeat a clarification, while the persisted typed candidate remains
  available.
- The local synthetic baseline accepts process-lifetime in-memory session retention.
  Durable session persistence, removal lifecycle, and conversation compaction are
  deferred until operational requirements justify them.
- A ready prepared request expires after 30 minutes; an expired, superseded, or
  incorrect ready draft must be replaced by starting a new preparation.
- The React application remains a request register and governed approval/retry
  surface; it does not create requests.
- Later Teams notifications for approval, rejection, provisioning, expiry, or retry
  are deferred.
- The fixed synthetic reference dataset, exact two-tool MCP surface, two human
  approval stages, immutable submitted scope, fixed eight-hour lifetime, and protected
  idempotent provisioning remain the product baseline.

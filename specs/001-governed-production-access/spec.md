# Feature Specification: Governed Production Access

**Feature Branch**: `N/A (no branch hook configured)`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "Use `governed-production-access-product-baseline.md` as the feature baseline, with a thin React UI built and served by the single ASP.NET Core host."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prepare and Submit a Safe Access Request (Priority: P1)

As an authenticated requester, I describe why I need temporary access to a client production environment, review a structured draft prepared with stored data, correct any missing or invalid fields, and submit a valid request for approval.

**Why this priority**: A trustworthy, valid request is the entry point for every later approval and access decision and demonstrates the intended value of model-assisted request preparation.

**Independent Test**: Authenticate as the requester, enter a request for Client Alpha read-only production access for four hours for incident `INC-1042`, and verify that a reviewable draft can be validated and submitted without granting access.

**Acceptance Scenarios**:

1. **Given** an authenticated requester and the Client Alpha environment, **When** the requester describes four hours of read-only access for active incident `INC-1042`, **Then** the system presents a typed draft containing verified client, environment, role, duration, justification, and incident identifiers for review.
2. **Given** a complete draft whose environment belongs to the selected client and whose role, duration, and incident are currently valid, **When** the requester submits it, **Then** the request is created in `AwaitingBusinessApproval` for version 1 and no access is granted.
3. **Given** malformed model output, an unknown identifier, an inactive incident, a client-environment mismatch, a role not assigned to the environment, or an excessive duration, **When** draft preparation or submission is attempted, **Then** the system returns a structured failure or identifies fields requiring correction and does not submit or authorize the request.
4. **Given** information proposed by the model, **When** the request is submitted, **Then** every identifier and business value is checked against current trusted server data rather than accepted from the model.

---

### User Story 2 - Make the Correct Business Decision (Priority: P2)

As the business approver responsible for the target client environment, I can review and approve or reject the exact current request version, while other client approvers cannot act on it.

**Why this priority**: Client isolation and explicit human business approval are essential authorization boundaries.

**Independent Test**: Create a valid Client Alpha request, attempt approval as both Client Beta and Client Alpha business approvers, and verify that only the configured Client Alpha approver can change the request state.

**Acceptance Scenarios**:

1. **Given** a Client Alpha request in `AwaitingBusinessApproval`, **When** the authenticated Client Alpha business approver approves its current version, **Then** the decision binds the request ID, version, role, and maximum duration and the request moves to `AwaitingDevOpsApproval`.
2. **Given** the same request, **When** the Client Beta business approver attempts to approve it, **Then** the attempt is rejected and audited without changing the workflow state.
3. **Given** a current request awaiting business approval, **When** the configured business approver rejects it, **Then** the request moves to `Rejected` and the authenticated decision is recorded.
4. **Given** a requester preparing or viewing a request, **When** approver responsibility is determined, **Then** it is resolved from current environment configuration and cannot be selected or supplied by the requester.

---

### User Story 3 - Approve and Provision the Authorized Scope (Priority: P3)

As the authenticated DevOps approver, I can approve the business-approved role for the same or a shorter duration, causing immediate protected provisioning, or reject the request.

**Why this priority**: The second human decision and independent deterministic revalidation prove that neither the model nor an upstream assertion can authorize production access.

**Independent Test**: Start with a current business-approved request, approve it as DevOps, and verify that current stored state is reloaded, the exact approved scope is enforced, and one synthetic time-limited grant becomes active.

**Acceptance Scenarios**:

1. **Given** a current business-approved request in `AwaitingDevOpsApproval`, **When** the authenticated DevOps approver approves the exact role and a duration no greater than the business-approved duration, **Then** the system immediately revalidates current request, approval, scope, environment, role, and incident state before creating one grant and moving the request to `Active`.
2. **Given** a DevOps decision that changes the role, increases duration, changes client or environment, references another version, or lacks a valid business approval, **When** approval is attempted, **Then** the decision is rejected and audited without provisioning access.
3. **Given** a user who is not the authenticated DevOps approver, **When** that user attempts the DevOps action, **Then** the attempt is rejected and audited without changing protected state.
4. **Given** a valid request awaiting DevOps approval, **When** DevOps rejects it, **Then** the request moves to `Rejected` and no grant is created.
5. **Given** the current duration limit or incident state changed after business approval, **When** DevOps approval triggers provisioning, **Then** provisioning fails safely rather than using stale approval assertions or creating a grant.

---

### User Story 4 - Protect Approvals from Material Edits (Priority: P4)

As a requester, I can materially correct a request, while the system ensures that prior approvals cannot authorize the changed request.

**Why this priority**: Version binding prevents a valid approval from being reused for a materially different access scope.

**Independent Test**: Approve version 1 at the business stage, materially edit the request, and verify version 2 returns to `Draft`, prior approval is invalid, and actions referencing version 1 are rejected.

**Acceptance Scenarios**:

1. **Given** a request with one or more decisions, **When** a material field is edited, **Then** the request version increments, prior approvals are invalidated for authorization, and the request returns to `Draft`.
2. **Given** a current request version differs from the version referenced by an approval action, **When** the action is attempted, **Then** it is rejected and audited without changing workflow state.
3. **Given** an audit timeline for the request, **When** a material edit occurs, **Then** the timeline retains evidence of the earlier version, the edit, and the invalidation of earlier approval authority.

---

### User Story 5 - Recover Safely and Review Evidence (Priority: P5)

As an authenticated DevOps approver, I can retry a failed provisioning attempt without changing the approved scope; as any authorized participant, I can review the resulting request, grant, and audit evidence.

**Why this priority**: Safe retry and visible evidence make partial failures recoverable without introducing a separate provisioning authority.

**Independent Test**: Simulate a provisioning result being lost, retry from a `ProvisioningFailed` request, and verify that the same logical operation returns the existing grant without a duplicate and that both attempts appear in the audit timeline.

**Acceptance Scenarios**:

1. **Given** a request in `ProvisioningFailed`, **When** the authenticated DevOps approver invokes retry without changing approved scope, **Then** the system repeats complete current-state revalidation using the same operation identity and records the retry outcome.
2. **Given** the original provisioning operation already created a grant but its response was lost, **When** the same operation is retried, **Then** the existing grant is returned and no duplicate grant is created.
3. **Given** a request not in `ProvisioningFailed` or an actor who is not DevOps, **When** retry is attempted, **Then** it is rejected and audited without starting a general-purpose provisioning action.
4. **Given** an active or failed request, **When** an authorized participant opens its details, **Then** the page presents request data, version, current status, validation results, approvals, grant outcome and logical expiry when applicable, and the audit timeline.

### Edge Cases

- Model output is syntactically malformed, omits required fields, uses display names instead of stable identifiers, or proposes unsupported values.
- A data lookup is unavailable, times out, is cancelled, or returns a typed not-found result.
- The selected environment does not belong to the selected client.
- The requested role is not assigned to the selected environment.
- Duration is zero, negative, over the environment limit, increased by DevOps, or reduced to a valid positive value.
- An incident is omitted when optional, unknown, inactive, or associated with a different client or environment.
- A wrong-client approver, requester, or other unauthorized principal attempts a protected decision or retry.
- Concurrent or delayed actions reference an obsolete request version.
- Provisioning fails after DevOps approval, times out after creating a grant, or receives duplicate retry attempts.
- Current time passes a grant's expiry timestamp; the grant appears logically expired without implying automated revocation.
- A rejected authorization or stale-version attempt must create evidence without mutating the protected workflow state.
- Cancellation or partial failure occurs during model assistance, context lookup, persistence, or provisioning; the operation must not be reported as successful.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow an authenticated requester to enter natural-language intent and receive a typed, reviewable access-request draft.
- **FR-002**: The draft MUST represent client, environment, requested role, requested duration, justification, and optional incident; nullable required values MUST support structured correction.
- **FR-003**: Model-produced content MUST be treated as untrusted and MUST NOT approve, reject, select an approver, change workflow state, provision, revoke, or override an authoritative validation result.
- **FR-004**: Model-assisted preparation MUST have access only to the three approved read-only context operations: `get_production_environment`, `get_incident`, and `get_available_roles`.
- **FR-005**: Each approved context operation MUST use explicit typed inputs and results, stable identifiers, and explicit not-found, invalid-input, timeout, cancellation, and unavailable outcomes.
- **FR-006**: No approval, provisioning, revocation, workflow-transition, arbitrary-data, or generic-query capability MUST be available to model-assisted preparation.
- **FR-007**: Before submission, the system MUST validate the authenticated requester, client and environment existence and relationship, environment-role assignment, positive duration within the environment limit, and any supplied incident's existence, active state, and relevant association.
- **FR-008**: Invalid, incomplete, malformed, cancelled, or timed-out draft preparation MUST produce a safe, understandable outcome that allows structured correction and MUST NOT create authorization evidence or a grant.
- **FR-009**: The system MUST derive the acting identity and authority for every protected action from authenticated server context and MUST ignore browser-supplied identity, role, or authorization claims as authority.
- **FR-010**: A submitted request MUST receive a stable request ID, version, status, requester identity, timestamps, and correlation identifier.
- **FR-011**: The system MUST resolve the required business approver from current stored configuration for the target environment; the requester MUST NOT select or supply that approver.
- **FR-012**: Only the authenticated business approver responsible for the target client environment MUST be able to approve or reject a request awaiting business approval.
- **FR-013**: A business decision MUST bind the exact request ID, request version, requested role, maximum approved duration, authenticated approver, decision, timestamp, and optional comment.
- **FR-014**: Business approval of the current version MUST move the request to `AwaitingDevOpsApproval`; business rejection MUST move it to `Rejected`.
- **FR-015**: Only the authenticated DevOps approver MUST be able to decide a request that has a valid business approval for its current version.
- **FR-016**: A DevOps decision MUST bind the exact request ID and version and MUST either reject or approve the exact business-approved role for a positive duration no greater than the business-approved duration.
- **FR-017**: The system MUST reject any DevOps attempt to change role, increase duration, change client or environment, act on a stale version, or act without a valid current business approval.
- **FR-018**: DevOps rejection MUST move the request to `Rejected` without creating an access grant.
- **FR-019**: Successful DevOps approval MUST immediately initiate protected provisioning without a separate human provisioning action or role.
- **FR-020**: Before creating a grant, provisioning MUST reload authoritative request and approval evidence and verify current workflow state, exact version agreement, approval order and authority, approved scope, current environment and role validity, incident validity when present, and consistency of the operation identity.
- **FR-021**: Provisioning MUST accept only references needed to identify the operation and MUST NOT trust caller-supplied assertions that approval, authorization, or validation has succeeded.
- **FR-022**: Successful provisioning MUST create or return exactly one synthetic access grant bound to requester, request ID and version, environment, role, activation, expiry, approved duration, correlation identifier, and a stable operation identity.
- **FR-023**: Repeating the same logical provisioning operation with the same operation identity MUST return the existing result and MUST NOT create a duplicate grant.
- **FR-024**: A provisioning failure MUST NOT be reported as success and MUST move the request to `ProvisioningFailed` when recovery by retry is appropriate.
- **FR-025**: Only the authenticated DevOps approver MUST be able to retry a request in `ProvisioningFailed`, and retry MUST preserve request version and approved client, environment, role, duration, and operation identity.
- **FR-026**: Retry MUST invoke the same full current-state revalidation and idempotent provisioning behavior as the initial attempt.
- **FR-027**: A material edit MUST increment the request version, invalidate all prior approvals for authorization, and return the request to `Draft`.
- **FR-028**: Material fields MUST include client, environment, requested role, requested duration, justification, and incident association; changing any of these MUST require validation and new approvals.
- **FR-029**: Protected actions referencing a non-current version or made by an unauthorized actor MUST be rejected, audited, and leave the protected workflow state unchanged.
- **FR-030**: The supported workflow MUST be limited to `Draft`, `AwaitingBusinessApproval`, `AwaitingDevOpsApproval`, `Rejected`, `ProvisioningFailed`, and `Active`, unless a later approved change demonstrates a deterministic need for another status.
- **FR-031**: The user experience MUST be limited to a request list, new-request page, and request-detail page, with actions shown according to authenticated identity, authorization, workflow state, and current request version.
- **FR-032**: The request-detail page MUST show request data, current version and status, validation results, business and DevOps decisions, grant outcome, activation and expiry when applicable, logical expiry, and audit timeline.
- **FR-033**: The request list MUST allow authenticated users to find requests relevant to them and identify requests on which they can currently act without separate approval inboxes.
- **FR-034**: Audit history MUST use insert-only events and, at minimum, record request creation, material edit, validation failure, business decision, DevOps decision, rejected authorization, stale-version rejection, provisioning attempt, success, failure, and duplicate retry.
- **FR-035**: Every audit event MUST identify event type, request ID and version, timestamp, correlation identifier, authenticated actor when applicable, and structured outcome details without recording secrets, raw prompts, or complete sensitive context payloads by default.
- **FR-036**: Operational evidence MUST allow request preparation, data lookups, authorization decisions, workflow transitions, and provisioning attempts to be correlated and MUST capture duration and outcome where an external or model-assisted operation occurs.
- **FR-037**: A grant MUST be displayed as logically expired after its expiry timestamp; automated revocation MUST NOT be implied or required.
- **FR-038**: Expected validation, authorization, stale-state, timeout, cancellation, and provisioning failures MUST be distinguishable outcomes, and cancellation MUST be honored across each in-progress asynchronous operation.
- **FR-039**: The feature MUST support exactly two clients, their two client-specific environments, the `ProductionReadOnly` and `ProductionSupport` roles described in the baseline, and exactly four fixed demonstration principals: requester, Client Alpha business approver, Client Beta business approver, and DevOps approver.
- **FR-040**: The complete required behavior MUST be verifiable without a live model by using repeatable valid, incomplete, malformed, failure, and timeout outcomes.

### Key Entities *(include if feature involves data)*

- **Client**: A client boundary identified by a stable ID and associated with one or more production environments; authorization for one client never applies to another.
- **Production Environment**: A client-owned target identified by a stable ID, with allowed roles, maximum access duration, and a configured business approver.
- **Access Role**: A stable allowed access scope for a particular environment; roles have no generalized privilege ordering in this feature.
- **Incident**: An optional stored record identified by a stable ID, with status and client or environment association used during validation.
- **Access Request**: The requester's desired client, environment, role, duration, justification, optional incident, version, workflow status, timestamps, and correlation identifier.
- **Approval Decision**: Authenticated business or DevOps evidence bound to one exact request ID and version, including decision, approver, role, duration, timestamp, and optional comment.
- **Access Grant**: The synthetic, time-limited result of successful protected provisioning, bound to the approved requester, request version, environment, role, activation, expiry, correlation, and operation identity.
- **Audit Event**: Insert-only evidence of a request action or outcome, tied to request version, actor when applicable, timestamp, correlation, and structured details.
- **Authenticated Principal**: One of the fixed demonstration identities whose authority is established by trusted server context rather than browser claims.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In usability validation, at least 90% of representative users can turn the primary example intent into a correctly submitted request on their first attempt within 3 minutes.
- **SC-002**: For 100% of acceptance tests, malformed or incomplete model output and invalid stored values result in correction guidance or a typed failure and never create a submitted approval or access grant.
- **SC-003**: Across all authorization tests, 100% of wrong-client, wrong-role, stale-version, missing-approval, duration-increase, and unauthorized-actor attempts are rejected without changing protected workflow state or creating a grant.
- **SC-004**: In 100% of successful paths, an active grant exactly matches the current request version, business-approved role, and a duration no greater than the business-approved duration.
- **SC-005**: Across at least 100 repeated or concurrent attempts for the same approved operation, exactly one grant exists and every successful response identifies that same grant.
- **SC-006**: Authorized participants can determine the current request status, approval evidence, grant outcome, expiry, and sequence of material events from the request-detail view within 60 seconds in at least 90% of usability checks.
- **SC-007**: Every required audit scenario produces one or more correlated events containing the request version, timestamp, outcome, and actor when applicable, while review confirms that no secrets, raw prompts, or complete sensitive context payloads are captured by default.
- **SC-008**: All required acceptance and negative-path checks complete repeatably without access to a live model or any real production, corporate identity, incident, or provisioning system.
- **SC-009**: Inspection of model-visible capabilities finds exactly the three approved read-only context operations and zero approval, provisioning, revocation, workflow-transition, arbitrary-data, or generic-query operations.
- **SC-010**: A single developer can demonstrate the successful request, wrong business approver, stale approval after material edit, and duplicate provisioning retry scenarios end to end in 15 minutes or less.

## Assumptions

- This is a portfolio-grade, locally demonstrable vertical slice; all identities, enterprise context, incidents, and access grants are synthetic and no real production access is created.
- Client Alpha uses environment `PROD-ALPHA-EU`, allows `ProductionReadOnly` and `ProductionSupport`, has an 8-hour maximum duration, and is assigned to the Client Alpha business approver.
- Client Beta uses environment `PROD-BETA-UK`, allows only `ProductionReadOnly`, has a 4-hour maximum duration, and is assigned to the Client Beta business approver.
- Incident `INC-1042` is available as active synthetic context for the primary Client Alpha demonstration scenario.
- A local identity selection mechanism establishes one of exactly four fixed authenticated principals and maps it to immutable trusted authority; it is explicitly not a production identity design.
- React is an explicit delivery constraint chosen to use existing developer expertise. Its built assets and same-origin UI endpoints remain part of the single deployable host; there is no separately deployed frontend.
- Justification is material because a changed business purpose must be reviewed again; display-only corrections that do not alter any listed material field may be treated as non-material during planning if they cannot change authorization meaning.
- Logical expiry is sufficient for this feature; automated revocation is deferred.
- Expected failures are presented in understandable, distinguishable forms, and all time-sensitive assisted or data lookup operations have finite bounds chosen during planning.
- The product baseline at `docs/governed-production-access-product-baseline.md` is the canonical source for fixed demonstration data, security boundaries, and scope.

### Scope Boundaries

- Included: model-assisted typed drafting, exactly three read-only data lookup operations, deterministic validation, two authenticated approval stages, version-bound decisions, immediate independently validated synthetic provisioning, safe retry, logical expiry, audit evidence, and a three-page React user experience served by the single host.
- Excluded: real identity or production integrations, permanent or emergency access, delegated or administrative roles, separation-of-duties analysis, role hierarchy, current-access conflict analysis, approval expiration, notifications, automated revocation, additional context or state-changing model tools, separate approval inboxes, administration pages, generic workflow or audit frameworks, autonomous agents, and enterprise-scale load behavior.
- Any deferred integration, stronger audit integrity, automated revocation, richer telemetry, or separately deployed component requires a later explicitly approved feature and must not expand this MVP.

### Dependencies

- Stable synthetic records must exist for the two clients, environments, supported roles, incidents, approver assignments, and four demonstration principals.
- The model-assisted drafting capability must support repeatable replacement with deterministic outcomes for validation and demonstration.
- Current stored data and workflow state must be available at both submission and provisioning time so stale-state rules can be verified.

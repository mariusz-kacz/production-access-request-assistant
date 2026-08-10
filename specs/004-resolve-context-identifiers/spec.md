# Feature Specification: Natural-Language Environment Resolution

**Feature Branch**: `004-resolve-context-identifiers`

**Created**: 2026-08-04

**Status**: Draft

**Input**: User description: "Allow the language model to resolve a production
environment's precise stable identifier from the requester's natural-language
description. Include the roles assigned to each environment in that authoritative
environment context so no separate role-listing tool is needed. Limit discovery to
environments; an optional incident must still be provided using its precise stable
identifier. When a value appears to be an environment identifier but exact lookup
does not find it, discover and show authoritative plausible environment alternatives
instead of silently correcting it. For environment clarification, preserve the
model's bounded conversational wording while the application independently validates
the model's structured option identifiers and renders authoritative option details."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resolve an Environment Description (Priority: P1)

An authenticated developer describes the target production environment using a
familiar client name, environment display name, location, or other readable terms
available in the authoritative environment context. The assistant consults that
context, proposes the one matching stable environment identifier, and derives the
client from the selected environment. The developer does not need to know or enter
the environment or client identifier.

**Why this priority**: Removing the need to look up an environment identifier is the
entire user-facing purpose of this feature.

**Independent Test**: Send a complete access request that identifies an environment
only through unambiguous readable terms and verify that the final request contains
the correct authoritative environment and client identifiers without asking the
developer to supply either identifier.

**Acceptance Scenarios**:

1. **Given** the authoritative context contains one environment matching "Client
   Alpha production in Europe," **When** a developer uses that wording without a
   stable identifier, **Then** the assistant proposes `PROD-ALPHA-EU` and derives
   `client-alpha` from the environment record.
2. **Given** the developer supplies an exact environment identifier, **When** the
   assistant prepares the request, **Then** the existing exact environment lookup
   and validation behavior continues to work.
3. **Given** one environment is resolved from readable terms, **When** the final
   request is presented, **Then** it shows the authoritative client and environment
   display values together with the exact stable scope and states that no access has
   yet been approved or granted.
4. **Given** the model proposes an environment identifier, **When** the typed
   candidate is evaluated, **Then** deterministic validation independently verifies
   that the environment exists, belongs to the derived client, and supports the
   requested role.
5. **Given** an environment candidate is returned for interpretation, **When** the
   assistant determines or clarifies the requested role, **Then** it uses only the
   authoritative roles included with that environment and performs no separate role
   lookup through the model-visible context surface.
6. **Given** a developer supplies a value that appears to be an environment
   identifier, **When** exact lookup does not find it, **Then** the assistant consults
   the bounded authoritative environment set and shows any plausible alternatives
   without silently replacing the rejected value.
7. **Given** one plausible authoritative alternative remains after a failed exact
   lookup, **When** the assistant responds, **Then** it asks the developer to confirm
   that environment using the model's conversational question accompanied by an
   application-rendered authoritative choice before proposing its identifier.

---

### User Story 2 - Clarify an Ambiguous Environment (Priority: P2)

A developer can use shorthand or partial wording that fits more than one production
environment. The assistant presents a small set of authoritative, readable choices
and asks one focused question rather than guessing. The developer can select a choice
without having to type its stable identifier.

**Why this priority**: Environment ambiguity must never result in access being
requested for the wrong client scope.

**Independent Test**: Use authoritative context in which one phrase matches multiple
environments, answer the focused clarification, and verify that no final request is
shown until one environment is selected and deterministically validated.

**Acceptance Scenarios**:

1. **Given** a phrase matches multiple production environments, **When** the
   assistant evaluates the request, **Then** it asks one focused model-authored
   question accompanied by application-rendered authoritative choices showing
   readable distinguishing information and does not select an environment on the
   developer's behalf.
2. **Given** a clarification presents ordered authoritative environment choices,
   **When** the developer responds with "the first one" while the relevant
   conversation history is available, **Then** the assistant proposes the stable
   identifier associated with that choice and the system independently validates it.
3. **Given** the developer names a client and exactly one production environment for
   that client matches all other stated terms, **When** context is resolved, **Then**
   that environment may be proposed without an identifier-specific clarification.
4. **Given** conversation history needed to interpret a relative choice is no longer
   available, **When** the developer sends the relative answer, **Then** the assistant
   repeats a self-contained environment clarification instead of guessing.
5. **Given** a failed exact lookup produces several plausible authoritative
   alternatives, **When** the assistant asks for clarification, **Then** it shows
   the model's focused conversational question plus readable authoritative client and
   environment information together with each unchanged stable identifier, and waits
   for the developer to select one.

---

### User Story 3 - Require an Exact Incident Identifier (Priority: P3)

A developer may associate an optional incident with the request, but this feature
does not discover incidents from titles or descriptions. The developer supplies the
precise stable incident identifier, after which the existing authoritative incident
validation checks its existence, active status, and relationship to the resolved
environment.

**Why this priority**: An explicit incident boundary keeps the change small and
prevents environment discovery from expanding into broader context search.

**Independent Test**: Submit one request with a valid exact incident identifier and
one using only an incident description; verify that the exact identifier is validated
normally while the description is never converted into an incident identifier.

**Acceptance Scenarios**:

1. **Given** a developer supplies the precise identifier `INC-1042`, **When** the
   assistant prepares the request, **Then** the existing exact incident lookup
   validates that identifier and its relationship to the resolved environment.
2. **Given** a developer refers only to "the payments outage" without a stable
   incident identifier, **When** the assistant prepares the request, **Then** it does
   not search for or infer an incident and asks the developer to provide the precise
   identifier or continue without an incident.
3. **Given** an exact incident identifier belongs to a different client or
   environment, is inactive, or does not exist, **When** it is validated, **Then** it
   is rejected and no final request containing that incident is displayed.

---

### User Story 4 - Recover Safely from Resolution Failure (Priority: P4)

A developer receives clear correction or retry guidance when an environment has no
match, context is unavailable, or model output is invalid. Failed environment
resolution never creates or advances an access request.

**Why this priority**: The convenience feature must preserve the existing safe
failure behavior and governed workflow boundary.

**Independent Test**: Exercise no-match, context timeout, cancellation,
unavailability, malformed output, and unexpected-tool cases and verify that each
fails safely without creating or advancing a request.

**Acceptance Scenarios**:

1. **Given** no authoritative environment matches the developer's wording, **When**
   resolution is attempted, **Then** the assistant says it could not find a valid
   match and asks for different environment information without inventing an
   identifier.
2. **Given** environment context times out, is unavailable, or is cancelled, **When**
   the turn ends, **Then** the developer receives a safe retry outcome and no request,
   approval, provisioning operation, or grant is created.
3. **Given** model output is malformed or proposes an environment absent from the
   authoritative candidate set, **When** the turn is validated, **Then** the output is
   rejected and no final request is displayed.
4. **Given** a user instructs the assistant to invent an identifier, ignore a
   mismatch, approve, or provision access, **When** the message is processed, **Then**
   those instructions do not change the candidate or workflow state.
5. **Given** an exact environment lookup fails because of timeout, cancellation,
   invalid input, or context unavailability rather than an authoritative no-match,
   **When** the turn ends, **Then** discovery fallback is not used to mask the failure
   and the developer receives safe retry or correction guidance.

### Edge Cases

- An environment display name differs from the user's wording only by capitalization,
  spacing, or punctuation; comparison may normalize those differences but always
  returns the stored stable identifier unchanged.
- The developer supplies only a client name, and that client has multiple production
  environments.
- Similar environment display names exist for different clients or locations.
- A description includes client terms that conflict with the selected environment's
  authoritative client relationship.
- The developer changes the environment after a role or incident identifier has
  already been supplied; dependent values are revalidated and incompatible values
  are cleared.
- The developer provides an incident title, partial identifier, reformatted
  identifier, or other description instead of the exact stable incident identifier.
- A potential environment identifier differs from an authoritative identifier by a
  missing suffix, extra punctuation, capitalization, or a transcription error.
- A rejected potential identifier resembles environments belonging to different
  clients or locations.
- A failed exact lookup has one plausible alternative, but readable client or
  location terms in the message conflict with that alternative.
- An exact incident identifier is valid but associated with a different environment.
- The resolved environment does not offer the requested role.
- Authoritative context changes between environment selection and final confirmation;
  confirmation-time validation rejects stale or invalid scope.
- The environment candidate set contains no records or too many matches to present
  clearly in one clarification.
- A model clarification message names an environment that is absent from its
  structured option identifiers; prose is never interpreted as an additional choice.
- One or more structured environment option identifiers are unknown, duplicated, or
  excessive; the associated model message and choices are not presented.
- The model calls an unapproved tool or the available tool catalog differs from the
  fixed allowlist.
- Two developers resolve similar environment descriptions concurrently; their
  candidate state and conversation context remain isolated.
- A repeated message or delivery retry must not create multiple preparations or
  requests.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow an authenticated developer to identify a
  production environment using readable terms without requiring a stable environment
  identifier.
- **FR-002**: During request preparation, the assistant MUST be able to consult a
  bounded authoritative set of production-environment candidates through the
  existing `get_production_environment` read-only capability.
- **FR-003**: Each discoverable environment candidate MUST include its stable
  environment identifier, authoritative client identifier, environment display name,
  the stable identifiers and readable names of the roles currently assigned to it,
  and enough non-sensitive readable context to distinguish it from other candidates.
- **FR-004**: The system MUST derive the client identifier from the selected
  authoritative environment record; the model and developer MUST NOT independently
  select or invent a conflicting client identifier.
- **FR-005**: The system MUST treat an environment description as unambiguous only
  when exactly one authoritative environment satisfies all explicit client,
  environment, and location terms in the developer's message and current candidate.
- **FR-006**: When exactly one valid environment match exists, the assistant MAY
  propose its stable identifier without an identifier-specific follow-up.
- **FR-007**: When multiple valid environment matches remain, the assistant MUST ask
  one focused clarification and MUST NOT select an environment on the developer's
  behalf. The model MUST provide the bounded conversational question and the proposed
  matches as separate structured environment identifiers.
- **FR-008**: When no valid environment match exists, the assistant MUST reject the
  unresolved value, avoid fabricating an identifier, and provide focused correction
  guidance.
- **FR-009**: The assistant MUST continue to support exact environment-identifier
  lookup in addition to readable environment discovery.
- **FR-010**: If a developer-provided client term conflicts with the selected
  environment's authoritative client relationship, the system MUST reject the
  conflict rather than silently reconciling it.
- **FR-011**: An optional incident MUST be accepted for validation only when the
  developer supplies its precise stable identifier; an incident title, description,
  partial identifier, or inferred reference MUST NOT be mapped to an incident.
- **FR-012**: When incident wording is present without a precise stable identifier,
  the assistant MUST ask the developer to provide that identifier or continue without
  an incident.
- **FR-013**: The existing exact `get_incident` lookup MUST validate every supplied
  incident identifier for existence, active status, and appropriate relationship to
  the resolved environment before it can appear in a final request.
- **FR-014**: The assistant MUST determine or clarify the requested role using the
  authoritative roles included with the resolved environment. The model-visible
  context surface MUST NOT expose a separate role-listing capability. Every role
  clarification MUST display all and only the authoritative role IDs returned for the
  selected environment, including when only one role is available.
- **FR-015**: Resolving or changing an environment MUST cause the client, requested
  role, and supplied incident to be re-evaluated; incompatible dependent values MUST
  not be carried into a final request.
- **FR-016**: The final request summary MUST display the authoritative client and
  environment names, the resolved stable environment scope, and any exact validated
  incident identifier that confirmation will submit.
- **FR-017**: Every model-proposed environment identifier and relationship MUST be
  schema-validated and independently checked against authoritative data before the
  intake can become ready for confirmation.
- **FR-018**: Environment discovery MUST NOT submit a request, record an approval,
  change workflow state, provision access, or serve as authorization evidence.
- **FR-019**: The model MUST receive exactly two approved read-only capabilities:
  `get_production_environment` and `get_incident`; it MUST receive no separate role
  capability, generic query, approval, workflow, provisioning, revocation, or
  arbitrary data capability.
- **FR-020**: `get_production_environment` MUST support bounded candidate discovery
  and exact lookup and MUST return the roles assigned to each environment.
  `get_incident` MUST remain precise-identifier-only.
- **FR-021**: Environment discovery MUST report invalid input, no match, ambiguity,
  timeout, cancellation, and unavailability as explicit safe outcomes where
  applicable.
- **FR-022**: If the approved context catalog is missing a required capability,
  contains an unexpected capability, or lacks its required read-only designation,
  the preparation turn MUST fail safely.
- **FR-023**: Environment choices may be interpreted from bounded active conversation
  history, but missing history MUST cause a self-contained re-clarification instead
  of a guessed selection.
- **FR-024**: Environment discovery MUST be limited to the fixed synthetic production
  environment dataset and MUST NOT become a generic search surface or large retrieval
  subsystem.
- **FR-025**: The system MUST avoid logging raw user messages, raw prompts, complete
  environment candidate payloads, or full clarification choices by default, while
  recording correlation, capability name, duration, and safe outcome metadata.
- **FR-026**: The existing immutable confirmation, authenticated human approval,
  deterministic authorization, fixed eight-hour grant, and idempotent provisioning
  behavior MUST remain unchanged after an environment is resolved.
- **FR-027**: When a developer supplies a value that plausibly represents an
  environment identifier, the assistant MUST attempt authoritative exact lookup
  before treating that value as readable environment context.
- **FR-028**: Only an authoritative exact-lookup no-match MAY trigger bounded
  environment discovery as a fallback. Invalid input, timeout, cancellation,
  unavailability, and malformed results MUST retain their explicit safe outcomes and
  MUST NOT be treated as no-match.
- **FR-029**: After an exact-lookup no-match, the assistant MUST compare the rejected
  value and the message's readable client and environment terms only against the
  bounded authoritative candidate set and MUST show only plausible alternatives from
  that set.
- **FR-030**: A failed exact lookup MUST NOT cause silent identifier correction. One
  plausible alternative requires explicit developer confirmation; multiple plausible
  alternatives require developer selection; no plausible alternative requires
  focused correction guidance.
- **FR-031**: Every fallback alternative MUST retain its authoritative stable
  identifier and readable client and environment information unchanged. Conflicting
  client, environment, or location terms MUST be disclosed and MUST prevent automatic
  proposal.
- **FR-032**: Fallback alternatives MUST be proposed as structured environment
  identifiers and independently checked against authoritative data. The model MAY
  supply the surrounding conversational question, but every displayed choice label,
  stable identifier, client relationship, and environment value MUST come from the
  authoritative records rather than generated prose.
- **FR-033**: For an environment clarification, the application MUST independently
  validate and reload every structured option identifier before presenting the
  model-authored message or any choices. Unknown, duplicate, excessive, or otherwise
  invalid option sets MUST fail safely and MUST suppress the associated message.
- **FR-034**: After successful option validation, the application MUST present the
  model's bounded conversational message as non-authoritative plain text and append
  choices whose identifiers and readable client and environment values come only
  from authoritative records. The application MUST NOT synthesize replacement
  conversational wording merely because valid choices are present.
- **FR-035**: Identifiers, names, relationships, instructions, or claims appearing
  only in model prose MUST NOT become choice data, candidate scope, workflow state,
  approval evidence, or authorization evidence.

### Governance & Trust Requirements *(mandatory)*

- **Authoritative actors and data**: The actor remains the authenticated developer in
  a personal Teams conversation. Stored production-environment, client,
  environment-role, incident, and principal records are authoritative. User wording,
  display names, and model matches are untrusted interpretation inputs. A client is
  derived from the authoritative environment record. An optional incident enters the
  candidate only through the precise stable identifier supplied by the developer.
- **State changes and authorization**: Environment discovery is read-only and does
  not authorize or change workflow state. Updating the durable preparation candidate
  follows existing authenticated intake rules. Only explicit requester confirmation
  may create an immutable request, and existing authenticated business and DevOps
  actions remain the only approval decisions.
- **Immutable scope and client isolation**: Environment resolution must honor the
  stored environment-to-client relationship and reject conflicting client wording.
  Exact incident identifiers remain subject to stored client and environment
  relationships. Confirmation binds the resolved scope to a new immutable request
  ID; corrections after submission require a new request and new approvals.
- **AI and MCP boundary**: Model output remains untrusted and schema-validated. The
  allowlist contains exactly `get_production_environment` and `get_incident`.
  Environment context provides bounded discovery, exact lookup, authoritative client
  relationships, and assigned roles. Incident context remains precise-identifier-
  only. No separate role tool is exposed. All proposed values remain independently
  validated, and no capability can authorize or mutate workflow state. A bounded
  model-authored clarification message may be shown only after its structured
  environment options pass validation; that prose remains informational and is never
  parsed or trusted as scope, choice data, approval, or authorization.
- **Provisioning and idempotency**: Provisioning is unaffected and remains unavailable
  to the model. It still begins only after valid human approvals, reloads persisted
  evidence, and uses the immutable request ID as its idempotency identity. Environment
  discovery creates no provisioning evidence and cannot trigger a retry.
- **Failure and audit evidence**: No environment match, ambiguous environments,
  conflicting client wording, imprecise or invalid incident input, malformed output,
  unexpected tool catalog, timeout, cancellation, and unavailable context are
  explicit safe outcomes. Operational evidence records correlation IDs, capability
  names, durations, and outcomes without raw prompts, full user messages, or complete
  context payloads. Existing request and decision audit events begin only at their
  existing governed boundaries.

### Key Entities *(include if feature involves data)*

- **Production environment candidate**: An authoritative production environment with
  a stable identifier, readable distinguishing context, a fixed relationship to one
  authoritative client, and the stable identifiers and readable names of its
  currently assigned roles.
- **Environment candidate set**: The bounded authoritative environments available to
  interpret the current description; it is temporary interpretation context, not
  authorization evidence or a workflow record.
- **Typed request candidate**: The current untrusted preparation snapshot containing
  the resolved environment and derived client identifiers, requested role,
  justification, and optional exact incident identifier.
- **Exact incident reference**: The precise stable incident identifier supplied by
  the developer and subsequently checked against authoritative incident status and
  relationships; incident descriptions are not resolution candidates.
- **Environment clarification choice**: A readable representation of one
  authoritative environment used to distinguish multiple matches within the active
  conversation. The model proposes its stable identifier separately from the bounded
  conversational message. The identifier is independently checked before the
  application renders authoritative display information beside that message. It is
  not approval evidence or a new persistent business record.
- **Rejected potential environment identifier**: A developer-supplied value that was
  attempted as an exact identifier and authoritatively returned no match. It may be
  used only to identify plausible clarification choices and is never corrected or
  persisted as authoritative scope.

### Verification Requirements *(mandatory)*

- **Domain/unit coverage**: Verify unique, zero-, and multiple-environment match
  handling; authoritative client derivation; conflicting client rejection;
  dependent role and incident revalidation after an environment change; exact-only
  incident requirements; validated structured choices remaining separate from model
  prose; and unchanged confirmation and workflow rules.
- **Integration/contract coverage**: Verify bounded discovery and exact lookup through
  `get_production_environment`, including the authoritative roles assigned to each
  environment; unchanged exact-identifier behavior for `get_incident`; absence of a
  separate role tool; stable identifiers and readable environment data; the exact
  two-tool catalog; explicit typed failures; cancellation and timeout propagation;
  exact-lookup no-match followed by discovery and clarification; absence of fallback
  for other failures; preservation of the bounded model clarification message beside
  independently rendered authoritative choices; suppression after invalid option
  sets; and final deterministic validation. Automated scenarios use a deterministic
  fake chat client and require no live model.
- **Negative coverage**: Verify invented or absent environment identifiers,
  ambiguous descriptions, no environment match, conflicting client terms, incident
  titles and partial identifiers, nonexistent or inactive exact incident identifiers,
  cross-client incidents, unsupported roles, malformed model output, unapproved or
  missing tools, context failure and timeout, lost conversation history,
  prompt-injection attempts, stale context at confirmation, concurrent conversations,
  replay behavior, silent typo correction, fallback suggestions absent from the
  authoritative catalog, identifiers present only in clarification prose, invalid
  option sets whose associated message must be suppressed, and fallback despite
  non-no-match failures.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of representative test requests containing an unambiguous
  readable environment description but no stable environment identifier, the final
  candidate contains the correct authoritative environment and derived client
  identifiers or fails safely; it never contains a different environment.
- **SC-002**: At least 90% of representative complete requests with an unambiguous
  environment description reach a confirmable final request without asking the
  developer to supply or look up an environment or client identifier.
- **SC-003**: In 100% of ambiguous, zero-match, and conflicting-client environment
  scenarios, no final request is presented until the developer selects or describes
  exactly one valid authoritative environment.
- **SC-004**: In 100% of incident-related tests, a precise valid incident identifier
  follows existing validation, while incident titles, descriptions, and partial
  identifiers are never converted into an incident identifier.
- **SC-005**: A developer can complete a representative request from an unambiguous
  environment description to a confirmable final summary in under two minutes,
  excluding time spent responding to genuinely ambiguous environment choices.
- **SC-006**: 100% of final request summaries show the readable environment context,
  derived client, exact stable environment scope, and any exact validated incident
  identifier before confirmation.
- **SC-007**: Across all automated resolution and failure scenarios, zero environment
  lookups create requests, record approvals, change workflow state, or initiate
  provisioning.
- **SC-008**: All environment-context timeouts, cancellations, unavailable
  dependencies, malformed outputs, and unexpected capability catalogs produce a
  comprehensible safe outcome within one conversation turn and preserve previously
  valid candidate values that do not depend on the failed resolution.
- **SC-009**: In 100% of representative requests, role choices shown after environment
  resolution are exactly the roles currently assigned to that environment, and an
  unsupported role never reaches the final request.
- **SC-010**: In 100% of representative failed exact-environment lookup scenarios,
  every suggested alternative comes from the authoritative environment set and no
  alternative becomes the proposed scope until the developer explicitly confirms or
  selects it.
- **SC-011**: In 100% of timeout, cancellation, invalid-input, unavailable-context,
  and malformed-result scenarios, the failure is not converted into discovery-based
  identifier correction.
- **SC-012**: In 100% of environment clarifications with a valid structured option
  set, the developer sees the model's focused conversational question together with
  authoritative choice labels and stable identifiers; generated names or identifiers
  appearing only in prose never appear as selectable choices.

## Assumptions

- The fixed synthetic production-environment dataset remains the authoritative
  discovery source; real corporate environment catalogs are outside this feature.
- Client information is returned as authoritative metadata of each environment and
  is derived from the resolved environment. There is no independent client discovery
  capability.
- `get_production_environment` evolves from exact-identifier-only lookup to bounded
  candidate discovery while retaining exact lookup, and each returned environment
  includes its authoritative assigned roles.
- `get_incident` continues to require the precise stable incident identifier supplied
  by the developer. Incident listing, searching, title matching, and semantic
  inference are explicitly outside scope.
- The separate `get_available_roles` capability is removed from the model-visible
  surface. Existing deterministic validation of the selected environment-role pair
  remains unchanged; generalized role discovery, hierarchy, or privilege comparison
  is outside scope.
- Exactly one valid authoritative environment is the safe default for automatic
  proposal; confidence scores alone never justify choosing among multiple records.
- A failed exact lookup is a distinct case: even one plausible alternative is shown
  for confirmation rather than silently proposed as a typo correction.
- Potential-identifier matching uses the same bounded authoritative environment set;
  no alias store, fuzzy-search service, or additional model-visible capability is
  introduced.
- Existing bounded process-local conversation history and the durable typed candidate
  support environment-choice follow-ups; candidate lists and transcripts are not new
  durable business records.
- Environment clarification output separates a bounded model-authored plain-text
  message from structured option identifiers. The message supplies conversational
  wording; the application supplies authoritative choice labels and controls after
  validating those identifiers. No second model call is required for rendering.
- Existing Teams-only intake, explicit confirmation, human approval, fixed eight-hour
  duration, provisioning, and audit journeys remain dependencies rather than redesign
  scope.
- The environment dataset is small enough for bounded discovery. Pagination,
  embeddings, and a general retrieval subsystem are outside scope.

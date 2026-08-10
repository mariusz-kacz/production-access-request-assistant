# Governed Access Workflow — Simplification Refactoring Tasks

## Refactoring Goal

Simplify the Governed Production Access implementation while preserving its important security and workflow guarantees.

The main problem to solve is duplication of domain authority:

* request scope is represented in multiple places;
* the same client/environment/role relationships are repeatedly checked;
* approval and provisioning evidence duplicates information already owned by the request;
* services contain defensive branches for states that should be impossible by construction;
* the test suite mirrors much of this internal complexity.

This is a simplification exercise, not an architectural modernization.

## Global Rules

Apply these rules throughout the refactoring:

1. Preserve existing externally visible HTTP, Teams, MCP, audit, authorization, and provisioning behavior unless a change is explicitly justified.

2. Keep model-generated and externally supplied data untrusted.

3. Keep authorization fail-closed.

4. Keep provisioning idempotent and concurrency-safe.

5. Prefer one authoritative representation of a domain fact rather than synchronizing several copies.

6. Remove defensive checks when the impossible state they protect against has been eliminated by construction or persistence.

7. Do not introduce:

   * CQRS;
   * MediatR;
   * generic repositories;
   * event sourcing;
   * additional services purely for layering;
   * additional deployment units;
   * schema-version infrastructure;
   * compatibility migration infrastructure for disposable development SQLite databases.

8. Existing tests are the primary characterization baseline.

9. Do not create a parallel replacement test suite.

10. Add a new test only when:

    * genuinely new behavior is introduced;
    * a new domain abstraction contains meaningful behavior;
    * an important trust/security boundary currently lacks coverage.

11. When implementation details disappear, delete or consolidate the tests that existed only for those details.

12. The final test count should preferably decrease and must not materially increase as a result of this simplification.

13. After every major task, stop and review whether the remaining refactoring is still justified.

---

# [X] Task 1 — Map Current Domain Authority and Establish the Baseline

## Objective

Understand exactly where request-scope authority currently exists before modifying it.

Do not change architecture during this task.

## Work

Identify every place where the following request facts are represented:

* client;
* environment;
* role;
* justification;
* optional incident;
* requester where relevant.

For each representation determine:

* where it originates;
* whether it is authoritative or derived;
* whether it is persisted;
* whether another component maintains a duplicate copy.

Identify all checks comparing these values across:

* request intake;
* request submission;
* `AccessRequest`;
* business approval;
* DevOps approval;
* workflow evidence;
* provisioning;
* persistence;
* query projections.

Classify each check as:

### A. Trust-boundary validation

Must normally remain.

Examples:

* validating model-generated identifiers;
* validating browser/API input;
* deriving authenticated actor authority;
* validating current approver assignment.

### B. Mutable-state validation

Usually remains.

Examples:

* approval still exists;
* assignment is still valid;
* request is still in an allowed state;
* provisioning operation remains retryable.

### C. Structural invariant

Prefer construction or database enforcement.

Examples:

* a decision belongs to one request;
* only one decision exists for a stage;
* only one provisioning operation exists for a request.

### D. Redundant internal defensive check

Candidate for deletion once authoritative ownership is established.

Examples:

* comparing a copied role against `AccessRequest.RoleId`;
* verifying that an operation contains the same environment as the request when operations are created exclusively from that request.

Also identify existing tests protecting the important external behavior.

Do not add characterization tests unless an important externally visible behavior currently has no meaningful coverage.

## Deliverable

Produce a short refactoring map showing:

* authoritative data flow;
* duplicated representations;
* checks to preserve;
* checks likely to remove;
* tests currently protecting the important boundaries.

## Done When

The intended authority model for the following tasks is understood before implementation begins.

---

# [X] Task 2 — Introduce One Canonical Set of Validated Request Details

## Objective

Create one authoritative immutable representation of validated request details.

This is the highest-value refactoring in the entire activity.

## Target Model

Introduce a concept similar to:

`ValidatedRequestDetails`

containing the canonical request facts required downstream:

* ClientId;
* EnvironmentId;
* RoleId.

These three identifiers form the access scope. The same value also contains the
governance context:

* Justification;
* optional IncidentId.

Exact naming and implementation can follow existing project conventions.

## Work

Clearly distinguish:

### Untrusted intake candidate

May contain:

* missing values;
* model-generated values;
* unknown identifiers;
* invalid combinations.

### Validated request details

Contains only:

* canonical identifiers;
* validated combinations;
* normalized values;
* immutable values.

Only the authoritative validation path should create validated request details from candidate data.

Make `AccessRequest.Details` own the canonical validated request details.

Remove independently writable copies of the same scope facts where they are no longer required.

Transport/query compatibility properties may temporarily project from `Details` when needed to preserve existing API shapes.

Persistence may continue storing flattened columns if convenient.

A new complex persistence model is not required merely because the domain uses a value object.

Avoid adding elaborate composite database constraints unless they clearly replace meaningful application complexity.

## Validation Boundaries

The canonical validated request details must still protect against:

* unknown identifiers;
* cross-client environment selection;
* invalid role/environment relationships;
* invalid incidents where applicable;
* malformed or incomplete model output.

Do not weaken those checks.

## Tests

Reuse existing validation tests.

Adapt them to the new authority model.

Add only small focused tests for `ValidatedRequestDetails` behavior that existing tests cannot provide, for example:

* authoritative creation;
* immutability/equality if relevant;
* persistence round-trip if custom reconstruction is introduced.

Do not create an exhaustive new value-object test matrix.

## Done When

There is one obvious answer to:

> Where do the authoritative validated request details live?

The answer should be:

> `AccessRequest.Details`, created by the authoritative validation path.

Stop and review the architecture before Task 3.

---

# [X] Task 3 — Simplify Intake and Confirmation

## Objective

Keep `RequestDraftService` and `RequestSubmissionService` focused now that validated request details have one authoritative representation.

## Work

Separate intake responsibilities into:

### Preparation

Responsible for:

* interpreting conversational input;
* merging candidate data;
* validating candidate data;
* producing clarification when incomplete;
* persisting candidate/ready intake state.

### Confirmation

Responsible for:

* authenticated ownership;
* expiry;
* replay protection;
* refreshing/revalidating facts that can legitimately become stale;
* atomically creating the submitted `AccessRequest`;
* recording required audit evidence;
* closing the intake.

Evaluate whether `RequestSubmissionService` still provides useful independent behavior.

If submission exists only as part of confirmation, remove it and perform submission within the confirmation use case.

Remove manual comparisons such as `MatchesScope` when the request is being constructed directly from the validated canonical details.

Simplify intake lifecycle states if several current states represent equivalent behavior.

Prefer a small behavioral lifecycle such as:

* Collecting;
* ReadyForConfirmation;
* Closed;

with a close reason where useful.

Do not force this exact state model if the existing one is already clearer after other simplifications.

## Preserve

* clarification behavior;
* ownership checks;
* exact expiry behavior;
* replay safety;
* `/new`/reset behavior;
* safe model failure;
* timeout/cancellation at the real model boundary;
* audit evidence;
* atomic request creation.

## Tests

Use existing intake and Teams tests as the baseline.

Modify tests affected by removed internal types.

Delete tests whose only purpose was proving:

* equality between duplicated representations;
* impossible internal states;
* deleted result factories;
* fake internal concurrency.

Keep focused tests for:

* ownership;
* expiry;
* replay;
* failed validation;
* atomic confirmation;
* complete Teams confirmation flow.

Do not create a new comprehensive intake test suite.

## Done When

The intake path can be explained approximately as:

candidate → validation → canonical details → ready intake → confirmation → AccessRequest

and no additional scope synchronization occurs during submission.

Stop and reassess whether the code is already sufficiently simpler before proceeding.

---

# [X] Task 4 — Simplify Business and DevOps Approval

## Objective

Make approvals authorize an immutable request rather than carrying another copy of request scope.

## Work

Review:

* business decision evidence;
* DevOps decision evidence;
* the stage-aware `ApprovalDecisionPolicy`;
* `WorkflowEvidencePolicy`;
* `AccessRequestWorkflowService`.

Approval evidence should normally contain only what is actually evidence of the decision:

* RequestId;
* actor;
* decision/outcome;
* timestamp;
* stage/order information where necessary.

Remove duplicated values such as `ApprovedRoleId` when the approved role is necessarily the role contained in the immutable request.

Derive compatibility/query projections from `AccessRequest.Details` where external responses still expose these values.

Approval authorization must reload and verify mutable authority such as:

* authenticated actor;
* current actor role;
* current business approver/client assignment;
* current request workflow state;
* previous approval where required;
* duplicate decision prevention.

Remove repeated canonical request-scope comparisons when those relationships were already validated before request creation and cannot subsequently change.

Reduce or remove approval branches in `AccessRequestWorkflowService`.

Introduce a focused approval service only if it replaces real complexity rather than adding another layer.

## Tests

Preserve tests for:

* correct business approver;
* wrong-client business approver;
* unauthorized actor;
* DevOps authorization;
* stale approver assignment;
* duplicate decision;
* wrong transition/order;
* missing business approval before DevOps;
* rejected-attempt audit behavior where required.

Delete tests for impossible mismatches between approval scope copies and request scope once those copies no longer exist.

Do not duplicate the same policy scenario at unit, SQLite, and full-host levels unless each level protects a distinct failure mode.

## Done When

A business or DevOps decision answers:

> Who made what decision about Request X?

It should not need to answer:

> Which separately copied role/environment/client did they approve?

Stop and review before modifying provisioning.

---

# [X] Task 5 — Simplify Provisioning Around Persisted Request Evidence

## Objective

Make provisioning consume the persisted authoritative request and mutable approval evidence without maintaining local copies of request scope.

## Work

Provisioning should receive or resolve a request identity and reload:

* `AccessRequest`;
* business decision;
* DevOps decision;
* existing provisioning operation;
* existing grant if applicable.

Validate only evidence that can legitimately change or become inconsistent.

Examples:

* required approvals exist;
* approvals are accepted;
* operation is in a valid/retryable state;
* completed operation has corresponding expected outcome;
* authenticated DevOps actor can trigger the action where required.

Construct provider input exclusively from:

`AccessRequest.Details`

Remove duplicated scope from:

* provisioning operation;
* grant;
* workflow evidence;

where those fields exist only to mirror the request.

Remove checks such as:

* operation role equals request role;
* grant environment equals request environment;

when those objects can only be created from `AccessRequest.Details`.

Preserve:

* request-ID idempotency;
* retry behavior;
* provider failures;
* lost-response recovery;
* partial failure handling;
* transaction consistency;
* concurrency safety.

Keep provisioning idempotency and confirmation convergence covered by the routine
SQLite component suite.

## Tests

Preserve high-value provisioning coverage:

* missing approval;
* unauthorized provisioning/retry;
* provider failure;
* retry;
* lost response;
* partial recovery;
* idempotency;
* concurrent confirmation convergence;
* provider receives access data from authoritative request details.

Delete:

* impossible local/request scope mismatch tests;
* exact collaborator-call-count tests;
* duplicated cancellation tests below the real provider boundary.

## Done When

The provisioning data flow is essentially:

RequestId
→ reload AccessRequest + mutable approvals/operation
→ validate current evidence
→ derive provider request from AccessRequest.Details
→ provision/recover idempotently

There should be no second provisioning-owned copy of the access-request scope.

---

# [X] Task 6 — Consolidate Tests After Production Simplification

## Objective

Reduce the test suite to coverage of meaningful behavior and distinct architectural boundaries.

Do this after the production design has stabilized.

## Work

Review all remaining tests and classify them by behavioral owner.

### Unit tests should primarily protect

* domain behavior;
* canonical request validation;
* approval policies;
* important state transitions.

### Persistence/component tests should primarily protect

* actual database constraints;
* transactions;
* persistence reconstruction;
* idempotency;
* concurrency;
* workflow integration with real SQLite.

### Full-host tests should primarily protect

* authentication;
* authenticated actor derivation;
* authorization integration;
* antiforgery;
* route/serialization compatibility;
* Teams integration;
* MCP read-only contract;
* a small number of complete workflows.

### Frontend tests should remain small

Protect only meaningful UI behavior.

## Remove or Consolidate

Actively look for tests that exist only because the previous architecture duplicated state.

Candidates include:

* request/approval role mismatch tests;
* request/operation scope mismatch tests;
* tests for deleted outcome factories;
* tests for impossible entity construction;
* identical validation permutations repeated at several test layers;
* exact mock invocation counts;
* fake concurrency tests already covered against SQLite;
* cancellation forwarding tests for small internal methods;
* tests covering private implementation details.

Prefer parameterized tests where several cases represent the same rule.

Do not remove a test simply because it is inconvenient.

Every deleted test should either:

* protect behavior that no longer exists;
* protect an impossible state;
* duplicate another test with the same failure-detection capability.

## Target

There is no required numerical target.

However, a successful simplification should normally result in fewer backend tests than before the refactoring.

A material increase in test count requires explicit justification.

## Done When

The test suite is easier to explain:

> these tests prove our domain rules, these prove persistence/concurrency, and these prove external boundaries.

rather than:

> every layer re-tests every possible invariant.

---

# [X] Task 7 — Remove Obsolete Architecture and Update Documentation

## Objective

Finish the refactoring by removing the old concepts rather than leaving compatibility architecture behind.

## Work

Search for and remove:

* obsolete services;
* obsolete scope properties;
* old scope-comparison helpers;
* retired mismatch error codes;
* old result types made unnecessary by deleted flows;
* unused factories;
* unused ports/interfaces;
* duplicate service registrations;
* outdated tests;
* stale documentation.

Examples may include:

* `RequestSubmissionService`;
* old `RequestValidator`;
* remaining portions of `AccessRequestWorkflowService`;

but delete them only if the preceding refactoring has genuinely made them unnecessary.

Update documentation to explain:

### Authoritative scope

Where candidate data becomes authoritative and where it lives afterwards.

### Validation boundaries

What is validated:

* at model/candidate boundary;
* at confirmation;
* during approval authorization;
* before provisioning.

### Invariant ownership

Which guarantees belong to:

* domain construction;
* application authorization;
* persistence constraints;
* provisioning idempotency.

### Testing philosophy

Explain why some rules intentionally have only one primary test owner instead of being tested independently at every layer.

Run:

* warnings-as-errors build;
* unit tests;
* integration tests;
* frontend tests;
* explicit provisioning concurrency test.

## Done When

No obsolete parallel model remains and documentation describes the implementation that actually exists.

---

# [X] Optional Task 8 — Simplify Application Result/Error Types

Completion note: the application already had the shared `ApplicationResult` and
`ApplicationFailure` model. The review retained outcome-specific preparation,
confirmation, and reset results because they represent distinct successful branches,
while removing factory guards and error codes that only defended against impossible
application-created values. No parallel error abstraction was introduced.

Do **not** perform this automatically.

After Tasks 1–7, review remaining service-specific:

* result wrappers;
* failure classes;
* error constants;
* factory methods.

Ask:

> Are these still making the application materially harder to understand?

If no, stop.

If yes, perform a small dedicated cleanup introducing a shared application result/error model.

The cleanup should reduce concepts, not merely rename them.

Do not combine this with scope, approval, or provisioning changes.

---

# Recommended Execution Order

Execute:

1. Task 1 — authority/invariant map
2. Task 2 — canonical request details
3. **STOP / REVIEW**
4. Task 3 — intake and confirmation
5. **STOP / REVIEW**
6. Task 4 — approvals
7. **STOP / REVIEW**
8. Task 5 — provisioning
9. **STOP / REVIEW**
10. Task 6 — test consolidation
11. Task 7 — cleanup/documentation
12. Task 8 only if still justified

Each major task can be one commit or a small sequence of cohesive commits.

Do not continue merely because the next task exists.

The purpose of each review gate is to ask:

> Is the remaining complexity still a real problem after the previous simplification?

If not, stop the refactoring.

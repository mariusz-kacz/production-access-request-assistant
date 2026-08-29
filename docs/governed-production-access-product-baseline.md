# Governed Production Access Request Assistant: Product Baseline

- **Status**: Current
- **Last reviewed**: 2026-08-28
- **Scope**: Local synthetic MVP

## Purpose

The application governs temporary access requests for client-specific production
environments. A requester prepares and confirms a request in a personal Microsoft
Teams conversation. Business and DevOps approvers make authenticated decisions in the
Web application. Successful DevOps approval immediately invokes synthetic,
request-keyed provisioning.

The governing rule is:

> AI interprets and gathers context. Humans approve. Deterministic services authorize
> and execute.

The model is never an authorization boundary. It can interpret requester text and use
read-only context tools, but it cannot create a request, select an approver, approve,
provision, retry, revoke, or change workflow state.

## Product boundary

- One modular ASP.NET Core host contains the Teams endpoint, React application,
  same-origin browser API, AI adapter, MCP endpoint, application services, SQLite
  persistence, and synthetic provisioner. Separate reference-authority and workflow
  SQLite databases remain independently owned inside that one host.
- Teams confirmation is the only request-creation path. The browser is a request
  register and human-decision surface.
- Submitted request scope is immutable. A correction requires a new intake, request ID,
  and approval sequence.
- Every grant lasts exactly eight hours from activation. Duration is not accepted from
  the requester, model, browser, or either approver.
- All identities, reference data, incidents, requests, and grants are synthetic. The
  application does not connect to a real production-access provider.

The current runtime structure is documented in the
[architecture](architecture.md); detailed threats and controls are in the
[security model](security-model.md).

## Authority and trust

### Model and MCP

Model output is untrusted and must match the closed request-proposal schema. Every
proposed client, environment, role, and incident identifier is reloaded and validated
against authoritative application data.

The model receives exactly four MCP tools:

- `search_production_environments` searches the bounded eligible production catalog
  with one structured query and returns at most five complete environment/client
  projections;
- `get_production_environment` exact-loads one eligible environment and its owning
  client;
- `get_environment_roles` lists roles currently assignable in one exact eligible
  environment; and
- `get_incident` exact-loads one precise incident ID without providing discovery.

The MCP project has no workflow, approval, provisioning, revocation, arbitrary
database, or generic-query dependency. Tool annotations and visibility are not
authorization.

### Human decisions

- Acting identity comes from authenticated server context.
- Browser-submitted identities, roles, claims, approver assignments, duration, and
  approval assertions are ignored or rejected.
- The owning client determines the business approver; the requester cannot nominate
  one.
- Business and DevOps decisions are structured authenticated actions bound to one
  immutable request ID.
- DevOps may approve the exact business-approved role or reject. It cannot substitute
  another role or change duration.

### Provisioning

The protected provisioning service accepts only a request ID. It reloads the request,
business approval, DevOps approval, provisioning operation, and any existing grant
before building provider input from the immutable request details.

The request ID is the provider idempotency identity. Repeating an operation for the
same request returns the matching existing grant rather than creating another one.
Provider execution and SQLite persistence are separate consistency boundaries; a
failed or lost local outcome is recovered through the narrowly authorized retry path.

## Synthetic context

The fixed dataset contains four clients, sixteen environments, three roles, three
incidents, and six principals. Startup creates missing records and fails if stored
reference data differs from these definitions.

| Client | Regions | Primary roles | Recovery roles |
|---|---|---|---|
| `client-alpha` | EU, US | `ProductionReadOnly`, `ProductionSupport`, `ProductionDeployment` | `ProductionReadOnly`, `ProductionSupport` |
| `client-beta` | UK, EU | `ProductionReadOnly` | `ProductionReadOnly` |
| `client-gamma` | US, APAC | `ProductionReadOnly`, `ProductionSupport`, `ProductionDeployment` | `ProductionReadOnly`, `ProductionSupport` |
| `client-theta` | APAC, US | `ProductionReadOnly` | `ProductionReadOnly` |

Each region has a primary environment named `PROD-{CLIENT}-{REGION}` and a recovery
environment named `RECOVERY-PROD-{CLIENT}-{REGION}`.

The principals are one requester, one business approver for each of the four clients,
and one DevOps approver. The browser identity selector maps a fixed key to immutable
server-side claims; it is convenient synthetic authentication, not proof of a real
human identity.

Each incident belongs to exactly one environment. Its client is derived from that
environment rather than stored independently:

| Incident | Status | Environment |
|---|---|---|
| `INC-1042` | Active | `PROD-ALPHA-EU` |
| `INC-1041` | Inactive | `PROD-ALPHA-EU` |
| `INC-2042` | Active | `PROD-BETA-UK` |

## Request lifecycle

### Preparation

An authenticated personal Teams actor starts or continues one intake bound to the
channel, tenant, actor, conversation, and fixed synthetic requester.

For every normal message:

1. The application loads or creates the active preparation and supplies the latest
   requester message, canonical candidate, lifecycle, and any persisted bounded
   environment/role clarification choices to the interpreter.
2. One fresh bounded MAF turn returns a closed dialogue act plus either a nonempty
   sparse `set`/`clear` patch, one closed discussion topic, or no semantic payload.
3. Core validates the proposal structure, evaluates environment/incident/role as one
   atomic scope group and justification independently, reloads authoritative facts,
   applies dependency cascades, selects and explains a sole assignable role when the
   role is otherwise unset, and determines readiness.
4. The workflow database persists only canonical preparation state, one optimistic
   concurrency version, bounded clarification context, lifecycle evidence, and
   bounded model-version attribution. It stores no raw requester transcript, prompt,
   provider session, model reasoning, raw proposal, or complete MCP payload.

Every turn creates a new provider session; there is no process-local conversation
history or choice store. Restart preserves the canonical candidate and bounded
clarification choices, so references to an active displayed choice can be interpreted
again without treating a transcript as authority. General coreference outside that
state is answered conservatively.

An exact trimmed, case-insensitive `/new` command supersedes an active unsubmitted
preparation without calling the model or MCP and atomically creates a clean collecting
preparation. Submitted requests are unaffected.

### Ready-draft discussion and revision

A ready preparation is an immutable confirmation snapshot. Discussion, submission
guidance, unrelated or unclear input, value-equal proposals, wholly rejected
proposals, and transient failures leave that preparation and its deadline unchanged.
An accepted material revision or clarification-producing revision atomically
supersedes the ready preparation and creates a predecessor-linked collecting or ready
replacement with a new preparation ID. Ambiguity preserves canonical scope in that
replacement while persisting the complete bounded choice set; it never clears scope
merely because the requester must choose.

Every card carries its own preparation ID. Confirmation reloads that exact intake, so
a stale or superseded card cannot submit replacement scope even if Teams could not
update the old activity visually.

If confirmation finds that authoritative context changed, it creates no request and
terminally supersedes the confirmed preparation. A corrected ready successor is the
only actionable revision, and its replacement card contains an application-owned
notice explaining the authoritative change. Only actionable ready cards are retained
as active draft presentation metadata; submitted request receipts are terminal cards
and are never rewritten as drafts.

### Submission and approval

Confirmation reloads the owned, unexpired ready preparation and revalidates its
canonical scope. One workflow-database save records the submitted preparation, immutable
`AwaitingBusinessApproval` request, and request-created audit event.

The workflow is:

```text
AwaitingBusinessApproval
  | approve
  v
AwaitingDevOpsApproval ---- reject ----> Rejected
  | approve
  v
Active <---- retry ---- ProvisioningFailed
```

Business rejection also moves the request to `Rejected`. DevOps approval is persisted
with a pending request-keyed operation before the provider is called. Provider success
finalizes the operation, request, grant, and audit evidence. Only the authenticated
DevOps approver may retry a failed operation, and retry accepts no replacement scope.

## Required behavior

| ID | Requirement |
|---|---|
| FR-01 | Accept natural-language request preparation only through authenticated personal Teams conversations and fail safely on malformed model output. |
| FR-02 | Expose exactly the four typed read-only MCP tools and no state-changing model capability. |
| FR-03 | Validate all proposed identifiers, relationships, roles, and incident state against authoritative data. |
| FR-04 | Derive acting identity and authority from authenticated server context. |
| FR-05 | Resolve the business approver from the selected environment's owning client. |
| FR-06 | Bind both human decisions to the same immutable request ID and exact scope. |
| FR-07 | Enforce the business-approved role and fixed eight-hour grant lifetime. |
| FR-08 | Keep submitted requests immutable; corrections create new requests and approvals. |
| FR-09 | Trigger protected provisioning immediately after successful DevOps approval. |
| FR-10 | Reload persisted request, approval, operation, and grant evidence before provider execution. |
| FR-11 | Use the request ID as the provisioning idempotency identity and permit only scoped failed-operation retry. |
| FR-12 | Record request, decision, provisioning, retry, success, failure, and rejected-action evidence with actor and correlation metadata where applicable. |
| FR-13 | Filter request lists and details to workflow participants; UI actions never replace server authorization. |
| FR-14 | Store activation and expiry timestamps and present grants as logically expired after eight hours. |

## Quality gates

- Nullable reference types are enabled and warnings are treated as errors.
- Cancellation propagates through asynchronous boundaries.
- Teams model/MCP work and synthetic provisioning have explicit deadlines.
- Expected validation, authorization, transition, concurrency, provider, timeout, and
  cancellation failures use typed outcomes.
- Automated tests require no live model, Teams tenant, Azure subscription, or real
  provider.
- Domain rules use unit tests; authentication, MCP, persistence, concurrency,
  idempotency, and full-host boundaries use integration tests.
- Logs contain correlation, duration, outcome, actor, transition, and operation
  metadata without requiring secrets, raw prompts, transcripts, or complete MCP
  payloads.

## Scope limits

The current baseline excludes real identity federation, real client or incident
systems, real credentials or production provisioning, mutable reference-data
administration, automatic revocation, notification workflows, generic workflow or
policy engines, additional MCP capabilities, large retrieval systems, background
reconciliation, message brokers, multiple deployable services, and independently
hosted frontend infrastructure.

A requirement for real identities or data, mutable systems of record, automatic
recovery, separate ownership or scaling, durable conversation history, or actual
credential-bearing provisioning requires a new security review and architecture
decision before expanding these boundaries.

## Acceptance cases

The repeatable evidence must cover:

1. complete Teams preparation and confirmation followed by both approvals and one
   eight-hour grant;
2. wrong-client business approval rejected without a workflow transition;
3. clarification and discussion preserving an existing ready card until a replacement
   outcome is determined;
4. submitted-scope correction producing a new request and approval sequence;
5. malformed model output, unknown identifiers, unavailable roles, inactive or
   incompatible incidents, timeouts, and dependency failures failing closed; and
6. lost provisioning response and retry converging on one request-keyed grant.

The executable validation sequence and test ownership are maintained in the
[testing strategy](testing-strategy.md).

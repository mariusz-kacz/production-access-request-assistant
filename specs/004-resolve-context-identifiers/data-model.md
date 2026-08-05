# Data Model: Natural-Language Environment Resolution

## Overview

This feature adds no persistent business entity. It reads the fixed reference tables
through a provider-neutral projection used only for request preparation. The submitted
request, approval, provisioning, and audit models remain unchanged.

## Existing Authoritative Entities

### Client

Authoritative owner of one or more production environments.

| Field | Type | Rules |
|------|------|-------|
| `Id` | stable identifier | Required, unique, ordinal comparison |
| `DisplayName` | readable text | Required; interpretation aid, never authority |
| `BusinessApproverPrincipalId` | principal identifier | Required; server-owned and never requester-selectable |

### ProductionEnvironment

Authoritative production scope that a request may target.

| Field | Type | Rules |
|------|------|-------|
| `Id` | stable identifier | Required, unique, ordinal comparison |
| `ClientId` | client identifier | Required; must reference exactly one `Client` |
| `DisplayName` | readable text | Required; supplied to the model for interpretation |

### EnvironmentRole

Authoritative assignment of one supported production role to one environment.

| Field | Type | Rules |
|------|------|-------|
| `EnvironmentId` | environment identifier | Required; part of composite key |
| `RoleId` | role identifier | Required; part of composite key; must be a supported role |

The current supported role identifiers are `ProductionReadOnly`,
`ProductionSupport`, and `ProductionDeployment`. There is no hierarchy, implication,
or generalized privilege comparison.

### Incident

Optional authoritative incident associated with a request.

| Field | Type | Rules |
|------|------|-------|
| `Id` | stable identifier | Must be supplied precisely by the requester |
| `ClientId` | client identifier | Must match the resolved request client |
| `EnvironmentId` | optional environment identifier | When present, must match the resolved environment |
| `Title` | readable text | Display only; not searchable in this feature |
| `Status` | `Active` or `Inactive` | Only active incidents may enter a final request |

## New Non-Persistent Read Projection

### ProductionEnvironmentContext

A Core-owned, provider-neutral snapshot that composes the authoritative records needed
by `get_production_environment`. It is not tracked, persisted, approved, or used as
provisioning evidence.

| Field | Source | Rules |
|------|--------|-------|
| `Environment` | `ProductionEnvironment` | Required authoritative record |
| `Client` | `Client` | Required; `Client.Id` must equal `Environment.ClientId` |
| `AssignedRoles` | `EnvironmentRole[]` | Required collection; every item must reference `Environment.Id`; stable ordering by `RoleId` |

Reader behavior:

- Exact read returns one context or typed `NotFound`.
- Discovery read returns contexts ordered by `Environment.Id` using ordinal ordering.
- Discovery reads at most `MaximumEnvironmentCandidates + 1` to detect overflow.
- More than 20 authoritative environments returns typed `Unavailable` with code
  `environment-candidate-limit-exceeded`; no partial contexts are returned.
- An empty authoritative catalog returns an empty successful collection.
- Persistence and cancellation failures return existing typed outcomes.

### MCP Environment Candidate

Infrastructure-bound representation of one `ProductionEnvironmentContext`.

| Field | Source | Rules |
|------|--------|-------|
| `environmentId` | `Environment.Id` | Stable authoritative ID |
| `clientId` | `Client.Id` | Stable authoritative ID |
| `clientDisplayName` | `Client.DisplayName` | Readable interpretation aid |
| `displayName` | `Environment.DisplayName` | Readable interpretation aid |
| `roles` | `AssignedRoles` | Stable ordered role IDs with boundary-owned display names |

The MCP candidate is not a domain entity. The MCP adapter translates the Core
projection into this contract and derives only readable role labels; it does not
authorize or mutate anything.

## Typed Request Candidate and Clarification

The model-output candidate fields remain unchanged:

| Field | Type | Feature behavior |
|------|------|------------------|
| `ClientId` | nullable stable ID | Derived from the selected authoritative environment |
| `EnvironmentId` | nullable stable ID | Proposed from exact lookup or bounded catalog interpretation |
| `RequestedRoleId` | nullable stable ID | Must be present in the selected environment candidate and later pass independent validation |
| `Justification` | nullable text | Existing rules unchanged |
| `IncidentId` | nullable stable ID | Only a precise requester-supplied ID may be proposed |

The candidate remains untrusted. `RequestValidator` reloads the selected environment,
client, environment-role assignment, and optional incident rather than trusting the
tool result or model statement.

The clarification object retains `target` and `message`, but its target enum is
narrowed to `environmentId`, `requestedRoleId`, `justification`, and `incidentId`.
`clientId` is deliberately excluded: the application derives it from the selected
authoritative environment and never asks the requester to choose it independently.

Every non-null clarification also contains `environmentOptionIds`, a required array
of zero to 20 unique stable IDs. It is empty for non-environment clarifications and
for an environment no-match correction. For an environment choice it contains only
the model-proposed shortlist. The array remains untrusted: the application reloads
the referenced environments, rejects duplicate, excessive, or unknown IDs, sorts
valid contexts by stable environment ID, presents the model's bounded `message` as
non-authoritative plain text, and appends client/environment names and stable IDs from
those records. The message is never parsed for additional options or scope, and an
invalid option set suppresses the associated message and choices. The model must
honor explicit readable scope terms, while mandatory confirmation or selection
prevents a fallback option from becoming scope automatically. Complete contexts and
choice lists are not persisted in the intake.

## Relationships

```text
Client 1 -------- * ProductionEnvironment
                         |
                         | 1
                         |
                         * EnvironmentRole

ProductionEnvironmentContext (non-persistent)
  |-- exactly one ProductionEnvironment
  |-- exactly one matching Client
  `-- zero or more assigned EnvironmentRole records

TypedRequestCandidate (untrusted)
  |-- proposes one EnvironmentId
  |-- derives one ClientId
  |-- proposes one role assigned to that environment
  `-- optionally carries one precise IncidentId
```

## Interpretation Outcomes

These are not persisted states or MCP failures:

| Catalog interpretation | Model outcome | Application behavior |
|------------------------|---------------|----------------------|
| Exactly one environment satisfies the readable terms | Candidate proposal | Independently validate all stable values |
| More than one environment remains plausible | One focused model-authored `environmentId` clarification plus structured option IDs | Validate every ID, render the message with authoritative choices, persist sanitized candidate only, and create no request |
| No environment satisfies the terms | One focused model-authored correction with no options | Keep environment unresolved; create no request |
| Potential identifier exact lookup returns `NotFound`, one catalog alternative is plausible | One focused model-authored "did you mean" `environmentId` clarification with one structured option ID | Validate the ID, render the message with the authoritative option, and do not replace the rejected value until the developer confirms |
| Potential identifier exact lookup returns `NotFound`, several catalog alternatives are plausible | One focused model-authored `environmentId` clarification with structured option IDs | Validate, reload, sort, and render the message with authoritative choices; accept only a developer-selected member |
| Potential identifier exact lookup returns `NotFound`, no catalog alternative is plausible | One focused correction | Keep environment unresolved; never fabricate an ID |
| Potential identifier exact lookup returns any other failure | Typed safe failure or retry guidance | Do not run discovery fallback or alter dependent values |
| Requested role absent from selected candidate | One focused `requestedRoleId` clarification | Never treat the role as allowed |
| Incident wording lacks a precise ID | One focused `incidentId` clarification | Do not call incident lookup or infer an ID |

## Existing State Transitions

No workflow state is added. Discovery occurs only while an intake is `Collecting`.
The existing transitions remain:

```text
Collecting --complete deterministic validation--> Ready
Collecting --superseded/reset--------------------> Superseded
Ready ------expired------------------------------> Expired
Ready ------authenticated confirmation----------> Submitted + immutable request
```

Changing the proposed environment re-evaluates the derived client, requested role,
and optional incident. Incompatible dependent values are cleared before the intake
can become `Ready`.

## Persistence Impact

- The fixed reference schema stores business-approver responsibility on `Client`
  rather than repeating it on every `ProductionEnvironment`.
- No new durable catalog, alias, confidence, ranking, or clarification-choice data.
- The rejected potential identifier and its fallback shortlist remain turn-local
  interpretation context and are not new durable fields.
- No transcript or full MCP payload persistence.
- Existing startup validation of the fixed synthetic dataset remains authoritative.

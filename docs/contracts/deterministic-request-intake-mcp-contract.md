# MCP Contract: Deterministic Request Intake

- **Status:** Current; promoted to production on 2026-08-27
- **Date:** 2026-08-26
- **Last reconciled:** 2026-08-28
- **Canonical machine-readable contract:** `mcp-tools.json`
- **Environment-search policy version:** `2.0.0`
- **Endpoint/transport:** Existing `/mcp` Streamable HTTP boundary
- **Normative source:** Current product baseline and `mcp-tools.json`

## 1. Catalog invariant

The initialized model-visible catalog contains exactly:

1. `search_production_environments`;
2. `get_production_environment`;
3. `get_environment_roles`; and
4. `get_incident`.

Every tool is annotated:

```json
{
  "readOnlyHint": true,
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false
}
```

The server advertises no model-visible resource, prompt, submit, approval,
provisioning, retry, revocation, workflow-transition, credential, arbitrary-database,
generic-query, client-wide role-search, or cross-environment role-search capability.

Missing, additional, renamed, or non-read-only tools cause interpreter initialization
or turn execution to fail closed.

## 2. Authority model

| Tool | Capability authority | Facts returned | Facts deliberately omitted |
|---|---|---|---|
| `search_production_environments` | Service-catalog/CMDB search projection | Complete bounded matching eligible production environment/client identities | Roles, scores, match reasons, business approver, workflow data |
| `get_production_environment` | Environment registry/CMDB | Exact eligible production environment identity and owning client | Assigned roles, incidents, business approver, workflow data |
| `get_environment_roles` | IAM/entitlement catalog | Roles currently assignable in one exact environment | Requester eligibility, client-wide/cross-environment roles, approval policy |
| `get_incident` | ITSM/incident system | Exact incident state and affected environment | Incident search/listing, environment roles |

The synthetic authority adapters may share the reference-authority SQLite database.
They never share the workflow database. Separate contracts remain because the
production-shaped authorities have different ownership, permissions, freshness,
latency, and failure semantics.

In the modular monolith, the authorities share only the reference-authority
database, not the workflow database. `GovernedAccess.ReferenceAuthority` owns direct
reference persistence and implements Core authority ports. `GovernedAccess.Mcp` owns
only these wire contracts and maps through those ports; it has no EF Core, reference
database, workflow persistence, or request-lifecycle dependency. The Web host composes
both modules without exposing either `DbContext` to MCP handlers, AI/Teams adapters, or
controllers.

Tool output is interpretation context only. Core independently searches/reloads all
facts and relationships before canonical mutation and again before request creation.

## 3. Shared contract and security rules

- All input and output objects reject unknown properties.
- Stable identifiers are nonblank strings.
- Display text and incident text are untrusted data, never instructions or authority.
- Success responses contain only the declared success shape.
- Expected failures use the shared failure envelope.
- The model receives no hidden authorization facts or state-changing capability.
- One normal turn allows at most one call to each tool and four calls total.
- Six provider iterations and zero structured-output repairs are the outer turn bounds.
- Model and tool work share one 30-second timeout/cancellation budget per turn.
- Raw arguments/results and agent-authored search queries are not persisted or logged.
- Tool-call order is diagnostic. Core revalidation is the correctness boundary.

### 3.1 Failure envelope

```json
{
  "outcome": "InvalidInput | NotFound | Timeout | Cancelled | Unavailable",
  "code": "stable-machine-code",
  "message": "safe bounded message",
  "correlationId": "server-correlation-id"
}
```

The message must not contain stack traces, secrets, connection strings, complete source
payloads, internal implementation details, or requester text.

## 4. Shared deterministic environment-search policy

The MCP search tool and Core's `searchQuery` path call the same protocol-neutral search
policy implementation. They must not maintain separate matching algorithms. Core does
not replay search when the agent returns `exactEnvironmentId`; it exact-reloads that ID
through the independent environment authority instead.

The policy normalizes and tokenizes deterministically, searches only the approved
environment, client, region, and canonical classification fields, includes only active
eligible production environments, and orders by stable environment ID. The component
contract and tests own exact Unicode, punctuation, tokenization, and storage-provider
conformance details; neither MCP nor Core may substitute a provider collation such as
SQLite `NOCASE` for that policy.

The complete result-count behavior is shared by MCP and Core:

| Count | Behavior |
|---:|---|
| 0 | No match. |
| 1 | Exact-reload the result before accepting it. |
| 2–5 | Persist and render the complete ordered clarification set. |
| More than 5 | Return `environment_query_too_broad` and request a more specific description; do not rank or truncate. |

For a `searchQuery` proposal, Core's current result controls
zero/unique/multiple/too-broad behavior and every rendered choice. When an MCP search
returns exactly one environment that is uniquely justified by requester intent, the
agent may instead propose its `exactEnvironmentId`; Core then exact-reloads the ID
without re-executing the query.

The application creates selectable clarification context only when the complete result
contains two to five environments. More than five results produce “narrow the query”
guidance without exposing or truncating a larger result set.

## 5. `search_production_environments`

### Input

```json
{
  "query": "alpha eu primary"
}
```

`query` is a structured agent proposal. It is required, trimmed, and 1–200 characters.
The adapter does not compare it with raw requester text and does not log it.

### Success

```json
{
  "environments": [
    {
      "environmentId": "PROD-ALPHA-EU",
      "displayName": "Client Alpha EU Production",
      "clientId": "client-alpha",
      "clientDisplayName": "Client Alpha"
    }
  ]
}
```

The result contains every deterministic match up to the five-result hard bound in stable
environment-ID order. It contains no roles, score, match explanation, pagination token,
or model ranking.

The result may help the agent produce an exact environment proposal and then gather
environment-scoped roles in the same turn. A two-or-more-result search must not be
collapsed into an unprompted exact environment ID.

## 6. `get_production_environment`

### Input

```json
{
  "environmentId": "PROD-ALPHA-EU"
}
```

Only one exact stable identifier is accepted. Display names, partial IDs, and empty
input are invalid.

### Success

```json
{
  "environmentId": "PROD-ALPHA-EU",
  "displayName": "Client Alpha EU Production",
  "clientId": "client-alpha",
  "clientDisplayName": "Client Alpha"
}
```

The response contains no `roles` property. Unknown, inactive, non-production, or
intake-ineligible environments return `NotFound` without disclosing the excluded
classification.

## 7. `get_environment_roles`

### Input

```json
{
  "environmentId": "PROD-ALPHA-EU"
}
```

Only one exact stable environment ID is accepted. The tool does not accept client,
requester, role query, display name, or cross-environment criteria.

### Success

```json
{
  "environmentId": "PROD-ALPHA-EU",
  "roles": [
    {
      "roleId": "ProductionReadOnly",
      "displayName": "Production read-only"
    },
    {
      "roleId": "ProductionSupport",
      "displayName": "Production support"
    }
  ]
}
```

Roles are ordered by stable role ID. A known eligible environment with no assignments
succeeds with `roles: []`. Unknown/ineligible environments return `NotFound`.

The result means roles currently assignable in the environment. It does not assert that
the requester is personally eligible or approved.

A same-turn preceding exact environment lookup is not a universal runtime requirement.
Core independently loads the environment and current assignments before applying a role
and before confirmation.

## 8. `get_incident`

### Input

```json
{
  "incidentId": "INC-1042"
}
```

Only one exact stable incident identifier is accepted. The value is a structured agent
proposal. Titles, descriptions, alerts, partial IDs, and empty values are invalid and do
not trigger search.

### Success

```json
{
  "incidentId": "INC-1042",
  "title": "Elevated customer errors",
  "status": "Active",
  "environmentId": "PROD-ALPHA-EU"
}
```

`status` is `Active` or `Inactive`. Unknown identifiers return `NotFound`.
`environmentId` is the incident authority's one nullable affected production
environment. A missing value remains a successful read-only lookup but makes the scope
group ineligible when Core validates the proposal.

Incident title is untrusted model context. Core uses only an independently loaded
record and application-safe rendering.

## 9. Tool-use diagnostics versus correctness

The interpreter may record tool name, safe argument classification, duration, outcome,
and sequence. It must not record raw arguments or result text.

It rejects:

- unknown tools;
- non-read-only annotations;
- malformed input/output;
- repeated calls beyond one per tool;
- more than four calls;
- more than six provider iterations;
- any structured-output repair or second interpreter invocation after output validation
  failure; and
- timeout or cancellation.

It does not reject an otherwise safe proposal solely because the model omitted a
redundant lookup or used a different valid read-only order. Core revalidation remains
the trust boundary: exact proposals receive exact reload only, while search-query
proposals execute authoritative search and exact-reload a unique result.

## 10. Source failure behavior

| Failure | Application consequence |
|---|---|
| Search source unavailable | Reject the scope group; a valid justification may still commit |
| Exact environment unavailable/not found/ineligible | Reject the scope group |
| Entitlement source unavailable | Reject the scope group; final candidate cannot become ready without a valid role |
| Known environment with no roles | Typed no-roles outcome; no role choice context |
| Incident source unavailable/not found/inactive or without an eligible environment | Reject the scope group; a valid justification may still commit |
| Explicit environment conflicts with the incident environment | Reject the scope group |
| Exact source result changes after model-side discovery | Core exact reload wins; reject missing/ineligible ID; never trust stale MCP payload |
| Core `searchQuery` result differs from model-side discovery | Core search result wins; record bounded drift; never trust stale MCP payload |
| MCP result contains instruction-like text | Treat as data; no policy/authorization effect |

## 11. Promotion record

The four-tool contract was promoted to production on 2026-08-27 after the deterministic
implementation gates passed. `docs/contracts/mcp-tools.json` is the canonical
machine-readable shape. Current product, architecture, security, orchestration,
testing, operator, and README guidance use this catalog without a target qualifier.

# Target MCP Contract: Deterministic Request Intake

- **Status:** Approved target; does not replace current as-built `mcp-tools.json` until implementation
- **Date:** 2026-08-24
- **Machine-readable companion:** `deterministic-request-intake-mcp-tools.json`
- **Environment-search policy version:** `1.0.0`
- **Endpoint/transport:** Existing `/mcp` Streamable HTTP boundary
- **Normative source:** `SPEC-deterministic-request-intake.md`

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
| `search_production_environments` | Service-catalog/CMDB search projection | Complete bounded matching eligible production environment/client identities | Roles, scores, match reasons, workflow data |
| `get_production_environment` | Environment registry/CMDB | Exact eligible production environment identity and owning client | Assigned roles, incidents, approvals |
| `get_environment_roles` | IAM/entitlement catalog | Roles currently assignable in one exact environment | Requester eligibility, client-wide/cross-environment roles, approval policy |
| `get_incident` | ITSM/incident system | Exact incident state and affected environment | Incident search/listing, environment roles |

The synthetic adapters may share SQLite. Separate contracts remain because the
production-shaped authorities have different ownership, permissions, freshness,
latency, and failure semantics.

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
- Six provider iterations and one structured-output repair are the outer turn bounds.
- Model, tool, and repair work share one 30-second timeout/cancellation budget per turn.
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

The MCP search tool and Core application port call the same protocol-neutral search
policy implementation. They must not maintain separate matching algorithms.

The policy:

1. Unicode NFC-normalizes, trims, and collapses whitespace;
2. rejects empty or over-200-character queries;
3. tokenizes on Unicode whitespace and punctuation;
4. requires every token to match case-insensitively against at least one approved field;
5. searches only environment ID, environment display name, client ID, client display
   name, region, and canonical `primary`/`recovery` classification;
6. includes only active production environments eligible for request intake;
7. orders by stable environment ID;
8. returns all matches when count is at most 20;
9. returns `environments: []` for zero matches; and
10. returns `Unavailable` with code `environment_query_too_broad` above 20, without
    ranking or truncation.

Core's current result controls zero/unique/multiple/too-broad behavior and every
rendered choice.

The application creates selectable clarification context only when the complete result
contains two to five environments. Six to twenty results produce “narrow the query”
guidance and no truncated choice list.

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

The result contains every deterministic match up to the hard bound in stable
environment-ID order. It contains no roles, score, match explanation, pagination token,
or model ranking.

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
- more than one structured-output repair; and
- timeout or cancellation.

It does not reject an otherwise safe proposal solely because the model omitted a
redundant lookup or used a different valid read-only order. Core revalidation remains
the trust boundary.

## 10. Source failure behavior

| Failure | Application consequence |
|---|---|
| Search source unavailable | Reject affected environment operation; independent accepted operations may commit |
| Exact environment unavailable/not found/ineligible | Reject environment and dependent role operation |
| Entitlement source unavailable | Reject affected role operation; final candidate cannot become ready without a valid role |
| Known environment with no roles | Typed no-roles outcome; no role choice context |
| Incident source unavailable/not found/inactive | Reject incident; independent accepted operations may commit |
| Source results change between model and Core reads | Core result wins; record bounded drift; never trust stale MCP payload |
| MCP result contains instruction-like text | Treat as data; no policy/authorization effect |

## 11. Promotion to the current contract

After implementation and required deterministic/live evidence pass:

1. replace the current canonical `docs/contracts/mcp-tools.json` with the verified
   four-tool machine-readable shape;
2. update product baseline, architecture, security model, request-intake orchestration,
   testing strategy, operator guidance, and README; and
3. retire the target qualifier or archive this document in favor of the canonical
   machine-readable contract.

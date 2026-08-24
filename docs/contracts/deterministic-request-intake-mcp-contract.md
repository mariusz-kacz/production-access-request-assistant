# Proposed MCP Contract: Deterministic Request Intake

- **Status:** Proposed; does not replace the current as-built `mcp-tools.json` until implementation
- **Date:** 2026-08-22
- **Machine-readable companion:** `deterministic-request-intake-mcp-tools.json`
- **Endpoint/transport:** Existing `/mcp` Streamable HTTP boundary

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
| `search_production_environments` | Service-catalog/CMDB search projection | Complete bounded matching environment/client identities | Roles, scores, match reasons, workflow data |
| `get_production_environment` | Environment registry/CMDB | Exact environment identity and owning client | Assigned roles, incidents, approvals |
| `get_environment_roles` | IAM/entitlement catalog | Roles currently assignable in one exact environment | Client-wide/cross-environment roles, approval policy |
| `get_incident` | ITSM/incident system | Exact incident state and affected environment | Incident search/listing, environment roles |

The synthetic adapters may share SQLite. The separate contracts are retained because
the production-shaped authorities have different ownership, permissions, freshness,
latency, and failure semantics.

## 3. Shared contract rules

- All input and output objects reject unknown properties.
- Stable identifiers are nonblank strings.
- Display text is untrusted data and never model instruction or application authority.
- Success responses contain only the declared success shape.
- Expected failures use the shared failure envelope.
- Model-visible output never replaces independent Core search/reload.
- One normal turn allows at most one call to each tool and four calls total.
- The existing shared model/MCP timeout remains the outer budget.

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
payloads, or internal implementation details.

## 4. `search_production_environments`

### Input

```json
{
  "query": "alpha eu primary"
}
```

`query` is required, trimmed, 1-200 characters, and must be requester-backed by the
current message before the Web boundary accepts the observation.

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

The result contains every deterministic match ordered by stable environment ID. It may
be empty. It contains no roles, score, match explanation, pagination token, or model
ranking.

### Deterministic search policy

1. Unicode NFC-normalize, trim, and collapse whitespace.
2. Reject empty or over-200-character queries.
3. Split on Unicode whitespace and punctuation and discard empty tokens.
4. Require every query token to match case-insensitively against at least one allowed
   field.
5. Allowed fields are environment ID, environment display name, client ID, client
   display name, region, and canonical `primary`/`recovery` classification.
6. Return all matches in ordinal ascending environment-ID order when count is at most
   20.
7. Return `environments: []` for no matches.
8. Return `Unavailable` with code `environment_query_too_broad` when more than 20
   matches exist. Never truncate or rank.

Core independently runs the same policy. Core's result controls zero/unique/multiple
behavior and every rendered choice.

## 5. `get_production_environment`

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

The response contains no `roles` property. Unknown identifiers return `NotFound`.

## 6. `get_environment_roles`

### Input

```json
{
  "environmentId": "PROD-ALPHA-EU"
}
```

Only one exact stable environment ID is accepted. The tool does not accept client,
role query, display name, or empty input.

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

Roles are ordered by stable role ID. A known environment with no assignments succeeds
with `roles: []`. An unknown environment returns `NotFound`.

The tool exposes no cross-environment search. The model may call it for an exact
environment from the current message, canonical state, incident context, exact lookup,
or unique search result. A same-turn preceding environment lookup is not a universal
runtime requirement. Core independently loads and validates current assignments.

## 7. `get_incident`

### Input

```json
{
  "incidentId": "INC-1042"
}
```

Only the exact requester-supplied stable incident ID is accepted. Titles,
descriptions, alerts, and partial IDs are invalid and must not trigger search.

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

## 8. Tool-use diagnostics versus correctness

The interpreter records tool name, safe argument classification, duration, outcome,
and sequence for diagnostics and live evaluation. It rejects:

- unknown tools;
- non-read-only annotations;
- malformed input/output;
- repeated calls beyond one per tool;
- more than four calls;
- more than six provider iterations; and
- timeout or cancellation.

It does not reject an otherwise safe proposal solely because the model omitted a
redundant lookup or used a different valid read-only order. Core revalidation is the
correctness and trust boundary.

## 9. Source failure behavior

| Failure | Application consequence |
|---|---|
| Search source unavailable | No search-driven mutation; preserve committed state; invite retry |
| Exact environment unavailable/not found | Do not accept changed environment; preserve unrelated state |
| Entitlement source unavailable | Do not accept changed role or become ready; preserve committed state |
| Known environment with no roles | Typed candidate rejection; no role choice context |
| Incident source unavailable/not found/inactive | Do not accept incident; preserve unrelated state |
| Source results change between model and Core reads | Core result wins; record bounded drift; never trust stale MCP payload |

## 10. Promotion to the current contract

After implementation and deterministic evidence pass:

1. replace the current `docs/contracts/mcp-tools.json` with the verified four-tool
   machine-readable shape;
2. update the product baseline, architecture, security model, request-intake
   orchestration, testing strategy, operator guidance, and README; and
3. remove the `Proposed` qualifier from this contract or retire it in favor of the
   canonical machine-readable file.

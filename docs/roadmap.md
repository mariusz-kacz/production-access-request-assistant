# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-07-29
- **Current baseline**:
  [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)

## Purpose

This document records credible follow-on product work without changing the active
baseline or expanding the current implementation scope. A roadmap item becomes
authoritative only after its business requirement is approved and the product
baseline, specification, contracts, and tests are updated together.

The immediate priority remains completing the Teams access-intake feature with its
current exact three-tool MCP surface:

- `get_production_environment`
- `get_incident`
- `get_available_roles`

That feature should first demonstrate a bounded incident-to-environment-to-role tool
chain, compact multi-turn clarification, deterministic validation, and safe failure
handling.

The active product boundary is Teams-only request creation: an authenticated personal
Teams preparation becomes a request only through deterministic confirmation. The Web
application remains the request register and business-decision, DevOps-decision,
provisioning-retry, and audit surface. Reintroducing browser drafting, a
request-creating `POST /api/requests`, `/requests/new`, or a `createRequest`
capability is not roadmap work unless the product baseline is explicitly amended.

## Candidate Next Feature: Active Incident Discovery

### Business problem

Requesters frequently know the affected client or operational symptom but not the
stable incident identifier required by the current intake flow. Requiring them to
leave the conversation, search another system, and return with the identifier adds
friction and increases incomplete or incorrectly scoped requests.

### Proposed requirement

A requester who does not know an incident ID can describe the operational problem in
natural language. The assistant searches a bounded authoritative set of active
incidents, presents stable choices when the result is ambiguous, and continues request
preparation using the selected incident identifier.

The assistant may gather and interpret context. It does not determine authoritative
scope, submit the request, approve access, or provision access.

### Proposed conversation

```text
Requester describes an active problem
        |
        v
search_active_incidents
        |
        +-- no matches ------> focused correction
        |
        +-- one match -------> select stable incident ID
        |
        +-- several matches -> bounded numbered clarification
                                  |
                                  v
get_incident
        |
        v
get_production_environment
        |
        v
get_available_roles
        |
        v
strict structured proposal
        |
        v
deterministic canonicalization and validation
```

### Proposed MCP operation

The feature would add one domain-specific read-only operation:

```text
search_active_incidents
```

Conceptual input:

```json
{
  "query": "Contoso checkout failures"
}
```

Conceptual output:

```json
{
  "matches": [
    {
      "incidentId": "INC-2087",
      "title": "Contoso checkout failures",
      "clientId": "contoso",
      "environmentId": "PROD-CONTOSO-EU",
      "status": "Active"
    }
  ]
}
```

The server, not the model, controls the result limit. The initial proposal should
return no more than five active incidents, use explicit typed schemas, reject empty
or oversized queries, and never expose arbitrary database filters or generic query
capabilities.

### Trust and authorization boundaries

- Search, lookup, and role tools remain read-only.
- Search results are untrusted context until authoritative application services reload
  and validate the selected stable identifier.
- Result ordering does not authorize an ordinal selection; the selected value must
  match the current persisted bounded options.
- Client, environment, incident, and role relationships are revalidated before
  preparing a snapshot and again at confirmation.
- MAF and MCP receive no submit, approval, provisioning, retry, revocation, workflow,
  credential, arbitrary-database, or generic-query capability.
- Authenticated server context remains the only source of acting identity.
- Confirmation and all subsequent workflow transitions bypass the model.

### Why this direction exercises MAF

The current three lookups can validate identifiers already supplied by a requester.
Incident discovery adds a genuinely adaptive model/tool interaction:

- decide whether discovery is required;
- form a bounded domain search;
- interpret zero, one, or several results;
- ask a focused clarification only when needed;
- carry the selected stable identifier into dependent lookups; and
- produce one strict candidate or clarification proposal.

MAF remains replaceable behind `IRequestPreparationInterpreter`; its role is the
conversational and read-only tool loop, not business authorization. The framework
choice should be revisited if this roadmap item does not proceed and MAF-native
evaluation or shared-channel reuse provides no demonstrated value.

### Minimum acceptance criteria

- A developer can begin with a problem description and no incident ID.
- Zero, one, and multiple-match searches produce distinct typed outcomes.
- Multiple matches are capped and rendered as authoritative numbered choices.
- A direct or ordinal selection is accepted only from the current option set.
- The selected incident drives dependent environment and role lookups.
- Representative requests reach a validated prepared candidate within five developer
  messages.
- Expected tool selection and arguments are covered by deterministic automated tests.
- Unknown tools, excessive results, malformed results, prompt injection, timeout,
  cancellation, and dependency failure create no request, approval, operation, or
  grant.
- Logs record correlation, tool name, duration, and outcome without recording raw
  prompts, transcripts, or complete MCP payloads.

### Required approval and design work

Before implementation:

1. Amend the product baseline to approve incident discovery and change the exact MCP
   catalog from three tools to four.
2. Create a dedicated feature specification rather than extending the active Teams
   feature in place.
3. Define authoritative search semantics over the synthetic dataset, including
   normalization, ranking, result limits, and stable ordering.
4. Update MCP schemas, allowlist validation, structured-output contracts, security
   analysis, and negative test requirements.
5. Decide whether MAF evaluation is used as a release gate for expected tool calls,
   candidate correctness, clarification count, and the five-message target.

## Explicitly Not on This Roadmap

This candidate feature does not justify:

- model-visible state-changing tools;
- agent-directed approval or provisioning;
- a generic enterprise search or database-query tool;
- transcript persistence;
- multi-agent orchestration;
- a generic workflow engine;
- a second deployable service; or
- real production access or identity integration.

# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-08-05
- **Current baseline**:
  [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)

## Purpose

This document records credible follow-on product work without independently changing
the active baseline. A roadmap item becomes authoritative only after its business
requirement is approved and the product baseline, constitution, specification,
contracts, and tests are updated together.

The delivered
[feature 004](../specs/004-resolve-context-identifiers/spec.md) is incorporated into
the active product baseline. It narrows model-assisted discovery to production
environments and establishes the exact two-tool MCP surface:

- `get_production_environment`
- `get_incident`

`get_production_environment` supports bounded environment discovery and exact
lookup. Each returned environment includes its authoritative client relationship
and assigned roles, so a separate role-listing capability is unnecessary.
`get_incident` remains an exact-identifier lookup. Incident listing, search, title
matching, and semantic inference are outside the baseline.

The checked-in runtime, current MCP contract, model allowlist and instructions,
tests, security analysis, and operator guidance now describe that delivered design.
No subsequent product increment is currently approved.

## Delivered Increment: Environment Identifier Resolution

### Business problem

Requesters know a client or environment by its familiar name but may not know the
stable production-environment identifier. Requiring them to leave the conversation
and find that identifier adds avoidable friction.

### Delivered behavior

The assistant may read a bounded authoritative set of production environments and
interpret the requester's readable description. One unambiguous environment may be
proposed; multiple matches require a focused clarification; no match must not produce
an invented identifier. The selected environment supplies the authoritative client
and currently assigned role choices.

An optional incident must be supplied using its precise stable identifier. The
assistant may validate that identifier but must not discover or infer it from a title
or problem description.

### Trust and authorization boundaries

- Both MCP tools remain read-only and use explicit typed schemas.
- Environment candidates and model selection remain untrusted until deterministic
  application services validate the stable environment, client relationship, and
  requested role.
- Incident validation uses only the precise stable identifier supplied by the
  requester.
- MAF and MCP receive no submit, approval, provisioning, retry, revocation, workflow,
  credential, arbitrary-database, generic-query, or separate role-listing capability.
- Authenticated server context remains the only source of acting identity.
- Confirmation and all subsequent workflow transitions bypass the model.

### Delivered acceptance boundaries

- A developer can identify one unambiguous environment without knowing its stable ID.
- Zero, one, and multiple environment matches produce distinct safe outcomes.
- Environment choices contain stable identifiers, readable context, authoritative
  client relationships, and assigned roles.
- Identifier-like environment values use exact lookup only. Exact `NotFound` keeps
  scope unresolved and asks for correction with no discovery alternatives; readable
  environment descriptions continue to use bounded discovery.
- A model-authored clarification message is shown only after its separate structured
  option IDs are reloaded and validated; selectable labels and identifiers come from
  authoritative records, never prose.
- Role choices shown to the requester are limited to those assigned to the selected
  environment and are independently validated before submission.
- Incident descriptions and partial identifiers are never mapped to an incident.
- Unknown tools, excessive results, malformed results, prompt injection, timeout,
  cancellation, and dependency failure create no request, approval, operation, or
  grant.
- Logs record correlation, tool name, duration, and outcome without recording raw
  prompts, transcripts, or complete MCP payloads.

## Explicitly Not on This Roadmap

The delivered environment-resolution increment does not justify:

- incident discovery, listing, or semantic search;
- a separate role-listing tool;
- model-visible state-changing tools;
- agent-directed approval or provisioning;
- a generic enterprise search or database-query tool;
- transcript persistence;
- multi-agent orchestration;
- a generic workflow engine;
- a second deployable service; or
- real production access or identity integration.

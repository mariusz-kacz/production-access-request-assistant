# Product Roadmap

- **Status**: Proposed; non-authoritative
- **Last reviewed**: 2026-08-04
- **Current baseline**:
  [Governed Production Access Product Baseline](governed-production-access-product-baseline.md)

## Purpose

This document records credible follow-on product work without independently changing
the active baseline. A roadmap item becomes authoritative only after its business
requirement is approved and the product baseline, constitution, specification,
contracts, and tests are updated together.

The approved next increment is
[feature 004](../specs/004-resolve-context-identifiers/spec.md). It narrows model-
assisted discovery to production environments and establishes an exact two-tool MCP
surface:

- `get_production_environment`
- `get_incident`

`get_production_environment` will support bounded environment discovery and exact
lookup. Each returned environment will include its authoritative client relationship
and assigned roles, so a separate role-listing capability is unnecessary.
`get_incident` remains an exact-identifier lookup. Incident listing, search, title
matching, and semantic inference are not part of the approved feature.

The checked-in implementation and as-built guidance continue to describe the former
three-tool surface until feature 004 is implemented. That delivery must update the
MCP contracts, model allowlist and instructions, tests, security analysis, runtime
guidance, and validation evidence together.

## Approved Next Feature: Environment Identifier Resolution

### Business problem

Requesters know a client or environment by its familiar name but may not know the
stable production-environment identifier. Requiring them to leave the conversation
and find that identifier adds avoidable friction.

### Approved requirement

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

### Minimum acceptance criteria

- A developer can identify one unambiguous environment without knowing its stable ID.
- Zero, one, and multiple environment matches produce distinct safe outcomes.
- Environment choices contain stable identifiers, readable context, authoritative
  client relationships, and assigned roles.
- Role choices shown to the requester are limited to those assigned to the selected
  environment and are independently validated before submission.
- Incident descriptions and partial identifiers are never mapped to an incident.
- Unknown tools, excessive results, malformed results, prompt injection, timeout,
  cancellation, and dependency failure create no request, approval, operation, or
  grant.
- Logs record correlation, tool name, duration, and outcome without recording raw
  prompts, transcripts, or complete MCP payloads.

## Explicitly Not on This Roadmap

This approved direction does not justify:

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

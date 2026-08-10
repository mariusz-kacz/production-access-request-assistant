# Quickstart: Validate Natural-Language Environment Resolution

## Purpose

Use this guide after feature 004 implementation to prove the exact two-tool catalog,
bounded environment discovery, embedded role context, exact-only incident behavior,
exact-lookup fallback clarification, deterministic validation, and unchanged governed
workflow.

See:

- [spec.md](spec.md) for requirements and acceptance scenarios;
- [data-model.md](data-model.md) for authoritative records and projections;
- [contracts/mcp-tools.json](contracts/mcp-tools.json) for the MCP wire contract; and
- [contracts/environment-resolution-turn-contract.md](contracts/environment-resolution-turn-contract.md)
  for model behavior; and
- [contracts/request-intake-proposal.schema.json](contracts/request-intake-proposal.schema.json)
  for the feature 004 structured-output contract.

## Prerequisites

- .NET 10 SDK
- repository dependencies already restored
- frontend dependencies already installed if the Web build requires them
- no live model, Teams tenant, Azure subscription, or external MCP server
- repository root as the working directory

Automated validation must use deterministic chat clients. Do not configure the
Foundry Responses profile for these scenarios.

## Contract Scenarios

### 1. Exact tool catalog

Inspect the initialized MCP server through the integration contract tests.

Expected:

- exactly `get_production_environment` and `get_incident` are advertised;
- both tools are read-only, non-destructive, idempotent, and closed-world;
- `get_available_roles`, resources, prompts, and state-changing tools are absent;
- missing, additional, or non-read-only tools are rejected by the model boundary.

### 2. Environment discovery

Call `get_production_environment` with an empty object.

Expected with the fixed dataset:

- two candidates ordered by environment ID;
- Client Alpha environment contains `ProductionReadOnly` and `ProductionSupport`;
- Client Beta environment contains only `ProductionReadOnly`;
- every candidate contains stable environment/client IDs, readable client/environment
  names, business-approver responsibility metadata, and ordered role values;
- no workflow row or audit event is created.

### 3. Exact environment lookup

Call `get_production_environment` with
`{"environmentId":"PROD-ALPHA-EU"}`.

Expected:

- one candidate in `environments`;
- its shape is identical to an item returned by discovery;
- unknown IDs return typed `NotFound`;
- blank, null, and unknown fields return typed `InvalidInput`.

### 4. Bounded catalog

Use a persistence test double containing 21 environments.

Expected:

- discovery returns typed `Unavailable` with
  `environment-candidate-limit-exceeded`;
- no partial or truncated candidate list is returned;
- cancellation reaches the context reader.

### 5. Exact incident lookup

Call `get_incident` with `{"incidentId":"INC-1042"}`.

Expected:

- the existing incident contract remains unchanged;
- an exact unknown ID returns typed `NotFound`;
- there is no discovery, title-search, or partial-ID mode.

## Conversational Scenarios

Exercise these through deterministic scripted chat clients and the authenticated Teams
test host.

### Unambiguous environment

Message:

```text
I need read-only access to Client Alpha production in Europe to investigate INC-1042.
```

Expected:

- the model calls environment discovery rather than inventing an ID;
- it selects `PROD-ALPHA-EU`, derives `client-alpha`, and uses
  `ProductionReadOnly` from the embedded role list;
- it calls `get_incident` only with exact `INC-1042`;
- deterministic validation produces a final confirmation snapshot;
- no request exists until authenticated confirmation.

### Ambiguous environment

Use a context double with two environments matching the supplied readable terms.

Expected:

- one focused model-authored environment clarification message is preserved;
- the application appends stable ordered readable choices loaded from authoritative
  context rather than model prose;
- no environment is guessed;
- no ready snapshot or request is created;
- "the first one" works only while the relevant process-local history is available;
- after history loss, the assistant repeats a self-contained clarification.

### Potential environment identifier not found

Message:

```text
I need read-only access to PROD-ALPHA.
```

Script the model to call exact lookup first, receive typed `NotFound`, call discovery,
and return a focused `message` together with
`environmentOptionIds: ["PROD-ALPHA-EU"]`.

Expected:

- exact lookup occurs before discovery;
- the proposed option ID is reloaded from authoritative context;
- the application preserves the bounded model-authored question and appends Client
  Alpha, Production Europe, and `PROD-ALPHA-EU` from authoritative data;
- identifiers or display values written only in the model message do not become
  selectable options;
- `environmentId` remains unresolved and no ready snapshot or request exists until
  the developer confirms the option.

Repeat with zero and multiple plausible option IDs. Expect focused correction for
zero, stable ordered authoritative choices for multiple, and no silent substitution.

### Invalid fallback choices

Return an unknown option ID, a duplicate ID, more than 20 IDs, or an environment ID
written only in free-form `message`.

Expected:

- malformed schema or invalid options are rejected;
- the associated model message is suppressed when the structured option set is
  invalid;
- unknown or prose-only IDs are never rendered as authoritative choices;
- no candidate, request, approval, operation, or grant is created.

Separately, script a potential identifier together with conflicting readable client
or location terms. Expect the conflict to remain explicit, the response to remain a
clarification, and no option to become candidate scope without a developer reply.

### No fallback for other exact-lookup failures

For an identifier-like message, make exact lookup return `InvalidInput`, timeout,
cancellation, `Unavailable`, or a malformed result.

Expected:

- no discovery call follows;
- the original typed correction or retry outcome is preserved;
- previously valid candidate values that do not depend on this resolution remain
  intact.

### Unsupported role

Message requests production support for Client Beta.

Expected:

- environment context exposes only `ProductionReadOnly`;
- the assistant clarifies the role rather than claiming support is available;
- a crafted model proposal for `ProductionSupport` is independently rejected with
  `role_unavailable`.

### Incident description without exact ID

Message refers to "the Client Alpha outage" without an incident ID.

Expected:

- `get_incident` is not called;
- no incident ID is inferred;
- the assistant asks for the precise ID or offers to continue without an incident.

### Changed environment

Resolve Client Alpha, then change the target to Client Beta.

Expected:

- client becomes `client-beta` from the new environment;
- `ProductionSupport` and `INC-1042` cannot carry forward if incompatible;
- the final request is unavailable until all dependent values validate.

### Failure and injection

Exercise malformed model output, unexpected tools, MCP timeout/unavailability,
cancellation, invented IDs, cross-client incidents, and instructions to bypass
validation or provision access.

Expected in every case:

- a typed safe outcome;
- no request, approval, provisioning operation, or grant;
- no raw message, prompt, transcript, or complete MCP catalog in logs.

## Optional Live-Model Quality Matrix

Run this only as release evidence with an explicitly configured development model;
it is not an automated test prerequisite. Use synthetic data, keep confirmation and
submission disabled, and record only sanitized outcomes.

Evaluate at least:

- ten varied unambiguous descriptions of the two fixed environments, expecting the
  correct authoritative environment ID or a safe failure in 100% of cases (SC-001),
  with at least 90% reaching confirmation without an identifier clarification
  (SC-002);
- ten ambiguous or no-match descriptions, expecting clarification and no invented ID
  in 100% of cases (SC-003);
- ten misspelled or incomplete potential environment identifiers, expecting only
  authoritative alternatives and explicit confirmation before substitution in 100%
  of cases (SC-010);
- incident titles, descriptions, and partial IDs, expecting no incident tool call and
  an exact-ID-or-omit clarification in 100% of cases; and
- client wording that conflicts with a precise environment ID, expecting correction
  rather than a client chosen independently.

Do not count scripted deterministic-chat tests as semantic-resolution measurements.

## Required Final Validation

After implementation, run these commands sequentially and exactly in this order:

```powershell
dotnet build ProductionAccessRequestAssistant.sln --no-restore --warnaserror
dotnet test tests/GovernedAccess.UnitTests/GovernedAccess.UnitTests.csproj --no-build --no-restore
dotnet test tests/GovernedAccess.IntegrationTests/GovernedAccess.IntegrationTests.csproj --no-build --no-restore --blame-hang-timeout 3m
```

Give the integration command an outer shell or tool timeout of at least four minutes.
It runs component and FullHost fixtures together in one test runner.

If a command times out, identify and stop only the test-runner process tree created by
that command before starting another test run. Never terminate unrelated or
pre-existing `dotnet` processes.

## Completion Evidence

Record:

- build and all three test-command outcomes;
- exact two-tool catalog inspection;
- discovery and exact environment contract evidence;
- embedded role membership and stable ordering;
- exact-only incident evidence;
- ambiguity, no-match, overflow, timeout, cancellation, and malformed-output outcomes;
- exact `NotFound` fallback with zero, one, and multiple plausible alternatives;
- structured option validation, preservation of bounded model-authored clarification
  wording, authoritative choice rendering, and explicit selection or confirmation
  before substitution;
- proof that non-`NotFound` failures never trigger discovery fallback;
- independent rejection of unsupported environment-role and cross-client incident
  combinations; and
- updated README, architecture, security, orchestration, testing, Teams,
  current ADR, and canonical MCP contract references.

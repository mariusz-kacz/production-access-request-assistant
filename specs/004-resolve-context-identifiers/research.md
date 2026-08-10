# Research: Natural-Language Environment Resolution

## R1. Interpretation versus authoritative context

**Decision**: MCP returns the complete bounded authoritative production-environment
catalog. The language model interprets the requester's wording against that catalog;
the MCP server does not perform fuzzy search, semantic ranking, alias expansion, or
confidence scoring.

**Rationale**: The synthetic catalog contains two environments and no authoritative
alias or search metadata. Keeping language interpretation in the model and stored-data
retrieval in MCP preserves the product rule that AI interprets while deterministic
services validate. It also avoids creating the prohibited generic-query or large
retrieval surface.

**Alternatives considered**:

- Send the user's free-text query to MCP: rejected because it moves language
  interpretation into infrastructure without authoritative matching rules.
- Put the catalog directly in the system prompt: rejected because it bypasses the
  real MCP boundary and can become stale or untestable.
- Add embeddings, aliases, ranking, or confidence thresholds: rejected as unsupported
  by the fixed data model and disproportionate to the two-record catalog.

## R2. One environment tool with discovery and exact lookup

**Decision**: `get_production_environment` accepts a closed input object with an
optional non-null `environmentId`. `{}` requests discovery; supplying a nonblank
stable ID requests exact lookup. JSON `null`, blank IDs, and unknown properties are
invalid. Both success modes return one closed envelope containing `environments[]`;
exact lookup returns a one-element array.

**Rationale**: Optional `environmentId` preserves the current exact-call shape and
adds the smallest possible discovery contract. A common output schema is easier for
the model and contract tests than a result union. MCP recommends a closed empty object
for parameterless calls and supports explicit input and output schemas.

**Alternatives considered**:

- Required `lookupMode` plus nullable `environmentId`: more self-describing, but adds
  cross-field combinations that the schema cannot fully prevent and changes every
  existing exact call.
- A second environment-discovery tool: rejected by the exact two-tool constitution.
- Missing ID returning a different top-level result shape: rejected because it makes
  model consumption and schema validation more complex.

## R3. Environment result composition

**Decision**: Every environment candidate contains `environmentId`, `clientId`,
`clientDisplayName`, environment `displayName`, and stable ordered `roles[]` entries
containing `roleId` and `displayName`. Environments are ordered by stable environment
ID; roles are ordered by stable role ID using ordinal comparison.

**Rationale**: These fields give the model enough readable authoritative context to
match a client/environment phrase and present only valid role choices. Stable ordering
supports deterministic tests and conversational references such as "the first one."
Business-approver responsibility is client-owned authorization context and is not
exposed to the model-facing environment contract.

**Alternatives considered**:

- Keep `get_available_roles`: rejected because it duplicates data naturally owned by
  the environment result and adds an avoidable tool call.
- Add roles to the persisted `ProductionEnvironment` entity: rejected because roles
  remain separately authoritative and many-to-one in the existing domain model.
- Return only IDs: rejected because the model and requester need readable context to
  resolve and verify the choice.

## R4. Provider-neutral read projection

**Decision**: Add a Core-owned, non-persistent `ProductionEnvironmentContext` read
projection and focused exact/list operations to `IRequestContextReader`. The EF reader
loads environments, clients, and assigned roles with no tracking and composes the
projection without MCP types. Keep `GetProductionEnvironmentAsync` and
`GetEnvironmentRoleAsync` for independent request validation. Remove
`GetEnvironmentRolesAsync` if the retired MCP tool is its only remaining caller.

**Rationale**: One enriched reader boundary avoids N+1 composition in the MCP adapter,
keeps infrastructure SDK contracts outside Core, and does not alter the persistence
schema. The existing exact environment-role lookup remains the security-relevant
deterministic check after model interpretation.

**Alternatives considered**:

- Compose clients and roles by repeated MCP-adapter calls: rejected because it creates
  inconsistent multi-read results and unnecessary coupling.
- Add a new repository/service hierarchy: rejected as unnecessary for one focused
  read projection.
- Reuse the MCP result record in persistence: rejected because SDK-facing contracts
  must remain at the infrastructure boundary.

## R5. Bounded complete-catalog behavior

**Decision**: Set a server-owned `MaximumEnvironmentCandidates` of 20. The reader
loads at most 21 records to detect overflow. Discovery above the cap fails closed with
typed `Unavailable` code `environment-candidate-limit-exceeded`; it never truncates or
paginates. An empty catalog is a successful `environments: []` result. Exact missing
ID remains `NotFound`.

**Rationale**: A truncated catalog could omit the correct environment and make a
remaining result appear falsely unique. The fixed dataset currently contains two
records, so a 20-record guard provides ample safety without creating a general search
contract. Empty discovery and natural-language zero-match are interpretation
outcomes, not missing exact records.

**Alternatives considered**:

- Caller-controlled maximum or pagination: rejected because it lets the model hide
  candidates and expands the contract beyond the fixed-data scope.
- Return the first 20 with `hasMore`: rejected because partial results are unsafe for
  uniqueness decisions.
- No limit: rejected because the specification requires bounded model context.

## R6. Exact-only incident behavior

**Decision**: Keep `get_incident` input, success result, and typed failures unchanged.
Model instructions require the precise requester-supplied stable incident ID before a
call. Titles, descriptions, partial IDs, reformatted IDs, and inferred references
produce a clarification asking for the exact ID or permission to continue without an
incident.

**Rationale**: This is the user-approved scope boundary. The incident remains optional
and existing deterministic validation still checks existence, active status, client,
and environment relationships.

**Alternatives considered**:

- Incident discovery or title matching: explicitly out of scope.
- Silently omit imprecise incident wording: rejected because it could discard stated
  requester intent without confirmation.

## R7. Model tool loop and proposal schema

**Decision**: Keep the existing closed request-proposal candidate fields and bounded
MAF session behavior. Remove `clientId` from the clarification-target enum because
the authoritative client is always derived from the selected environment. Add a
required `environmentOptionIds` array to each non-null clarification; it is empty for
non-environment clarifications and contains the proposed authoritative shortlist for
an environment clarification. Change the tool allowlist and agent instructions to
the exact two-tool design. Keep
`AllowMultipleToolCalls = false`: it limits a single model response to one tool call
but does not prevent sequential calls in the function-calling loop, so an environment
discovery and exact incident lookup, or an exact environment lookup and exact incident
lookup, can still occur in one preparation turn. Exact lookup and environment
discovery cannot be combined in the same turn. The calls plus the final proposal fit
within the existing six-iteration request limit.

**Rationale**: The candidate already contains nullable client, environment, role,
justification, and incident fields plus one focused clarification. The model still
proposes the derived client so deterministic comparison can reject a mismatch, but it
must not ask the requester to choose a client independently. No new durable state or
candidate field is needed; the clarification contract gains only the bounded option
ID array. Retaining one call per model response reduces parallel ambiguity while
allowing the framework to perform sequential reads. Structured option IDs let the
application reload and render authoritative names rather than trusting identifiers
or display values embedded in free-form model text. The existing bounded `message`
remains the model-owned conversational question; it is informational and separate
from the structured choice data.

**Alternatives considered**:

- Enable parallel tool calls: unnecessary for the small bounded local reads and harder to
  reason about when the incident must be checked against the selected environment.
- Add model confidence fields: rejected because confidence is not authority.
- Put complete environment records or display labels in model output: rejected
  because only stable option IDs are needed and all displayed context must be loaded
  authoritatively by the application.
- Persist environment choices: rejected because the current session/candidate model
  already safely re-clarifies after history loss.

## R8. MCP structured output and annotations

**Decision**: Continue advertising explicit closed input/output schemas, return both
`structuredContent` and its serialized text representation, and mark both tools
read-only, non-destructive, idempotent, and closed-world. Continue treating those
annotations as metadata rather than authorization.

**Rationale**: The MCP tool specification requires result structured content to match
an advertised output schema and recommends retaining serialized text for compatibility.
It also states that annotations are hints, matching the project's rule that tool
visibility and annotations cannot replace deterministic validation.

**Alternatives considered**:

- Unstructured text-only results: rejected because they weaken contract validation.
- Trust `readOnlyHint` as enforcement: rejected by both MCP guidance and the project
  constitution.
- Upgrade the MCP SDK during this feature: rejected because version 1.4.1 already
  supports the required typed schemas and structured results; a dependency upgrade is
  unrelated scope.

## R9. Failure, logging, and migration behavior

**Decision**: Preserve the existing `McpFailureEnvelope` outcomes and correlation.
Propagate the invocation cancellation token through every reader call; map expected
invalid input, not found, timeout, cancellation, and unavailability outcomes; record
only tool name, duration, and outcome. Implement in stages: read projection, MCP
contract, model boundary, Teams scenarios, then current documentation/contract sync.

**Rationale**: This maintains existing operational and security behavior while
isolating failures before request creation. Staging keeps the deterministic validator
green while the model-visible tool catalog changes incompatibly.

**Alternatives considered**:

- Add a second tool-specific timeout: rejected because the existing bounded request
  and MCP client cancellation chain already covers the operation.
- Log the candidate catalog for debugging: rejected because complete MCP payloads are
  excluded by default.
- Change workflow or audit events for discovery: rejected because discovery is
  read-only interpretation context, not a business transition.

## R10. Semantic-resolution quality evidence

**Decision**: Automated tests prove deterministic orchestration, contracts, and
validation with scripted chat clients. A small optional live-model evaluation matrix
provides release evidence for the specification's semantic-resolution success
criteria; it is not part of CI and cannot create or confirm requests.

**Rationale**: A deterministic fake can verify that the catalog is supplied and that
ambiguous or invalid proposals fail safely, but it cannot measure whether a deployed
language model maps varied natural-language descriptions correctly. Separating the
two evidence types keeps CI reproducible while making the model-quality criteria
measurable before a release.

**Alternatives considered**:

- Treat scripted model outputs as semantic-quality evidence: rejected because they
  only prove application behavior for predetermined proposals.
- Require a live Foundry model in CI: rejected because repository tests must run
  without a live LLM and would become nondeterministic and externally dependent.
- Omit model-quality evaluation: rejected because success criteria SC-001 and SC-002
  explicitly describe resolution behavior that contract tests alone cannot measure.

## R11. Suspected-identifier handling

**Decision**: When the model interprets a developer-supplied value as a possible
environment identifier, it performs exact lookup. No exact outcome permits a second,
parameterless discovery call in the same preparation turn. Typed `NotFound` keeps the
environment and derived client unresolved and requires a focused corrected-identifier
question with no structured environment options. Other failures retain their existing
typed safe outcomes. Catalog discovery remains available only when the original input
contains readable environment or client wording without an identifier-like value.

**Rationale**: Exact-only handling preserves authoritative identifier semantics and
prevents a rejected identifier or typo from being reinterpreted as a different,
security-relevant production scope. Typed failure discrimination also prevents
outages or malformed calls from masquerading as authoritative absence.

**Alternatives considered**:

- Treat every identifier-like value as readable context: rejected because it skips
  the authoritative exact lookup and may conceal a valid stable identifier.
- Discover after typed `NotFound`: rejected because identifier correction must remain
  explicit and must not broaden into catalog interpretation in the same turn.
- Automatically accept a single similar catalog entry: rejected because similarity
  is probabilistic and a typo correction changes security-relevant scope.
- Discover after timeout, cancellation, invalid input, or unavailability: rejected
  because those outcomes do not establish authoritative scope.
- Add fuzzy search or aliases to MCP: rejected because the model already interprets
  the bounded catalog and another search surface is outside the fixed-data scope.

## R12. Clarification wording and authoritative choices

**Decision**: Use one model response containing a bounded conversational `message`
and separate structured `environmentOptionIds`. After all option IDs pass schema and
authoritative validation, the Teams adapter presents the model message as plain
informational text and appends application-rendered choices containing only stored
client names, environment names, and stable IDs. An invalid option set suppresses the
associated message and choices. Text appearing only in `message` is never parsed into
an option, candidate value, relationship, action, approval, or authorization fact.

**Rationale**: This keeps conversational phrasing with the language model while the
application retains exclusive ownership of selectable values and authoritative
display facts. It avoids duplicating the model's question in deterministic templates,
does not add a second model call, and preserves the rule that model output cannot
change governed state.

**Alternatives considered**:

- Replace every non-empty environment clarification with an application-authored
  question: rejected because it discards useful model phrasing and duplicates
  response composition after the model already produced a focused question.
- Make a second model call after option validation: rejected because it adds latency,
  another timeout/failure path, and no additional authority.
- Render model-generated option labels or parse choices from `message`: rejected
  because prose cannot be schema-validated as authoritative identifiers or
  relationships.

## Sources

- [MCP tool specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
  for closed schemas, structured results, output-schema conformance, and annotation
  trust guidance.
- [Microsoft.Extensions.AI `AllowMultipleToolCalls`](https://learn.microsoft.com/de-de/dotnet/api/microsoft.extensions.ai.chatoptions.allowmultipletoolcalls)
  for the distinction between calls in one response and sequential function-calling
  iterations.
- Local package and source baseline: `ModelContextProtocol` 1.4.1,
  `Microsoft.Extensions.AI` 10.7.0, `RequestContextTools`,
  `MafRequestPreparationInterpreter`, `RequestValidator`, and
  `EfRequestContextReader`.

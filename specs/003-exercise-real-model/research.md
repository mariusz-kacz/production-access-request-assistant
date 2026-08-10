# Research: Exercise the Real Conversational Model

## Decision 1: Reuse the existing `IChatClient` and MAF path

**Decision**: Add one Azure AI Foundry `ResponsesClient` adapted to
`Microsoft.Extensions.AI.IChatClient`, then pass it through the same
function-invocation middleware and `MafRequestPreparationInterpreter` used by the
deterministic client.

**Rationale**: `ChatClientAgent` already accepts `IChatClient`; the current
interpreter already supplies JSON-schema response format, process-local MAF history,
and the exact MCP tool list. Official .NET guidance documents
`ResponsesClient.AsIChatClient(deploymentName)`. This makes the provider a
Web-boundary substitution and avoids changing Core or introducing a second agent
implementation while matching the Foundry project `/openai/v1` endpoint.

**Alternatives considered**:

- Refactor to a provider-specific Agent Framework agent: rejected because hosted
  tools and a second agent construction path are unnecessary; the existing local MCP
  and structured `IChatClient` path already meets the feature.
- Call the provider directly from `RequestDraftService`: rejected because it would
  leak provider concerns across the infrastructure boundary and bypass MAF history
  and tool controls.
- Add a general multi-provider router: rejected because the scope calls for one
  explicitly selected real profile, not runtime routing.

**Sources**:

- [Use the IChatClient interface](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient)
- [OpenAI .NET library](https://github.com/openai/openai-dotnet)

## Decision 2: Use Foundry Responses with pinned compatible packages

**Decision**: Pin `Microsoft.Extensions.AI.OpenAI` 10.7.0 alongside the existing
`Microsoft.Extensions.AI` 10.7.0, `OpenAI` 2.11.0, and `Azure.Identity` 1.21.0. Use
the OpenAI Responses client directly and adapt it to the existing portable
`IChatClient` tool-calling and structured-response path.

**Rationale**: The selected packages support .NET 10, and the extensions package
provides the required Responses `AsIChatClient` adapter. The OpenAI client accepts a
custom Foundry endpoint and authentication policy, and the model deployment is passed
to the adapter. Exact pins preserve the repository's reproducible-package convention.

**Alternatives considered**:

- `Azure.AI.OpenAI` chat completions: rejected because the approved Foundry project
  exposes the OpenAI Responses-compatible `/openai/v1` endpoint.
- Add `Microsoft.Agents.AI.OpenAI`: rejected because its provider-specific agent
  construction is not needed when the existing `ChatClientAgent` consumes
  `IChatClient` directly.
- API-key authentication: rejected because Foundry and Entra RBAC avoid a second
  credential model.

**Sources**:

- [Microsoft.Extensions.AI.OpenAI 10.7.0](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/10.7.0)
- [OpenAI 2.11.0](https://www.nuget.org/packages/OpenAI/2.11.0)
- [Azure.Identity 1.21.0](https://www.nuget.org/packages/Azure.Identity/1.21.0)

## Decision 3: Use Microsoft Entra credentials, not an application API-key option

**Decision**: Authenticate the real profile with `DefaultAzureCredential` and a
`BearerTokenPolicy` requesting `https://ai.azure.com/.default`. Local developers
select the intended tenant through Azure CLI or Visual Studio sign-in and receive the
minimum inference role. No tenant or model API key appears in the application
configuration contract.

**Rationale**: Microsoft Entra authentication avoids committing,
copying, logging, or rotating a provider key and supports local developer identity as
well as a future managed identity without changing the model boundary. Missing or
unauthorized credentials naturally become a safe provider-unavailable turn outcome.

**Alternatives considered**:

- Foundry API key in user secrets: rejected because it adds a high-value shared
  secret when the developer identity path is available.
- Client secret service principal: rejected for the local live-model exercise because
  it introduces secret lifecycle and broader setup with no benefit.
- Managed identity only: rejected because the current target is local execution, not
  an Azure-hosted production deployment.

**Sources**:

- [Azure Identity authentication best practices](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices?tabs=aspdotnet)

## Decision 4: Select one process-wide profile and fail closed without fallback

**Decision**: Add a separate `RequestPreparationModel` configuration section with
closed `Deterministic` and `FoundryResponses` profile values. Default to
`Deterministic` in checked-in settings. When `FoundryResponses` is selected, require
a trusted `*.services.ai.azure.com/openai/v1` endpoint and bounded deployment name.
The explicitly configured deployment is the operator-approved deployment. Unknown or
invalid real settings install an
`UnavailableChatClient`; they never select the deterministic client.

**Rationale**: Profile selection is a server/operator action and is resolved once at
host composition. Keeping invalid real configuration turn-safe allows the running
Teams endpoint to return the required safe failure. A start-up validation exception
would make that acceptance behavior impossible.

**Alternatives considered**:

- `ValidateOnStart` for all real-profile fields: rejected because the host could not
  answer the attempted Teams turn with safe guidance.
- Per-request or conversational profile choice: rejected because requester input is
  untrusted and the feature assumes one selected profile per host.
- Fallback to deterministic on error: rejected because it conceals the failed real
  exercise and directly violates the feature contract.

## Decision 5: Reuse the native ASP.NET Core request timeout

**Decision**: Keep the existing 100-second Teams endpoint request timeout as the one
overall deadline. Propagate its cancellation token through MCP connection/catalog
work, provider and tool calls, response parsing, and session save. Do not add a
second interpreter timer.

**Rationale**: ASP.NET Core already owns the request lifetime and cancels the token
supplied to the complete Teams handling path. Reusing it removes duplicate policy,
configuration, classification, and tests while still bounding model and MCP work and
preventing failed turns from saving session or workflow state.

**Alternatives considered**:

- Add a shorter interpreter deadline: rejected because conversational reply headroom
  does not justify a second timer for this local scope.
- Separate per-provider and per-tool deadlines: rejected as overall controls because
  their budgets could accumulate beyond the endpoint deadline.

## Decision 6: Preserve the existing schema, tool allowlist, and validation path

**Decision**: Do not create a real-model-specific request proposal or MCP catalog.
Continue using `request-intake-proposal.schema.json`, exact equality with the three
read-only MCP tools, strict parsing, `RequestValidator`, immutable readiness, and the
existing confirmation service.

**Rationale**: Model provenance must not affect trust. Sharing the exact path is the
strongest proof that a real provider does not weaken identifier validation,
client isolation, requester confirmation, approvals, or provisioning.

**Alternatives considered**:

- Relax schema for provider compatibility: rejected because malformed or extra model
  output must remain untrusted.
- Give the real provider direct data access: rejected because the real loopback MCP
  boundary is an explicit requirement and must remain visible in the exercised path.
- Add a submission or approval tool: rejected because the model is not an
  authorization boundary.

## Decision 7: Normalize provider failures and log only safe metadata

**Decision**: Wrap the Foundry-backed `IChatClient` in one Web-only delegating adapter
that translates SDK authentication, service, quota, transport, and timeout exceptions
into the existing provider-neutral unavailable/timeout behavior. Record profile ID,
deployment name, correlation, duration, and closed outcome. Never record endpoint,
credential data, prompts, transcript, response text, serialized session, card body,
or complete MCP payload.

**Rationale**: The interpreter currently understands provider-neutral timeout,
cancellation, network, malformed, and MCP failures. Central translation avoids SDK
types in Core and prevents an unexpected provider SDK exception from becoming an unsafe
500 response. The additional metadata satisfies the operational evidence requirement
without sensitive payload capture.

**Alternatives considered**:

- Catch provider SDK exceptions in Core: rejected because domain/application logic
  must remain provider-neutral.
- Catch every exception in the Teams adapter: rejected because it would blur
  programming defects with expected dependency failures.
- Enable sensitive provider telemetry: rejected because it risks prompt and response
  capture and is unnecessary for the MVP.

## Decision 8: Do not persist profile or model provenance

**Decision**: Treat execution profile, deployment name, and provider operation
outcome as process-wide configuration and structured operational metadata.
Do not add them to `RequestIntakeSession`, `AccessRequest`, approval, operation, grant,
or audit tables.

**Rationale**: The candidate is validated identically regardless of provider, and
model provenance is not authorization evidence. Persisting it would expand the
domain and database without affecting workflow correctness. Existing immutable
request and human decision evidence remain sufficient.

**Alternatives considered**:

- Add model profile to the immutable request: rejected because it invites downstream
  authorization logic to depend on probabilistic provenance.
- Add a new model-operation table: rejected as disproportionate for one local manual
  exercise; safe structured logs are sufficient before request creation.

## Decision 9: Keep automated tests offline and make the real run a manual gate

**Decision**: Test profile validation, exact selection, no fallback, provider error
translation, native request cancellation, unchanged saved sessions, exact MCP tools,
authoritative rejection, and safe logging with deterministic clients and loopback
hosts. Document a separate manual Foundry Responses/Teams exercise for complete,
clarification, rejection, and configuration-failure scenarios.

**Rationale**: The constitution requires tests to run without a live LLM. Offline
tests prove every deterministic trust boundary; the manual exercise proves provider
interoperability without making CI depend on credentials, cost, network, quotas, or
stochastic output.

**Alternatives considered**:

- Live-model integration tests in the normal suite: rejected because they are
  nondeterministic, costly, credential-dependent, and constitutionally prohibited.
- Manual demonstration only: rejected because failure, timeout, isolation, and
  no-fallback behavior require repeatable automated regression evidence.

## Decision 10: Reset preparation with one exact transport command and an existing lifecycle transition

**Decision**: Reserve the exact trimmed, case-insensitive Teams message `/new` as an
authenticated lifecycle command. Recognize it in the Teams adapter before invoking
the interpreter, then call a provider-neutral Core intake operation for the same
authenticated actor and conversation. That operation supersedes an active
`Collecting` or unexpired `Ready` intake using the existing terminal transition,
marks an already-expired ready intake expired, and succeeds idempotently when no
active intake exists. It does not immediately create a replacement. The next
ordinary message creates a new intake ID, which is also a new MAF session key.

**Rationale**: Restarting the host is dependency recovery, not a conversation reset,
because the intake is durable while the MAF session is process-local. Lifecycle
state must not depend on whether a model recognizes phrases such as "start over."
An exact command is small deterministic control-plane behavior, not deterministic
natural-language understanding. Reusing the existing supersession transition keeps
candidate clearing, readiness invalidation, actor/conversation isolation, and audit
evidence consistent without a new status or persistence model. Exact matching avoids
discarding work when `/new` merely appears inside a longer request message.

**Alternatives considered**:

- Restart the application: rejected because it preserves the durable active intake
  while discarding only process-local model history, creating an inconsistent and
  confusing continuation.
- Delete intake rows or clear the database: rejected because this is destructive,
  bypasses lifecycle evidence, and is disproportionate for a normal user action.
- Ask the LLM to infer reset intent: rejected because the model is untrusted,
  nondeterministic, and must not control persisted lifecycle transitions.
- Delete only the MAF session: rejected because it does not clear the durable
  candidate or invalidate a ready confirmation card.
- Add an MCP reset tool: rejected because reset is state-changing and the product
  baseline permits exactly three read-only MCP tools.

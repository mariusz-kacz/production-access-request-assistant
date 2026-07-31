# Research: Teams Access Request Intake

## 1. Teams Transport and MAF Hosting

**Decision**: Host the Microsoft 365 Agents SDK and Microsoft Agent Framework in the
existing ASP.NET Core executable. Use `Microsoft.Agents.Hosting.AspNetCore` for the
authenticated `/api/messages` Activity Protocol endpoint and
`Microsoft.Agents.AI` for one bounded `ChatClientAgent` invoked inside the message
handler. Do not expose MAF A2A, AG-UI, Responses, Durable, or Foundry-hosted endpoints.

**Rationale**: Microsoft publishes a current .NET sample with the exact
Microsoft 365 Agents SDK → MAF `ChatClientAgent` → `IChatClient` composition in one
ASP.NET Core host. The transport SDK handles Teams activity routing and authentication;
MAF handles only the model/tool loop. The feature needs no second agent protocol.

**Alternatives considered**:

- Microsoft Teams SDK plus MAF: credible and Teams-first, but its main advantages are
  deep Teams-native collaboration features that this personal-chat/basic-card slice
  excludes.
- MAF hosting or Foundry hosted agents behind a Teams adapter: adds another runtime or
  protocol without a product need and weakens the single-host story.
- Direct `IChatClient` without MAF: lowest implementation complexity, but it would not
  meet the explicit portfolio goal of demonstrating MAF.

Sources: [official M365 Agents SDK + MAF .NET sample](https://github.com/microsoft/Agents/tree/main/samples/dotnet/Agent%20Framework),
[SDK comparison](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/teams/sdk-comparison),
[MAF ASP.NET hosting options](https://learn.microsoft.com/en-us/agent-framework/get-started/hosting)

## 2. Genuine Need and Bounded Use of MAF

**Decision**: Use MAF only for conversational interpretation, tool dispatch, and
provider-neutral agent invocation. Do not use MAF Workflows, handoffs, multi-agent
orchestration, durable execution, RAG, autonomous memory, or agent-to-agent protocols.

**Rationale**: Multi-turn interpretation with three context tools is a genuine though
not exclusive fit for an agent abstraction. Approval order, readiness, confirmation,
submission, and provisioning are already deterministic application responsibilities;
moving them into an agent would be artificial and unsafe.

**Alternatives considered**:

- Model the complete access workflow as a MAF Workflow: duplicates the existing
  domain state machine and makes probabilistic infrastructure appear authoritative.
- Use several specialized agents: no independent roles or handoff need exists.
- Retain the existing one-shot interpreter only: simpler but does not provide the
  intended multi-turn MAF demonstration.

Sources: [MAF repository and fit guidance](https://github.com/microsoft/agent-framework),
[adding tools](https://learn.microsoft.com/en-us/agent-framework/journey/adding-tools)

## 3. Conversation State Ownership

**Decision**: Persist only the compact application-owned candidate and intake
lifecycle. Keep one bounded MAF `AgentSession` in process-local memory for each active
authenticated intake, and use that history as the primary context for previous
questions, tool exchanges, and references such as "the first one". Do not persist the
MAF session, raw Teams transcript, clarification prompt, or ordered options.

Supply the durable canonical candidate and a `historyAvailable` signal as run-scoped
context on every turn. If the session is absent after host restart, inactivity
eviction, or cleanup, the model must not resolve a relative reply from newly queried
ordering; it must repeat a focused clarification. Remove process-local history when
the intake becomes ready, submitted, superseded, expired, or invalidated.

**Rationale**: MAF-native history demonstrates genuine conversational continuity
without creating a second durable representation of the same clarification. The
typed candidate is the only restart-safe preparation state required for deterministic
readiness and recovery. Losing best-effort history can cause a repeated question but
cannot create a request or change authorization. A small in-process cache is
proportionate to the single-host, short-conversation scope and avoids SDK-session
serialization compatibility, transcript retention, and distributed-cache concerns.

**Alternatives considered**:

- Persist clarification targets and ordered choices alongside the candidate: creates
  duplicate conversational memory and bypasses the MAF history capability the feature
  is intended to demonstrate.
- Persist or checkpoint the complete MAF session: provides seamless restart
  continuity but retains raw conversation content, couples stored data to an SDK
  serialization format, and is disproportionate when safe re-clarification is an
  acceptable recovery.
- Use only MAF history with no durable typed candidate: makes restart recovery and
  deterministic readiness depend on best-effort process memory.
- Add a transcript database or distributed cache: explicitly outside the
  single-host feature need.

Source: [Microsoft 365 Agents SDK application and state model](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/agent-application)

## 4. Structured Agent Proposal

**Decision**: Require every MAF turn to produce the closed
`request-intake-proposal.schema.json` contract: a complete nullable candidate snapshot
and either one typed clarification target with a bounded user-facing message or a
candidate proposal with no clarification. Deserialize strictly, reject extra fields,
validate the kind/clarification pairing in the provider-neutral proposal constructor,
and let deterministic code canonicalize candidate values and decide readiness.
The schema intentionally avoids conditional JSON Schema keywords that are not
uniformly supported by structured-output model providers.

**Rationale**: Structured output makes the model useful without allowing prose or a
model-reported “complete” flag to transition state. The existing `RequestValidator`
can canonicalize and validate the proposed fields against authoritative data.

**Alternatives considered**:

- Free-form assistant responses: difficult to validate and unsafe as a readiness
  signal.
- A model-produced readiness boolean: duplicates a deterministic business rule.
- A model-produced or application-persisted clarification option array: duplicates
  conversation history and still cannot prove that the model mapped an ordinal phrase
  to the semantically intended choice.
- Agent-generated Adaptive Cards: makes untrusted model content define the
  confirmation surface.

Source: [MAF structured outputs](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs)

## 5. MCP Tool Integration

**Decision**: Reuse the real loopback MCP endpoint. List its catalog, require exact
set equality with `get_production_environment`, `get_incident`, and
`get_available_roles`, then pass only those three `McpClientTool` instances to MAF.
Keep the existing post-model authoritative validation.

**Rationale**: This preserves the demonstrated MCP boundary and prevents accidental
capability expansion. Tool annotations and visibility are not authorization.

**Alternatives considered**:

- Register in-process functions that bypass MCP: simpler but loses the existing real
  MCP integration story.
- Pass every discovered MCP tool to the agent: violates the explicit allowlist and
  fails open if the server changes.
- Add a submit or confirmation MCP tool: violates the constitution and makes the
  model part of a state-changing path.

Sources: [MAF local MCP tools](https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools),
[MAF tool registration guidance](https://learn.microsoft.com/en-us/agent-framework/journey/adding-tools)

## 6. Confirmation and Human-in-the-Loop

**Decision**: Treat **Confirm and submit** as deterministic requester attestation
through an Adaptive Card `Action.Execute`. Do not wrap submission in a MAF
approval-required function and never resume the agent to perform submission.

**Rationale**: MAF tool approval is appropriate when the model may call a sensitive
tool after a human permits it. Here the stronger boundary is that the model cannot call
submission at all. The authenticated action handler can reload exact server evidence,
verify ownership/expiry, and invoke deterministic request creation.

**Alternatives considered**:

- MAF tool approval: gives the model a submission capability and confuses requester
  confirmation with workflow approval.
- Plain text “yes”: ambiguous, replay-prone, and cannot bind visibly to an immutable
  snapshot.
- An editable card: permits scope changes after the final snapshot.

Sources: [MAF tool approval](https://learn.microsoft.com/en-us/agent-framework/journey/adding-tools#tool-approval-human-in-the-loop),
[Adaptive Card actions](https://learn.microsoft.com/en-us/adaptive-cards/rendering-cards/actions),
[Teams universal action model](https://learn.microsoft.com/en-us/adaptive-cards/authoring-cards/universal-action-model)

## 7. Teams Actor and Conversation Binding

**Decision**: Accept only SDK-authenticated `msteams` personal activities from the
configured tenant. Derive an actor binding from verified tenant and Teams/AAD user
identifiers, bind state to channel + tenant + actor + conversation, and map the actor
server-side to the single synthetic requester. Do not add Teams SSO, Graph permissions,
or user-delegated OAuth.

**Rationale**: Bot/channel authentication makes activity metadata usable at the
adapter boundary; the separate actor binding prevents all accepted users mapped to
`requester` from sharing ownership. User OAuth is unnecessary because the feature
does not access Microsoft 365 data on the user's behalf.

**Alternatives considered**:

- Bind only to the synthetic requester: enables cross-developer confirmation.
- Trust actor fields in the card payload: caller-controlled and replayable.
- Add Graph/SSO identity: expands permissions and deployment effort without a
  synthetic-demo need.

Sources: [M365 Agents SDK authentication](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/configure-authentication-msal),
[Activity Protocol](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/activity-protocol)

## 8. Prepared Snapshot and Atomic Idempotency

**Decision**: Generate both a high-entropy preparation reference and reserved request
ID when deterministic validation succeeds. On confirmation, atomically transition the
prepared row and insert the access request and request-created audit in the same
SQLite save. Enforce unique reserved request ID and optimistic concurrency; on an
expected collision, reload and return the stored request ID. Emit structured
operational telemetry after the transaction commits.

**Rationale**: Teams may redeliver actions or lose responses. The prepared reference
identifies the user interaction, while the reserved request ID makes every accepted
delivery converge on the same immutable workflow record. A shared transaction avoids
a submitted request with an unsubmitted preparation marker.

**Alternatives considered**:

- Generate the request ID after the click: concurrent handlers can create duplicates.
- Deduplicate only by Teams activity ID: activity IDs are transport metadata and may
  differ across retries.
- Add a queue or distributed lock: unnecessary for one SQLite-backed host.

## 9. Card Contract and Response Timing

**Decision**: Render a server-owned Adaptive Card with no inputs and one
`Action.Execute` verb. Its data contains only contract version and opaque preparation
reference. Keep confirmation free of model and MCP calls and target completion within
five seconds. Return deterministic submitted, already-submitted, expired,
superseded, invalidated, unauthorized, or unavailable responses.

**Rationale**: `Action.Execute` supports current Teams invoke handling and replacement
responses. A short deterministic path avoids background infrastructure. UI disabling
is defense in depth; persisted idempotency is the control.

**Alternatives considered**:

- Legacy `Action.Submit`: usable, but has weaker response/update behavior for new
  Teams scenarios.
- Background acceptance and queue processing: justified only if measurement proves
  confirmation cannot meet the invoke deadline.
- Trust the displayed card fields on submit: allows tampering.

Sources: [universal Adaptive Card actions](https://learn.microsoft.com/en-us/adaptive-cards/authoring-cards/universal-action-model),
[Teams cards and action behavior](https://learn.microsoft.com/en-us/microsoftteams/platform/task-modules-and-cards/cards/cards-actions)

## 10. Local and Real Teams Validation

**Decision**: Keep automated acceptance independent of Teams and a live model by
using fake authenticated activity context and a deterministic history-sensitive chat
client. Test process-local session continuity, isolation, eviction, and safe
re-clarification without serializing MAF history. Use Microsoft Agents Playground
only for local transport/card UX. Validate the real Teams story separately with an
authenticated Azure Bot registration, a stable HTTPS development tunnel, a
personal-scope app manifest, and sideloading in a development tenant.

**Rationale**: Playground anonymous mode cannot prove the production authentication
boundary. Real Teams requires public HTTPS and tenant policy, which should not make
the normal test suite flaky or credential-dependent.

**Alternatives considered**:

- Require Teams for all tests: slow, nondeterministic, and credential-dependent.
- Add an unauthenticated production endpoint for demos: violates actor trust.
- Add proactive messaging configuration: explicitly deferred.

Sources: [M365 Agents SDK quickstart and Playground](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/quickstart),
[Teams app package](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/build-and-test/apps-package),
[custom app upload](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/deploy-and-publish/apps-upload)

## 11. SDK Maturity and Version Policy

**Decision**: Pin the current stable packages `Microsoft.Agents.AI` 1.15.0 and
`Microsoft.Agents.Hosting.AspNetCore` 1.6.150, and make package compilation plus an
authenticated message/card round trip an early implementation task. Exclude adjacent
preview features such as DevUI, AG-UI, Foundry hosted agents, and durable workflows.

**Rationale**: MAF core reached 1.0 in April 2026, while the Agents SDK remains actively
developed. Exact pinning and a narrow adapter reduce churn; preview surfaces add no
value to the approved feature.

**Alternatives considered**:

- Floating package versions: harms repeatable builds.
- Copy all dependencies from a broad sample: imports unrelated telemetry, transcript,
  storage, and provider packages.
- Use DevUI as the delivery surface: useful for debugging but does not demonstrate the
  approved enterprise Teams integration.

Sources: [MAF 1.0 announcement](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/),
[M365 Agents SDK .NET reference](https://learn.microsoft.com/en-us/dotnet/api/copilot-sdk-docs-dotnet/overview),
[MAF NuGet package](https://www.nuget.org/packages/Microsoft.Agents.AI/),
[M365 ASP.NET hosting NuGet package](https://www.nuget.org/packages/Microsoft.Agents.Hosting.AspNetCore/)

## 12. Teams-Only Request Creation

**Decision**: Make authenticated confirmation of a server-owned Teams intake session
the sole executable request-creation path. Keep the Web application as the request
register and authenticated business-decision, DevOps-decision, provisioning-retry,
and audit surface.

**Rationale**: Maintaining both browser and Teams intake duplicated model
interpretation, DTOs, routes, tests, and request-ID behavior without adding governed
product value. One creation boundary makes identity, immutable scope, and idempotency
easier to explain and verify. Existing requests and all downstream workflow entities
remain channel-neutral and unchanged.

**Removed inventory**:

- browser draft endpoint and one-shot interpreter;
- browser `POST /api/requests` and public submission operation;
- new-request React page, route, navigation, list action, DTOs, session capability,
  and creation-only styles;
- tests and configuration that existed only for browser creation.

**Retained inventory**:

- Teams interpreter, actor resolution, intake session, immutable confirmation card,
  deterministic confirmation, and shared save;
- Web request list/detail, business and DevOps decisions, protected retry, session,
  and audit presentation;
- existing `AccessRequest`, approval, provisioning, grant, and audit persistence.

**Alternatives considered**:

- Keep both creation paths: preserves duplicated behavior and an ambiguous product
  boundary.
- Hide browser creation only in the UI: leaves an undocumented creation API.
- Replace the removed form with a new browser form: recreates the same duplicate
  boundary.

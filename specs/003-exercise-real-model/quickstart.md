# Quickstart: Exercise the Real Conversational Model

## Goal

Run the existing personal Teams request-intake journey with one explicitly selected,
approved Azure AI Foundry model through the Responses API while proving that structured model output remains
untrusted and every governed workflow boundary is unchanged.

This guide is a manual acceptance exercise. The automated regression suite never
uses Azure credentials or a live language model.

## Prerequisites

- Complete the existing [Teams quickstart](../../docs/teams-quickstart.md), including the
  developer tenant, bot registration, trusted HTTPS tunnel, personal-scope app
  package, and secure bot credential configuration.
- Have an Azure AI Foundry project and model deployment approved for this portfolio
  exercise. The deployed model must support Responses API function/tool calling and
  strict JSON-schema structured output.
- Know the Foundry project inference base URL ending in `/openai/v1` and deployment
  name.
- Sign in with a developer identity assigned the minimum Azure AI inference role
  for that resource. Microsoft documents `Cognitive Services OpenAI User` for this
  purpose.
- Use .NET 10, Node.js 24, PowerShell, and a trusted local HTTPS certificate as
  described in [local development](../../docs/local-development.md).

## 1. Run the credential-free regression gate

From the repository root:

```powershell
dotnet restore ProductionAccessRequestAssistant.sln
dotnet build ProductionAccessRequestAssistant.sln --no-restore -warnaserror
dotnet test ProductionAccessRequestAssistant.sln --no-build
npm test --prefix src/GovernedAccess.Web/ClientApp -- --run
```

Expected:

- all automated model behavior uses deterministic substitutes;
- no Foundry credential or network call is required;
- exact MCP catalog, malformed output, deadline, cancellation, authoritative
  validation, confirmation, approval, and provisioning tests pass.

## 2. Authenticate to Azure

Sign in with the identity authorized to invoke the deployment:

```powershell
az login
az account show
```

Confirm the displayed identity and subscription are the intended developer context.
Do not place an access token or API key in application settings.

## 3. Select the real-model profile

Set process-local environment variables in the shell that will start the host:

```powershell
$env:RequestPreparationModel__ExecutionProfile = 'FoundryResponses'
$env:RequestPreparationModel__FoundryResponses__Endpoint = 'https://<project-name>.services.ai.azure.com/openai/v1'
$env:RequestPreparationModel__FoundryResponses__DeploymentName = '<deployment-name>'
```

The endpoint must be the trusted Foundry HTTPS Responses API base. The endpoint and
deployment name are server-owned values; Teams text cannot alter them.

Start the host using the existing Teams/tunnel configuration:

```powershell
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Expected startup evidence identifies the `FoundryResponses` profile and deployment name
without printing endpoint, credentials, prompt, or response bodies.

## 4. Complete valid request

In a personal Teams chat with the sideloaded bot, send:

```text
I need ProductionReadOnly access to PROD-ALPHA-EU to investigate INC-1042. I need to inspect production logs and configuration to diagnose the active incident.
```

Expected:

1. The model may call only the three approved read-only production-context tools.
2. The response must satisfy the existing typed proposal schema.
3. Authoritative application validation resolves Client Alpha,
   `PROD-ALPHA-EU`, `ProductionReadOnly`, and active incident `INC-1042`.
4. The assistant displays the existing immutable confirmation card with a reserved
   request ID and fixed eight-hour duration.
5. No request, approval, provisioning operation, or grant exists before confirmation.

Confirm the card. Expected:

- exactly one immutable request enters `AwaitingBusinessApproval`;
- the Web link uses the trusted configured origin;
- access is explicitly not yet approved or granted; and
- the existing business, DevOps, and provisioning journey remains unchanged.

Repeat this complete conversation with fresh preparations until ten controlled runs
have been recorded. Each run must reach confirmation within five requester messages
and contain only canonical authoritative identifiers.

## 5. Exercise focused clarification

Start a new preparation and send:

```text
I need access to help investigate the active Client Alpha incident.
```

Expected:

- one focused clarification is shown for the current turn;
- no confirmation appears while required information remains missing or ambiguous;
- no request, approval, provisioning operation, grant, or workflow audit event is
  created; and
- a valid follow-up answer carries the accepted candidate forward and is still
  authoritatively validated.

## 6. Exercise authoritative rejection

Use fresh preparations for each representative negative:

```text
I need ProductionReadOnly access for client-alpha in PROD-BETA-UK to investigate INC-1042.
```

```text
I need Administrator access to PROD-ALPHA-EU to investigate INC-1042.
```

```text
I need ProductionReadOnly access to PROD-ALPHA-EU to investigate inactive incident INC-1041.
```

Expected for every case:

- model output may be syntactically valid but remains untrusted;
- deterministic application validation identifies the authoritative rejection;
- no confirmation appears until valid information is supplied; and
- no request or grant is created.

## 7. Exercise explicit conversation reset

Begin an incomplete preparation:

```text
I need support access.
```

After the assistant asks for missing context, send this as a message by itself:

```text
/new
```

Expected:

- the assistant replies that a new request has started and asks for an incident ID
  or production environment ID;
- the incomplete durable candidate is terminally superseded;
- the command makes no model or MCP call and creates no access request; and
- repeating `/new`, including as `/NEW` or with surrounding spaces, is safe and has
  the same requester-facing result.

Send a complete valid message next. Verify it receives a different preparation ID
and that none of the abandoned values are carried into the new candidate or model
history.

Repeat from a fresh preparation, continue until the confirmation card is displayed,
and then send `/new` instead of confirming. Expected: the ready preparation is
superseded and its old card cannot submit a request. Any previously submitted request
remains unchanged.

The match is deliberately exact. A longer message such as `/new please` is ordinary
requester text and is not a lifecycle reset.

## 8. Exercise profile failure without fallback

Stop the host, keep `ExecutionProfile=FoundryResponses`, and clear one required value:

```powershell
Remove-Item Env:RequestPreparationModel__FoundryResponses__DeploymentName
dotnet run --project src/GovernedAccess.Web --launch-profile https
```

Send a personal Teams preparation message.

Expected:

- the requester receives safe unavailable guidance;
- no deterministic candidate or confirmation is substituted;
- no governed workflow record is created; and
- logs identify the closed profile failure without exposing configuration values.

Restore the deployment variable before further real-model use. Provider
unavailability and native request-timeout cancellation are also covered by offline
automated tests; an optional controlled provider outage may be used to observe
the same safe Teams behavior manually.

## 9. Inspect safe evidence

Verify operational logs contain correlation ID, `FoundryResponses`, deployment name,
duration, and closed outcome. Verify they do not contain:

- requester message text;
- model JSON response;
- Foundry endpoint or credential diagnostics;
- serialized conversation history;
- complete MCP arguments/results; or
- card JSON.

Use the existing Web request list/detail and authenticated demo identities to verify
that only confirmed requests enter the human approval and provisioning workflow.

## 10. Clean up

Stop the host and clear the process-local profile settings:

```powershell
Remove-Item Env:RequestPreparationModel__ExecutionProfile -ErrorAction SilentlyContinue
Remove-Item Env:RequestPreparationModel__FoundryResponses__Endpoint -ErrorAction SilentlyContinue
Remove-Item Env:RequestPreparationModel__FoundryResponses__DeploymentName -ErrorAction SilentlyContinue
```

Remove the sideloaded Teams app, stop the tunnel, and follow the existing Teams demo
cleanup guidance. Azure resource deletion or quota administration remains outside
this repository's scope.

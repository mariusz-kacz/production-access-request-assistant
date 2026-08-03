# Contract: Teams Preparation Reset Command

## Purpose

Allow an authenticated requester to abandon the active, unsubmitted preparation in
the current personal Teams conversation and begin the next message with clean durable
state and clean model history.

This is a transport command and an application lifecycle operation. It is not model
input, an MCP tool, or a workflow action.

## Teams input contract

The adapter trims leading and trailing whitespace and compares using ordinal,
case-insensitive equality:

```text
/new
```

- `/new`, `/NEW`, and `  /new  ` match.
- `/new please`, `start /new`, and other longer text do not match and continue through
  the ordinary request-preparation path.
- Authentication and personal-conversation resolution happen before command handling.
- Actor, tenant, requester, and conversation identifiers come only from the existing
  authenticated server context.

## Core application contract

The implementation adds a provider-neutral command and closed result alongside the
existing intake contracts. Suggested shape:

```csharp
ResetRequestIntakeCommand(
    AuthenticatedChannelActor Actor,
    string CorrelationId)

ResetRequestIntakeResultKind = Reset | AlreadyClear | Failed
```

The command deliberately has no intake ID, candidate values, request ID, model
session ID, or caller-selected status. Core resolves the one active intake using the
authenticated actor and conversation binding.

## Required behavior

| Persisted state for this actor and conversation | Application action | Result |
|---|---|---|
| Active `Collecting` intake | Apply existing `MarkSuperseded`; clear candidate through the existing domain transition; save | `Reset` |
| Active unexpired `Ready` intake | Apply existing `MarkSuperseded`; clear candidate and invalidate its confirmation card; save | `Reset` |
| Active expired `Ready` intake | Apply existing `MarkExpired`; clear candidate; save | `Reset` |
| No active intake | Make no persistence change | `AlreadyClear` |
| Existing `Submitted` request/intake | Do not select or change it | `AlreadyClear` unless another active intake exists |
| Load or save failure | Return the existing typed application failure | `Failed` |

The operation:

- does not call the LLM or any MCP tool;
- does not create an intake or access request;
- does not delete persistence or model-session records;
- records the terminal lifecycle transition with existing time and correlation
  metadata; and
- is safe to repeat.

The next ordinary Teams message uses the existing preparation path. Because there is
no active intake, Core creates a new server-owned intake ID. That ID is the existing
MAF session key, so prior model history cannot be loaded for the replacement.

## Teams response contract

Both `Reset` and `AlreadyClear` return the same safe requester guidance:

```text
Started a new request. Send an incident ID or production environment ID when you are ready.
```

The response does not echo previous candidate details or disclose whether an active
preparation existed. `Failed` uses the existing safe dependency/application failure
guidance and structured logging conventions.

## Concurrency and replay

- Repeated `/new` messages are idempotent.
- Actor and conversation scoping prevents one chat from resetting another.
- Existing persistence concurrency handling decides races with confirmation or a
  simultaneous preparation turn. A reset must never mutate an already-submitted
  request, and a confirmation card can submit only if its exact ready intake still
  reloads and revalidates successfully.
- The lifecycle operation logs correlation, authenticated actor, duration, result,
  affected intake ID when available, and typed failure metadata. It never logs the
  prior candidate, raw message transcript, prompt, response, or MCP payload.

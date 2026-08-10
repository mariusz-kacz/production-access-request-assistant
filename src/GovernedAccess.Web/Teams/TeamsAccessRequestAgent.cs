using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.AdaptiveCards;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Authenticated Teams transport adapter for request preparation and deterministic
/// confirmation. Durable lifecycle transitions belong to Core; this adapter does
/// not couple them to the process-lifetime MAF session store.
/// </summary>
public sealed partial class TeamsAccessRequestAgent : AgentApplication
{
    private const string ConfirmAndSubmitVerb = "confirmAndSubmit";

    private const string NewRequestCommand = "/new";

    private const string ResetSucceededMessage =
        "Started a new request. Send an incident ID or production environment ID when you are ready.";

    private const string RejectedActivityMessage =
        "This assistant accepts production-access requests only from an authenticated personal Microsoft Teams chat.";

    private const string EmptyMessageMessage =
        "Describe the temporary production access you need, including the client, environment, requested role, and operational justification.";

    private readonly TeamsActorResolver actorResolver;
    private readonly RequestDraftService draftService;
    private readonly RequestSubmissionService submissionService;
    private readonly PreparedRequestCardFactory cardFactory;
    private readonly TeamsDraftCardTracker cardTracker;
    private readonly ILogger<TeamsAccessRequestAgent> logger;
    private readonly RequestPreparationModelMetadata modelMetadata;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        RequestDraftService draftService,
        RequestSubmissionService submissionService,
        PreparedRequestCardFactory cardFactory,
        TeamsDraftCardTracker cardTracker,
        ILogger<TeamsAccessRequestAgent> logger,
        RequestPreparationModelMetadata modelMetadata)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(draftService);
        ArgumentNullException.ThrowIfNull(submissionService);
        ArgumentNullException.ThrowIfNull(cardFactory);
        ArgumentNullException.ThrowIfNull(cardTracker);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(modelMetadata);

        this.actorResolver = actorResolver;
        this.draftService = draftService;
        this.submissionService = submissionService;
        this.cardFactory = cardFactory;
        this.cardTracker = cardTracker;
        this.logger = logger;
        this.modelMetadata = modelMetadata;

        AdaptiveCards.OnActionExecute(
            ConfirmAndSubmitVerb,
            OnConfirmAndSubmitAsync);
    }

    [MessageRoute]
    public async Task OnMessageAsync(
        ITurnContext turnContext,
        ITurnState _,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);

        if (!actorResolver.TryResolve(
                turnContext.Activity,
                turnContext.Identity,
                out var actor))
        {
            await SendTextAsync(
                turnContext,
                RejectedActivityMessage,
                InputHints.IgnoringInput,
                cancellationToken);
            return;
        }

        var latestMessage = turnContext.Activity.Text?.Trim();
        if (string.IsNullOrEmpty(latestMessage))
        {
            await SendTextAsync(
                turnContext,
                EmptyMessageMessage,
                InputHints.ExpectingInput,
                cancellationToken);
            return;
        }

        var correlationId = CreateCorrelationId();
        if (string.Equals(
                latestMessage,
                NewRequestCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            await ResetPreparationAsync(
                turnContext,
                actor,
                correlationId,
                cancellationToken);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var outcome = await draftService.PrepareAsync(
            new PrepareAccessRequestCommand(
                actor,
                latestMessage,
                correlationId),
            cancellationToken);
        var sessionId = outcome.Session?.Id;
        var requestId = outcome.Session?.ReservedRequestId;
        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMs =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogPreparationCompleted(
                logger,
                "Prepare",
                outcome.Kind,
                modelMetadata.ProfileId,
                modelMetadata.DeploymentName,
                turnContext.Activity.Type,
                durationMs,
                correlationId,
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId,
                sessionId,
                requestId,
                outcome.ValidationErrors.Count,
                outcome.Failure?.Kind,
                outcome.Failure?.Code);
        }

        switch (outcome.Kind)
        {
            case RequestPreparationResultKind.DraftDiscussion:
                await SendTextAsync(
                    turnContext,
                    RenderMessageWithEnvironmentChoices(
                        outcome.DiscussionMessage!,
                        outcome.EnvironmentChoices),
                    InputHints.AcceptingInput,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.ClarificationRequired:
                if (!outcome.PreservesReadyDraft)
                {
                    await DisableTrackedDraftCardAsync(
                        turnContext,
                        actor,
                        "Draft being revised",
                        "This draft is no longer ready for submission. Continue in the chat to complete the revised details.",
                        cancellationToken);
                }
                await SendTextAsync(
                    turnContext,
                    RenderClarification(
                        outcome.Clarification!,
                        outcome.EnvironmentChoices),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.CandidateRejected:
                await DisableTrackedDraftCardAsync(
                    turnContext,
                    actor,
                    "Draft being revised",
                    "This draft is no longer ready for submission. Correct the details in the chat to prepare a revised draft.",
                    cancellationToken);
                await SendTextAsync(
                    turnContext,
                    RenderCandidateRejection(outcome.ValidationErrors),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.ReadyForConfirmation:
                await SendReadyCardAsync(
                    turnContext,
                    actor,
                    outcome.Session!,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.Failed:
                await SendFailureAsync(
                    turnContext,
                    outcome.Failure!,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException(
                    "The request-preparation outcome is unsupported.");
        }
    }

    private async Task ResetPreparationAsync(
        ITurnContext turnContext,
        AuthenticatedChannelActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = await draftService.ResetAsync(
            new ResetRequestIntakeCommand(actor, correlationId),
            cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMs =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogResetCompleted(
                logger,
                "Reset",
                outcome.Kind,
                turnContext.Activity.Type,
                durationMs,
                correlationId,
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId,
                outcome.IntakeId,
                outcome.Failure?.Kind,
                outcome.Failure?.Code);
        }

        switch (outcome.Kind)
        {
            case RequestIntakeResetResultKind.Reset:
                await DisableTrackedDraftCardAsync(
                    turnContext,
                    actor,
                    "Draft discarded",
                    "This draft was discarded and can no longer be submitted.",
                    cancellationToken);
                await SendTextAsync(
                    turnContext,
                    ResetSucceededMessage,
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestIntakeResetResultKind.AlreadyClear:
                await SendTextAsync(
                    turnContext,
                    ResetSucceededMessage,
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestIntakeResetResultKind.Failed:
                await SendResetFailureAsync(
                    turnContext,
                    outcome.Failure!,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException(
                    "The request-intake reset outcome is unsupported.");
        }
    }

    private async Task<AdaptiveCardInvokeResponse> OnConfirmAndSubmitAsync(
        ITurnContext turnContext,
        ITurnState _,
        object data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);

        if (!actorResolver.TryResolve(
                turnContext.Activity,
                turnContext.Identity,
                out var actor))
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(
                RejectedActivityMessage);
        }

        if (!TryReadConfirmationData(data, out var preparationId))
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(
                "The confirmation action is invalid. No request was submitted.");
        }

        var correlationId = CreateCorrelationId();
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = await submissionService.ConfirmDraftAsync(
            new ConfirmRequestIntakeCommand(
                actor,
                preparationId,
                correlationId),
            cancellationToken);
        var requestId = outcome.RequestId == Guid.Empty
            ? (Guid?)null
            : outcome.RequestId;
        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMs =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogConfirmationCompleted(
                logger,
                "Confirm",
                outcome.Kind,
                turnContext.Activity.Type,
                durationMs,
                correlationId,
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId,
                preparationId,
                requestId,
                outcome.WasAlreadySubmitted,
                outcome.Failure?.Kind,
                outcome.Failure?.Code);
        }

        if (outcome.Kind is RequestConfirmationResultKind.Submitted
            or RequestConfirmationResultKind.AlreadySubmitted)
        {
            cardTracker.TryRemove(actor, preparationId);
        }

        return outcome.Kind switch
        {
            RequestConfirmationResultKind.Submitted
                or RequestConfirmationResultKind.AlreadySubmitted =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    CreateSubmittedCard(outcome)),
            RequestConfirmationResultKind.Failed
                when TryCreateInactiveConfirmationCard(
                    outcome.Failure!,
                    out var inactiveCard) =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    inactiveCard.ToJsonString()),
            RequestConfirmationResultKind.Failed =>
                AdaptiveCardInvokeResponseFactory.Message(
                    CreateConfirmationFailureMessage(outcome.Failure!)),
            _ => throw new InvalidOperationException(
                "The prepared-request confirmation outcome is unsupported."),
        };
    }

    private async Task SendReadyCardAsync(
        ITurnContext turnContext,
        AuthenticatedChannelActor actor,
        RequestIntakeSession session,
        CancellationToken cancellationToken)
    {
        var cardResult = await cardFactory.CreateAsync(
            session,
            cancellationToken);
        if (cardResult.IsFailure)
        {
            await SendFailureAsync(
                turnContext,
                cardResult.Failure!,
                cancellationToken);
            return;
        }

        await DisableTrackedDraftCardAsync(
            turnContext,
            actor,
            "Draft being revised",
            "This draft was replaced by a revised version and can no longer be submitted. Use the latest request draft card.",
            cancellationToken);

        var response = await turnContext.SendActivityAsync(
            MessageFactory.Attachment(
                cardResult.Value,
                inputHint: InputHints.AcceptingInput),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(response.Id))
        {
            cardTracker.Set(actor, session.Id, response.Id);
        }
    }

    private async Task DisableTrackedDraftCardAsync(
        ITurnContext turnContext,
        AuthenticatedChannelActor actor,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        if (!cardTracker.TryRemove(actor, out var current))
        {
            return;
        }

        _ = await TryUpdateCardActivityAsync(
            turnContext,
            current.ActivityId,
            CreateInactiveDraftCardAttachment(title, message),
            cancellationToken);
    }

    private async Task<bool> TryUpdateCardActivityAsync(
        ITurnContext turnContext,
        string activityId,
        Attachment attachment,
        CancellationToken cancellationToken)
    {
        var replacement = turnContext.Activity.CreateReply();
        replacement.Id = activityId;
        replacement.InputHint = InputHints.IgnoringInput;
        replacement.Attachments = [attachment];

        try
        {
            await turnContext.UpdateActivityAsync(replacement, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDraftCardUpdateFailed(
                logger,
                turnContext.Activity.ChannelId ?? "unknown",
                turnContext.Activity.Conversation.Id,
                exception.GetType().Name);
            return false;
        }
    }

    private static Attachment CreateInactiveDraftCardAttachment(
        string title,
        string message)
    {
        return new Attachment
        {
            ContentType = PreparedRequestCardFactory.AdaptiveCardContentType,
            Content = JsonSerializer.SerializeToElement(
                CreateInactiveDraftCard(title, message)),
        };
    }

    private static JsonObject CreateInactiveDraftCard(
        string title,
        string message) =>
        new()
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["size"] = "Medium",
                    ["weight"] = "Bolder",
                    ["text"] = title,
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = message,
                    ["wrap"] = true,
                },
            },
        };

    private static bool TryCreateInactiveConfirmationCard(
        ApplicationFailure failure,
        out JsonObject card)
    {
        (string Title, string Message)? content = failure.Code switch
        {
            RequestSubmissionService.SupersededCode => (
                "Draft replaced",
                "This draft was replaced by a newer version and can no longer be submitted. Use the latest request draft card."),
            RequestSubmissionService.ExpiredCode => (
                "Draft expired",
                "This draft expired and can no longer be submitted. Continue in the chat to prepare a new draft."),
            RequestSubmissionService.InvalidatedCode => (
                "Draft no longer valid",
                "This draft is no longer valid against current production context and cannot be submitted."),
            _ => null,
        };

        if (content is null)
        {
            card = null!;
            return false;
        }

        card = CreateInactiveDraftCard(
            content.Value.Title,
            content.Value.Message);
        return true;
    }

    private static Task SendFailureAsync(
        ITurnContext turnContext,
        ApplicationFailure failure,
        CancellationToken cancellationToken)
    {
        var message = failure.Code switch
        {
            RequestDraftService.MalformedModelOutputCode =>
                "The assistant response could not be validated. No request was submitted; please try again.",
            RequestDraftService.ModelTimeoutCode =>
                "Request preparation timed out before any request was submitted. Please try again.",
            RequestDraftService.ModelCancelledCode =>
                "Request preparation was cancelled before any request was submitted. Send the request again when you are ready.",
            RequestDraftService.ModelUnavailableCode =>
                "Request preparation is temporarily unavailable. No request was submitted; please try again later.",
            _ => CreateGenericPreparationFailureMessage(failure.Kind),
        };

        return SendTextAsync(
            turnContext,
            message,
            InputHints.AcceptingInput,
            cancellationToken);
    }

    private static Task SendResetFailureAsync(
        ITurnContext turnContext,
        ApplicationFailure failure,
        CancellationToken cancellationToken)
    {
        var message = failure.Kind switch
        {
            ApplicationFailureKind.Timeout =>
                "Starting a new request timed out. Your current preparation was not safely reset; please try again.",
            ApplicationFailureKind.Cancelled =>
                "Starting a new request was cancelled. Your current preparation was not safely reset; please try again.",
            ApplicationFailureKind.DependencyUnavailable
                or ApplicationFailureKind.DependencyFailure =>
                "Starting a new request is temporarily unavailable. Your current preparation was not safely reset; please try again later.",
            ApplicationFailureKind.ConcurrencyConflict =>
                "The preparation changed while the new request was starting. Please try /new again.",
            _ =>
                "A new request could not be started safely. Please try again.",
        };

        return SendTextAsync(
            turnContext,
            message,
            InputHints.AcceptingInput,
            cancellationToken);
    }

    private static string CreateGenericPreparationFailureMessage(
        ApplicationFailureKind failureKind) =>
        failureKind switch
        {
            ApplicationFailureKind.Timeout =>
                "Request preparation timed out before any request was submitted. Please try again.",
            ApplicationFailureKind.Cancelled =>
                "Request preparation was cancelled before any request was submitted. Send the request again when you are ready.",
            ApplicationFailureKind.DependencyUnavailable
                or ApplicationFailureKind.DependencyFailure =>
                "Request preparation is temporarily unavailable. No request was submitted; please try again later.",
            ApplicationFailureKind.InvalidTransition =>
                "This preparation can no longer be updated. Start a new request in this chat.",
            ApplicationFailureKind.ConcurrencyConflict =>
                "The preparation changed while this message was being processed. No request was submitted; please try again.",
            _ =>
                "The request could not be prepared safely. No request was submitted; please try again.",
        };

    private static string RenderCandidateRejection(
        IReadOnlyList<FieldValidationError> validationErrors)
    {
        var message = new StringBuilder(
            "Deterministic application validation rejected the assistant's candidate. No final request was created:");

        foreach (var error in validationErrors)
        {
            message.AppendLine();
            message.Append("- ");
            message.Append(error.Message);
        }

        message.AppendLine();
        message.Append(
            "Correct the listed details in your next message. Nothing has been submitted.");

        return message.ToString();
    }

    private static string RenderClarification(
        RequestClarificationProposal clarification,
        IReadOnlyList<RequestEnvironmentChoice> environmentChoices) =>
        RenderMessageWithEnvironmentChoices(
            clarification.Message,
            environmentChoices);

    private static string RenderMessageWithEnvironmentChoices(
        string text,
        IReadOnlyList<RequestEnvironmentChoice> environmentChoices)
    {
        if (environmentChoices.Count == 0)
        {
            return text;
        }

        var message = new StringBuilder(text);
        message.AppendLine();

        foreach (var choice in environmentChoices.OrderBy(
                     static choice => choice.EnvironmentId,
                     StringComparer.Ordinal))
        {
            message.AppendLine();
            message.Append("- ");
            message.Append(choice.ClientDisplayName);
            message.Append(" — ");
            message.Append(choice.EnvironmentDisplayName);
            message.Append(" (");
            message.Append(choice.EnvironmentId);
            message.Append(')');
        }

        return message.ToString();
    }

    private static string CreateSubmittedCard(
        RequestConfirmationResult outcome)
    {
        var title = outcome.WasAlreadySubmitted
            ? "Request already submitted"
            : "Request submitted";
        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["size"] = "Medium",
                    ["weight"] = "Bolder",
                    ["text"] = title,
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] =
                        $"Request {outcome.RequestId:D} is awaiting business approval. Access is not yet approved or granted.",
                    ["wrap"] = true,
                },
            },
        };

        return card.ToJsonString();
    }

    private static string CreateConfirmationFailureMessage(
        ApplicationFailure failure)
    {
        return failure.Code switch
        {
            RequestSubmissionService.ForbiddenCode =>
                CreateConcealedConfirmationMessage(),
            RequestSubmissionService.ExpiredCode =>
                "This prepared request has expired. No request was submitted; start a new request in this chat.",
            RequestSubmissionService.SupersededCode =>
                "This prepared request was replaced by a newer preparation. No request was submitted; use the latest card or start a new request.",
            RequestSubmissionService.InvalidatedCode =>
                "This prepared request is no longer valid against current production context. No request was submitted; start a new request in this chat.",
            RequestSubmissionService.NotReadyCode =>
                "This preparation is not ready for confirmation. No request was submitted; continue the request in this chat.",
            _ => CreateGenericConfirmationFailureMessage(failure.Kind),
        };
    }

    private static string CreateGenericConfirmationFailureMessage(
        ApplicationFailureKind failureKind) =>
        failureKind switch
        {
            ApplicationFailureKind.Unauthorized
                or ApplicationFailureKind.NotFound =>
                CreateConcealedConfirmationMessage(),
            ApplicationFailureKind.InvalidTransition =>
                "This prepared request can no longer be submitted. Start a new request in this chat.",
            ApplicationFailureKind.ConcurrencyConflict =>
                "The prepared request changed while confirmation was being processed. No additional request was submitted.",
            ApplicationFailureKind.DependencyUnavailable
                or ApplicationFailureKind.DependencyFailure =>
                "Request confirmation is temporarily unavailable. No request was submitted; please try again later.",
            _ =>
                "The request could not be confirmed safely. No request was submitted.",
        };

    private static string CreateConcealedConfirmationMessage() =>
        "The prepared request could not be found for this authenticated conversation. No request was submitted.";

    private static bool TryReadConfirmationData(
        object? data,
        out Guid preparationId)
    {
        preparationId = Guid.Empty;

        try
        {
            var element = data switch
            {
                JsonElement jsonElement => jsonElement,
                JsonDocument jsonDocument => jsonDocument.RootElement,
                not null => JsonSerializer.SerializeToElement(data),
                _ => default,
            };

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = element.EnumerateObject().ToArray();
            if (properties.Length != 2
                || !element.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version != 1
                || !element.TryGetProperty(
                    "preparedRequestId",
                    out var preparedRequestId)
                || preparedRequestId.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var reference = preparedRequestId.GetString();
            return Guid.TryParseExact(reference, "D", out preparationId)
                && preparationId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static Task SendTextAsync(
        ITurnContext turnContext,
        string message,
        string inputHint,
        CancellationToken cancellationToken) =>
        SendActivityAsync(
            turnContext,
            MessageFactory.Text(
                message,
                inputHint: inputHint),
            cancellationToken);

    private static async Task SendActivityAsync(
        ITurnContext turnContext,
        IActivity activity,
        CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync(activity, cancellationToken);
    }

    private static string CreateCorrelationId()
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId ?? default;
        return traceId != default
            ? traceId.ToString()
            : Guid.NewGuid().ToString("N");
    }

    [LoggerMessage(
        EventId = 1001,
        EventName = "TeamsIntakePreparationCompleted",
        Level = LogLevel.Information,
        Message = "Teams intake {Transition} completed with {Outcome} using profile {ProfileId} and deployment {DeploymentName} for activity {ActivityType} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; session {SessionId}; request {RequestId}; validation error count {ValidationErrorCount}; failure kind {FailureKind}; failure code {FailureCode}.")]
    private static partial void LogPreparationCompleted(
        ILogger logger,
        string transition,
        RequestPreparationResultKind outcome,
        string profileId,
        string? deploymentName,
        string activityType,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid? sessionId,
        Guid? requestId,
        int validationErrorCount,
        ApplicationFailureKind? failureKind,
        string? failureCode);

    [LoggerMessage(
        EventId = 1002,
        EventName = "TeamsIntakeConfirmationCompleted",
        Level = LogLevel.Information,
        Message = "Teams intake {Transition} completed with {Outcome} for activity {ActivityType} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; session {SessionId}; request {RequestId}; replay {WasReplay}; failure kind {FailureKind}; failure code {FailureCode}.")]
    private static partial void LogConfirmationCompleted(
        ILogger logger,
        string transition,
        RequestConfirmationResultKind outcome,
        string activityType,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid sessionId,
        Guid? requestId,
        bool wasReplay,
        ApplicationFailureKind? failureKind,
        string? failureCode);

    [LoggerMessage(
        EventId = 1003,
        EventName = "TeamsIntakeResetCompleted",
        Level = LogLevel.Information,
        Message = "Teams intake {Transition} completed with {Outcome} for activity {ActivityType} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; session {SessionId}; failure kind {FailureKind}; failure code {FailureCode}.")]
    private static partial void LogResetCompleted(
        ILogger logger,
        string transition,
        RequestIntakeResetResultKind outcome,
        string activityType,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid? sessionId,
        ApplicationFailureKind? failureKind,
        string? failureCode);

    [LoggerMessage(
        EventId = 1004,
        EventName = "TeamsDraftCardUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Teams draft-card presentation update failed for channel {Channel} and conversation {ConversationId}. Failure type {FailureType}; durable intake validation remains authoritative.")]
    private static partial void LogDraftCardUpdateFailed(
        ILogger logger,
        string? channel,
        string conversationId,
        string failureType);
}

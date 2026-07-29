using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.AdaptiveCards;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Options;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Authenticated Teams transport adapter for request preparation. This agent can
/// update preparation state and display a ready snapshot.
/// </summary>
public sealed partial class TeamsAccessRequestAgent : AgentApplication
{
    private const string ConfirmAndSubmitVerb = "confirmAndSubmit";

    private const string RejectedActivityMessage =
        "This assistant accepts production-access requests only from an authenticated personal Microsoft Teams chat.";

    private const string EmptyMessageMessage =
        "Describe the temporary production access you need, including the client, environment, requested role, and operational justification.";

    private readonly TeamsActorResolver actorResolver;
    private readonly RequestIntakeService intakeService;
    private readonly PreparedRequestCardFactory cardFactory;
    private readonly ILogger<TeamsAccessRequestAgent> logger;
    private readonly Uri trustedWebBaseUri;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        RequestIntakeService intakeService,
        PreparedRequestCardFactory cardFactory,
        ILogger<TeamsAccessRequestAgent> logger,
        IOptions<TeamsAccessRequestOptions> teamsOptions)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(intakeService);
        ArgumentNullException.ThrowIfNull(cardFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(teamsOptions);

        this.actorResolver = actorResolver;
        this.intakeService = intakeService;
        this.cardFactory = cardFactory;
        this.logger = logger;
        trustedWebBaseUri = teamsOptions.Value.TrustedWebBaseUri
            ?? throw new InvalidOperationException(
                "A trusted Web base URI is required for Teams request links.");

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
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = await intakeService.PrepareAsync(
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
                durationMs,
                correlationId,
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId,
                sessionId,
                requestId);
        }

        switch (outcome.Kind)
        {
            case RequestPreparationResultKind.ClarificationRequired:
                await SendTextAsync(
                    turnContext,
                    RenderClarification(outcome.Clarification!),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.CandidateRejected:
                await SendTextAsync(
                    turnContext,
                    RenderCandidateRejection(outcome.ValidationErrors),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestPreparationResultKind.ReadyForConfirmation:
                await SendReadyCardAsync(
                    turnContext,
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
        var outcome = await intakeService.ConfirmAsync(
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
                durationMs,
                correlationId,
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId,
                preparationId,
                requestId);
        }

        return outcome.Kind switch
        {
            RequestConfirmationResultKind.Submitted
                or RequestConfirmationResultKind.AlreadySubmitted =>
                AdaptiveCardInvokeResponseFactory.Message(
                    CreateConfirmationMessage(outcome)),
            RequestConfirmationResultKind.Failed =>
                AdaptiveCardInvokeResponseFactory.Message(
                    CreateConfirmationFailureMessage(outcome.Failure!)),
            _ => throw new InvalidOperationException(
                "The prepared-request confirmation outcome is unsupported."),
        };
    }

    private async Task SendReadyCardAsync(
        ITurnContext turnContext,
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

        await turnContext.SendActivityAsync(
            MessageFactory.Attachment(
                cardResult.Value,
                inputHint: InputHints.AcceptingInput),
            cancellationToken);
    }

    private static Task SendFailureAsync(
        ITurnContext turnContext,
        ApplicationFailure failure,
        CancellationToken cancellationToken)
    {
        var message = failure.Kind switch
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

        return SendTextAsync(
            turnContext,
            message,
            InputHints.AcceptingInput,
            cancellationToken);
    }

    private static string RenderClarification(
        RequestClarificationContext clarification)
    {
        if (clarification.Options.Count == 0)
        {
            return clarification.Prompt;
        }

        var message = new StringBuilder(clarification.Prompt);
        message.AppendLine();
        message.AppendLine();
        message.Append("Choose one of these authoritative options:");

        for (var index = 0; index < clarification.Options.Count; index++)
        {
            var option = clarification.Options[index];
            message.AppendLine();
            message.Append(index + 1);
            message.Append(". ");
            message.Append(option.Label);
            message.Append(" (");
            message.Append(option.Value);
            message.Append(')');
        }

        return message.ToString();
    }

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

    private string CreateConfirmationMessage(
        RequestConfirmationResult outcome)
    {
        var requestUri = new Uri(
            trustedWebBaseUri,
            $"requests/{outcome.RequestId:D}");
        var status = outcome.WasAlreadySubmitted
            ? "was already submitted"
            : "was submitted";

        return $"Request {outcome.RequestId:D} {status} and is awaiting business approval. Access is not yet approved or granted. Open the request: {requestUri.AbsoluteUri}";
    }

    private static string CreateConfirmationFailureMessage(
        ApplicationFailure failure)
    {
        return failure.Kind switch
        {
            ApplicationFailureKind.Unauthorized
                or ApplicationFailureKind.NotFound =>
                "The prepared request could not be found for this authenticated conversation. No request was submitted.",
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
    }

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
        Message = "Teams intake {Transition} completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; session {SessionId}; request {RequestId}.")]
    private static partial void LogPreparationCompleted(
        ILogger logger,
        string transition,
        RequestPreparationResultKind outcome,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid? sessionId,
        Guid? requestId);

    [LoggerMessage(
        EventId = 1002,
        EventName = "TeamsIntakeConfirmationCompleted",
        Level = LogLevel.Information,
        Message = "Teams intake {Transition} completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; session {SessionId}; request {RequestId}.")]
    private static partial void LogConfirmationCompleted(
        ILogger logger,
        string transition,
        RequestConfirmationResultKind outcome,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid sessionId,
        Guid? requestId);
}

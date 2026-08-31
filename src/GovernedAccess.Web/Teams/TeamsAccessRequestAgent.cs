using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.AdaptiveCards;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal sealed partial class TeamsAccessRequestAgent : AgentApplication
{
    private const string ConfirmAndSubmitVerb = "confirmAndSubmit";
    private const string RejectedActivityMessage =
        "This assistant accepts production-access requests only from an authenticated personal Microsoft Teams chat.";

    private readonly TeamsActorResolver actorResolver;
    private readonly TeamsRequestHandler handler;
    private readonly TeamsDraftCardTracker cardTracker;
    private readonly ILogger<TeamsAccessRequestAgent> logger;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        TeamsRequestHandler handler,
        TeamsDraftCardTracker cardTracker,
        ILogger<TeamsAccessRequestAgent> logger)
        : base(options)
    {
        this.actorResolver = actorResolver
            ?? throw new ArgumentNullException(nameof(actorResolver));
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        this.cardTracker = cardTracker
            ?? throw new ArgumentNullException(nameof(cardTracker));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
        if (!TryResolve(turnContext, out var context))
        {
            await SendTextAsync(
                turnContext,
                RejectedActivityMessage,
                InputHints.IgnoringInput,
                cancellationToken);
            return;
        }

        var result = await handler.HandleMessageAsync(
            context,
            turnContext.Activity.Text,
            CreateCorrelationId(),
            cancellationToken);
        await PresentAsync(turnContext, context, result, cancellationToken);
    }

    internal async Task<AdaptiveCardInvokeResponse> OnConfirmAndSubmitAsync(
        ITurnContext turnContext,
        ITurnState _,
        object data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        if (!TryResolve(turnContext, out var context))
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(
                RejectedActivityMessage);
        }

        if (!TryParseConfirmationData(data, out var confirmedPreparationId))
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(
                "The confirmation action is invalid. No request was submitted.");
        }

        var result = await handler.HandleConfirmationAsync(
            context,
            confirmedPreparationId,
            CreateCorrelationId(),
            cancellationToken);

        if (result is TeamsReplacementDraftCardResponse replacement
            && cardTracker.TryRemove(
                context.Conversation,
                confirmedPreparationId,
                out var tracked))
        {
            cardTracker.Set(
                context.Conversation,
                replacement.PreparationId,
                tracked.ActivityId);
        }
        else if (result is TeamsRetiringTextResponse or TeamsTerminalCardResponse)
        {
            cardTracker.TryRemove(
                context.Conversation,
                confirmedPreparationId);
        }

        return result switch
        {
            TeamsCardResponse card =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    GetCardJson(card.Card)),
            TeamsMessageResponse message =>
                AdaptiveCardInvokeResponseFactory.Message(message.Message),
            _ => throw new InvalidOperationException(
                "The Teams confirmation result is unsupported."),
        };
    }

    private async Task PresentAsync(
        ITurnContext turnContext,
        TeamsAuthenticatedContext context,
        TeamsResponse result,
        CancellationToken cancellationToken)
    {
        if (result is TeamsMessageResponse message)
        {
            if (message is TeamsRetiringTextResponse)
            {
                await DisableTrackedCardAsync(
                    turnContext,
                    context.Conversation,
                    cancellationToken);
            }

            await SendTextAsync(
                turnContext,
                message.Message,
                message.InputHint,
                cancellationToken);
            return;
        }

        if (result is TeamsTerminalCardResponse terminal)
        {
            await DisableTrackedCardAsync(
                turnContext,
                context.Conversation,
                cancellationToken);
            _ = await SendAttachmentAsync(
                turnContext,
                terminal.Card,
                terminal.InputHint,
                cancellationToken);
            return;
        }

        if (result is not TeamsActionableCardResponse actionable)
        {
            throw new InvalidOperationException(
                "The Teams preparation result is unsupported.");
        }

        if (cardTracker.TryGet(context.Conversation, out var current))
        {
            if (current.PreparationId == actionable.PreparationId
                && await TryUpdateAttachmentAsync(
                    turnContext,
                    current.ActivityId,
                    actionable.Card,
                    cancellationToken))
            {
                return;
            }

            await DisableTrackedCardAsync(
                turnContext,
                context.Conversation,
                cancellationToken);
        }

        var activityId = await SendAttachmentAsync(
            turnContext,
            actionable.Card,
            actionable.InputHint,
            cancellationToken);
        if (activityId is not null)
        {
            cardTracker.Set(
                context.Conversation,
                actionable.PreparationId,
                activityId);
        }
    }

    internal static bool TryParseConfirmationData(
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
            return properties.Length == 2
                && element.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.TryGetInt32(out var version)
                && version == TeamsAdaptiveCardRenderer.ContractSchemaVersion
                && element.TryGetProperty(
                    "preparationId",
                    out var preparationIdProperty)
                && preparationIdProperty.ValueKind == JsonValueKind.String
                && Guid.TryParseExact(
                    preparationIdProperty.GetString(),
                    "D",
                    out preparationId)
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

    private async Task DisableTrackedCardAsync(
        ITurnContext turnContext,
        TeamsConversationReference conversation,
        CancellationToken cancellationToken)
    {
        if (!cardTracker.TryRemove(conversation, out var current))
        {
            return;
        }

        _ = await TryUpdateAttachmentAsync(
            turnContext,
            current.ActivityId,
            TeamsAdaptiveCardRenderer.CreateStatusCard(
                "Draft replaced",
                "This draft can no longer be submitted. Use the latest request draft card."),
            cancellationToken);
    }

    private static async Task SendTextAsync(
        ITurnContext turnContext,
        string message,
        string inputHint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        await turnContext.SendActivityAsync(
            MessageFactory.Text(message, inputHint: inputHint),
            cancellationToken);
    }

    private static async Task<string?> SendAttachmentAsync(
        ITurnContext turnContext,
        Attachment attachment,
        string inputHint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        ArgumentNullException.ThrowIfNull(attachment);
        var response = await turnContext.SendActivityAsync(
            MessageFactory.Attachment(attachment, inputHint: inputHint),
            cancellationToken);
        return string.IsNullOrWhiteSpace(response.Id)
            ? null
            : response.Id.Trim();
    }

    private async Task<bool> TryUpdateAttachmentAsync(
        ITurnContext turnContext,
        string activityId,
        Attachment attachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        ArgumentNullException.ThrowIfNull(attachment);

        var replacement = turnContext.Activity.CreateReply();
        replacement.Id = activityId.Trim();
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
            LogCardUpdateFailed(
                logger,
                turnContext.Activity.ChannelId.ToString(),
                turnContext.Activity.Conversation.Id is { Length: > 0 } conversationId
                    ? conversationId
                    : "unknown",
                exception.GetType().Name);
            return false;
        }
    }

    private bool TryResolve(
        ITurnContext turnContext,
        out TeamsAuthenticatedContext context)
    {
        var resolved = actorResolver.TryResolve(
            turnContext.Activity,
            turnContext.Identity,
            out var authenticatedContext);
        context = authenticatedContext!;
        return resolved;
    }

    private static string GetCardJson(Attachment attachment) =>
        attachment.Content is JsonElement content
            ? content.GetRawText()
            : throw new InvalidOperationException(
                "The Teams card renderer returned unsupported content.");

    private static string CreateCorrelationId()
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId ?? default;
        return traceId != default
            ? traceId.ToString()
            : Guid.NewGuid().ToString("N");
    }

    [LoggerMessage(
        EventId = 1010,
        EventName = "TeamsCardUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Teams card presentation update failed for channel {Channel} and conversation {ConversationId}. Failure type {FailureType}; durable workflow state remains authoritative.")]
    private static partial void LogCardUpdateFailed(
        ILogger logger,
        string channel,
        string conversationId,
        string failureType);
}

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

        var hasPreparationId = TeamsRequestHandler.TryReadConfirmationData(
            data,
            out var confirmedPreparationId);
        var result = await handler.HandleConfirmationAsync(
            context,
            data,
            CreateCorrelationId(),
            cancellationToken);
        if (result.Kind == TeamsResponseKind.InvalidAction)
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(result.Message!);
        }

        if (!hasPreparationId)
        {
            throw new InvalidOperationException(
                "A valid confirmation response requires a preparation identifier.");
        }

        if (result.InvalidatesTrackedCard
            && cardTracker.TryRemove(
                context.Conversation,
                confirmedPreparationId,
                out var tracked)
            && result.TrackAsActiveDraft)
        {
            var replacementId = result.PreparationId
                ?? throw new InvalidOperationException(
                    "An actionable Teams draft requires a preparation identifier.");
            cardTracker.Set(
                context.Conversation,
                replacementId,
                tracked.ActivityId);
        }

        return result.Kind switch
        {
            TeamsResponseKind.Card =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    GetCardJson(result.Card!)),
            TeamsResponseKind.Text =>
                AdaptiveCardInvokeResponseFactory.Message(result.Message!),
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
        if (result.Kind == TeamsResponseKind.Text)
        {
            if (result.InvalidatesTrackedCard)
            {
                await DisableTrackedCardAsync(
                    turnContext,
                    context.Conversation,
                    cancellationToken);
            }

            await SendTextAsync(
                turnContext,
                result.Message!,
                result.InputHint,
                cancellationToken);
            return;
        }

        if (result.Kind != TeamsResponseKind.Card
            || result.Card is null)
        {
            throw new InvalidOperationException(
                "The Teams preparation result is unsupported.");
        }

        Guid? preparationId = result.TrackAsActiveDraft
            ? result.PreparationId
                ?? throw new InvalidOperationException(
                    "An actionable Teams draft requires a preparation identifier.")
            : null;

        if (cardTracker.TryGet(context.Conversation, out var current))
        {
            if (preparationId is Guid currentPreparationId
                && current.PreparationId == currentPreparationId
                && await TryUpdateAttachmentAsync(
                    turnContext,
                    current.ActivityId,
                    result.Card,
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
            result.Card,
            result.InputHint,
            cancellationToken);
        if (activityId is not null)
        {
            if (preparationId is Guid activePreparationId)
            {
                cardTracker.Set(
                    context.Conversation,
                    activePreparationId,
                    activityId);
            }
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

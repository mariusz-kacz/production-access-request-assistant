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
    private readonly TeamsAccessRequestAdapter adapter;
    private readonly TeamsDraftCardTracker cardTracker;
    private readonly ILogger<TeamsAccessRequestAgent> logger;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        TeamsAccessRequestAdapter adapter,
        TeamsDraftCardTracker cardTracker,
        ILogger<TeamsAccessRequestAgent> logger)
        : base(options)
    {
        this.actorResolver = actorResolver
            ?? throw new ArgumentNullException(nameof(actorResolver));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
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

        var result = await adapter.HandleMessageAsync(
            context,
            turnContext.Activity.Text,
            CreateCorrelationId(),
            cancellationToken);
        await PresentAsync(turnContext, context, result, cancellationToken);
    }

    private async Task<AdaptiveCardInvokeResponse> OnConfirmAndSubmitAsync(
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

        var result = await adapter.HandleConfirmationAsync(
            context,
            data,
            CreateCorrelationId(),
            cancellationToken);
        if (result.Kind == TeamsAdapterResultKind.InvalidAction)
        {
            return AdaptiveCardInvokeResponseFactory.BadRequest(result.Message!);
        }

        if (result.InvalidatesTrackedCard
            && cardTracker.TryRemove(context.Conversation, out var tracked)
            && result.Card is not null
            && result.PreparationId is Guid replacementId)
        {
            cardTracker.Set(
                context.Conversation,
                replacementId,
                tracked.ActivityId);
        }

        return result.Kind switch
        {
            TeamsAdapterResultKind.Card =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    GetCardJson(result.Card!)),
            TeamsAdapterResultKind.Text =>
                AdaptiveCardInvokeResponseFactory.Message(result.Message!),
            _ => throw new InvalidOperationException(
                "The Teams confirmation result is unsupported."),
        };
    }

    private async Task PresentAsync(
        ITurnContext turnContext,
        TeamsAuthenticatedContext context,
        TeamsAdapterResult result,
        CancellationToken cancellationToken)
    {
        if (result.Kind == TeamsAdapterResultKind.Text)
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

        if (result.Kind != TeamsAdapterResultKind.Card
            || result.Card is null
            || result.PreparationId is not Guid preparationId)
        {
            throw new InvalidOperationException(
                "The Teams preparation result is unsupported.");
        }

        if (cardTracker.TryGet(context.Conversation, out var current))
        {
            if (current.PreparationId == preparationId
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
            cardTracker.Set(context.Conversation, preparationId, activityId);
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

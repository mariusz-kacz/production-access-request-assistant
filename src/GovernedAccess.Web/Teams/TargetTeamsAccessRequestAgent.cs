using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.AdaptiveCards;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal sealed class TeamsAccessRequestAgent : AgentApplication
{
    private const string ConfirmAndSubmitVerb = "confirmAndSubmit";
    private const string RejectedActivityMessage =
        "This assistant accepts production-access requests only from an authenticated personal Microsoft Teams chat.";

    private readonly TeamsActorResolver actorResolver;
    private readonly TargetTeamsAccessRequestAdapter adapter;
    private readonly TeamsDraftCardTracker cardTracker;
    private readonly TeamsActivityPresenter presenter;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        TargetTeamsAccessRequestAdapter adapter,
        TeamsDraftCardTracker cardTracker,
        TeamsActivityPresenter presenter)
        : base(options)
    {
        this.actorResolver = actorResolver
            ?? throw new ArgumentNullException(nameof(actorResolver));
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.cardTracker = cardTracker
            ?? throw new ArgumentNullException(nameof(cardTracker));
        this.presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

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
            await TeamsActivityPresenter.SendTextAsync(
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
        if (result.Kind == TargetTeamsAdapterResultKind.InvalidAction)
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
            TargetTeamsAdapterResultKind.Card =>
                AdaptiveCardInvokeResponseFactory.AdaptiveCard(
                    GetCardJson(result.Card!)),
            TargetTeamsAdapterResultKind.Text =>
                AdaptiveCardInvokeResponseFactory.Message(result.Message!),
            _ => throw new InvalidOperationException(
                "The target Teams confirmation result is unsupported."),
        };
    }

    private async Task PresentAsync(
        ITurnContext turnContext,
        TeamsAuthenticatedContext context,
        TargetTeamsAdapterResult result,
        CancellationToken cancellationToken)
    {
        if (result.Kind == TargetTeamsAdapterResultKind.Text)
        {
            if (result.InvalidatesTrackedCard)
            {
                await DisableTrackedCardAsync(
                    turnContext,
                    context.Conversation,
                    cancellationToken);
            }

            await TeamsActivityPresenter.SendTextAsync(
                turnContext,
                result.Message!,
                result.InputHint,
                cancellationToken);
            return;
        }

        if (result.Kind != TargetTeamsAdapterResultKind.Card
            || result.Card is null
            || result.PreparationId is not Guid preparationId)
        {
            throw new InvalidOperationException(
                "The target Teams preparation result is unsupported.");
        }

        if (cardTracker.TryGet(context.Conversation, out var current))
        {
            if (current.PreparationId == preparationId
                && await presenter.TryUpdateAttachmentAsync(
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

        var activityId = await TeamsActivityPresenter.SendAttachmentAsync(
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

        _ = await presenter.TryUpdateAttachmentAsync(
            turnContext,
            current.ActivityId,
            TeamsAdaptiveCardRenderer.CreateStatusCard(
                new TeamsStatusCardPresentation(
                    "Draft replaced",
                    "This draft can no longer be submitted. Use the latest request draft card.")),
            cancellationToken);
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
}

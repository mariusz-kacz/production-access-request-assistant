using System.Text;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Authenticated Teams transport adapter for request preparation. This agent can
/// update preparation state and display a ready snapshot.
/// </summary>
public sealed class TeamsAccessRequestAgent : AgentApplication
{
    private const string RejectedActivityMessage =
        "This assistant accepts production-access requests only from an authenticated personal Microsoft Teams chat.";

    private const string EmptyMessageMessage =
        "Describe the temporary production access you need, including the client, environment, requested role, and operational justification.";

    private readonly TeamsActorResolver actorResolver;
    private readonly RequestPreparationService preparationService;
    private readonly PreparedRequestCardFactory cardFactory;

    public TeamsAccessRequestAgent(
        AgentApplicationOptions options,
        TeamsActorResolver actorResolver,
        RequestPreparationService preparationService,
        PreparedRequestCardFactory cardFactory)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(preparationService);
        ArgumentNullException.ThrowIfNull(cardFactory);

        this.actorResolver = actorResolver;
        this.preparationService = preparationService;
        this.cardFactory = cardFactory;
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

        var outcome = await preparationService.PrepareAsync(
            new PrepareAccessRequestCommand(
                actor,
                latestMessage,
                CreateCorrelationId()),
            cancellationToken);

        switch (outcome)
        {
            case RequestClarificationRequired clarification:
                await SendTextAsync(
                    turnContext,
                    RenderClarification(clarification.Clarification),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestCandidateRejected rejected:
                await SendTextAsync(
                    turnContext,
                    RenderCandidateRejection(rejected.ValidationErrors),
                    InputHints.ExpectingInput,
                    cancellationToken);
                return;

            case RequestReadyForConfirmation ready:
                await SendReadyCardAsync(
                    turnContext,
                    ready,
                    cancellationToken);
                return;

            case RequestPreparationFailed failed:
                await SendFailureAsync(
                    turnContext,
                    failed.Failure,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException(
                    "The request-preparation outcome is unsupported.");
        }
    }

    private async Task SendReadyCardAsync(
        ITurnContext turnContext,
        RequestReadyForConfirmation ready,
        CancellationToken cancellationToken)
    {
        var cardResult = await cardFactory.CreateAsync(
            ready.PreparedRequest,
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
}

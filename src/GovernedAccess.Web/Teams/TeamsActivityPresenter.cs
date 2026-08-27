using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal sealed partial class TeamsActivityPresenter(
    ILogger<TeamsActivityPresenter> logger)
{
    internal static async Task SendTextAsync(
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

    internal static async Task<string?> SendAttachmentAsync(
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

    internal async Task<bool> TryUpdateAttachmentAsync(
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

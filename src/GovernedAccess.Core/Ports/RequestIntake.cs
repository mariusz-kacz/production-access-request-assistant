using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Application-owned identity and conversation binding derived from an
/// authenticated channel context. Message and action payloads must not construct
/// this value.
/// </summary>
public sealed record AuthenticatedChannelActor
{
    public AuthenticatedChannelActor(
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId)
    {
        Channel = NormalizeRequired(channel, nameof(channel));
        TenantId = NormalizeRequired(tenantId, nameof(tenantId));
        ChannelActorId = NormalizeRequired(channelActorId, nameof(channelActorId));
        ConversationId = NormalizeRequired(conversationId, nameof(conversationId));
        RequesterId = NormalizeRequired(requesterId, nameof(requesterId));
    }

    public string Channel { get; }

    public string TenantId { get; }

    public string ChannelActorId { get; }

    public string ConversationId { get; }

    public string RequesterId { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Starts or continues request preparation using only an authenticated actor
/// binding, the latest message, and server correlation metadata.
/// </summary>
public sealed record PrepareAccessRequestCommand
{
    public PrepareAccessRequestCommand(
        AuthenticatedChannelActor actor,
        string latestMessage,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(latestMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Actor = actor;
        LatestMessage = latestMessage.Trim();
        CorrelationId = correlationId.Trim();
    }

    public AuthenticatedChannelActor Actor { get; }

    public string LatestMessage { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// Confirms one server-owned prepared snapshot. The command deliberately contains
/// no caller-supplied request scope, role, duration, or approval assertion.
/// </summary>
public sealed record ConfirmPreparedAccessRequestCommand
{
    public ConfirmPreparedAccessRequestCommand(
        AuthenticatedChannelActor actor,
        Guid preparationId,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preparation identifier must not be empty.",
                nameof(preparationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Actor = actor;
        PreparationId = preparationId;
        CorrelationId = correlationId.Trim();
    }

    public AuthenticatedChannelActor Actor { get; }

    public Guid PreparationId { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// Closed application result for one request-preparation turn.
/// </summary>
public abstract record RequestPreparationOutcome;

public sealed record RequestClarificationRequired : RequestPreparationOutcome
{
    public RequestClarificationRequired(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        question = question.Trim();

        if (question.Length > RequestPreparationProposal.MaximumClarificationQuestionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(question),
                question.Length,
                $"A clarification question cannot exceed {RequestPreparationProposal.MaximumClarificationQuestionLength} characters.");
        }

        Question = question;
    }

    public string Question { get; }
}

public sealed record RequestReadyForConfirmation : RequestPreparationOutcome
{
    public RequestReadyForConfirmation(PreparedAccessRequest preparedRequest)
    {
        ArgumentNullException.ThrowIfNull(preparedRequest);
        PreparedRequest = preparedRequest;
    }

    public PreparedAccessRequest PreparedRequest { get; }
}

public sealed record RequestPreparationFailed : RequestPreparationOutcome
{
    public RequestPreparationFailed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public ApplicationFailure Failure { get; }
}

/// <summary>
/// Closed application result for deterministic prepared-request confirmation.
/// </summary>
public abstract record PreparedRequestConfirmationOutcome;

public sealed record PreparedRequestConfirmationSucceeded
    : PreparedRequestConfirmationOutcome
{
    public PreparedRequestConfirmationSucceeded(
        Guid requestId,
        bool wasAlreadySubmitted)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "The submitted request identifier must not be empty.",
                nameof(requestId));
        }

        RequestId = requestId;
        WasAlreadySubmitted = wasAlreadySubmitted;
    }

    public Guid RequestId { get; }

    public bool WasAlreadySubmitted { get; }
}

public sealed record PreparedRequestConfirmationFailed
    : PreparedRequestConfirmationOutcome
{
    public PreparedRequestConfirmationFailed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public ApplicationFailure Failure { get; }
}

/// <summary>
/// Provides focused persistence operations for preparation conversations and
/// immutable prepared snapshots. A save commits every change tracked by the shared
/// persistence unit, including workflow changes staged during confirmation.
/// </summary>
public interface IRequestIntakeStore
{
    void AddConversation(RequestPreparationConversation conversation);

    Task<ApplicationResult<RequestPreparationConversation>> GetActiveConversationAsync(
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken);

    Task<ApplicationResult<RequestPreparationConversation>> GetConversationAsync(
        Guid conversationRecordId,
        CancellationToken cancellationToken);

    void AddPreparedRequest(PreparedAccessRequest preparedRequest);

    Task<ApplicationResult<PreparedAccessRequest>> GetPreparedRequestAsync(
        Guid preparationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<PreparedAccessRequest>> ReloadPreparedRequestAsync(
        Guid preparationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken);
}

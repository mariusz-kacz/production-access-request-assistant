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
/// Abandons only the active unsubmitted preparation resolved from the authenticated
/// actor and exact conversation binding. The command deliberately contains no
/// caller-selected intake identity, candidate, or lifecycle status.
/// </summary>
public sealed record ResetRequestIntakeCommand
{
    public ResetRequestIntakeCommand(
        AuthenticatedChannelActor actor,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Actor = actor;
        CorrelationId = correlationId.Trim();
    }

    public AuthenticatedChannelActor Actor { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// Confirms one server-owned prepared snapshot. The command deliberately contains
/// no caller-supplied request scope, role, duration, or approval assertion.
/// </summary>
public sealed record ConfirmRequestIntakeCommand
{
    public ConfirmRequestIntakeCommand(
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

public enum RequestPreparationResultKind
{
    ClarificationRequired,
    ReadyForConfirmation,
    CandidateRejected,
    Failed,
}

/// <summary>
/// Closed application result for one request-preparation turn. Only the payload
/// matching <see cref="Kind"/> is populated.
/// </summary>
public sealed class RequestPreparationResult
{
    private RequestPreparationResult(
        RequestPreparationResultKind kind,
        RequestClarificationProposal? clarification = null,
        RequestIntakeSession? session = null,
        IReadOnlyList<FieldValidationError>? validationErrors = null,
        ApplicationFailure? failure = null)
    {
        Kind = kind;
        Clarification = clarification;
        Session = session;
        ValidationErrors = validationErrors ?? [];
        Failure = failure;
    }

    public RequestPreparationResultKind Kind { get; }

    public RequestClarificationProposal? Clarification { get; }

    public RequestIntakeSession? Session { get; }

    public IReadOnlyList<FieldValidationError> ValidationErrors { get; }

    public ApplicationFailure? Failure { get; }

    public static RequestPreparationResult ClarificationRequired(
        RequestClarificationProposal clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        return new(
            RequestPreparationResultKind.ClarificationRequired,
            clarification: clarification);
    }

    public static RequestPreparationResult ReadyForConfirmation(
        RequestIntakeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(
            RequestPreparationResultKind.ReadyForConfirmation,
            session: session);
    }

    public static RequestPreparationResult CandidateRejected(
        IEnumerable<FieldValidationError> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);
        var errors = validationErrors.ToArray();
        if (errors.Length == 0)
        {
            throw new ArgumentException(
                "At least one candidate validation error is required.",
                nameof(validationErrors));
        }

        return new(
            RequestPreparationResultKind.CandidateRejected,
            validationErrors: Array.AsReadOnly(errors));
    }

    public static RequestPreparationResult Failed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(RequestPreparationResultKind.Failed, failure: failure);
    }
}

public enum RequestIntakeResetResultKind
{
    Reset,
    AlreadyClear,
    Failed,
}

/// <summary>
/// Closed application result for an explicit preparation reset. An affected intake
/// identity is returned only as safe operation metadata; requesters receive the same
/// guidance for <see cref="RequestIntakeResetResultKind.Reset"/> and
/// <see cref="RequestIntakeResetResultKind.AlreadyClear"/>.
/// </summary>
public sealed class RequestIntakeResetResult
{
    private RequestIntakeResetResult(
        RequestIntakeResetResultKind kind,
        Guid? intakeId,
        ApplicationFailure? failure)
    {
        Kind = kind;
        IntakeId = intakeId;
        Failure = failure;
    }

    public RequestIntakeResetResultKind Kind { get; }

    public Guid? IntakeId { get; }

    public ApplicationFailure? Failure { get; }

    public static RequestIntakeResetResult Reset(Guid intakeId)
    {
        if (intakeId == Guid.Empty)
        {
            throw new ArgumentException(
                "The reset intake identifier must not be empty.",
                nameof(intakeId));
        }

        return new(RequestIntakeResetResultKind.Reset, intakeId, failure: null);
    }

    public static RequestIntakeResetResult AlreadyClear() =>
        new(
            RequestIntakeResetResultKind.AlreadyClear,
            intakeId: null,
            failure: null);

    public static RequestIntakeResetResult Failed(
        ApplicationFailure failure,
        Guid? intakeId = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (intakeId == Guid.Empty)
        {
            throw new ArgumentException(
                "The failed reset intake identifier must not be empty.",
                nameof(intakeId));
        }

        return new(RequestIntakeResetResultKind.Failed, intakeId, failure);
    }
}

public enum RequestConfirmationResultKind
{
    Submitted,
    AlreadySubmitted,
    Failed,
}

/// <summary>
/// Closed application result for deterministic prepared-request confirmation.
/// </summary>
public sealed class RequestConfirmationResult
{
    private RequestConfirmationResult(
        RequestConfirmationResultKind kind,
        Guid requestId,
        ApplicationFailure? failure)
    {
        Kind = kind;
        RequestId = requestId;
        Failure = failure;
    }

    public RequestConfirmationResultKind Kind { get; }

    public Guid RequestId { get; }

    public ApplicationFailure? Failure { get; }

    public bool WasAlreadySubmitted =>
        Kind == RequestConfirmationResultKind.AlreadySubmitted;

    public static RequestConfirmationResult Submitted(Guid requestId) =>
        Succeeded(RequestConfirmationResultKind.Submitted, requestId);

    public static RequestConfirmationResult AlreadySubmitted(Guid requestId) =>
        Succeeded(RequestConfirmationResultKind.AlreadySubmitted, requestId);

    public static RequestConfirmationResult Failed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(RequestConfirmationResultKind.Failed, Guid.Empty, failure);
    }

    private static RequestConfirmationResult Succeeded(
        RequestConfirmationResultKind kind,
        Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "The submitted request identifier must not be empty.",
                nameof(requestId));
        }

        return new(kind, requestId, failure: null);
    }
}

/// <summary>
/// Provides focused persistence operations for preparation conversations and
/// immutable prepared snapshots. A save commits every change tracked by the shared
/// persistence unit, including workflow changes staged during confirmation.
/// </summary>
public interface IRequestIntakeStore
{
    void Add(RequestIntakeSession session);

    Task<ApplicationResult<RequestIntakeSession>> GetActiveAsync(
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken);

    Task<ApplicationResult<RequestIntakeSession>> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears stale persistence state after an optimistic-concurrency conflict and
    /// returns the already-submitted request identity only when the persisted intake
    /// still belongs to the exact authenticated actor and conversation binding.
    /// </summary>
    Task<ApplicationResult<Guid>> RecoverSubmittedRequestAsync(
        Guid sessionId,
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken);

    Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken);
}

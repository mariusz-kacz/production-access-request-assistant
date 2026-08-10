using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Drafts;

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

/// <summary>
/// Application-owned environment choice created only after an option identifier is
/// reloaded from authoritative context. Model-provided display values must never be
/// used to construct this record.
/// </summary>
public sealed record RequestEnvironmentChoice
{
    public RequestEnvironmentChoice(
        string environmentId,
        string environmentDisplayName,
        string clientId,
        string clientDisplayName)
    {
        EnvironmentId = NormalizeRequired(environmentId, nameof(environmentId));
        EnvironmentDisplayName = NormalizeRequired(
            environmentDisplayName,
            nameof(environmentDisplayName));
        ClientId = NormalizeRequired(clientId, nameof(clientId));
        ClientDisplayName = NormalizeRequired(
            clientDisplayName,
            nameof(clientDisplayName));
    }

    public string EnvironmentId { get; }

    public string EnvironmentDisplayName { get; }

    public string ClientId { get; }

    public string ClientDisplayName { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

public enum RequestPreparationResultKind
{
    DraftDiscussion,
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
        string? discussionMessage = null,
        RequestClarificationProposal? clarification = null,
        IReadOnlyList<RequestEnvironmentChoice>? environmentChoices = null,
        RequestIntakeSession? session = null,
        IReadOnlyList<FieldValidationError>? validationErrors = null,
        ApplicationFailure? failure = null)
    {
        Kind = kind;
        DiscussionMessage = discussionMessage;
        Clarification = clarification;
        EnvironmentChoices = environmentChoices ?? [];
        Session = session;
        ValidationErrors = validationErrors ?? [];
        Failure = failure;
    }

    public RequestPreparationResultKind Kind { get; }

    public string? DiscussionMessage { get; }

    public RequestClarificationProposal? Clarification { get; }

    public IReadOnlyList<RequestEnvironmentChoice> EnvironmentChoices { get; }

    public RequestIntakeSession? Session { get; }

    public IReadOnlyList<FieldValidationError> ValidationErrors { get; }

    public ApplicationFailure? Failure { get; }

    public static RequestPreparationResult DraftDiscussion(
        string message,
        RequestIntakeSession session,
        IEnumerable<RequestEnvironmentChoice>? environmentChoices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(session);
        if (session.Status != RequestIntakeStatus.Ready)
        {
            throw new ArgumentException(
                "Draft discussion requires an active ready intake.",
                nameof(session));
        }

        message = message.Trim();
        if (message.Length > RequestClarificationProposal.MaximumMessageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                message.Length,
                $"A draft-discussion message cannot exceed {RequestClarificationProposal.MaximumMessageLength} characters.");
        }

        var choices = environmentChoices?.ToArray() ?? [];
        if (choices.Length
            > RequestClarificationProposal.MaximumEnvironmentOptionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(environmentChoices),
                choices.Length,
                $"A draft discussion cannot contain more than {RequestClarificationProposal.MaximumEnvironmentOptionCount} environment choices.");
        }

        foreach (var choice in choices)
        {
            ArgumentNullException.ThrowIfNull(choice);
        }

        if (choices
                .Select(choice => choice.EnvironmentId)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != choices.Length)
        {
            throw new ArgumentException(
                "Draft-discussion environment choices must have unique identifiers.",
                nameof(environmentChoices));
        }

        return new(
            RequestPreparationResultKind.DraftDiscussion,
            discussionMessage: message,
            environmentChoices: Array.AsReadOnly(choices),
            session: session);
    }

    public static RequestPreparationResult ClarificationRequired(
        RequestClarificationProposal clarification) =>
        ClarificationRequired(clarification, []);

    public static RequestPreparationResult ClarificationRequired(
        RequestClarificationProposal clarification,
        IEnumerable<RequestEnvironmentChoice> environmentChoices)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        ArgumentNullException.ThrowIfNull(environmentChoices);

        var choiceSnapshot = environmentChoices.ToArray();
        if (choiceSnapshot.Length
            > RequestClarificationProposal.MaximumEnvironmentOptionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(environmentChoices),
                choiceSnapshot.Length,
                $"A clarification cannot contain more than {RequestClarificationProposal.MaximumEnvironmentOptionCount} environment choices.");
        }

        foreach (var choice in choiceSnapshot)
        {
            ArgumentNullException.ThrowIfNull(choice);
        }

        var choiceIds = choiceSnapshot
            .Select(choice => choice.EnvironmentId)
            .ToArray();
        if (choiceIds.Distinct(StringComparer.Ordinal).Count() != choiceIds.Length)
        {
            throw new ArgumentException(
                "Environment choices must have unique identifiers.",
                nameof(environmentChoices));
        }

        if (clarification.Target != RequestClarificationTarget.EnvironmentId
            && choiceSnapshot.Length > 0)
        {
            throw new ArgumentException(
                "Only an environment clarification can contain environment choices.",
                nameof(environmentChoices));
        }

        if (choiceIds.Length != clarification.EnvironmentOptionIds.Count
            || choiceIds.Except(
                    clarification.EnvironmentOptionIds,
                    StringComparer.Ordinal)
                .Any())
        {
            throw new ArgumentException(
                "Validated environment choices must match the proposed option identifiers.",
                nameof(environmentChoices));
        }

        return new(
            RequestPreparationResultKind.ClarificationRequired,
            clarification: clarification,
            environmentChoices: Array.AsReadOnly(choiceSnapshot));
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

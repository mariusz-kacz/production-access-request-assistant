using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

/// <summary>
/// Confirms one persisted, immutable prepared request under its authenticated
/// channel binding. Confirmation remains deterministic and never invokes an AI
/// provider or model-visible tool.
/// </summary>
public sealed class PreparedRequestConfirmationService
{
    public const string ForbiddenCode = "prepared_request_forbidden";
    public const string ExpiredCode = "prepared_request_expired";
    public const string SupersededCode = "prepared_request_superseded";
    public const string InvalidatedCode = "prepared_request_invalidated";
    public const string NotReadyCode = "prepared_request_not_ready";
    public const string ConversationMissingCode =
        "prepared_request_conversation_missing";
    public const string ConversationMismatchCode =
        "prepared_request_conversation_mismatch";
    public const string ScopeMismatchCode = "prepared_request_scope_mismatch";
    public const string SubmissionEvidenceInvalidCode =
        "prepared_request_submission_evidence_invalid";

    private readonly IRequestIntakeStore intakeStore;
    private readonly RequestSubmissionService submissionService;
    private readonly IClock clock;

    public PreparedRequestConfirmationService(
        IRequestIntakeStore intakeStore,
        RequestSubmissionService submissionService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(intakeStore);
        ArgumentNullException.ThrowIfNull(submissionService);
        ArgumentNullException.ThrowIfNull(clock);

        this.intakeStore = intakeStore;
        this.submissionService = submissionService;
        this.clock = clock;
    }

    public async Task<PreparedRequestConfirmationOutcome> ConfirmAsync(
        ConfirmPreparedAccessRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var preparedResult = await intakeStore.ReloadPreparedRequestAsync(
            command.PreparationId,
            cancellationToken);
        if (preparedResult.IsFailure)
        {
            return Failed(preparedResult.Failure!);
        }

        var preparedRequest = preparedResult.Value;
        if (!preparedRequest.IsOwnedBy(
                command.Actor.Channel,
                command.Actor.TenantId,
                command.Actor.ChannelActorId,
                command.Actor.ConversationId,
                command.Actor.RequesterId))
        {
            return Failed(
                ApplicationFailureKind.Unauthorized,
                ForbiddenCode,
                "The authenticated channel actor does not own this prepared request.");
        }

        var statusOutcome = GetStatusOutcome(preparedRequest);
        if (statusOutcome is not null)
        {
            return statusOutcome;
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (preparedRequest.IsExpired(occurredAt))
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ExpiredCode,
                "The prepared request has expired and cannot be confirmed.");
        }

        var conversationResult = await intakeStore.GetConversationAsync(
            preparedRequest.ConversationRecordId,
            cancellationToken);
        if (conversationResult.IsFailure)
        {
            return conversationResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed(
                    ApplicationFailureKind.DependencyFailure,
                    ConversationMissingCode,
                    "The persisted preparation conversation is unavailable.")
                : Failed(conversationResult.Failure);
        }

        var conversation = conversationResult.Value;
        if (!MatchesConversation(
                conversation,
                command.Actor,
                preparedRequest.PreparationId))
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ConversationMismatchCode,
                "The persisted preparation conversation does not match the confirmation.");
        }

        var submissionOutcome =
            await submissionService.StagePreparedConfirmationAsync(
                preparedRequest,
                command.CorrelationId,
                occurredAt,
                cancellationToken);

        if (submissionOutcome is RequestSubmissionValidationRejected)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                InvalidatedCode,
                "Authoritative request context no longer accepts the prepared scope.");
        }

        if (submissionOutcome is RequestSubmissionFailed submissionFailed)
        {
            return Failed(submissionFailed.Failure);
        }

        if (submissionOutcome is not RequestSubmitted submitted)
        {
            throw new InvalidOperationException(
                "The prepared request submission outcome is unsupported.");
        }

        if (!MatchesPreparedScope(preparedRequest, submitted.Request))
        {
            return Failed(
                ApplicationFailureKind.DependencyFailure,
                ScopeMismatchCode,
                "The staged request does not match the immutable prepared scope.");
        }

        preparedRequest.MarkSubmitted(occurredAt);
        conversation.MarkSubmitted(occurredAt, command.CorrelationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? Failed(saveResult.Failure!)
            : new PreparedRequestConfirmationSucceeded(
                submitted.Request.Id,
                wasAlreadySubmitted: false);
    }

    private static PreparedRequestConfirmationOutcome? GetStatusOutcome(
        PreparedAccessRequest preparedRequest)
    {
        return preparedRequest.Status switch
        {
            PreparedAccessRequestStatus.Ready => null,
            PreparedAccessRequestStatus.Submitted =>
                preparedRequest.SubmittedRequestId is Guid submittedRequestId
                    && submittedRequestId == preparedRequest.ReservedRequestId
                    ? new PreparedRequestConfirmationSucceeded(
                        submittedRequestId,
                        wasAlreadySubmitted: true)
                    : Failed(
                        ApplicationFailureKind.DependencyFailure,
                        SubmissionEvidenceInvalidCode,
                        "Persisted submission evidence is incomplete."),
            PreparedAccessRequestStatus.Superseded => Failed(
                ApplicationFailureKind.InvalidTransition,
                SupersededCode,
                "The prepared request was superseded and cannot be confirmed."),
            PreparedAccessRequestStatus.Expired => Failed(
                ApplicationFailureKind.InvalidTransition,
                ExpiredCode,
                "The prepared request has expired and cannot be confirmed."),
            PreparedAccessRequestStatus.Invalidated => Failed(
                ApplicationFailureKind.InvalidTransition,
                InvalidatedCode,
                "The prepared request is invalidated and cannot be confirmed."),
            _ => Failed(
                ApplicationFailureKind.InvalidTransition,
                NotReadyCode,
                "The prepared request is not ready for confirmation."),
        };
    }

    private static bool MatchesConversation(
        RequestPreparationConversation conversation,
        AuthenticatedChannelActor actor,
        Guid preparationId)
    {
        return conversation.Status == RequestPreparationConversationStatus.Ready
            && conversation.ActivePreparationId == preparationId
            && Matches(conversation.Channel, actor.Channel)
            && Matches(conversation.TenantId, actor.TenantId)
            && Matches(conversation.ChannelActorId, actor.ChannelActorId)
            && Matches(conversation.ConversationId, actor.ConversationId)
            && Matches(conversation.RequesterId, actor.RequesterId);
    }

    private static bool MatchesPreparedScope(
        PreparedAccessRequest preparedRequest,
        AccessRequest request)
    {
        return request.Id == preparedRequest.ReservedRequestId
            && Matches(request.RequesterId, preparedRequest.RequesterId)
            && Matches(request.ClientId, preparedRequest.ClientId)
            && Matches(request.EnvironmentId, preparedRequest.EnvironmentId)
            && Matches(request.RequestedRoleId, preparedRequest.RequestedRoleId)
            && Matches(request.Justification, preparedRequest.Justification)
            && MatchesOptional(request.IncidentId, preparedRequest.IncidentId)
            && request.Status == RequestStatus.AwaitingBusinessApproval;
    }

    private static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool MatchesOptional(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static PreparedRequestConfirmationFailed Failed(
        ApplicationFailure failure) =>
        new(failure);

    private static PreparedRequestConfirmationFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        new(new ApplicationFailure(kind, code, message));
}

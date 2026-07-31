using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

/// <summary>
/// Coordinates one channel-neutral request-preparation turn. Interpreter proposals
/// remain untrusted until the application validator accepts and canonicalizes them.
/// </summary>
public sealed class RequestIntakeService
{
    public const string ConversationNotCollectingCode =
        "request_preparation_not_collecting";

    public const string MalformedModelOutputCode =
        "request_preparation_model_output_malformed";

    public const string ModelTimeoutCode =
        "request_preparation_model_timeout";

    public const string ModelCancelledCode =
        "request_preparation_model_cancelled";

    public const string ModelUnavailableCode =
        "request_preparation_model_unavailable";

    public const string ForbiddenCode = "prepared_request_forbidden";
    public const string ExpiredCode = "prepared_request_expired";
    public const string SupersededCode = "prepared_request_superseded";
    public const string InvalidatedCode = "prepared_request_invalidated";
    public const string NotReadyCode = "prepared_request_not_ready";
    public const string ScopeMismatchCode = "prepared_request_scope_mismatch";
    public const string SubmissionEvidenceInvalidCode =
        "prepared_request_submission_evidence_invalid";

    private readonly IRequestPreparationInterpreter interpreter;
    private readonly RequestValidator requestValidator;
    private readonly IRequestIntakeStore intakeStore;
    private readonly RequestSubmissionService submissionService;
    private readonly IClock clock;

    public RequestIntakeService(
        IRequestPreparationInterpreter interpreter,
        RequestValidator requestValidator,
        IRequestIntakeStore intakeStore,
        RequestSubmissionService submissionService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(intakeStore);
        ArgumentNullException.ThrowIfNull(submissionService);
        ArgumentNullException.ThrowIfNull(clock);

        this.interpreter = interpreter;
        this.requestValidator = requestValidator;
        this.intakeStore = intakeStore;
        this.submissionService = submissionService;
        this.clock = clock;
    }

    public async Task<RequestPreparationResult> PrepareAsync(
        PrepareAccessRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sessionResult = await intakeStore.GetActiveAsync(
            command.Actor,
            cancellationToken);
        if (sessionResult.IsFailure
            && sessionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return RequestPreparationResult.Failed(sessionResult.Failure);
        }

        var session = sessionResult.IsSuccess
            ? sessionResult.Value
            : CreateSession(command);

        if (session.Status == RequestIntakeStatus.Ready)
        {
            session.MarkSuperseded(
                clock.UtcNow,
                command.CorrelationId);
            var supersessionSave = await intakeStore.SaveChangesAsync(
                cancellationToken);
            if (supersessionSave.IsFailure)
            {
                return RequestPreparationResult.Failed(
                    supersessionSave.Failure!);
            }

            session = CreateSession(command);
        }
        else if (session.Status != RequestIntakeStatus.Collecting)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ConversationNotCollectingCode,
                "The active preparation is not collecting request details.");
        }

        var interpretation = await interpreter.InterpretAsync(
            new RequestPreparationTurn(
                session.Id,
                command.LatestMessage,
                ToCandidate(session),
                validationFeedback: [],
                command.CorrelationId),
            cancellationToken);

        if (interpretation.Kind != RequestPreparationInterpretationOutcomeKind.Proposal)
        {
            return MapInterpretationFailure(interpretation.Kind);
        }

        var proposal = interpretation.Proposal
            ?? throw new InvalidOperationException(
                "A successful preparation interpretation must contain a proposal.");

        var candidate = proposal.Candidate;
        var validation = await requestValidator.ValidateAsync(
            new RequestValidationInput(
                candidate.ClientId,
                candidate.EnvironmentId,
                candidate.RequestedRoleId,
                candidate.Justification,
                candidate.IncidentId),
            cancellationToken);

        if (validation is RequestValidationFailed validationFailed)
        {
            return RequestPreparationResult.Failed(validationFailed.Failure);
        }

        if (validation is RequestValidationSucceeded validationSucceeded)
        {
            return await PersistReadyAsync(
                session,
                validationSucceeded.Fields,
                command.CorrelationId,
                cancellationToken);
        }

        if (proposal.Kind == RequestPreparationProposalKind.Clarification)
        {
            return await PersistClarificationAsync(
                session,
                candidate,
                proposal.Clarification!,
                command.CorrelationId,
                cancellationToken);
        }

        if (validation is RequestValidationRejected validationRejected)
        {
            return RequestPreparationResult.CandidateRejected(
                validationRejected.Errors);
        }

        throw new InvalidOperationException(
            "The request validation outcome is unsupported.");
    }

    private async Task<RequestPreparationResult> PersistReadyAsync(
        RequestIntakeSession session,
        ValidatedRequestFields fields,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = clock.UtcNow.ToUniversalTime();
        session.UpdateCandidate(
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.Justification,
            fields.IncidentId,
            occurredAt,
            correlationId);
        session.MarkReady(
            Guid.NewGuid(),
            occurredAt,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.ReadyForConfirmation(session);
    }

    public async Task<RequestConfirmationResult> ConfirmAsync(
        ConfirmRequestIntakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sessionResult = await intakeStore.GetAsync(
            command.PreparationId,
            cancellationToken);
        if (sessionResult.IsFailure)
        {
            return ConfirmationFailed(sessionResult.Failure!);
        }

        var session = sessionResult.Value;
        if (!session.IsOwnedBy(
                command.Actor.Channel,
                command.Actor.TenantId,
                command.Actor.ChannelActorId,
                command.Actor.ConversationId,
                command.Actor.RequesterId))
        {
            return ConfirmationFailed(
                ApplicationFailureKind.Unauthorized,
                ForbiddenCode,
                "The authenticated channel actor does not own this intake.");
        }

        var statusResult = GetStatusResult(session);
        if (statusResult is not null)
        {
            return statusResult;
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (session.IsExpired(occurredAt))
        {
            return ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                ExpiredCode,
                "The prepared request has expired and cannot be confirmed.");
        }

        if (session.ReservedRequestId is not { } reservedRequestId
            || session.ClientId is null
            || session.EnvironmentId is null
            || session.RequestedRoleId is null
            || session.Justification is null)
        {
            return ConfirmationFailed(
                ApplicationFailureKind.DependencyFailure,
                SubmissionEvidenceInvalidCode,
                "Persisted intake evidence is incomplete.");
        }

        var submission = await submissionService.StageAsync(
            session.RequesterId,
            new RequestValidationInput(
                session.ClientId,
                session.EnvironmentId,
                session.RequestedRoleId,
                session.Justification,
                session.IncidentId),
            reservedRequestId,
            command.CorrelationId,
            occurredAt,
            cancellationToken);

        if (submission is RequestSubmissionValidationRejected)
        {
            return ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                InvalidatedCode,
                "Authoritative context no longer accepts the prepared scope.");
        }

        if (submission is RequestSubmissionFailed submissionFailed)
        {
            return ConfirmationFailed(submissionFailed.Failure);
        }

        if (submission is not RequestSubmitted submitted)
        {
            throw new InvalidOperationException(
                "The intake submission outcome is unsupported.");
        }

        if (!MatchesScope(session, submitted.Request))
        {
            return ConfirmationFailed(
                ApplicationFailureKind.DependencyFailure,
                ScopeMismatchCode,
                "The staged request does not match the immutable intake scope.");
        }

        session.MarkSubmitted(occurredAt, command.CorrelationId);
        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? ConfirmationFailed(saveResult.Failure!)
            : RequestConfirmationResult.Submitted(submitted.Request.Id);
    }

    private static RequestConfirmationResult? GetStatusResult(
        RequestIntakeSession session) =>
        session.Status switch
        {
            RequestIntakeStatus.Ready => null,
            RequestIntakeStatus.Submitted =>
                session.ReservedRequestId is { } requestId
                    ? RequestConfirmationResult.AlreadySubmitted(requestId)
                    : ConfirmationFailed(
                        ApplicationFailureKind.DependencyFailure,
                        SubmissionEvidenceInvalidCode,
                        "Persisted submission evidence is incomplete."),
            RequestIntakeStatus.Superseded => ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                SupersededCode,
                "The prepared request was superseded and cannot be confirmed."),
            RequestIntakeStatus.Expired => ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                ExpiredCode,
                "The prepared request has expired and cannot be confirmed."),
            RequestIntakeStatus.Invalidated => ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                InvalidatedCode,
                "The prepared request is invalidated and cannot be confirmed."),
            _ => ConfirmationFailed(
                ApplicationFailureKind.InvalidTransition,
                NotReadyCode,
                "The prepared request is not ready for confirmation."),
        };

    private RequestIntakeSession CreateSession(
        PrepareAccessRequestCommand command)
    {
        var actor = command.Actor;
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            actor.Channel,
            actor.TenantId,
            actor.ChannelActorId,
            actor.ConversationId,
            actor.RequesterId,
            clock.UtcNow,
            command.CorrelationId);
        intakeStore.Add(session);
        return session;
    }

    private async Task<RequestPreparationResult> PersistClarificationAsync(
        RequestIntakeSession session,
        RequestCandidate candidate,
        RequestClarificationProposal clarification,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        session.UpdateCandidate(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification,
            candidate.IncidentId,
            clock.UtcNow,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.ClarificationRequired(clarification);
    }

    private static RequestCandidate ToCandidate(
        RequestIntakeSession session) =>
        new(
            session.ClientId,
            session.EnvironmentId,
            session.RequestedRoleId,
            session.Justification,
            session.IncidentId);

    private static RequestPreparationResult MapInterpretationFailure(
        RequestPreparationInterpretationOutcomeKind kind) =>
        kind switch
        {
            RequestPreparationInterpretationOutcomeKind.MalformedModelOutput => Failed(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant returned an invalid response."),
            RequestPreparationInterpretationOutcomeKind.Timeout => Failed(
                ApplicationFailureKind.Timeout,
                ModelTimeoutCode,
                "Request preparation timed out."),
            RequestPreparationInterpretationOutcomeKind.Cancelled => Failed(
                ApplicationFailureKind.Cancelled,
                ModelCancelledCode,
                "Request preparation was cancelled."),
            RequestPreparationInterpretationOutcomeKind.Unavailable => Failed(
                ApplicationFailureKind.DependencyUnavailable,
                ModelUnavailableCode,
                "The request assistant is unavailable."),
            RequestPreparationInterpretationOutcomeKind.Proposal =>
                throw new InvalidOperationException(
                    "A proposal is not an interpretation failure."),
            _ => throw new InvalidOperationException(
                "The preparation interpretation outcome is unsupported."),
        };

    private static bool MatchesScope(
        RequestIntakeSession session,
        AccessRequest request) =>
        request.Id == session.ReservedRequestId
        && string.Equals(
            request.RequesterId,
            session.RequesterId,
            StringComparison.Ordinal)
        && string.Equals(request.ClientId, session.ClientId, StringComparison.Ordinal)
        && string.Equals(
            request.EnvironmentId,
            session.EnvironmentId,
            StringComparison.Ordinal)
        && string.Equals(
            request.RequestedRoleId,
            session.RequestedRoleId,
            StringComparison.Ordinal)
        && string.Equals(
            request.Justification,
            session.Justification,
            StringComparison.Ordinal)
        && string.Equals(
            request.IncidentId,
            session.IncidentId,
            StringComparison.Ordinal)
        && request.Status == RequestStatus.AwaitingBusinessApproval;

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailure failure) =>
        RequestConfirmationResult.Failed(failure);

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        RequestConfirmationResult.Failed(
            new ApplicationFailure(kind, code, message));

    private static RequestPreparationResult Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        RequestPreparationResult.Failed(
            new ApplicationFailure(kind, code, message));
}

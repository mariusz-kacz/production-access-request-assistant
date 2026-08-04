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
    private readonly IRequestContextReader requestContext;
    private readonly IRequestIntakeStore intakeStore;
    private readonly RequestSubmissionService submissionService;
    private readonly IClock clock;

    public RequestIntakeService(
        IRequestPreparationInterpreter interpreter,
        RequestValidator requestValidator,
        IRequestContextReader requestContext,
        IRequestIntakeStore intakeStore,
        RequestSubmissionService submissionService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(intakeStore);
        ArgumentNullException.ThrowIfNull(submissionService);
        ArgumentNullException.ThrowIfNull(clock);

        this.interpreter = interpreter;
        this.requestValidator = requestValidator;
        this.requestContext = requestContext;
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
            var occurredAt = clock.UtcNow.ToUniversalTime();
            if (session.IsExpired(occurredAt))
            {
                session.MarkExpired(occurredAt, command.CorrelationId);
            }
            else
            {
                session.MarkSuperseded(occurredAt, command.CorrelationId);
            }

            var lifecycleSave = await intakeStore.SaveChangesAsync(
                cancellationToken);
            if (lifecycleSave.IsFailure)
            {
                return RequestPreparationResult.Failed(
                    lifecycleSave.Failure!);
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
                command.CorrelationId),
            cancellationToken);

        if (interpretation is RequestPreparationInterpretationFailed failed)
        {
            return MapInterpretationFailure(failed.Failure);
        }

        if (interpretation is not RequestPreparationInterpretationSucceeded succeeded)
        {
            throw new InvalidOperationException(
                "The request preparation interpretation result is unsupported.");
        }

        var proposal = succeeded.Proposal;
        var assessmentResult =
            await requestValidator.AssessCandidateAsync(
                proposal.Candidate,
                cancellationToken);
        if (assessmentResult.IsFailure)
        {
            return RequestPreparationResult.Failed(
                assessmentResult.Failure!);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentRejected rejected)
        {
            return await PersistRejectedCandidateAsync(
                session,
                rejected.Candidate,
                rejected.Errors,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentReady ready)
        {
            return await PersistReadyAsync(
                session,
                ready.Fields,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
                is RequestCandidateAssessmentIncomplete incomplete
            && proposal.Kind == RequestPreparationProposalKind.Clarification
            && IsClarificationUnresolved(
                incomplete.Candidate,
                proposal.Clarification!.Target))
        {
            return await PersistClarificationAsync(
                session,
                incomplete.Candidate,
                proposal.Clarification,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentIncomplete rejectedIncomplete)
        {
            return await PersistRejectedCandidateAsync(
                session,
                rejectedIncomplete.Candidate,
                rejectedIncomplete.Errors,
                command.CorrelationId,
                cancellationToken);
        }

        throw new InvalidOperationException(
            "The request candidate assessment is unsupported.");
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

    public async Task<RequestIntakeResetResult> ResetAsync(
        ResetRequestIntakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sessionResult = await intakeStore.GetActiveAsync(
            command.Actor,
            cancellationToken);
        if (sessionResult.IsFailure)
        {
            return sessionResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? RequestIntakeResetResult.AlreadyClear()
                : RequestIntakeResetResult.Failed(sessionResult.Failure);
        }

        var session = sessionResult.Value;
        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (session.IsExpired(occurredAt))
        {
            session.MarkExpired(occurredAt, command.CorrelationId);
        }
        else
        {
            session.MarkSuperseded(occurredAt, command.CorrelationId);
        }

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestIntakeResetResult.Failed(
                saveResult.Failure!,
                session.Id)
            : RequestIntakeResetResult.Reset(session.Id);
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
            session.MarkExpired(occurredAt, command.CorrelationId);
            var expirySave = await intakeStore.SaveChangesAsync(cancellationToken);
            if (expirySave.IsFailure)
            {
                return ConfirmationFailed(expirySave.Failure!);
            }

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
            session.MarkInvalidated(occurredAt, command.CorrelationId);
            var invalidationSave = await intakeStore.SaveChangesAsync(
                cancellationToken);
            if (invalidationSave.IsFailure)
            {
                return ConfirmationFailed(invalidationSave.Failure!);
            }

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
        if (saveResult.IsSuccess)
        {
            return RequestConfirmationResult.Submitted(submitted.Request.Id);
        }

        var saveFailure = saveResult.Failure!;
        if (saveFailure.Kind is not (
                ApplicationFailureKind.ConcurrencyConflict
                or ApplicationFailureKind.DependencyFailure))
        {
            return ConfirmationFailed(saveFailure);
        }

        var recovery = await intakeStore.RecoverSubmittedRequestAsync(
            command.PreparationId,
            command.Actor,
            cancellationToken);
        if (recovery.IsSuccess)
        {
            return RequestConfirmationResult.AlreadySubmitted(recovery.Value);
        }

        return saveFailure.Kind == ApplicationFailureKind.ConcurrencyConflict
            ? ConfirmationFailed(recovery.Failure!)
            : ConfirmationFailed(saveFailure);
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

        var environmentChoices = new List<RequestEnvironmentChoice>(
            clarification.EnvironmentOptionIds.Count);
        foreach (var environmentOptionId in clarification.EnvironmentOptionIds)
        {
            var contextResult =
                await requestContext.GetProductionEnvironmentContextAsync(
                    environmentOptionId,
                    cancellationToken);
            if (contextResult.IsFailure)
            {
                if (contextResult.Failure!.Kind
                    != ApplicationFailureKind.NotFound)
                {
                    return RequestPreparationResult.Failed(
                        contextResult.Failure);
                }

                return await PersistRejectedCandidateAsync(
                    session,
                    candidate,
                    [InvalidEnvironmentOptionError()],
                    correlationId,
                    cancellationToken);
            }

            var context = contextResult.Value;
            if (!string.Equals(
                    context.Environment.Id,
                    environmentOptionId,
                    StringComparison.Ordinal))
            {
                return await PersistRejectedCandidateAsync(
                    session,
                    candidate,
                    [InvalidEnvironmentOptionError()],
                    correlationId,
                    cancellationToken);
            }

            environmentChoices.Add(
                new RequestEnvironmentChoice(
                    context.Environment.Id,
                    context.Environment.DisplayName,
                    context.Client.Id,
                    context.Client.DisplayName));
        }

        environmentChoices.Sort(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.EnvironmentId,
                right.EnvironmentId));

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
            : RequestPreparationResult.ClarificationRequired(
                clarification,
                environmentChoices);
    }

    private static FieldValidationError InvalidEnvironmentOptionError() =>
        new(
            "environmentOptionIds",
            "environment_option_not_found",
            "A proposed production environment option does not exist.");

    private async Task<RequestPreparationResult> PersistRejectedCandidateAsync(
        RequestIntakeSession session,
        RequestCandidate candidate,
        IReadOnlyList<FieldValidationError> validationErrors,
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
            : RequestPreparationResult.CandidateRejected(validationErrors);
    }

    private static RequestCandidate ToCandidate(
        RequestIntakeSession session) =>
        new(
            session.ClientId,
            session.EnvironmentId,
            session.RequestedRoleId,
            session.Justification,
            session.IncidentId);

    private static bool IsClarificationUnresolved(
        RequestCandidate candidate,
        RequestClarificationTarget target) =>
        target switch
        {
            RequestClarificationTarget.EnvironmentId =>
                candidate.EnvironmentId is null,
            RequestClarificationTarget.RequestedRoleId =>
                candidate.RequestedRoleId is null,
            RequestClarificationTarget.Justification =>
                candidate.Justification is null,
            RequestClarificationTarget.IncidentId => candidate.IncidentId is null,
            _ => throw new InvalidOperationException(
                "The clarification target is unsupported."),
        };

    private static RequestPreparationResult MapInterpretationFailure(
        RequestPreparationInterpretationFailure failure) =>
        failure switch
        {
            RequestPreparationInterpretationFailure.MalformedModelOutput => Failed(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant returned an invalid response."),
            RequestPreparationInterpretationFailure.Timeout => Failed(
                ApplicationFailureKind.Timeout,
                ModelTimeoutCode,
                "Request preparation timed out."),
            RequestPreparationInterpretationFailure.Cancelled => Failed(
                ApplicationFailureKind.Cancelled,
                ModelCancelledCode,
                "Request preparation was cancelled."),
            RequestPreparationInterpretationFailure.Unavailable => Failed(
                ApplicationFailureKind.DependencyUnavailable,
                ModelUnavailableCode,
                "The request assistant is unavailable."),
            _ => throw new InvalidOperationException(
                "The preparation interpretation failure is unsupported."),
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

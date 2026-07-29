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

        if (session.Status != RequestIntakeStatus.Collecting)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ConversationNotCollectingCode,
                "The active preparation is not collecting request details.");
        }

        var interpretation = await interpreter.InterpretAsync(
            new RequestPreparationTurn(
                command.LatestMessage,
                ToCandidate(session),
                session.PendingClarification,
                command.CorrelationId),
            cancellationToken);

        if (interpretation.Kind != RequestPreparationInterpretationOutcomeKind.Proposal)
        {
            return MapInterpretationFailure(interpretation.Kind);
        }

        var proposal = interpretation.Proposal
            ?? throw new InvalidOperationException(
                "A successful preparation interpretation must contain a proposal.");

        if (proposal.Kind == RequestPreparationProposalKind.Clarification)
        {
            var clarificationResult = await CanonicalizeClarificationAsync(
                proposal.Clarification!,
                proposal.Candidate,
                cancellationToken);
            if (clarificationResult.IsFailure)
            {
                return RequestPreparationResult.Failed(
                    clarificationResult.Failure!);
            }

            return await PersistClarificationAsync(
                session,
                proposal.Candidate,
                clarificationResult.Value,
                command.CorrelationId,
                cancellationToken);
        }

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

        if (validation is RequestValidationRejected validationRejected)
        {
            return RequestPreparationResult.CandidateRejected(
                validationRejected.Errors);
        }

        if (validation is not RequestValidationSucceeded validationSucceeded)
        {
            throw new InvalidOperationException(
                "The request validation outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var fields = validationSucceeded.Fields;
        session.UpdateCandidate(
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.Justification,
            fields.IncidentId,
            pendingClarification: null,
            occurredAt,
            command.CorrelationId);
        session.MarkReady(
            Guid.NewGuid(),
            occurredAt,
            command.CorrelationId);

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
        RequestClarificationContext clarification,
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
            clarification,
            clock.UtcNow,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.ClarificationRequired(clarification);
    }

    private async Task<ApplicationResult<RequestClarificationContext>>
        CanonicalizeClarificationAsync(
            RequestClarificationContext clarification,
            RequestCandidate candidate,
            CancellationToken cancellationToken)
    {
        var canonicalOptions = new List<RequestClarificationOption>(
            clarification.Options.Count);
        foreach (var option in clarification.Options)
        {
            var optionResult = await CanonicalizeOptionAsync(
                clarification.Target,
                option.Value,
                candidate,
                cancellationToken);
            if (optionResult.IsFailure)
            {
                return ApplicationResult.Failed<RequestClarificationContext>(
                    optionResult.Failure!);
            }

            canonicalOptions.Add(optionResult.Value);
        }

        return ApplicationResult.Succeeded(
            new RequestClarificationContext(
                clarification.Target,
                clarification.Prompt,
                canonicalOptions));
    }

    private async Task<ApplicationResult<RequestClarificationOption>>
        CanonicalizeOptionAsync(
            RequestClarificationTarget target,
            string proposedValue,
            RequestCandidate candidate,
            CancellationToken cancellationToken)
    {
        switch (target)
        {
            case RequestClarificationTarget.ClientId:
                {
                    var result = await requestContext.GetClientAsync(
                        proposedValue,
                        cancellationToken);
                    return result.IsFailure
                        ? FailedOption(result.Failure!)
                        : CanonicalOption(result.Value.Id, result.Value.DisplayName);
                }

            case RequestClarificationTarget.EnvironmentId:
                {
                    var result = await requestContext.GetProductionEnvironmentAsync(
                        proposedValue,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return FailedOption(result.Failure!);
                    }

                    var environment = result.Value;
                    if (candidate.ClientId is not null
                        && !string.Equals(
                            environment.ClientId,
                            candidate.ClientId,
                            StringComparison.Ordinal))
                    {
                        return InvalidOption();
                    }

                    return CanonicalOption(environment.Id, environment.DisplayName);
                }

            case RequestClarificationTarget.RequestedRoleId:
                {
                    if (candidate.EnvironmentId is null)
                    {
                        return InvalidOption();
                    }

                    var result = await requestContext.GetEnvironmentRoleAsync(
                        candidate.EnvironmentId,
                        proposedValue,
                        cancellationToken);
                    return result.IsFailure
                        ? FailedOption(result.Failure!)
                        : CanonicalOption(
                            result.Value.RoleId,
                            GetRoleDisplayName(result.Value.RoleId));
                }

            case RequestClarificationTarget.IncidentId:
                {
                    var result = await requestContext.GetIncidentAsync(
                        proposedValue,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return FailedOption(result.Failure!);
                    }

                    var incident = result.Value;
                    var matchesCandidate = incident.Status == IncidentStatus.Active
                        && (candidate.ClientId is null
                            || string.Equals(
                                incident.ClientId,
                                candidate.ClientId,
                                StringComparison.Ordinal))
                        && (candidate.EnvironmentId is null
                            || incident.EnvironmentId is null
                            || string.Equals(
                                incident.EnvironmentId,
                                candidate.EnvironmentId,
                                StringComparison.Ordinal));
                    return matchesCandidate
                        ? CanonicalOption(incident.Id, incident.Title)
                        : InvalidOption();
                }

            case RequestClarificationTarget.Justification:
                return InvalidOption();

            default:
                throw new InvalidOperationException(
                    "The clarification target is unsupported.");
        }
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

    private static ApplicationResult<RequestClarificationOption> CanonicalOption(
        string value,
        string label) =>
        ApplicationResult.Succeeded(new RequestClarificationOption(value, label));

    private static ApplicationResult<RequestClarificationOption> FailedOption(
        ApplicationFailure failure) =>
        failure.Kind == ApplicationFailureKind.NotFound
            ? InvalidOption()
            : ApplicationResult.Failed<RequestClarificationOption>(failure);

    private static ApplicationResult<RequestClarificationOption> InvalidOption() =>
        ApplicationResult.Failed<RequestClarificationOption>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant proposed an invalid clarification option."));

    private static string GetRoleDisplayName(string roleId) =>
        roleId switch
        {
            ProductionRoleIds.ReadOnly => "Production read-only",
            ProductionRoleIds.Support => "Production support",
            _ => throw new InvalidOperationException(
                "The authoritative role identifier is unsupported."),
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

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Drafts;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public abstract record RequestSubmissionOutcome;

public sealed record RequestSubmitted(AccessRequest Request)
    : RequestSubmissionOutcome;

public sealed record RequestSubmissionValidationRejected : RequestSubmissionOutcome
{
    public RequestSubmissionValidationRejected(
        IEnumerable<FieldValidationError> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);

        var errors = validationErrors.ToArray();
        if (errors.Length == 0)
        {
            throw new ArgumentException(
                "At least one field validation error is required.",
                nameof(validationErrors));
        }

        ValidationErrors = Array.AsReadOnly(errors);
    }

    public IReadOnlyList<FieldValidationError> ValidationErrors { get; }
}

public sealed record RequestSubmissionFailed(ApplicationFailure Failure)
    : RequestSubmissionOutcome;

/// <summary>
/// Confirms an owned ready draft, revalidates its scope, and atomically persists the
/// immutable request, audit evidence, and terminal draft transition.
/// </summary>
public sealed class RequestSubmissionService
{
    public const string ForbiddenCode = "prepared_request_forbidden";
    public const string ExpiredCode = "prepared_request_expired";
    public const string SupersededCode = "prepared_request_superseded";
    public const string InvalidatedCode = "prepared_request_invalidated";
    public const string NotReadyCode = "prepared_request_not_ready";
    public const string ScopeMismatchCode = "prepared_request_scope_mismatch";
    public const string SubmissionEvidenceInvalidCode =
        "prepared_request_submission_evidence_invalid";

    private readonly AccessRequestValidator requestValidator;
    private readonly IRequestContextReader requestContext;
    private readonly IRequestIntakeStore intakeStore;
    private readonly IWorkflowStore workflowStore;
    private readonly IClock clock;

    public RequestSubmissionService(
        AccessRequestValidator requestValidator,
        IRequestContextReader requestContext,
        IRequestIntakeStore intakeStore,
        IWorkflowStore workflowStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(intakeStore);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestValidator = requestValidator;
        this.requestContext = requestContext;
        this.intakeStore = intakeStore;
        this.workflowStore = workflowStore;
        this.clock = clock;
    }

    public async Task<RequestConfirmationResult> ConfirmDraftAsync(
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

        var submission = await CreateAndStageValidatedRequestAsync(
            session.RequesterId,
            new AccessRequestValidationInput(
                session.ClientId,
                session.EnvironmentId,
                session.RequestedRoleId,
                session.Justification,
                session.IncidentId),
            command.CorrelationId,
            reservedRequestId,
            occurredAt,
            cancellationToken);

        if (submission is RequestSubmissionValidationRejected)
        {
            session.MarkInvalidated(occurredAt, command.CorrelationId);
            var invalidationSave = await intakeStore.SaveChangesAsync(cancellationToken);
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
                "The request submission outcome is unsupported.");
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

    private async Task<RequestSubmissionOutcome> CreateAndStageValidatedRequestAsync(
        string requesterId,
        AccessRequestValidationInput input,
        string correlationId,
        Guid reservedRequestId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var normalizedPrincipalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            requesterId);
        if (normalizedPrincipalId is null)
        {
            return new RequestSubmissionFailed(
                new ApplicationFailure(
                    ApplicationFailureKind.Unauthenticated,
                    "authentication_required",
                    "An authenticated requester is required."));
        }

        var normalizedCorrelationId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            correlationId);
        if (normalizedCorrelationId is null)
        {
            return new RequestSubmissionFailed(
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "correlation_id_required",
                    "A correlation identifier is required."));
        }

        var principalResult = await requestContext.GetPrincipalAsync(
            normalizedPrincipalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? new RequestSubmissionFailed(
                    new ApplicationFailure(
                        ApplicationFailureKind.Unauthenticated,
                        "authenticated_principal_not_found",
                        "The authenticated principal is unavailable."))
                : new RequestSubmissionFailed(principalResult.Failure);
        }

        var principal = principalResult.Value;
        if (principal.Kind != PrincipalKind.Requester)
        {
            return new RequestSubmissionFailed(
                new ApplicationFailure(
                    ApplicationFailureKind.Unauthorized,
                    "requester_required",
                    "Only an authenticated requester can submit an access request."));
        }

        var validationOutcome = await requestValidator.ValidateAsync(input, cancellationToken);
        if (validationOutcome is AccessRequestValidationFailed validationFailed)
        {
            return new RequestSubmissionFailed(validationFailed.Failure);
        }

        if (validationOutcome is AccessRequestValidationRejected validationRejected)
        {
            return new RequestSubmissionValidationRejected(validationRejected.Errors);
        }

        if (validationOutcome is not AccessRequestValidationSucceeded validationSucceeded)
        {
            throw new InvalidOperationException("The request validation outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fields = validationSucceeded.Fields;
        var requestCreatedAt = occurredAt.ToUniversalTime();
        var request = new AccessRequest(
            reservedRequestId,
            principal.Id,
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.Justification,
            fields.IncidentId,
            requestCreatedAt,
            normalizedCorrelationId);

        var auditEvent = AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            new RequestCreatedAuditDetails(request.Status));

        workflowStore.AddRequest(request);
        workflowStore.AddAuditEvent(auditEvent);

        return new RequestSubmitted(request);
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

    private static bool MatchesScope(
        RequestIntakeSession session,
        AccessRequest request) =>
        request.Id == session.ReservedRequestId
        && string.Equals(request.RequesterId, session.RequesterId, StringComparison.Ordinal)
        && string.Equals(request.ClientId, session.ClientId, StringComparison.Ordinal)
        && string.Equals(request.EnvironmentId, session.EnvironmentId, StringComparison.Ordinal)
        && string.Equals(request.RequestedRoleId, session.RequestedRoleId, StringComparison.Ordinal)
        && string.Equals(request.Justification, session.Justification, StringComparison.Ordinal)
        && string.Equals(request.IncidentId, session.IncidentId, StringComparison.Ordinal)
        && request.Status == RequestStatus.AwaitingBusinessApproval;

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailure failure) =>
        RequestConfirmationResult.Failed(failure);

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        RequestConfirmationResult.Failed(new ApplicationFailure(kind, code, message));
}

using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

/// <summary>
/// Confirms a prepared draft by reloading authoritative context and atomically
/// staging the immutable request, audit evidence, and terminal intake state.
/// </summary>
public sealed class RequestSubmissionService
{
    public const string ForbiddenCode = "prepared_request_forbidden";
    public const string ExpiredCode = "prepared_request_expired";
    public const string SupersededCode = "prepared_request_superseded";
    public const string InvalidatedCode = "prepared_request_invalidated";
    public const string NotReadyCode = "prepared_request_not_ready";

    private readonly RequestValidator requestValidator;
    private readonly IRequestContextReader requestContext;
    private readonly IRequestIntakeStore intakeStore;
    private readonly IWorkflowStore workflowStore;
    private readonly IClock clock;

    public RequestSubmissionService(
        RequestValidator requestValidator,
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
        if (!command.Actor.Owns(session))
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

        var reservedRequestId = session.ReservedRequestId!.Value;

        var requesterResult = await LoadRequesterAsync(
            session.RequesterId,
            cancellationToken);
        if (requesterResult.IsFailure)
        {
            return ConfirmationFailed(requesterResult.Failure!);
        }

        var preparedDetails = session.PreparedDetails;
        if (preparedDetails is null)
        {
            session.MarkInvalidated(occurredAt, command.CorrelationId);
            var invalidSnapshotSave = await intakeStore.SaveChangesAsync(
                cancellationToken);
            return invalidSnapshotSave.IsFailure
                ? ConfirmationFailed(invalidSnapshotSave.Failure!)
                : ConfirmationFailed(
                    ApplicationFailureKind.InvalidTransition,
                    InvalidatedCode,
                    "The prepared request snapshot is invalid.");
        }

        var validation = await requestValidator.RevalidateAsync(
            preparedDetails,
            cancellationToken);

        if (validation is RequestValidationRejected)
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

        if (validation is RequestValidationFailed validationFailed)
        {
            return ConfirmationFailed(validationFailed.Failure);
        }

        if (validation is not RequestValidationSucceeded validationSucceeded)
        {
            throw new InvalidOperationException(
                "The request validation outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = new AccessRequest(
            reservedRequestId,
            requesterResult.Value.Id,
            validationSucceeded.Details,
            occurredAt,
            command.CorrelationId);
        workflowStore.AddRequest(request);
        workflowStore.AddAuditEvent(AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            new RequestCreatedAuditDetails(request.Status)));

        session.MarkSubmitted(occurredAt, command.CorrelationId);
        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        if (saveResult.IsSuccess)
        {
            return RequestConfirmationResult.Submitted(request.Id);
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
                RequestConfirmationResult.AlreadySubmitted(
                    session.ReservedRequestId!.Value),
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

    private async Task<ApplicationResult<AuthenticatedPrincipal>> LoadRequesterAsync(
        string requesterId,
        CancellationToken cancellationToken)
    {
        var principalResult = await requestContext.GetPrincipalAsync(
            requesterId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? ApplicationResult.Failed<AuthenticatedPrincipal>(
                    new ApplicationFailure(
                        ApplicationFailureKind.Unauthenticated,
                        "authenticated_principal_not_found",
                        "The authenticated principal is unavailable."))
                : principalResult;
        }

        return principalResult.Value.Kind == PrincipalKind.Requester
            ? principalResult
            : ApplicationResult.Failed<AuthenticatedPrincipal>(
                new ApplicationFailure(
                    ApplicationFailureKind.Unauthorized,
                    "requester_required",
                    "Only an authenticated requester can submit an access request."));
    }

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailure failure) =>
        RequestConfirmationResult.Failed(failure);

    private static RequestConfirmationResult ConfirmationFailed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        RequestConfirmationResult.Failed(
            new ApplicationFailure(kind, code, message));
}

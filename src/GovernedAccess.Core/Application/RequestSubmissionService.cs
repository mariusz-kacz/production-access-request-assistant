using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

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
/// Creates and submits an access request using an actor identifier obtained from
/// authenticated server context. Proposed request fields never supply identity or
/// approver authority.
/// </summary>
public sealed class RequestSubmissionService
{
    private readonly RequestValidator requestValidator;
    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly IClock clock;

    public RequestSubmissionService(
        RequestValidator requestValidator,
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestValidator = requestValidator;
        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.clock = clock;
    }

    public async Task<RequestSubmissionOutcome> SubmitAsync(
        string? authenticatedPrincipalId,
        RequestValidationInput input,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var normalizedPrincipalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
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
        if (validationOutcome is RequestValidationFailed validationFailed)
        {
            return new RequestSubmissionFailed(validationFailed.Failure);
        }

        if (validationOutcome is RequestValidationRejected validationRejected)
        {
            return new RequestSubmissionValidationRejected(validationRejected.Errors);
        }

        if (validationOutcome is not RequestValidationSucceeded validationSucceeded)
        {
            throw new InvalidOperationException("The request validation outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fields = validationSucceeded.Fields;
        var occurredAt = clock.UtcNow.ToUniversalTime();
        var request = new AccessRequest(
            Guid.NewGuid(),
            principal.Id,
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.RequestedDurationMinutes,
            fields.Justification,
            fields.IncidentId,
            occurredAt,
            normalizedCorrelationId)
        {
            Status = RequestStatus.AwaitingBusinessApproval,
        };

        var auditEvent = AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            new RequestCreatedAuditDetails(request.Status));

        workflowStore.AddRequest(request);
        workflowStore.AddAuditEvent(auditEvent);

        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new RequestSubmissionFailed(saveResult.Failure!)
            : new RequestSubmitted(request);
    }
}

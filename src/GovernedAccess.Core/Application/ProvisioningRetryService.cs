using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public abstract record ProvisioningRetryOutcome;

public sealed record ProvisioningRetryCompleted(
    AccessRequest Request,
    ProvisioningOperation Operation,
    AccessGrant Grant) : ProvisioningRetryOutcome;

public sealed record ProvisioningRetryFailed(ApplicationFailure Failure)
    : ProvisioningRetryOutcome;

/// <summary>
/// Authorizes a retry from authenticated server context and delegates persisted
/// evidence validation and idempotent provider work to the protected handler.
/// </summary>
public sealed class ProvisioningRetryService
{
    public const string NotAuthorizedCode = "provisioning_retry_not_authorized";

    public const string InvalidTransitionCode =
        "provisioning_retry_invalid_transition";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly IClock clock;

    public ProvisioningRetryService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        ProtectedProvisioningService protectedProvisioning,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.protectedProvisioning = protectedProvisioning;
        this.clock = clock;
    }

    public async Task<ProvisioningRetryOutcome> RetryAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "request_id_required",
                "An access request identifier is required.");
        }

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated DevOps approver is required.");
        }

        var normalizedCorrelationId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            correlationId);
        if (normalizedCorrelationId is null)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "correlation_id_required",
                "A correlation identifier is required.");
        }

        var principalResult = await requestContext.GetPrincipalAsync(
            principalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed(
                    ApplicationFailureKind.Unauthenticated,
                    "authenticated_principal_not_found",
                    "The authenticated principal is unavailable.")
                : new ProvisioningRetryFailed(principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return new ProvisioningRetryFailed(requestResult.Failure!);
        }

        var request = requestResult.Value;
        var principal = principalResult.Value;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                new ApplicationFailure(
                    ApplicationFailureKind.Unauthorized,
                    NotAuthorizedCode,
                    "Only the authenticated DevOps approver can retry provisioning."),
                authorizationRejected: true,
                cancellationToken);
        }

        if (request.Status is not (
                RequestStatus.ProvisioningFailed or
                RequestStatus.Active))
        {
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    InvalidTransitionCode,
                    "Only a request with failed provisioning can be retried."),
                authorizationRejected: false,
                cancellationToken);
        }

        var outcome = await protectedProvisioning.RetryAsync(
            request.Id,
            cancellationToken);
        return outcome switch
        {
            ProtectedProvisioningCompleted completed =>
                new ProvisioningRetryCompleted(
                    completed.Request,
                    completed.Operation,
                    completed.Grant),
            ProtectedProvisioningFailed failed =>
                new ProvisioningRetryFailed(failed.Failure),
            _ => throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported."),
        };
    }

    private async Task<ProvisioningRetryOutcome> RejectAndAuditAsync(
        AccessRequest request,
        string actorId,
        string correlationId,
        ApplicationFailure failure,
        bool authorizationRejected,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow.ToUniversalTime();
        var auditEvent = authorizationRejected
            ? AuditEvent.CreateAuthorizationRejected(
                Guid.NewGuid(),
                request,
                ApprovalStage.DevOps,
                actorId,
                occurredAt,
                correlationId,
                failure.Code)
            : AuditEvent.CreateInvalidTransitionRejected(
                Guid.NewGuid(),
                request,
                ApprovalStage.DevOps,
                actorId,
                occurredAt,
                correlationId,
                failure.Code);

        workflowStore.AddAuditEvent(auditEvent);
        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new ProvisioningRetryFailed(saveResult.Failure!)
            : new ProvisioningRetryFailed(failure);
    }

    private static ProvisioningRetryFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return new ProvisioningRetryFailed(
            new ApplicationFailure(kind, code, message));
    }
}

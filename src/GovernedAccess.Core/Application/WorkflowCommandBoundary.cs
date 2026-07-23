using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record WorkflowCommandContext(
    AccessRequest Request,
    AuthenticatedPrincipal Principal,
    string CorrelationId);

/// <summary>
/// Resolves the authenticated actor and persisted request used by state-changing
/// workflow commands. Role and stage authorization remain the responsibility of
/// the command-specific service.
/// </summary>
public sealed class WorkflowCommandContextLoader
{
    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;

    public WorkflowCommandContextLoader(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
    }

    public async Task<ApplicationResult<WorkflowCommandContext>> LoadAsync(
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
                "An authenticated workflow actor is required.");
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
                : ApplicationResult.Failed<WorkflowCommandContext>(
                    principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return ApplicationResult.Failed<WorkflowCommandContext>(
                requestResult.Failure!);
        }

        return ApplicationResult.Succeeded(
            new WorkflowCommandContext(
                requestResult.Value,
                principalResult.Value,
                normalizedCorrelationId));
    }

    private static ApplicationResult<WorkflowCommandContext> Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return ApplicationResult.Failed<WorkflowCommandContext>(
            new ApplicationFailure(kind, code, message));
    }
}

public enum WorkflowAttemptRejectionKind
{
    Authorization,
    InvalidTransition,
}

/// <summary>
/// Persists the common audit evidence for rejected state-changing workflow
/// attempts and gives persistence failures precedence over the original rejection.
/// </summary>
public sealed class RejectedWorkflowAttemptRecorder
{
    private readonly IWorkflowStore workflowStore;
    private readonly IClock clock;

    public RejectedWorkflowAttemptRecorder(
        IWorkflowStore workflowStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.workflowStore = workflowStore;
        this.clock = clock;
    }

    public async Task<ApplicationFailure> RecordAsync(
        AccessRequest request,
        ApprovalStage stage,
        string actorId,
        string correlationId,
        ApplicationFailure rejection,
        WorkflowAttemptRejectionKind rejectionKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rejection);

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var auditEvent = rejectionKind switch
        {
            WorkflowAttemptRejectionKind.Authorization =>
                AuditEvent.CreateAuthorizationRejected(
                    Guid.NewGuid(),
                    request,
                    stage,
                    actorId,
                    occurredAt,
                    correlationId,
                    rejection.Code),
            WorkflowAttemptRejectionKind.InvalidTransition =>
                AuditEvent.CreateInvalidTransitionRejected(
                    Guid.NewGuid(),
                    request,
                    stage,
                    actorId,
                    occurredAt,
                    correlationId,
                    rejection.Code),
            _ => throw new ArgumentOutOfRangeException(
                nameof(rejectionKind),
                rejectionKind,
                "The workflow rejection kind is unsupported."),
        };

        workflowStore.AddAuditEvent(auditEvent);
        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? saveResult.Failure!
            : rejection;
    }
}

using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public abstract record ProtectedProvisioningOutcome;

public sealed record ProtectedProvisioningCompleted(
    AccessRequest Request,
    ProvisioningOperation Operation,
    AccessGrant Grant) : ProtectedProvisioningOutcome;

public sealed record ProtectedProvisioningFailed(ApplicationFailure Failure)
    : ProtectedProvisioningOutcome;

/// <summary>
/// Executes provisioning only after reloading and independently validating the
/// persisted request, approval, and operation evidence.
/// </summary>
public sealed class ProtectedProvisioningService
{
    public const string RequestIdInvalidCode = "provisioning_request_id_invalid";

    public const string WorkflowStateInvalidCode = "provisioning_workflow_state_invalid";

    public const string OperationScopeMismatchCode = "provisioning_operation_scope_mismatch";

    public const string ApprovalEvidenceInvalidCode = "provisioning_approval_invalid";

    public const string CompletedGrantMissingCode = "provisioning_completed_grant_missing";

    public const string SuccessCode = "provisioning_succeeded";

    private readonly IWorkflowStore workflowStore;
    private readonly IAccessProvisioner provisioner;
    private readonly IClock clock;

    public ProtectedProvisioningService(
        IWorkflowStore workflowStore,
        IAccessProvisioner provisioner,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(clock);

        this.workflowStore = workflowStore;
        this.provisioner = provisioner;
        this.clock = clock;
    }

    public async Task<ProtectedProvisioningOutcome> ProvisionAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                RequestIdInvalidCode,
                "A request identifier is required for provisioning.");
        }

        var initialContextResult = await LoadAndValidateAsync(
            requestId,
            cancellationToken);
        if (initialContextResult.IsFailure)
        {
            return new ProtectedProvisioningFailed(initialContextResult.Failure!);
        }

        var initialContext = initialContextResult.Value;
        if (initialContext.Operation.Status == ProvisioningOperationStatus.Succeeded)
        {
            return await GetCompletedAsync(initialContext, cancellationToken);
        }

        var attemptedAt = clock.UtcNow.ToUniversalTime();
        initialContext.Operation.LastAttemptAt = attemptedAt;
        workflowStore.AddAuditEvent(AuditEvent.CreateProvisioningAttempted(
            Guid.NewGuid(),
            initialContext.Request,
            initialContext.DevOpsApproval,
            initialContext.Operation,
            attemptedAt));

        var attemptSaveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        if (attemptSaveResult.IsFailure)
        {
            return new ProtectedProvisioningFailed(attemptSaveResult.Failure!);
        }

        var providerOutcome = await provisioner.GetOrCreateAsync(
            new AccessProvisioningRequest(
                initialContext.Request.Id,
                initialContext.Request.RequesterId,
                initialContext.Request.EnvironmentId,
                initialContext.Operation.RoleId,
                initialContext.DevOpsApproval.CorrelationId),
            cancellationToken);

        if (providerOutcome is AccessProvisioningFailed providerFailed)
        {
            return new ProtectedProvisioningFailed(providerFailed.Failure);
        }

        if (providerOutcome is not AccessProvisioningSucceeded providerSucceeded)
        {
            throw new InvalidOperationException(
                "The access provisioner returned an unsupported outcome.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var grant = new AccessGrant(
            providerSucceeded.GrantId,
            initialContext.Request.Id,
            initialContext.Request.RequesterId,
            initialContext.Request.EnvironmentId,
            initialContext.Operation.RoleId,
            providerSucceeded.ActivatedAt,
            initialContext.DevOpsApproval.CorrelationId);
        var completedAt = clock.UtcNow.ToUniversalTime();
        if (providerSucceeded.ActivatedAt > completedAt)
        {
            completedAt = providerSucceeded.ActivatedAt;
        }

        if (completedAt <= attemptedAt)
        {
            completedAt = attemptedAt.AddTicks(1);
        }

        initialContext.Operation.Status = ProvisioningOperationStatus.Succeeded;
        initialContext.Operation.LastOutcomeCode = SuccessCode;
        initialContext.Request.Status = RequestStatus.Active;
        initialContext.Request.LastModifiedAt = completedAt;
        initialContext.Request.PersistenceVersion++;

        workflowStore.AddAccessGrant(grant);
        workflowStore.AddAuditEvent(AuditEvent.CreateProvisioningSucceeded(
            Guid.NewGuid(),
            initialContext.Request,
            initialContext.DevOpsApproval,
            initialContext.Operation,
            grant,
            completedAt));

        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new ProtectedProvisioningFailed(saveResult.Failure!)
            : new ProtectedProvisioningCompleted(
                initialContext.Request,
                initialContext.Operation,
                grant);
    }

    private async Task<ApplicationResult<AuthoritativeProvisioningContext>>
        LoadAndValidateAsync(
            Guid requestId,
            CancellationToken cancellationToken)
    {
        var operationResult = await workflowStore.ReloadProvisioningOperationAsync(
            requestId,
            cancellationToken);
        if (operationResult.IsFailure)
        {
            return FailedContext(operationResult.Failure!);
        }

        var operation = operationResult.Value;
        if (operation.RequestId != requestId)
        {
            return FailedContext(Stale(
                OperationScopeMismatchCode,
                "The provisioning operation does not match its immutable request."));
        }

        var requestResult = await workflowStore.ReloadRequestAsync(
            operation.RequestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return FailedContext(requestResult.Failure!);
        }

        var request = requestResult.Value;
        if (!IsWorkflowStateValid(request.Status, operation.Status))
        {
            return FailedContext(Stale(
                WorkflowStateInvalidCode,
                "The request and provisioning operation are not in a provisionable state."));
        }

        if (operation.EnvironmentId != request.EnvironmentId ||
            operation.RoleId != request.RequestedRoleId)
        {
            return FailedContext(Stale(
                OperationScopeMismatchCode,
                "The provisioning operation scope does not match the immutable request."));
        }

        var businessApprovalResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.Business,
            cancellationToken);
        if (businessApprovalResult.IsFailure)
        {
            return FailedContext(MapMissingApproval(businessApprovalResult.Failure!));
        }

        var devOpsApprovalResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.DevOps,
            cancellationToken);
        if (devOpsApprovalResult.IsFailure)
        {
            return FailedContext(MapMissingApproval(devOpsApprovalResult.Failure!));
        }

        var businessApproval = businessApprovalResult.Value;
        var devOpsApproval = devOpsApprovalResult.Value;
        if (!IsApprovalEvidenceValid(request, businessApproval, devOpsApproval))
        {
            return FailedContext(Stale(
                ApprovalEvidenceInvalidCode,
                "The required approval evidence is invalid for this request."));
        }

        return ApplicationResult.Succeeded(
            new AuthoritativeProvisioningContext(
                request,
                operation,
                businessApproval,
                devOpsApproval));
    }

    private async Task<ProtectedProvisioningOutcome> GetCompletedAsync(
        AuthoritativeProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var grantResult = await workflowStore.GetAccessGrantForRequestAsync(
            context.Request.Id,
            cancellationToken);
        if (grantResult.IsFailure)
        {
            return grantResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed(
                    ApplicationFailureKind.DependencyFailure,
                    CompletedGrantMissingCode,
                    "The completed provisioning operation has no access grant.")
                : new ProtectedProvisioningFailed(grantResult.Failure);
        }

        var grant = grantResult.Value;
        return grant.RequestId == context.Request.Id &&
               grant.RequesterId == context.Request.RequesterId &&
               grant.EnvironmentId == context.Operation.EnvironmentId &&
               grant.RoleId == context.Operation.RoleId
            ? new ProtectedProvisioningCompleted(
                context.Request,
                context.Operation,
                grant)
            : Failed(
                ApplicationFailureKind.DependencyFailure,
                OperationScopeMismatchCode,
                "The stored access grant does not match the provisioning operation.");
    }

    private static bool IsWorkflowStateValid(
        RequestStatus requestStatus,
        ProvisioningOperationStatus operationStatus)
    {
        return (requestStatus, operationStatus) switch
        {
            (RequestStatus.AwaitingDevOpsApproval, ProvisioningOperationStatus.Pending) => true,
            (RequestStatus.ProvisioningFailed, ProvisioningOperationStatus.Failed) => true,
            (RequestStatus.Active, ProvisioningOperationStatus.Succeeded) => true,
            _ => false,
        };
    }

    private static bool IsApprovalEvidenceValid(
        AccessRequest request,
        ApprovalDecision businessApproval,
        ApprovalDecision devOpsApproval)
    {
        return businessApproval.RequestId == request.Id &&
               businessApproval.Stage == ApprovalStage.Business &&
               businessApproval.Decision == ApprovalOutcome.Approved &&
               businessApproval.ApprovedRoleId == request.RequestedRoleId &&
               businessApproval.DecidedAt >= request.CreatedAt &&
               devOpsApproval.RequestId == request.Id &&
               devOpsApproval.Stage == ApprovalStage.DevOps &&
               devOpsApproval.Decision == ApprovalOutcome.Approved &&
               devOpsApproval.ApprovedRoleId == businessApproval.ApprovedRoleId &&
               devOpsApproval.DecidedAt >= businessApproval.DecidedAt;
    }

    private static ApplicationFailure MapMissingApproval(ApplicationFailure failure)
    {
        return failure.Kind == ApplicationFailureKind.NotFound
            ? Stale(
                ApprovalEvidenceInvalidCode,
                "The required approval evidence is missing.")
            : failure;
    }

    private static ApplicationFailure Stale(string code, string message)
    {
        return new ApplicationFailure(
            ApplicationFailureKind.InvalidTransition,
            code,
            message);
    }

    private static ApplicationResult<AuthoritativeProvisioningContext> FailedContext(
        ApplicationFailure failure)
    {
        return ApplicationResult.Failed<AuthoritativeProvisioningContext>(failure);
    }

    private static ProtectedProvisioningFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return new ProtectedProvisioningFailed(
            new ApplicationFailure(kind, code, message));
    }

    private sealed record AuthoritativeProvisioningContext(
        AccessRequest Request,
        ProvisioningOperation Operation,
        ApprovalDecision BusinessApproval,
        ApprovalDecision DevOpsApproval);
}

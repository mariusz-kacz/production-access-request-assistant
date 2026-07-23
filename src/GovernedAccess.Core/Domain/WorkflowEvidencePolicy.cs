namespace GovernedAccess.Core.Domain;

public enum WorkflowEvidencePolicyError
{
    InvalidBusinessApproval,
    BusinessApprovalScopeMismatch,
    InvalidDevOpsApproval,
    ApprovalScopeMismatch,
    ApprovalSequenceInvalid,
    OperationScopeMismatch,
    GrantScopeMismatch,
}

/// <summary>
/// Defines the immutable-scope relationships between a request and the human and
/// provisioning evidence that can authorize access.
/// </summary>
public static class WorkflowEvidencePolicy
{
    public static WorkflowEvidencePolicyError? ValidateBusinessApproval(
        AccessRequest request,
        ApprovalDecision businessApproval)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(businessApproval);

        if (businessApproval.RequestId != request.Id ||
            businessApproval.Stage != ApprovalStage.Business ||
            businessApproval.Decision != ApprovalOutcome.Approved ||
            businessApproval.DecidedAt < request.CreatedAt)
        {
            return WorkflowEvidencePolicyError.InvalidBusinessApproval;
        }

        return !StringComparer.Ordinal.Equals(
                businessApproval.ApprovedRoleId,
                request.RequestedRoleId)
            ? WorkflowEvidencePolicyError.BusinessApprovalScopeMismatch
            : null;
    }

    public static WorkflowEvidencePolicyError? ValidateApprovalEvidence(
        AccessRequest request,
        ApprovalDecision businessApproval,
        ApprovalDecision devOpsApproval)
    {
        ArgumentNullException.ThrowIfNull(devOpsApproval);

        var businessError = ValidateBusinessApproval(request, businessApproval);
        if (businessError is not null)
        {
            return businessError;
        }

        if (devOpsApproval.RequestId != request.Id ||
            devOpsApproval.Stage != ApprovalStage.DevOps ||
            devOpsApproval.Decision != ApprovalOutcome.Approved)
        {
            return WorkflowEvidencePolicyError.InvalidDevOpsApproval;
        }

        if (!StringComparer.Ordinal.Equals(
                devOpsApproval.ApprovedRoleId,
                businessApproval.ApprovedRoleId))
        {
            return WorkflowEvidencePolicyError.ApprovalScopeMismatch;
        }

        return devOpsApproval.DecidedAt < businessApproval.DecidedAt
            ? WorkflowEvidencePolicyError.ApprovalSequenceInvalid
            : null;
    }

    public static WorkflowEvidencePolicyError? ValidateProvisioningEvidence(
        AccessRequest request,
        ApprovalDecision devOpsApproval,
        ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(devOpsApproval);
        ArgumentNullException.ThrowIfNull(operation);

        if (devOpsApproval.RequestId != request.Id ||
            devOpsApproval.Stage != ApprovalStage.DevOps ||
            devOpsApproval.Decision != ApprovalOutcome.Approved)
        {
            return WorkflowEvidencePolicyError.InvalidDevOpsApproval;
        }

        var operationError = ValidateOperationScope(request, operation);
        if (operationError is not null)
        {
            return operationError;
        }

        return !StringComparer.Ordinal.Equals(
                devOpsApproval.ApprovedRoleId,
                operation.RoleId)
            ? WorkflowEvidencePolicyError.ApprovalScopeMismatch
            : null;
    }

    public static WorkflowEvidencePolicyError? ValidateOperationScope(
        AccessRequest request,
        ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);

        return operation.RequestId != request.Id ||
               !StringComparer.Ordinal.Equals(
                   operation.EnvironmentId,
                   request.EnvironmentId) ||
               !StringComparer.Ordinal.Equals(
                   operation.RoleId,
                   request.RequestedRoleId)
            ? WorkflowEvidencePolicyError.OperationScopeMismatch
            : null;
    }

    public static WorkflowEvidencePolicyError? ValidateGrantScope(
        AccessRequest request,
        ProvisioningOperation operation,
        AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(grant);

        var operationError = ValidateOperationScope(request, operation);
        if (operationError is not null)
        {
            return operationError;
        }

        return grant.RequestId != request.Id ||
               !StringComparer.Ordinal.Equals(
                   grant.RequesterId,
                   request.RequesterId) ||
               !StringComparer.Ordinal.Equals(
                   grant.EnvironmentId,
                   operation.EnvironmentId) ||
               !StringComparer.Ordinal.Equals(
                   grant.RoleId,
                   operation.RoleId)
            ? WorkflowEvidencePolicyError.GrantScopeMismatch
            : null;
    }
}

namespace GovernedAccess.Core.Domain;

public enum DevOpsDecisionPolicyError
{
    InvalidTransition,
    DuplicateStage,
    InvalidBusinessApproval,
    BusinessApprovalScopeMismatch,
}

public sealed record DevOpsDecisionCommand(
    Guid DecisionId,
    ApprovalOutcome Decision,
    string ApproverId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);

public abstract record DevOpsDecisionPolicyResult;

public sealed record DevOpsDecisionApplied(
    ApprovalDecision Decision,
    ProvisioningOperation? Operation)
    : DevOpsDecisionPolicyResult;

public sealed record DevOpsDecisionNotApplied(DevOpsDecisionPolicyError Error)
    : DevOpsDecisionPolicyResult;

public static class DevOpsDecisionPolicy
{
    public static DevOpsDecisionPolicyResult Apply(
        AccessRequest request,
        ApprovalDecision businessApproval,
        DevOpsDecisionCommand command,
        bool hasExistingDevOpsDecision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(businessApproval);
        ArgumentNullException.ThrowIfNull(command);

        if (hasExistingDevOpsDecision)
        {
            return new DevOpsDecisionNotApplied(
                DevOpsDecisionPolicyError.DuplicateStage);
        }

        if (request.Status != RequestStatus.AwaitingDevOpsApproval)
        {
            return new DevOpsDecisionNotApplied(
                DevOpsDecisionPolicyError.InvalidTransition);
        }

        var businessApprovalError = WorkflowEvidencePolicy.ValidateBusinessApproval(
            request,
            businessApproval);
        if (businessApprovalError is not null)
        {
            return new DevOpsDecisionNotApplied(
                businessApprovalError ==
                    WorkflowEvidencePolicyError.BusinessApprovalScopeMismatch
                    ? DevOpsDecisionPolicyError.BusinessApprovalScopeMismatch
                    : DevOpsDecisionPolicyError.InvalidBusinessApproval);
        }

        var isApproval = command.Decision == ApprovalOutcome.Approved;
        var approvedRoleId = isApproval
            ? businessApproval.ApprovedRoleId
            : null;
        var decision = new ApprovalDecision(
            command.DecisionId,
            request.Id,
            ApprovalStage.DevOps,
            command.Decision,
            command.ApproverId,
            approvedRoleId,
            command.Comment,
            command.DecidedAt,
            command.CorrelationId);

        ProvisioningOperation? operation = null;
        if (isApproval)
        {
            operation = new ProvisioningOperation(
                request.Id,
                request.EnvironmentId,
                approvedRoleId!,
                command.DecidedAt);
        }

        request.Status = isApproval
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        request.LastModifiedAt = command.DecidedAt.ToUniversalTime();
        request.PersistenceVersion++;

        return new DevOpsDecisionApplied(decision, operation);
    }
}

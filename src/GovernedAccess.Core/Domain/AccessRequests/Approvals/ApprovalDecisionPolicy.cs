namespace GovernedAccess.Core.Domain.AccessRequests;

public enum ApprovalDecisionPolicyError
{
    InvalidTransition,
    DuplicateStage,
    InvalidPriorApproval,
    PriorApprovalScopeMismatch,
}

public abstract record ApprovalDecisionPolicyResult;

public sealed record ApprovalDecisionApplied(
    ApprovalDecision Decision,
    ProvisioningOperation? Operation)
    : ApprovalDecisionPolicyResult;

public sealed record ApprovalDecisionNotApplied(ApprovalDecisionPolicyError Error)
    : ApprovalDecisionPolicyResult;

public static class ApprovalDecisionPolicy
{
    public static ApprovalDecisionPolicyResult Apply(
        AccessRequest request,
        ApprovalStage stage,
        ApprovalDecision? priorApproval,
        ApprovalCommand command,
        bool hasExistingDecision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        if (hasExistingDecision)
        {
            return new ApprovalDecisionNotApplied(
                ApprovalDecisionPolicyError.DuplicateStage);
        }

        var expectedStatus = stage == ApprovalStage.Business
            ? RequestStatus.AwaitingBusinessApproval
            : RequestStatus.AwaitingDevOpsApproval;
        if (request.Status != expectedStatus)
        {
            return new ApprovalDecisionNotApplied(
                ApprovalDecisionPolicyError.InvalidTransition);
        }

        if (stage == ApprovalStage.DevOps)
        {
            var priorApprovalError = ValidatePriorApproval(request, priorApproval);
            if (priorApprovalError is not null)
            {
                return new ApprovalDecisionNotApplied(priorApprovalError.Value);
            }
        }

        var isApproval = command.Decision == ApprovalOutcome.Approved;
        var approvedRoleId = isApproval
            ? stage == ApprovalStage.Business
                ? request.RequestedRoleId
                : priorApproval!.ApprovedRoleId
            : null;
        var decision = new ApprovalDecision(
            command.DecisionId,
            request.Id,
            stage,
            command.Decision,
            command.ApproverId,
            approvedRoleId,
            command.Comment,
            command.DecidedAt,
            command.CorrelationId);

        var operation = stage == ApprovalStage.DevOps && isApproval
            ? new ProvisioningOperation(
                request.Id,
                request.EnvironmentId,
                approvedRoleId!,
                command.DecidedAt)
            : null;

        request.Status = isApproval
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        request.LastModifiedAt = command.DecidedAt.ToUniversalTime();
        request.PersistenceVersion++;

        return new ApprovalDecisionApplied(decision, operation);
    }

    private static ApprovalDecisionPolicyError? ValidatePriorApproval(
        AccessRequest request,
        ApprovalDecision? priorApproval)
    {
        if (priorApproval is null)
        {
            return ApprovalDecisionPolicyError.InvalidPriorApproval;
        }

        var evidenceError = WorkflowEvidencePolicy.ValidateBusinessApproval(
            request,
            priorApproval);
        return evidenceError switch
        {
            null => null,
            WorkflowEvidencePolicyError.BusinessApprovalScopeMismatch =>
                ApprovalDecisionPolicyError.PriorApprovalScopeMismatch,
            _ => ApprovalDecisionPolicyError.InvalidPriorApproval,
        };
    }
}

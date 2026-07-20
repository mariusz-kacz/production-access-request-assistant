namespace GovernedAccess.Core.Domain;

public enum BusinessDecisionPolicyError
{
    InvalidTransition,
    DuplicateStage,
}

public sealed record BusinessDecisionCommand(
    Guid DecisionId,
    ApprovalOutcome Decision,
    string ApproverId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);

public abstract record BusinessDecisionPolicyResult;

public sealed record BusinessDecisionApplied(ApprovalDecision Decision)
    : BusinessDecisionPolicyResult;

public sealed record BusinessDecisionNotApplied(BusinessDecisionPolicyError Error)
    : BusinessDecisionPolicyResult;

public static class BusinessDecisionPolicy
{
    public static BusinessDecisionPolicyResult Apply(
        AccessRequest request,
        BusinessDecisionCommand command,
        bool hasExistingBusinessDecision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(command);

        if (hasExistingBusinessDecision)
        {
            return new BusinessDecisionNotApplied(
                BusinessDecisionPolicyError.DuplicateStage);
        }

        if (request.Status != RequestStatus.AwaitingBusinessApproval)
        {
            return new BusinessDecisionNotApplied(
                BusinessDecisionPolicyError.InvalidTransition);
        }

        var isApproval = command.Decision == ApprovalOutcome.Approved;
        var decision = new ApprovalDecision(
            command.DecisionId,
            request.Id,
            ApprovalStage.Business,
            command.Decision,
            command.ApproverId,
            isApproval ? request.RequestedRoleId : null,
            command.Comment,
            command.DecidedAt,
            command.CorrelationId);

        request.Status = isApproval
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        request.LastModifiedAt = command.DecidedAt.ToUniversalTime();
        request.PersistenceVersion++;

        return new BusinessDecisionApplied(decision);
    }
}

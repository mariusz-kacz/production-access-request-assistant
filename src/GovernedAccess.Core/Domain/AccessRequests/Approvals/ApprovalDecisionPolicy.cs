namespace GovernedAccess.Core.Domain.AccessRequests;

public enum ApprovalDecisionPolicyError
{
    InvalidTransition,
    DuplicateStage,
    InvalidPriorApproval,
}

public sealed record ApprovalCommand(
    Guid DecisionId,
    ApprovalOutcome Decision,
    string ApproverId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);

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

        if (stage == ApprovalStage.DevOps
            && !IsValidBusinessApproval(request, priorApproval))
        {
            return new ApprovalDecisionNotApplied(
                ApprovalDecisionPolicyError.InvalidPriorApproval);
        }

        var isApproval = command.Decision == ApprovalOutcome.Approved;
        var decision = new ApprovalDecision(
            command.DecisionId,
            request.Id,
            stage,
            command.Decision,
            command.ApproverId,
            command.Comment,
            command.DecidedAt,
            command.CorrelationId);

        var operation = stage == ApprovalStage.DevOps && isApproval
            ? new ProvisioningOperation(request.Id, command.DecidedAt)
            : null;

        request.Status = isApproval
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        request.LastModifiedAt = command.DecidedAt.ToUniversalTime();
        request.PersistenceVersion++;

        return new ApprovalDecisionApplied(decision, operation);
    }

    private static bool IsValidBusinessApproval(
        AccessRequest request,
        ApprovalDecision? priorApproval) =>
        priorApproval is not null
        && priorApproval.RequestId == request.Id
        && priorApproval.Stage == ApprovalStage.Business
        && priorApproval.Decision == ApprovalOutcome.Approved;
}

using GovernedAccess.Core.Domain.ReferenceData;

namespace GovernedAccess.Core.Domain.AccessRequests;

public enum ApprovalStage
{
    Business,
    DevOps,
}

public enum ApprovalOutcome
{
    Approved,
    Rejected,
}

public sealed record ApprovalCommand(
    Guid DecisionId,
    ApprovalOutcome Decision,
    string ApproverId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);


public sealed class ApprovalDecision
{
    public const int MaximumCommentLength = 1000;

    public ApprovalDecision(
        Guid id,
        Guid requestId,
        ApprovalStage stage,
        ApprovalOutcome decision,
        string approverId,
        string? approvedRoleId,
        string? comment,
        DateTimeOffset decidedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        WorkflowEvidenceValidation.EnsureDefined(stage, nameof(stage));
        WorkflowEvidenceValidation.EnsureDefined(decision, nameof(decision));

        approverId = AccessRequestNormalization.NormalizeIdentifier(approverId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);
        comment = WorkflowEvidenceValidation.NormalizeOptionalText(
            comment,
            MaximumCommentLength,
            nameof(comment));

        if (decision == ApprovalOutcome.Approved)
        {
            approvedRoleId = AccessRequestNormalization.NormalizeIdentifier(approvedRoleId!);

            if (!ProductionRoleIds.IsSupported(approvedRoleId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(approvedRoleId),
                    approvedRoleId,
                    "The approved role is not supported by this feature.");
            }

        }
        else if (approvedRoleId is not null)
        {
            throw new ArgumentException("A rejection must not carry an approved role.");
        }

        Id = id;
        RequestId = requestId;
        Stage = stage;
        Decision = decision;
        ApproverId = approverId;
        ApprovedRoleId = approvedRoleId;
        Comment = comment;
        DecidedAt = decidedAt.ToUniversalTime();
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public ApprovalStage Stage { get; private set; }

    public ApprovalOutcome Decision { get; private set; }

    public string ApproverId { get; private set; }

    public string? ApprovedRoleId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string CorrelationId { get; private set; }
}


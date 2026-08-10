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

public sealed class ApprovalDecision
{
    public const int MaximumCommentLength = 1000;

    public ApprovalDecision(
        Guid id,
        Guid requestId,
        ApprovalStage stage,
        ApprovalOutcome decision,
        string approverId,
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

        Id = id;
        RequestId = requestId;
        Stage = stage;
        Decision = decision;
        ApproverId = approverId;
        Comment = comment;
        DecidedAt = decidedAt.ToUniversalTime();
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public ApprovalStage Stage { get; private set; }

    public ApprovalOutcome Decision { get; private set; }

    public string ApproverId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string CorrelationId { get; private set; }
}

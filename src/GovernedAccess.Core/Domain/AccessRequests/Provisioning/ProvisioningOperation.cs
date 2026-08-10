namespace GovernedAccess.Core.Domain.AccessRequests;

public enum ProvisioningOperationStatus
{
    Pending,
    Succeeded,
    Failed,
}

public sealed class ProvisioningOperation
{
    public ProvisioningOperation(
        Guid requestId,
        DateTimeOffset createdAt)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));

        RequestId = requestId;
        Status = ProvisioningOperationStatus.Pending;
        AttemptCount = 1;
        CreatedAt = createdAt.ToUniversalTime();
        LastAttemptAt = CreatedAt;
    }

    public Guid RequestId { get; private set; }

    public ProvisioningOperationStatus Status { get; internal set; }

    public int AttemptCount { get; internal set; }

    public string? LastOutcomeCode { get; internal set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastAttemptAt { get; internal set; }
}

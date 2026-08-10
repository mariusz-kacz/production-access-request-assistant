namespace GovernedAccess.Core.Domain.AccessRequests;

public enum AccessGrantOutcome
{
    Succeeded,
}

public sealed class AccessGrant
{
    public static readonly TimeSpan FixedLifetime = TimeSpan.FromHours(8);

    public AccessGrant(
        Guid id,
        Guid requestId,
        DateTimeOffset activatedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        Id = id;
        RequestId = requestId;
        ActivatedAt = activatedAt.ToUniversalTime();
        ExpiresAt = ActivatedAt.Add(FixedLifetime);
        Outcome = AccessGrantOutcome.Succeeded;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public DateTimeOffset ActivatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public AccessGrantOutcome Outcome { get; private set; }

    public string CorrelationId { get; private set; }

    public bool IsExpired(DateTimeOffset currentTime)
    {
        return currentTime.ToUniversalTime() >= ExpiresAt;
    }
}

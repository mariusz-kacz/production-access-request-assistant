using GovernedAccess.Core.Domain.ReferenceData;

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
        string requesterId,
        string environmentId,
        string roleId,
        DateTimeOffset activatedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        requesterId = AccessRequestNormalization.NormalizeIdentifier(requesterId);
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The granted role is not supported by this feature.");
        }

        Id = id;
        RequestId = requestId;
        RequesterId = requesterId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        ActivatedAt = activatedAt.ToUniversalTime();
        ExpiresAt = ActivatedAt.Add(FixedLifetime);
        Outcome = AccessGrantOutcome.Succeeded;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string RequesterId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

    public DateTimeOffset ActivatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public AccessGrantOutcome Outcome { get; private set; }

    public string CorrelationId { get; private set; }

    public bool IsExpired(DateTimeOffset currentTime)
    {
        return currentTime.ToUniversalTime() >= ExpiresAt;
    }
}



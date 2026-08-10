using GovernedAccess.Core.Domain.ReferenceData;

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
        string environmentId,
        string roleId,
        DateTimeOffset createdAt)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The provisioning role is not supported by this feature.");
        }

        RequestId = requestId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        Status = ProvisioningOperationStatus.Pending;
        AttemptCount = 1;
        CreatedAt = createdAt.ToUniversalTime();
        LastAttemptAt = CreatedAt;
    }

    public Guid RequestId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

    public ProvisioningOperationStatus Status { get; internal set; }

    public int AttemptCount { get; internal set; }

    public string? LastOutcomeCode { get; internal set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastAttemptAt { get; internal set; }
}



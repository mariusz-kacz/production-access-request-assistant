using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Canonical, server-validated scope for one idempotent provisioning operation.
/// Callers must populate this request from authoritative workflow state rather than
/// browser or model assertions. The immutable request identifier is also the
/// provider idempotency identity.
/// </summary>
public sealed record AccessProvisioningRequest
{
    public AccessProvisioningRequest(
        Guid requestId,
        string requesterId,
        string environmentId,
        string roleId,
        string correlationId)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "The request identifier must not be empty.",
                nameof(requestId));
        }

        requesterId = AccessRequestNormalization.NormalizeIdentifier(requesterId);
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The provisioning role is not supported by this feature.");
        }

        RequestId = requestId;
        RequesterId = requesterId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        CorrelationId = correlationId;
    }

    public Guid RequestId { get; }

    public string RequesterId { get; }

    public string EnvironmentId { get; }

    public string RoleId { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// Closed provider-neutral result for an idempotent provisioning attempt.
/// </summary>
public abstract record AccessProvisioningOutcome;

public sealed record AccessProvisioningSucceeded : AccessProvisioningOutcome
{
    public AccessProvisioningSucceeded(Guid grantId, DateTimeOffset activatedAt)
    {
        if (grantId == Guid.Empty)
        {
            throw new ArgumentException(
                "The grant identifier must not be empty.",
                nameof(grantId));
        }

        GrantId = grantId;
        ActivatedAt = activatedAt.ToUniversalTime();
    }

    public Guid GrantId { get; }

    public DateTimeOffset ActivatedAt { get; }
}

/// <summary>
/// Represents a safe typed provider failure, including dependency failure,
/// dependency unavailability, timeout, or caller cancellation.
/// </summary>
public sealed record AccessProvisioningFailed : AccessProvisioningOutcome
{
    public AccessProvisioningFailed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public ApplicationFailure Failure { get; }
}

/// <summary>
/// Creates or returns the one synthetic grant identified by
/// <see cref="AccessProvisioningRequest.RequestId"/>. Implementations translate
/// provider-specific contracts and expected failures at this boundary.
/// </summary>
public interface IAccessProvisioner
{
    Task<AccessProvisioningOutcome> GetOrCreateAsync(
        AccessProvisioningRequest request,
        CancellationToken cancellationToken);
}

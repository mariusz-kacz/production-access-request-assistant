namespace GovernedAccess.Core.Domain;

public static class ProductionRoleIds
{
    public const string ReadOnly = "ProductionReadOnly";

    public const string Support = "ProductionSupport";

    public static bool IsSupported(string roleId)
    {
        return string.Equals(roleId, ReadOnly, StringComparison.Ordinal)
            || string.Equals(roleId, Support, StringComparison.Ordinal);
    }
}

public enum IncidentStatus
{
    Active,
    Inactive,
}

public enum PrincipalKind
{
    Requester,
    BusinessApprover,
    DevOpsApprover,
}

public sealed class Client
{
    public Client(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; private set; }

    public string DisplayName { get; private set; }
}

public sealed class ProductionEnvironment
{
    public ProductionEnvironment(
        string id,
        string clientId,
        string displayName,
        int maximumDurationMinutes,
        string businessApproverPrincipalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessApproverPrincipalId);
        EnsurePositiveDuration(maximumDurationMinutes);

        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
        MaximumDurationMinutes = maximumDurationMinutes;
        BusinessApproverPrincipalId = businessApproverPrincipalId;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }

    public int MaximumDurationMinutes { get; private set; }

    public string BusinessApproverPrincipalId { get; private set; }

    public void UpdateMaximumDuration(int maximumDurationMinutes)
    {
        EnsurePositiveDuration(maximumDurationMinutes);

        MaximumDurationMinutes = maximumDurationMinutes;
    }

    private static void EnsurePositiveDuration(int maximumDurationMinutes)
    {
        if (maximumDurationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDurationMinutes),
                maximumDurationMinutes,
                "The maximum duration must be positive.");
        }
    }
}

public sealed class EnvironmentRole
{
    public EnvironmentRole(string environmentId, string roleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The role identifier is not supported by this feature.");
        }

        EnvironmentId = environmentId;
        RoleId = roleId;
    }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

}

public sealed class Incident
{
    public Incident(
        string id,
        string clientId,
        string? environmentId,
        string title,
        IncidentStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (environmentId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        }

        EnsureValidStatus(status);

        Id = id;
        ClientId = clientId;
        EnvironmentId = environmentId;
        Title = title;
        Status = status;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string? EnvironmentId { get; private set; }

    public string Title { get; private set; }

    public IncidentStatus Status { get; private set; }

    public void SetStatus(IncidentStatus status)
    {
        EnsureValidStatus(status);
        Status = status;
    }

    private static void EnsureValidStatus(IncidentStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The incident status is not supported.");
        }
    }
}

public sealed class AuthenticatedPrincipal
{
    public AuthenticatedPrincipal(
        string id,
        string displayName,
        PrincipalKind kind,
        string? clientId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The principal kind is not supported.");
        }

        if (kind == PrincipalKind.BusinessApprover)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        }
        else if (clientId is not null)
        {
            throw new ArgumentException(
                "Only a business approver can have a client responsibility.",
                nameof(clientId));
        }

        Id = id;
        DisplayName = displayName;
        Kind = kind;
        ClientId = clientId;
    }

    public string Id { get; private set; }

    public string DisplayName { get; private set; }

    public PrincipalKind Kind { get; private set; }

    public string? ClientId { get; private set; }
}

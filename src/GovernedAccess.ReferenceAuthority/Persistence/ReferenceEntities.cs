using GovernedAccess.Core.Preparations.Authority;

namespace GovernedAccess.ReferenceAuthority.Persistence;

internal sealed class ReferenceClient
{
    internal ReferenceClient(
        string id,
        string displayName,
        string businessApproverPrincipalId)
    {
        Id = Required(id, nameof(id));
        DisplayName = Required(displayName, nameof(displayName));
        BusinessApproverPrincipalId = Required(
            businessApproverPrincipalId,
            nameof(businessApproverPrincipalId));
    }

    private ReferenceClient()
    {
        Id = null!;
        DisplayName = null!;
        BusinessApproverPrincipalId = null!;
    }

    internal string Id { get; private set; }

    internal string DisplayName { get; private set; }

    internal string BusinessApproverPrincipalId { get; private set; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

internal sealed class ReferenceProductionEnvironment
{
    internal ReferenceProductionEnvironment(
        string id,
        string clientId,
        string displayName,
        string region,
        EnvironmentClassification classification,
        bool isActive,
        bool isProduction,
        bool isEligibleForIntake)
    {
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        Id = Required(id, nameof(id));
        ClientId = Required(clientId, nameof(clientId));
        DisplayName = Required(displayName, nameof(displayName));
        Region = Required(region, nameof(region));
        Classification = classification;
        IsActive = isActive;
        IsProduction = isProduction;
        IsEligibleForIntake = isEligibleForIntake;
    }

    private ReferenceProductionEnvironment()
    {
        Id = null!;
        ClientId = null!;
        DisplayName = null!;
        Region = null!;
    }

    internal string Id { get; private set; }

    internal string ClientId { get; private set; }

    internal string DisplayName { get; private set; }

    internal string Region { get; private set; }

    internal EnvironmentClassification Classification { get; private set; }

    internal bool IsActive { get; private set; }

    internal bool IsProduction { get; private set; }

    internal bool IsEligibleForIntake { get; private set; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

internal sealed class ReferenceEnvironmentRole
{
    internal ReferenceEnvironmentRole(
        string environmentId,
        string roleId,
        string displayName,
        bool isCurrentlyAssignable)
    {
        EnvironmentId = Required(environmentId, nameof(environmentId));
        RoleId = Required(roleId, nameof(roleId));
        DisplayName = Required(displayName, nameof(displayName));
        IsCurrentlyAssignable = isCurrentlyAssignable;
    }

    private ReferenceEnvironmentRole()
    {
        EnvironmentId = null!;
        RoleId = null!;
        DisplayName = null!;
    }

    internal string EnvironmentId { get; private set; }

    internal string RoleId { get; private set; }

    internal string DisplayName { get; private set; }

    internal bool IsCurrentlyAssignable { get; private set; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

internal sealed class ReferenceIncident
{
    internal ReferenceIncident(
        string id,
        string title,
        bool isActive,
        string? environmentId)
    {
        Id = Required(id, nameof(id));
        Title = Required(title, nameof(title));
        IsActive = isActive;
        EnvironmentId = environmentId is null
            ? null
            : Required(environmentId, nameof(environmentId));
    }

    private ReferenceIncident()
    {
        Id = null!;
        Title = null!;
    }

    internal string Id { get; private set; }

    internal string Title { get; private set; }

    internal bool IsActive { get; private set; }

    internal string? EnvironmentId { get; private set; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

namespace GovernedAccess.Core.Preparations.Authority;

public enum EnvironmentClassification
{
    Primary,
    Recovery,
}

public sealed record EnvironmentSearchDocument
{
    public EnvironmentSearchDocument(
        string environmentId,
        string displayName,
        string clientId,
        string clientDisplayName,
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

        EnvironmentId = AuthorityValue.Normalize(environmentId, nameof(environmentId));
        DisplayName = AuthorityValue.Normalize(displayName, nameof(displayName));
        ClientId = AuthorityValue.Normalize(clientId, nameof(clientId));
        ClientDisplayName = AuthorityValue.Normalize(
            clientDisplayName,
            nameof(clientDisplayName));
        Region = AuthorityValue.Normalize(region, nameof(region));
        Classification = classification;
        IsActive = isActive;
        IsProduction = isProduction;
        IsEligibleForIntake = isEligibleForIntake;
    }

    public string EnvironmentId { get; }

    public string DisplayName { get; }

    public string ClientId { get; }

    public string ClientDisplayName { get; }

    public string Region { get; }

    public EnvironmentClassification Classification { get; }

    public bool IsActive { get; }

    public bool IsProduction { get; }

    public bool IsEligibleForIntake { get; }

    public bool CanBecomeCanonical =>
        IsActive && IsProduction && IsEligibleForIntake;
}

public sealed record EnvironmentAuthorityProjection
{
    public EnvironmentAuthorityProjection(
        string environmentId,
        string displayName,
        string clientId,
        string clientDisplayName,
        string businessApproverPrincipalId,
        bool isActive,
        bool isProduction,
        bool isEligibleForIntake)
    {
        EnvironmentId = AuthorityValue.Normalize(environmentId, nameof(environmentId));
        DisplayName = AuthorityValue.Normalize(displayName, nameof(displayName));
        ClientId = AuthorityValue.Normalize(clientId, nameof(clientId));
        ClientDisplayName = AuthorityValue.Normalize(
            clientDisplayName,
            nameof(clientDisplayName));
        BusinessApproverPrincipalId = AuthorityValue.Normalize(
            businessApproverPrincipalId,
            nameof(businessApproverPrincipalId));
        IsActive = isActive;
        IsProduction = isProduction;
        IsEligibleForIntake = isEligibleForIntake;
    }

    public string EnvironmentId { get; }

    public string DisplayName { get; }

    public string ClientId { get; }

    public string ClientDisplayName { get; }

    public string BusinessApproverPrincipalId { get; }

    public bool IsActive { get; }

    public bool IsProduction { get; }

    public bool IsEligibleForIntake { get; }

    public bool CanBecomeCanonical =>
        IsActive && IsProduction && IsEligibleForIntake;
}

public sealed record EnvironmentRoleAuthorityProjection
{
    public EnvironmentRoleAuthorityProjection(
        string environmentId,
        string roleId,
        string displayName,
        bool isCurrentlyAssignable)
    {
        EnvironmentId = AuthorityValue.Normalize(environmentId, nameof(environmentId));
        RoleId = AuthorityValue.Normalize(roleId, nameof(roleId));
        DisplayName = AuthorityValue.Normalize(displayName, nameof(displayName));
        IsCurrentlyAssignable = isCurrentlyAssignable;
    }

    public string EnvironmentId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public bool IsCurrentlyAssignable { get; }
}

public sealed record IncidentAuthorityProjection
{
    public IncidentAuthorityProjection(
        string incidentId,
        string title,
        bool isActive,
        IEnumerable<string> eligibleEnvironmentIds)
    {
        ArgumentNullException.ThrowIfNull(eligibleEnvironmentIds);

        var environmentIds = eligibleEnvironmentIds
            .Select(identifier => AuthorityValue.Normalize(
                identifier,
                nameof(eligibleEnvironmentIds)))
            .ToArray();
        if (environmentIds.Distinct(StringComparer.Ordinal).Count()
            != environmentIds.Length)
        {
            throw new ArgumentException(
                "Eligible incident environment identifiers must be unique.",
                nameof(eligibleEnvironmentIds));
        }

        Array.Sort(environmentIds, StringComparer.Ordinal);

        IncidentId = AuthorityValue.Normalize(incidentId, nameof(incidentId));
        Title = AuthorityValue.Normalize(title, nameof(title));
        IsActive = isActive;
        EligibleEnvironmentIds = Array.AsReadOnly(environmentIds);
    }

    public string IncidentId { get; }

    public string Title { get; }

    public bool IsActive { get; }

    public IReadOnlyList<string> EligibleEnvironmentIds { get; }
}

internal static class AuthorityValue
{
    internal const int MaximumLength = 200;

    internal static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"An authority projection value cannot exceed {MaximumLength} characters.");
        }

        return value;
    }
}

namespace GovernedAccess.Core.Domain;

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

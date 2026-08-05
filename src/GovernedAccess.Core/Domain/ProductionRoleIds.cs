namespace GovernedAccess.Core.Domain;

public static class ProductionRoleIds
{
    public const string ReadOnly = "ProductionReadOnly";

    public const string Support = "ProductionSupport";

    public const string Deployment = "ProductionDeployment";

    public static bool IsSupported(string roleId)
    {
        return string.Equals(roleId, ReadOnly, StringComparison.Ordinal)
            || string.Equals(roleId, Support, StringComparison.Ordinal)
            || string.Equals(roleId, Deployment, StringComparison.Ordinal);
    }
}

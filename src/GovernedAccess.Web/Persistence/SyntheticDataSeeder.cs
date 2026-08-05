using GovernedAccess.Core.Domain;
using static GovernedAccess.Web.Demo.DemoDataIds;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal static class SyntheticDataSeeder
{
    internal static async Task SeedAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        Client[] clients =
        [
            new(ClientAlphaId, "Client Alpha"),
            new(ClientBetaId, "Client Beta"),
            new(ClientGammaId, "Client Gamma"),
            new(ClientThetaId, "Client Theta"),
        ];

        AuthenticatedPrincipal[] principals =
        [
            new(RequesterPrincipalId, "Demo Requester", PrincipalKind.Requester),
            new(
                ClientAlphaApproverPrincipalId,
                "Client Alpha Business Approver",
                PrincipalKind.BusinessApprover,
                ClientAlphaId),
            new(
                ClientBetaApproverPrincipalId,
                "Client Beta Business Approver",
                PrincipalKind.BusinessApprover,
                ClientBetaId),
            new(
                ClientGammaApproverPrincipalId,
                "Client Gamma Business Approver",
                PrincipalKind.BusinessApprover,
                ClientGammaId),
            new(
                ClientThetaApproverPrincipalId,
                "Client Theta Business Approver",
                PrincipalKind.BusinessApprover,
                ClientThetaId),
            new(DevOpsApproverPrincipalId, "DevOps Approver", PrincipalKind.DevOpsApprover),
        ];

        var environments = CreateEnvironments();
        var roles = CreateEnvironmentRoles(environments);

        Incident[] incidents =
        [
            new(
                PrimaryIncidentId,
                ClientAlphaId,
                ClientAlphaEnvironmentId,
                "Client Alpha production investigation",
                IncidentStatus.Active),
            new(
                InactiveIncidentId,
                ClientAlphaId,
                ClientAlphaEnvironmentId,
                "Resolved Client Alpha production incident",
                IncidentStatus.Inactive),
            new(
                ClientBetaIncidentId,
                ClientBetaId,
                ClientBetaEnvironmentId,
                "Client Beta production investigation",
                IncidentStatus.Active),
        ];

        await SeedExactAsync(
            dbContext.Clients,
            clients,
            client => client.Id,
            ValidateClient,
            cancellationToken);
        await SeedExactAsync(
            dbContext.AuthenticatedPrincipals,
            principals,
            principal => principal.Id,
            ValidatePrincipal,
            cancellationToken);
        await SeedExactAsync(
            dbContext.ProductionEnvironments,
            environments,
            environment => environment.Id,
            ValidateEnvironment,
            cancellationToken);
        await SeedExactAsync(
            dbContext.EnvironmentRoles,
            roles,
            role => new EnvironmentRoleKey(role.EnvironmentId, role.RoleId),
            static (_, _) => { },
            cancellationToken);
        await SeedExactAsync(
            dbContext.Incidents,
            incidents,
            incident => incident.Id,
            ValidateIncident,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ProductionEnvironment[] CreateEnvironments()
    {
        ClientRegion[] clientRegions =
        [
            new(ClientAlphaId, "Alpha", "EU", ClientAlphaApproverPrincipalId),
            new(ClientAlphaId, "Alpha", "US", ClientAlphaApproverPrincipalId),
            new(ClientBetaId, "Beta", "UK", ClientBetaApproverPrincipalId),
            new(ClientBetaId, "Beta", "EU", ClientBetaApproverPrincipalId),
            new(ClientGammaId, "Gamma", "US", ClientGammaApproverPrincipalId),
            new(ClientGammaId, "Gamma", "APAC", ClientGammaApproverPrincipalId),
            new(ClientThetaId, "Theta", "APAC", ClientThetaApproverPrincipalId),
            new(ClientThetaId, "Theta", "US", ClientThetaApproverPrincipalId),
        ];

        return clientRegions
            .SelectMany(static definition =>
            new ProductionEnvironment[]
            {
                new ProductionEnvironment(
                    $"PROD-{definition.ClientCode.ToUpperInvariant()}-{definition.Region}",
                    definition.ClientId,
                    $"Client {definition.ClientCode} Primary Production {definition.Region}",
                    definition.ApproverPrincipalId),
                new ProductionEnvironment(
                    $"RECOVERY-PROD-{definition.ClientCode.ToUpperInvariant()}-{definition.Region}",
                    definition.ClientId,
                    $"Client {definition.ClientCode} Recovery Production {definition.Region}",
                    definition.ApproverPrincipalId),
            })
            .ToArray();
    }

    private static EnvironmentRole[] CreateEnvironmentRoles(
        IEnumerable<ProductionEnvironment> environments)
    {
        return environments
            .SelectMany(static environment => GetRoleIds(environment)
                .Select(roleId => new EnvironmentRole(environment.Id, roleId)))
            .ToArray();
    }

    private static IEnumerable<string> GetRoleIds(ProductionEnvironment environment)
    {
        yield return ProductionRoleIds.ReadOnly;

        if (environment.ClientId is not (ClientAlphaId or ClientGammaId))
        {
            yield break;
        }

        yield return ProductionRoleIds.Support;
        if (!environment.Id.StartsWith("RECOVERY-", StringComparison.Ordinal))
        {
            yield return ProductionRoleIds.Deployment;
        }
    }

    private static async Task SeedExactAsync<TEntity, TKey>(
        DbSet<TEntity> entities,
        IReadOnlyCollection<TEntity> expectedEntities,
        Func<TEntity, TKey> keySelector,
        Action<TEntity, TEntity> validate,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull
    {
        var expectedByKey = expectedEntities.ToDictionary(keySelector);
        var existingEntities = await entities.AsNoTracking().ToListAsync(cancellationToken);
        var existingKeys = new HashSet<TKey>();

        foreach (var existingEntity in existingEntities)
        {
            var key = keySelector(existingEntity);

            if (!expectedByKey.TryGetValue(key, out var expectedEntity))
            {
                throw new InvalidOperationException(
                    $"Unexpected {typeof(TEntity).Name} record '{key}' in the synthetic dataset.");
            }

            validate(existingEntity, expectedEntity);
            existingKeys.Add(key);
        }

        foreach (var expectedEntity in expectedEntities)
        {
            if (!existingKeys.Contains(keySelector(expectedEntity)))
            {
                entities.Add(expectedEntity);
            }
        }
    }

    private static void ValidateClient(Client actual, Client expected)
    {
        EnsureMatches(actual.DisplayName == expected.DisplayName, nameof(Client), actual.Id);
    }

    private static void ValidatePrincipal(
        AuthenticatedPrincipal actual,
        AuthenticatedPrincipal expected)
    {
        EnsureMatches(
            actual.DisplayName == expected.DisplayName
            && actual.Kind == expected.Kind
            && actual.ClientId == expected.ClientId,
            nameof(AuthenticatedPrincipal),
            actual.Id);
    }

    private static void ValidateEnvironment(
        ProductionEnvironment actual,
        ProductionEnvironment expected)
    {
        EnsureMatches(
            actual.ClientId == expected.ClientId
            && actual.DisplayName == expected.DisplayName
            && actual.BusinessApproverPrincipalId == expected.BusinessApproverPrincipalId,
            nameof(ProductionEnvironment),
            actual.Id);
    }

    private static void ValidateIncident(Incident actual, Incident expected)
    {
        EnsureMatches(
            actual.ClientId == expected.ClientId
            && actual.EnvironmentId == expected.EnvironmentId
            && actual.Title == expected.Title
            && actual.Status == expected.Status,
            nameof(Incident),
            actual.Id);
    }

    private static void EnsureMatches(bool matches, string entityName, string identifier)
    {
        if (!matches)
        {
            throw new InvalidOperationException(
                $"{entityName} record '{identifier}' conflicts with the synthetic dataset.");
        }
    }

    private readonly record struct EnvironmentRoleKey(string EnvironmentId, string RoleId)
    {
        public override string ToString() => $"{EnvironmentId}/{RoleId}";
    }

    private sealed record ClientRegion(
        string ClientId,
        string ClientCode,
        string Region,
        string ApproverPrincipalId);
}

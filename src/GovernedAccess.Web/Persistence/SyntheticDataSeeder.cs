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
            new(DevOpsApproverPrincipalId, "DevOps Approver", PrincipalKind.DevOpsApprover),
        ];

        ProductionEnvironment[] environments =
        [
            new(
                ClientAlphaEnvironmentId,
                ClientAlphaId,
                "Client Alpha Production EU",
                isActive: true,
                maximumDurationMinutes: 480,
                ClientAlphaApproverPrincipalId),
            new(
                ClientBetaEnvironmentId,
                ClientBetaId,
                "Client Beta Production UK",
                isActive: true,
                maximumDurationMinutes: 240,
                ClientBetaApproverPrincipalId),
        ];

        EnvironmentRole[] roles =
        [
            new(ClientAlphaEnvironmentId, ProductionRoleIds.ReadOnly, isAvailable: true),
            new(ClientAlphaEnvironmentId, ProductionRoleIds.Support, isAvailable: true),
            new(ClientBetaEnvironmentId, ProductionRoleIds.ReadOnly, isAvailable: true),
        ];

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
            ValidateRole,
            cancellationToken);
        await SeedExactAsync(
            dbContext.Incidents,
            incidents,
            incident => incident.Id,
            ValidateIncident,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
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
                    $"Unexpected authoritative {typeof(TEntity).Name} record '{key}'.");
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
            && actual.IsActive == expected.IsActive
            && actual.MaximumDurationMinutes == expected.MaximumDurationMinutes
            && actual.BusinessApproverPrincipalId == expected.BusinessApproverPrincipalId,
            nameof(ProductionEnvironment),
            actual.Id);
    }

    private static void ValidateRole(EnvironmentRole actual, EnvironmentRole expected)
    {
        EnsureMatches(
            actual.IsAvailable == expected.IsAvailable,
            nameof(EnvironmentRole),
            $"{actual.EnvironmentId}/{actual.RoleId}");
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
                $"Authoritative {entityName} record '{identifier}' conflicts with the synthetic dataset.");
        }
    }

    private readonly record struct EnvironmentRoleKey(string EnvironmentId, string RoleId)
    {
        public override string ToString() => $"{EnvironmentId}/{RoleId}";
    }
}

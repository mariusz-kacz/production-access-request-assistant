using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Preparations.Authority;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.ReferenceAuthority.Persistence;

internal static class SyntheticReferenceData
{
    private const string ClientAlphaId = "client-alpha";
    private const string ClientBetaId = "client-beta";
    private const string ClientGammaId = "client-gamma";
    private const string ClientThetaId = "client-theta";

    internal static async Task SeedAsync(
        ReferenceAuthorityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        ReferenceClient[] clients =
        [
            new(ClientAlphaId, "Client Alpha", "client-alpha-business-approver"),
            new(ClientBetaId, "Client Beta", "client-beta-business-approver"),
            new(ClientGammaId, "Client Gamma", "client-gamma-business-approver"),
            new(ClientThetaId, "Client Theta", "client-theta-business-approver"),
        ];
        var environments = CreateEnvironments();
        var roles = CreateEnvironmentRoles(environments);
        ReferenceIncident[] incidents =
        [
            new("INC-1041", "Resolved Client Alpha production incident", isActive: false),
            new("INC-1042", "Client Alpha production investigation", isActive: true),
            new("INC-2042", "Client Beta production investigation", isActive: true),
        ];
        ReferenceIncidentEnvironmentLink[] incidentLinks =
        [
            new("INC-1041", "PROD-ALPHA-EU"),
            new("INC-1042", "PROD-ALPHA-EU"),
            new("INC-2042", "PROD-BETA-UK"),
        ];

        await SeedExactAsync(
            dbContext.Clients,
            clients,
            entity => entity.Id,
            static (actual, expected) =>
                actual.DisplayName == expected.DisplayName
                && actual.BusinessApproverPrincipalId
                    == expected.BusinessApproverPrincipalId,
            cancellationToken);
        await SeedExactAsync(
            dbContext.ProductionEnvironments,
            environments,
            entity => entity.Id,
            static (actual, expected) =>
                actual.ClientId == expected.ClientId
                && actual.DisplayName == expected.DisplayName
                && actual.Region == expected.Region
                && actual.Classification == expected.Classification
                && actual.IsActive == expected.IsActive
                && actual.IsProduction == expected.IsProduction
                && actual.IsEligibleForIntake == expected.IsEligibleForIntake,
            cancellationToken);
        await SeedExactAsync(
            dbContext.EnvironmentRoles,
            roles,
            entity => (entity.EnvironmentId, entity.RoleId),
            static (actual, expected) =>
                actual.DisplayName == expected.DisplayName
                && actual.IsCurrentlyAssignable == expected.IsCurrentlyAssignable,
            cancellationToken);
        await SeedExactAsync(
            dbContext.Incidents,
            incidents,
            entity => entity.Id,
            static (actual, expected) =>
                actual.Title == expected.Title
                && actual.IsActive == expected.IsActive,
            cancellationToken);
        await SeedExactAsync(
            dbContext.IncidentEnvironmentLinks,
            incidentLinks,
            entity => (entity.IncidentId, entity.EnvironmentId),
            static (_, _) => true,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ReferenceProductionEnvironment[] CreateEnvironments()
    {
        ClientRegion[] clientRegions =
        [
            new(ClientAlphaId, "ALPHA", "EU"),
            new(ClientAlphaId, "ALPHA", "US"),
            new(ClientBetaId, "BETA", "UK"),
            new(ClientBetaId, "BETA", "EU"),
            new(ClientGammaId, "GAMMA", "US"),
            new(ClientGammaId, "GAMMA", "APAC"),
            new(ClientThetaId, "THETA", "APAC"),
            new(ClientThetaId, "THETA", "US"),
        ];

        return clientRegions.SelectMany(static definition =>
        new ReferenceProductionEnvironment[]
        {
            new(
                $"PROD-{definition.ClientCode}-{definition.Region}",
                definition.ClientId,
                $"Primary Production {definition.Region}",
                definition.Region,
                EnvironmentClassification.Primary,
                isActive: true,
                isProduction: true,
                isEligibleForIntake: true),
            new(
                $"RECOVERY-PROD-{definition.ClientCode}-{definition.Region}",
                definition.ClientId,
                $"Recovery Production {definition.Region}",
                definition.Region,
                EnvironmentClassification.Recovery,
                isActive: true,
                isProduction: true,
                isEligibleForIntake: true),
        }).ToArray();
    }

    private static ReferenceEnvironmentRole[] CreateEnvironmentRoles(
        IEnumerable<ReferenceProductionEnvironment> environments) =>
        environments
            .SelectMany(environment => GetRoleIds(environment)
                .Select(roleId => new ReferenceEnvironmentRole(
                    environment.Id,
                    roleId,
                    GetRoleDisplayName(roleId),
                    isCurrentlyAssignable: true)))
            .ToArray();

    private static IEnumerable<string> GetRoleIds(
        ReferenceProductionEnvironment environment)
    {
        yield return ProductionRoleIds.ReadOnly;

        if (environment.ClientId is not (ClientAlphaId or ClientGammaId))
        {
            yield break;
        }

        yield return ProductionRoleIds.Support;
        if (environment.Classification == EnvironmentClassification.Primary)
        {
            yield return ProductionRoleIds.Deployment;
        }
    }

    private static string GetRoleDisplayName(string roleId) => roleId switch
    {
        ProductionRoleIds.ReadOnly => "Production read-only",
        ProductionRoleIds.Support => "Production support",
        ProductionRoleIds.Deployment => "Production deployment",
        _ => throw new InvalidOperationException("The synthetic role is unsupported."),
    };

    private static async Task SeedExactAsync<TEntity, TKey>(
        DbSet<TEntity> entities,
        IReadOnlyCollection<TEntity> expectedEntities,
        Func<TEntity, TKey> keySelector,
        Func<TEntity, TEntity, bool> matches,
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
            if (!expectedByKey.TryGetValue(key, out var expectedEntity)
                || !matches(existingEntity, expectedEntity))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name} record '{key}' conflicts with the synthetic reference dataset.");
            }

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

    private sealed record ClientRegion(
        string ClientId,
        string ClientCode,
        string Region);
}

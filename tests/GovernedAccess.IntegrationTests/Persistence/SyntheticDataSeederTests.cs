using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class SyntheticDataSeederTests
{
    [Fact]
    public async Task SeedAsyncCreatesAConsistentSyntheticDatasetAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new GovernedAccessDbContext(options);

        await SyntheticDataSeeder.SeedAsync(context, TestContext.Current.CancellationToken);
        await SyntheticDataSeeder.SeedAsync(context, TestContext.Current.CancellationToken);

        var clients = await context.Clients
            .AsNoTracking()
            .OrderBy(client => client.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                (DemoDataIds.ClientAlphaId, "Client Alpha"),
                (DemoDataIds.ClientBetaId, "Client Beta"),
                (DemoDataIds.ClientGammaId, "Client Gamma"),
                (DemoDataIds.ClientThetaId, "Client Theta"),
            ],
            clients.Select(client => (client.Id, client.DisplayName)));

        var environments = await context.ProductionEnvironments
            .AsNoTracking()
            .OrderBy(environment => environment.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(environments);
        Assert.Equal(
            environments.Count,
            environments.Select(environment => environment.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            clients,
            client =>
            {
                Assert.Contains(environments, environment =>
                    environment.ClientId == client.Id
                    && !environment.Id.StartsWith("RECOVERY-", StringComparison.Ordinal));
                Assert.Contains(environments, environment =>
                    environment.ClientId == client.Id
                    && environment.Id.StartsWith("RECOVERY-", StringComparison.Ordinal));
            });

        var roles = await context.EnvironmentRoles
            .AsNoTracking()
            .OrderBy(role => role.EnvironmentId)
            .ThenBy(role => role.RoleId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(
            environments,
            environment => Assert.Contains(roles, role =>
                role.EnvironmentId == environment.Id
                && role.RoleId == ProductionRoleIds.ReadOnly));
        AssertRoleSet(
            roles,
            DemoDataIds.ClientAlphaEnvironmentId,
            ProductionRoleIds.Deployment,
            ProductionRoleIds.ReadOnly,
            ProductionRoleIds.Support);
        AssertRoleSet(
            roles,
            DemoDataIds.ClientAlphaRecoveryEnvironmentId,
            ProductionRoleIds.ReadOnly,
            ProductionRoleIds.Support);
        AssertRoleSet(
            roles,
            DemoDataIds.ClientBetaEnvironmentId,
            ProductionRoleIds.ReadOnly);

        var principals = await context.AuthenticatedPrincipals
            .AsNoTracking()
            .OrderBy(principal => principal.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, principals.Count);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.RequesterPrincipalId
            && principal.Kind == PrincipalKind.Requester
            && principal.ClientId == null);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.ClientAlphaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == DemoDataIds.ClientAlphaId);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.ClientBetaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == DemoDataIds.ClientBetaId);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.ClientGammaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == DemoDataIds.ClientGammaId);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.ClientThetaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == DemoDataIds.ClientThetaId);
        Assert.Contains(principals, principal =>
            principal.Id == DemoDataIds.DevOpsApproverPrincipalId
            && principal.Kind == PrincipalKind.DevOpsApprover
            && principal.ClientId == null);

        var incidents = await context.Incidents
            .AsNoTracking()
            .OrderBy(incident => incident.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, incidents.Count);
        Assert.Contains(incidents, incident =>
            incident.Id == DemoDataIds.PrimaryIncidentId
            && incident.ClientId == DemoDataIds.ClientAlphaId
            && incident.EnvironmentId == DemoDataIds.ClientAlphaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident =>
            incident.ClientId == DemoDataIds.ClientBetaId
            && incident.EnvironmentId == DemoDataIds.ClientBetaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident => incident.Status == IncidentStatus.Inactive);

        Assert.Empty(context.AccessRequests);
        Assert.Empty(context.ApprovalDecisions);
        Assert.Empty(context.ProvisioningOperations);
        Assert.Empty(context.AccessGrants);
        Assert.Empty(context.AuditEvents);
    }

    private static void AssertRoleSet(
        IEnumerable<EnvironmentRole> roles,
        string environmentId,
        params string[] expectedRoleIds)
    {
        Assert.Equal(
            expectedRoleIds,
            roles.Where(role => role.EnvironmentId == environmentId)
                .Select(role => role.RoleId));
    }
}

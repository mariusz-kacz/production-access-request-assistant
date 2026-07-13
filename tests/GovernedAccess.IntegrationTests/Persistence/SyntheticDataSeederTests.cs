using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class SyntheticDataSeederTests
{
    [Fact]
    public async Task SeedAsyncCreatesTheExactAuthoritativeDatasetAndIsIdempotent()
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
        Assert.Collection(
            clients,
            client =>
            {
                Assert.Equal(SyntheticDataSeeder.ClientAlphaId, client.Id);
                Assert.Equal("Client Alpha", client.DisplayName);
            },
            client =>
            {
                Assert.Equal(SyntheticDataSeeder.ClientBetaId, client.Id);
                Assert.Equal("Client Beta", client.DisplayName);
            });

        var environments = await context.ProductionEnvironments
            .AsNoTracking()
            .OrderBy(environment => environment.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Collection(
            environments,
            environment =>
            {
                Assert.Equal(SyntheticDataSeeder.ClientAlphaEnvironmentId, environment.Id);
                Assert.Equal(SyntheticDataSeeder.ClientAlphaId, environment.ClientId);
                Assert.True(environment.IsActive);
                Assert.Equal(480, environment.MaximumDurationMinutes);
                Assert.Equal(
                    SyntheticDataSeeder.ClientAlphaApproverPrincipalId,
                    environment.BusinessApproverPrincipalId);
            },
            environment =>
            {
                Assert.Equal(SyntheticDataSeeder.ClientBetaEnvironmentId, environment.Id);
                Assert.Equal(SyntheticDataSeeder.ClientBetaId, environment.ClientId);
                Assert.True(environment.IsActive);
                Assert.Equal(240, environment.MaximumDurationMinutes);
                Assert.Equal(
                    SyntheticDataSeeder.ClientBetaApproverPrincipalId,
                    environment.BusinessApproverPrincipalId);
            });

        var roles = await context.EnvironmentRoles
            .AsNoTracking()
            .OrderBy(role => role.EnvironmentId)
            .ThenBy(role => role.RoleId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Collection(
            roles,
            role => AssertRole(
                role,
                SyntheticDataSeeder.ClientAlphaEnvironmentId,
                ProductionRoleIds.ReadOnly),
            role => AssertRole(
                role,
                SyntheticDataSeeder.ClientAlphaEnvironmentId,
                ProductionRoleIds.Support),
            role => AssertRole(
                role,
                SyntheticDataSeeder.ClientBetaEnvironmentId,
                ProductionRoleIds.ReadOnly));

        var principals = await context.AuthenticatedPrincipals
            .AsNoTracking()
            .OrderBy(principal => principal.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, principals.Count);
        Assert.Contains(principals, principal =>
            principal.Id == SyntheticDataSeeder.RequesterPrincipalId
            && principal.Kind == PrincipalKind.Requester
            && principal.ClientId == null);
        Assert.Contains(principals, principal =>
            principal.Id == SyntheticDataSeeder.ClientAlphaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == SyntheticDataSeeder.ClientAlphaId);
        Assert.Contains(principals, principal =>
            principal.Id == SyntheticDataSeeder.ClientBetaApproverPrincipalId
            && principal.Kind == PrincipalKind.BusinessApprover
            && principal.ClientId == SyntheticDataSeeder.ClientBetaId);
        Assert.Contains(principals, principal =>
            principal.Id == SyntheticDataSeeder.DevOpsApproverPrincipalId
            && principal.Kind == PrincipalKind.DevOpsApprover
            && principal.ClientId == null);

        var incidents = await context.Incidents
            .AsNoTracking()
            .OrderBy(incident => incident.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, incidents.Count);
        Assert.Contains(incidents, incident =>
            incident.Id == SyntheticDataSeeder.PrimaryIncidentId
            && incident.ClientId == SyntheticDataSeeder.ClientAlphaId
            && incident.EnvironmentId == SyntheticDataSeeder.ClientAlphaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident =>
            incident.ClientId == SyntheticDataSeeder.ClientBetaId
            && incident.EnvironmentId == SyntheticDataSeeder.ClientBetaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident => incident.Status == IncidentStatus.Inactive);

        Assert.Empty(context.AccessRequests);
        Assert.Empty(context.ApprovalDecisions);
        Assert.Empty(context.ProvisioningOperations);
        Assert.Empty(context.AccessGrants);
        Assert.Empty(context.AuditEvents);
    }

    private static void AssertRole(EnvironmentRole role, string environmentId, string roleId)
    {
        Assert.Equal(environmentId, role.EnvironmentId);
        Assert.Equal(roleId, role.RoleId);
        Assert.True(role.IsAvailable);
    }
}

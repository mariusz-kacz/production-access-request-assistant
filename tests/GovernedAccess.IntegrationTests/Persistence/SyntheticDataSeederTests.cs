using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class SyntheticDataSeederTests
{
    [Fact]
    public async Task SeedAsyncCreatesTheExactSyntheticDatasetAndIsIdempotent()
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
                (DemoDataIds.ClientAlphaId, "Client Alpha",
                    DemoDataIds.ClientAlphaApproverPrincipalId),
                (DemoDataIds.ClientBetaId, "Client Beta",
                    DemoDataIds.ClientBetaApproverPrincipalId),
                (DemoDataIds.ClientGammaId, "Client Gamma",
                    DemoDataIds.ClientGammaApproverPrincipalId),
                (DemoDataIds.ClientThetaId, "Client Theta",
                    DemoDataIds.ClientThetaApproverPrincipalId),
            ],
            clients.Select(client => (
                client.Id,
                client.DisplayName,
                client.BusinessApproverPrincipalId)));

        var environments = await context.ProductionEnvironments
            .AsNoTracking()
            .OrderBy(environment => environment.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                (DemoDataIds.ClientAlphaEnvironmentId, DemoDataIds.ClientAlphaId,
                    "Primary Production EU"),
                ("PROD-ALPHA-US", DemoDataIds.ClientAlphaId,
                    "Primary Production US"),
                ("PROD-BETA-EU", DemoDataIds.ClientBetaId,
                    "Primary Production EU"),
                (DemoDataIds.ClientBetaEnvironmentId, DemoDataIds.ClientBetaId,
                    "Primary Production UK"),
                ("PROD-GAMMA-APAC", DemoDataIds.ClientGammaId,
                    "Primary Production APAC"),
                (DemoDataIds.ClientGammaEnvironmentId, DemoDataIds.ClientGammaId,
                    "Primary Production US"),
                (DemoDataIds.ClientThetaEnvironmentId, DemoDataIds.ClientThetaId,
                    "Primary Production APAC"),
                ("PROD-THETA-US", DemoDataIds.ClientThetaId,
                    "Primary Production US"),
                (DemoDataIds.ClientAlphaRecoveryEnvironmentId, DemoDataIds.ClientAlphaId,
                    "Recovery Production EU"),
                ("RECOVERY-PROD-ALPHA-US", DemoDataIds.ClientAlphaId,
                    "Recovery Production US"),
                ("RECOVERY-PROD-BETA-EU", DemoDataIds.ClientBetaId,
                    "Recovery Production EU"),
                (DemoDataIds.ClientBetaRecoveryEnvironmentId, DemoDataIds.ClientBetaId,
                    "Recovery Production UK"),
                ("RECOVERY-PROD-GAMMA-APAC", DemoDataIds.ClientGammaId,
                    "Recovery Production APAC"),
                (DemoDataIds.ClientGammaRecoveryEnvironmentId, DemoDataIds.ClientGammaId,
                    "Recovery Production US"),
                (DemoDataIds.ClientThetaRecoveryEnvironmentId, DemoDataIds.ClientThetaId,
                    "Recovery Production APAC"),
                ("RECOVERY-PROD-THETA-US", DemoDataIds.ClientThetaId,
                    "Recovery Production US"),
            ],
            environments.Select(environment => (
                environment.Id,
                environment.ClientId,
                environment.DisplayName)));

        var roles = await context.EnvironmentRoles
            .AsNoTracking()
            .OrderBy(role => role.EnvironmentId)
            .ThenBy(role => role.RoleId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                (DemoDataIds.ClientAlphaEnvironmentId, ProductionRoleIds.Deployment),
                (DemoDataIds.ClientAlphaEnvironmentId, ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientAlphaEnvironmentId, ProductionRoleIds.Support),
                ("PROD-ALPHA-US", ProductionRoleIds.Deployment),
                ("PROD-ALPHA-US", ProductionRoleIds.ReadOnly),
                ("PROD-ALPHA-US", ProductionRoleIds.Support),
                ("PROD-BETA-EU", ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientBetaEnvironmentId, ProductionRoleIds.ReadOnly),
                ("PROD-GAMMA-APAC", ProductionRoleIds.Deployment),
                ("PROD-GAMMA-APAC", ProductionRoleIds.ReadOnly),
                ("PROD-GAMMA-APAC", ProductionRoleIds.Support),
                (DemoDataIds.ClientGammaEnvironmentId, ProductionRoleIds.Deployment),
                (DemoDataIds.ClientGammaEnvironmentId, ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientGammaEnvironmentId, ProductionRoleIds.Support),
                (DemoDataIds.ClientThetaEnvironmentId, ProductionRoleIds.ReadOnly),
                ("PROD-THETA-US", ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientAlphaRecoveryEnvironmentId, ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientAlphaRecoveryEnvironmentId, ProductionRoleIds.Support),
                ("RECOVERY-PROD-ALPHA-US", ProductionRoleIds.ReadOnly),
                ("RECOVERY-PROD-ALPHA-US", ProductionRoleIds.Support),
                ("RECOVERY-PROD-BETA-EU", ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientBetaRecoveryEnvironmentId, ProductionRoleIds.ReadOnly),
                ("RECOVERY-PROD-GAMMA-APAC", ProductionRoleIds.ReadOnly),
                ("RECOVERY-PROD-GAMMA-APAC", ProductionRoleIds.Support),
                (DemoDataIds.ClientGammaRecoveryEnvironmentId, ProductionRoleIds.ReadOnly),
                (DemoDataIds.ClientGammaRecoveryEnvironmentId, ProductionRoleIds.Support),
                (DemoDataIds.ClientThetaRecoveryEnvironmentId, ProductionRoleIds.ReadOnly),
                ("RECOVERY-PROD-THETA-US", ProductionRoleIds.ReadOnly),
            ],
            roles.Select(role => (role.EnvironmentId, role.RoleId)));

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
            && incident.EnvironmentId == DemoDataIds.ClientAlphaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident =>
            incident.EnvironmentId == DemoDataIds.ClientBetaEnvironmentId
            && incident.Status == IncidentStatus.Active);
        Assert.Contains(incidents, incident => incident.Status == IncidentStatus.Inactive);

        Assert.Empty(context.AccessRequests);
        Assert.Empty(context.ApprovalDecisions);
        Assert.Empty(context.ProvisioningOperations);
        Assert.Empty(context.AccessGrants);
        Assert.Empty(context.AuditEvents);
    }
}

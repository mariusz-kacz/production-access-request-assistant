using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal static class MinimalTestDataSeeder
{
    internal static async Task SeedAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        dbContext.Clients.AddRange(
            new Client(DemoDataIds.ClientAlphaId, "Client Alpha"),
            new Client(DemoDataIds.ClientBetaId, "Client Beta"));

        dbContext.AuthenticatedPrincipals.AddRange(
            new AuthenticatedPrincipal(
                DemoDataIds.RequesterPrincipalId,
                "Demo Requester",
                PrincipalKind.Requester),
            new AuthenticatedPrincipal(
                DemoDataIds.ClientAlphaApproverPrincipalId,
                "Client Alpha Business Approver",
                PrincipalKind.BusinessApprover,
                DemoDataIds.ClientAlphaId),
            new AuthenticatedPrincipal(
                DemoDataIds.ClientBetaApproverPrincipalId,
                "Client Beta Business Approver",
                PrincipalKind.BusinessApprover,
                DemoDataIds.ClientBetaId),
            new AuthenticatedPrincipal(
                DemoDataIds.DevOpsApproverPrincipalId,
                "DevOps Approver",
                PrincipalKind.DevOpsApprover));

        dbContext.ProductionEnvironments.AddRange(
            new ProductionEnvironment(
                DemoDataIds.ClientAlphaEnvironmentId,
                DemoDataIds.ClientAlphaId,
                "Client Alpha Primary Production EU",
                DemoDataIds.ClientAlphaApproverPrincipalId),
            new ProductionEnvironment(
                DemoDataIds.ClientAlphaRecoveryEnvironmentId,
                DemoDataIds.ClientAlphaId,
                "Client Alpha Recovery Production EU",
                DemoDataIds.ClientAlphaApproverPrincipalId),
            new ProductionEnvironment(
                DemoDataIds.ClientBetaEnvironmentId,
                DemoDataIds.ClientBetaId,
                "Client Beta Primary Production UK",
                DemoDataIds.ClientBetaApproverPrincipalId));

        dbContext.EnvironmentRoles.AddRange(
            new EnvironmentRole(
                DemoDataIds.ClientAlphaEnvironmentId,
                ProductionRoleIds.Deployment),
            new EnvironmentRole(
                DemoDataIds.ClientAlphaEnvironmentId,
                ProductionRoleIds.ReadOnly),
            new EnvironmentRole(
                DemoDataIds.ClientAlphaEnvironmentId,
                ProductionRoleIds.Support),
            new EnvironmentRole(
                DemoDataIds.ClientAlphaRecoveryEnvironmentId,
                ProductionRoleIds.ReadOnly),
            new EnvironmentRole(
                DemoDataIds.ClientAlphaRecoveryEnvironmentId,
                ProductionRoleIds.Support),
            new EnvironmentRole(
                DemoDataIds.ClientBetaEnvironmentId,
                ProductionRoleIds.ReadOnly));

        dbContext.Incidents.AddRange(
            new Incident(
                DemoDataIds.PrimaryIncidentId,
                DemoDataIds.ClientAlphaId,
                DemoDataIds.ClientAlphaEnvironmentId,
                "Client Alpha production investigation",
                IncidentStatus.Active),
            new Incident(
                DemoDataIds.ClientBetaIncidentId,
                DemoDataIds.ClientBetaId,
                DemoDataIds.ClientBetaEnvironmentId,
                "Client Beta production investigation",
                IncidentStatus.Active));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

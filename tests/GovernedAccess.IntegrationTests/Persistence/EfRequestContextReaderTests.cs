using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class EfRequestContextReaderTests
{
    [Fact]
    public async Task ListEnvironmentContextsProjectsAuthoritativeDataInStableOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await using var context = CreateContext(connection);
        await SyntheticDataSeeder.SeedAsync(context, cancellationToken);
        context.ChangeTracker.Clear();
        var reader = new EfRequestContextReader(context);

        var result = await reader.ListProductionEnvironmentContextsAsync(
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            environmentContext =>
            {
                Assert.Equal(
                    DemoDataIds.ClientAlphaEnvironmentId,
                    environmentContext.Environment.Id);
                Assert.Equal(
                    DemoDataIds.ClientAlphaId,
                    environmentContext.Client.Id);
                Assert.Equal("Client Alpha", environmentContext.Client.DisplayName);
                Assert.Equal(
                    "Client Alpha Production EU",
                    environmentContext.Environment.DisplayName);
                Assert.Collection(
                    environmentContext.AssignedRoles,
                    role => AssertRole(
                        role,
                        DemoDataIds.ClientAlphaEnvironmentId,
                        ProductionRoleIds.ReadOnly),
                    role => AssertRole(
                        role,
                        DemoDataIds.ClientAlphaEnvironmentId,
                        ProductionRoleIds.Support));
            },
            environmentContext =>
            {
                Assert.Equal(
                    DemoDataIds.ClientBetaEnvironmentId,
                    environmentContext.Environment.Id);
                Assert.Equal(
                    DemoDataIds.ClientBetaId,
                    environmentContext.Client.Id);
                Assert.Equal("Client Beta", environmentContext.Client.DisplayName);
                Assert.Equal(
                    "Client Beta Production UK",
                    environmentContext.Environment.DisplayName);
                var role = Assert.Single(environmentContext.AssignedRoles);
                AssertRole(
                    role,
                    DemoDataIds.ClientBetaEnvironmentId,
                    ProductionRoleIds.ReadOnly);
            });
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task ListEnvironmentContextsHandlesEmptyAndOverflowCatalogs(
        int environmentCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        if (environmentCount > 0)
        {
            await SeedEnvironmentCatalogAsync(
                context,
                environmentCount,
                cancellationToken);
        }

        context.ChangeTracker.Clear();
        var reader = new EfRequestContextReader(context);

        var result = await reader.ListProductionEnvironmentContextsAsync(
            cancellationToken);

        if (environmentCount == 0)
        {
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
        else
        {
            Assert.True(result.IsFailure);
            var failure = Assert.IsType<ApplicationFailure>(result.Failure);
            Assert.Equal(
                ApplicationFailureKind.DependencyUnavailable,
                failure.Kind);
            Assert.Equal(
                "environment-candidate-limit-exceeded",
                failure.Code);
            Assert.False(result.TryGetValue(out var partialContexts));
            Assert.Null(partialContexts);
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    _ = result.Value;
                });
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static GovernedAccessDbContext CreateContext(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .Options;
        return new GovernedAccessDbContext(options);
    }

    private static async Task SeedEnvironmentCatalogAsync(
        GovernedAccessDbContext context,
        int environmentCount,
        CancellationToken cancellationToken)
    {
        const string clientId = "client-catalog";
        const string approverId = "client-catalog-approver";
        context.Clients.Add(new Client(clientId, "Catalog Client"));
        context.AuthenticatedPrincipals.Add(
            new AuthenticatedPrincipal(
                approverId,
                "Catalog Business Approver",
                PrincipalKind.BusinessApprover,
                clientId));
        context.ProductionEnvironments.AddRange(
            Enumerable.Range(1, environmentCount)
                .Select(index => new ProductionEnvironment(
                    $"PROD-CATALOG-{index:D2}",
                    clientId,
                    $"Catalog Production {index:D2}",
                    approverId)));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void AssertRole(
        EnvironmentRole role,
        string environmentId,
        string roleId)
    {
        Assert.Equal(environmentId, role.EnvironmentId);
        Assert.Equal(roleId, role.RoleId);
    }
}

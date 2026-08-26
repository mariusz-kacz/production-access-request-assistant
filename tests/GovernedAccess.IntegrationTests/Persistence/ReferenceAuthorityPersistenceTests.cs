using GovernedAccess.ReferenceAuthority;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class ReferenceAuthorityPersistenceTests
{
    [Fact]
    public async Task FreshDatabaseMigratesAndSeedsOnlyReferenceAuthorityTables()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();

        var tables = await ReadTableNamesAsync(
            context,
            TestContext.Current.CancellationToken);
        var migrations = await context.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Clients",
                "EnvironmentRoles",
                "Incidents",
                "ProductionEnvironments",
                "__EFMigrationsHistory",
                "__EFMigrationsLock",
            ],
            tables);
        Assert.Single(migrations);
        Assert.Equal(4, await context.Clients.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            16,
            await context.ProductionEnvironments.CountAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(3, await context.Incidents.CountAsync(TestContext.Current.CancellationToken));
        var incidentEntity = context.Model.FindEntityType(typeof(ReferenceIncident));
        Assert.NotNull(incidentEntity);
        Assert.Equal(
            ["EnvironmentId", "Id", "IsActive", "Title"],
            incidentEntity.GetProperties().Select(property => property.Name).Order());
        var incidentEnvironmentForeignKey = Assert.Single(
            incidentEntity.GetForeignKeys());
        Assert.Equal(
            nameof(ReferenceIncident.EnvironmentId),
            Assert.Single(incidentEnvironmentForeignKey.Properties).Name);
        Assert.False(incidentEnvironmentForeignKey.IsRequired);
        Assert.Equal(
            nameof(ReferenceIncident.EnvironmentId),
            Assert.Single(Assert.Single(incidentEntity.GetIndexes()).Properties).Name);
        Assert.Equal(
            "PROD-ALPHA-EU",
            (await context.Incidents.SingleAsync(
                incident => incident.Id == "INC-1042",
                TestContext.Current.CancellationToken)).EnvironmentId);
        Assert.DoesNotContain(tables, table => table.Contains("Request", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.Contains("Approval", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.Contains("Grant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartUsesTheSameIndependentMigrationAndIdempotentSeed()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"reference-authority-{Guid.NewGuid():N}.db");

        try
        {
            await using (var first = await ReferenceAuthorityFixture.CreateAsync(databasePath))
            {
                await using var scope = first.Services.CreateAsyncScope();
                var context = scope.ServiceProvider
                    .GetRequiredService<ReferenceAuthorityDbContext>();
                await ReferenceAuthorityDatabase.InitializeAsync(
                    first.Services,
                    TestContext.Current.CancellationToken);
                Assert.Equal(
                    16,
                    await context.ProductionEnvironments.CountAsync(
                        TestContext.Current.CancellationToken));
            }

            await using var restarted = await ReferenceAuthorityFixture.CreateAsync(databasePath);
            await using var restartedScope = restarted.Services.CreateAsyncScope();
            var restartedContext = restartedScope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();

            Assert.Equal(
                16,
                await restartedContext.ProductionEnvironments.CountAsync(
                    TestContext.Current.CancellationToken));
            Assert.Single(
                await restartedContext.Database.GetAppliedMigrationsAsync(
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        ReferenceAuthorityDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var names = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

}

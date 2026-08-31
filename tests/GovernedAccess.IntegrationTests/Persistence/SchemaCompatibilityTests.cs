using GovernedAccess.ReferenceAuthority;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class SchemaCompatibilityTests
{
    [Fact]
    public async Task TransitionalReferenceDatabaseFailsWithBoundedResetGuidance()
    {
        var tableName = "IncidentEnvironment" + "Links";
        await AssertIncompatibleDatabaseIsRetainedAsync(
            "ReferenceAuthority",
            tableName,
            static configuration => new ServiceCollection()
                .AddReferenceAuthority(configuration)
                .BuildServiceProvider(validateScopes: true),
            static services => ReferenceAuthorityDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TransitionalWorkflowDatabaseFailsWithBoundedResetGuidance()
    {
        var tableName = "Request" + "IntakeSessions";
        await AssertIncompatibleDatabaseIsRetainedAsync(
            "WorkflowPersistence",
            tableName,
            static configuration => new ServiceCollection()
                .AddWorkflowPersistence(configuration)
                .BuildServiceProvider(validateScopes: true),
            static services => WorkflowPersistenceDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken));
    }

    private static async Task AssertIncompatibleDatabaseIsRetainedAsync(
        string connectionStringName,
        string tableName,
        Func<IConfiguration, ServiceProvider> createServices,
        Func<IServiceProvider, Task> initialize)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"schema-compatibility-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE \"{tableName}\" (\"Id\" TEXT NOT NULL PRIMARY KEY)";
                await command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken);
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{connectionStringName}"] = connectionString,
                })
                .Build();
            await using var services = createServices(configuration);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => initialize(services));

            Assert.Contains("reset", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(databasePath, error.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(databasePath));

            await using var verification = new SqliteConnection(connectionString);
            await verification.OpenAsync(TestContext.Current.CancellationToken);
            await using var verificationCommand = verification.CreateCommand();
            verificationCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            verificationCommand.Parameters.AddWithValue("$name", tableName);
            Assert.Equal(
                1L,
                await verificationCommand.ExecuteScalarAsync(
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

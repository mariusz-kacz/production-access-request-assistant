using GovernedAccess.Workflow.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class WorkflowPersistenceFixture : IAsyncDisposable
{
    private readonly bool ownsDatabase;

    private WorkflowPersistenceFixture(
        ServiceProvider services,
        string databasePath,
        bool ownsDatabase)
    {
        Services = services;
        DatabasePath = databasePath;
        this.ownsDatabase = ownsDatabase;
    }

    internal ServiceProvider Services { get; }

    internal string DatabasePath { get; }

    internal static async Task<WorkflowPersistenceFixture> CreateAsync(
        string? databasePath = null)
    {
        var ownsDatabase = databasePath is null;
        databasePath ??= Path.Combine(
            Path.GetTempPath(),
            $"workflow-persistence-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WorkflowPersistence"] =
                    $"Data Source={databasePath};Pooling=False",
            })
            .Build();
        var services = new ServiceCollection()
            .AddWorkflowPersistence(configuration)
            .BuildServiceProvider(validateScopes: true);
        await WorkflowPersistenceDatabase.InitializeAsync(
            services,
            TestContext.Current.CancellationToken);
        return new WorkflowPersistenceFixture(services, databasePath, ownsDatabase);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (ownsDatabase)
        {
            File.Delete(DatabasePath);
        }
    }
}

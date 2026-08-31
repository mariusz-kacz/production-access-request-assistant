using GovernedAccess.Core.Ports;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.Web.Authority;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class TargetPersistenceFixture : IAsyncDisposable
{
    private TargetPersistenceFixture(
        ServiceProvider services,
        string referenceDatabasePath,
        string workflowDatabasePath)
    {
        Services = services;
        ReferenceDatabasePath = referenceDatabasePath;
        WorkflowDatabasePath = workflowDatabasePath;
    }

    internal ServiceProvider Services { get; }

    internal string ReferenceDatabasePath { get; }

    internal string WorkflowDatabasePath { get; }

    internal static async Task<TargetPersistenceFixture> CreateAsync()
    {
        var referenceDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-reference-{Guid.NewGuid():N}.db");
        var workflowDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-workflow-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={referenceDatabasePath};Pooling=False",
                ["ConnectionStrings:WorkflowPersistence"] =
                    $"Data Source={workflowDatabasePath};Pooling=False",
            })
            .Build();
        var services = new ServiceCollection()
            .AddReferenceAuthority(configuration)
            .AddWorkflowPersistence(configuration)
            .AddScoped<IRequestContextReader, AuthoritativeRequestContextReader>()
            .BuildServiceProvider(validateScopes: true);

        try
        {
            await ReferenceAuthorityDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken);
            await WorkflowPersistenceDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken);
            return new TargetPersistenceFixture(
                services,
                referenceDatabasePath,
                workflowDatabasePath);
        }
        catch
        {
            await services.DisposeAsync();
            File.Delete(referenceDatabasePath);
            File.Delete(workflowDatabasePath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        File.Delete(ReferenceDatabasePath);
        File.Delete(WorkflowDatabasePath);
    }
}

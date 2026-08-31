using GovernedAccess.ReferenceAuthority;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class ReferenceAuthorityFixture : IAsyncDisposable
{
    private readonly bool ownsDatabase;

    private ReferenceAuthorityFixture(
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

    internal static async Task<ReferenceAuthorityFixture> CreateAsync(
        string? databasePath = null)
    {
        var ownsDatabase = databasePath is null;
        databasePath ??= Path.Combine(
            Path.GetTempPath(),
            $"reference-authority-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={databasePath};Pooling=False",
            })
            .Build();
        var services = new ServiceCollection()
            .AddReferenceAuthority(configuration)
            .BuildServiceProvider(validateScopes: true);
        await ReferenceAuthorityDatabase.InitializeAsync(
            services,
            TestContext.Current.CancellationToken);
        return new ReferenceAuthorityFixture(services, databasePath, ownsDatabase);
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

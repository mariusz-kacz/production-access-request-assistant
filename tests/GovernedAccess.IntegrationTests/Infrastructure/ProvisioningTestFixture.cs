using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class ProvisioningTestFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset DefaultUtcNow =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly DbContextOptions<GovernedAccessDbContext> dbContextOptions;

    private ProvisioningTestFixture(
        SqliteConnection connection,
        DbContextOptions<GovernedAccessDbContext> dbContextOptions)
    {
        this.connection = connection;
        this.dbContextOptions = dbContextOptions;
        Clock = new DeterministicClock(DefaultUtcNow);
    }

    public DeterministicClock Clock { get; }

    public static async Task<ProvisioningTestFixture> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .Options;
        var fixture = new ProvisioningTestFixture(connection, options);

        try
        {
            await using var dbContext = fixture.CreateDbContext();
            await MinimalTestDataSeeder.SeedAsync(dbContext, cancellationToken);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public GovernedAccessDbContext CreateDbContext() => new(dbContextOptions);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

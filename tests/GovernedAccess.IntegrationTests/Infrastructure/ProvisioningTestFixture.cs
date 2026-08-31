namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class ProvisioningTestFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset DefaultUtcNow =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly TargetPersistenceFixture targetPersistence;

    private ProvisioningTestFixture(TargetPersistenceFixture targetPersistence)
    {
        this.targetPersistence = targetPersistence;
        Clock = new DeterministicClock(DefaultUtcNow);
    }

    internal DeterministicClock Clock { get; }

    internal IServiceProvider Services => targetPersistence.Services;

    internal static async Task<ProvisioningTestFixture> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProvisioningTestFixture(
            await TargetPersistenceFixture.CreateAsync());
    }

    public ValueTask DisposeAsync() => targetPersistence.DisposeAsync();
}

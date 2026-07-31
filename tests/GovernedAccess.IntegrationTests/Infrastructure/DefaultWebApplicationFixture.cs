namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class DefaultWebApplicationFixture : IAsyncDisposable
{
    public GovernedAccessWebFactory Factory { get; } = new();

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

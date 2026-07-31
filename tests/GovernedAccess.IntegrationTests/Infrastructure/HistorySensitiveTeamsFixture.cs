using GovernedAccess.Web.Ai;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class HistorySensitiveTeamsFixture : IAsyncDisposable
{
    public HistorySensitiveTeamsFixture()
    {
        ChatClient = new DeterministicChatClient(
            DeterministicChatMode.HistorySensitive);
        Factory = new GovernedAccessWebFactory(ChatClient);
    }

    public DeterministicChatClient ChatClient { get; }

    public GovernedAccessWebFactory Factory { get; }

    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        await Factory.ResetDatabaseAsync(cancellationToken);
        return ChatClient.RequestCount;
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

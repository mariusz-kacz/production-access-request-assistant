using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class ConfigurableTeamsFixture : IAsyncDisposable
{
    public ConfigurableTeamsFixture()
    {
        ChatClient = new ConfigurableDeterministicChatClient();
        Factory = new GovernedAccessWebFactory(ChatClient);
    }

    public ConfigurableDeterministicChatClient ChatClient { get; }

    public GovernedAccessWebFactory Factory { get; }

    public async Task ResetAsync(
        DeterministicChatMode mode,
        CancellationToken cancellationToken)
    {
        ChatClient.Configure(mode);
        await Factory.ResetDatabaseAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

public sealed class ConfigurableDeterministicChatClient : IChatClient
{
    private IChatClient current = new DeterministicChatClient(
        DeterministicChatMode.Candidate);

    public void Configure(DeterministicChatMode mode)
    {
        var replacement = new DeterministicChatClient(mode);
        var previous = Interlocked.Exchange(ref current, replacement);
        previous.Dispose();
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Volatile.Read(ref current).GetResponseAsync(
            messages,
            options,
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Volatile.Read(ref current).GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : Volatile.Read(ref current).GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        Interlocked.Exchange<IChatClient>(
            ref current,
            new DeterministicChatClient(DeterministicChatMode.Candidate))
            .Dispose();
    }
}

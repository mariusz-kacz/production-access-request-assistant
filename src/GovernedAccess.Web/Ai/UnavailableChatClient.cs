using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace GovernedAccess.Web.Ai;

internal sealed class UnavailableChatClient : IChatClient
{
    private readonly string safeFailure;

    public UnavailableChatClient(string safeFailure)
    {
        if (string.IsNullOrWhiteSpace(safeFailure))
        {
            throw new ArgumentException(
                "A safe availability failure is required.",
                nameof(safeFailure));
        }

        if (safeFailure.Length > 256)
        {
            throw new ArgumentException(
                "The safe availability failure is too long.",
                nameof(safeFailure));
        }

        this.safeFailure = safeFailure;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ChatResponse>(
            new HttpRequestException(safeFailure));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Yield();
            throw new HttpRequestException(safeFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : null;
    }

    public void Dispose()
    {
    }
}

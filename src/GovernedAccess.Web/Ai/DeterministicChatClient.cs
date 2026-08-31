using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace GovernedAccess.Web.Ai;

public enum DeterministicChatMode
{
    Unclear,
    Malformed,
    Timeout,
    Cancellation,
    Unavailable,
}

public sealed class DeterministicChatClient(DeterministicChatMode mode) : IChatClient
{
    private const string MalformedResponse =
        """
        {
        """;

    private const string UnclearResponse =
        """
        {"schemaVersion":1,"dialogueAct":"unclear","patch":null,"discussionTopic":null}
        """;

    private int requestCount;

    public int RequestCount => Volatile.Read(ref requestCount);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _ = messages.ToArray();
        Interlocked.Increment(ref requestCount);

        if (mode is DeterministicChatMode.Timeout or DeterministicChatMode.Cancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (mode is DeterministicChatMode.Unavailable)
        {
            throw new HttpRequestException("The deterministic chat client is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new ChatResponse(
            new ChatMessage(
                ChatRole.Assistant,
                GetResponseText()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (ChatMessage message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private string GetResponseText() => mode switch
    {
        DeterministicChatMode.Unclear => UnclearResponse,
        DeterministicChatMode.Malformed => MalformedResponse,
        DeterministicChatMode.Timeout or DeterministicChatMode.Cancellation =>
            throw new InvalidOperationException(
                "A cancelled deterministic response cannot produce content."),
        DeterministicChatMode.Unavailable =>
            throw new InvalidOperationException(
                "An unavailable deterministic response cannot produce content."),
        _ => throw new InvalidOperationException(
            $"Unsupported deterministic chat mode '{mode}'.")
    };
}

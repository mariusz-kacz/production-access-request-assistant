using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed record ModelExecutionChatInvocation(
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions? Options,
    CancellationToken CancellationToken);

internal abstract class ModelExecutionTestChatClient : IChatClient
{
    private readonly ConcurrentQueue<ModelExecutionChatInvocation> invocations = [];

    public int InvocationCount => invocations.Count;

    public IReadOnlyList<ModelExecutionChatInvocation> Invocations =>
        invocations.ToArray();

    public ModelExecutionChatInvocation? LastInvocation =>
        invocations.ToArray().LastOrDefault();

    public abstract Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(
            messages,
            options,
            cancellationToken);

        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
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

    protected ModelExecutionChatInvocation RecordInvocation(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var invocation = new ModelExecutionChatInvocation(
            messages.ToArray(),
            options,
            cancellationToken);
        invocations.Enqueue(invocation);
        return invocation;
    }

    protected static ChatResponse CreateResponse(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        return new ChatResponse(
            new ChatMessage(ChatRole.Assistant, responseText));
    }
}

internal sealed class RecordingChatClient : ModelExecutionTestChatClient
{
    private readonly Func<ModelExecutionChatInvocation, Task<ChatResponse>>
        responseFactory;

    public RecordingChatClient(string responseText = "{}")
        : this(_ => Task.FromResult(CreateResponse(responseText)))
    {
    }

    public RecordingChatClient(
        Func<ModelExecutionChatInvocation, Task<ChatResponse>> responseFactory)
    {
        this.responseFactory = responseFactory
            ?? throw new ArgumentNullException(nameof(responseFactory));
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var invocation = RecordInvocation(messages, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return responseFactory(invocation);
    }
}

internal sealed class BlockingChatClient : ModelExecutionTestChatClient
{
    public TaskCompletionSource<ModelExecutionChatInvocation> Started { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<ModelExecutionChatInvocation> CancellationObserved
        { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var invocation = RecordInvocation(messages, options, cancellationToken);
        Started.TrySetResult(invocation);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult(invocation);
            }
        }

        throw new InvalidOperationException(
            "The blocking chat client unexpectedly completed without cancellation.");
    }
}

internal sealed class ThrowingChatClient(Exception exception)
    : ModelExecutionTestChatClient
{
    private readonly Exception exception = exception
        ?? throw new ArgumentNullException(nameof(exception));

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = RecordInvocation(messages, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<ChatResponse>(exception);
    }
}

internal sealed class SentinelChatClient(string responseText = "sentinel-response")
    : ModelExecutionTestChatClient
{
    private readonly ChatResponse response = CreateResponse(responseText);

    public TaskCompletionSource<ModelExecutionChatInvocation> Invoked { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var invocation = RecordInvocation(messages, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Invoked.TrySetResult(invocation);
        return Task.FromResult(response);
    }
}

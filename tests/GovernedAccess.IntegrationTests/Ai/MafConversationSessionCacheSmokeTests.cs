using GovernedAccess.Web.Ai;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class MafConversationSessionStoreSmokeTests
{
    [Fact]
    public async Task NativeStoreRestoresConversationHistoryForTheSameIntake()
    {
        var chatClient = new RecordingChatClient();
        var innerAgent = new ChatClientAgent(
            chatClient,
            instructions: "Reply deterministically.",
            name: "session-store-smoke-test",
            description: null,
            tools: null,
            NullLoggerFactory.Instance,
            services: null);
        AgentSessionStore sessionStore = new InMemoryAgentSessionStore();
        var hostAgent = new AIHostAgent(innerAgent, sessionStore);
        var coordinator = new MafConversationTurnCoordinator();
        var intakeId = Guid.NewGuid();

        _ = await coordinator.ExecuteTurnAsync(
            intakeId,
            hostAgent,
            (session, cancellationToken) => hostAgent.RunAsync(
                "first",
                session,
                options: null,
                cancellationToken),
            TestContext.Current.CancellationToken);

        _ = await coordinator.ExecuteTurnAsync(
            intakeId,
            hostAgent,
            (session, cancellationToken) => hostAgent.RunAsync(
                "second",
                session,
                options: null,
                cancellationToken),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.User && message.Text == "first");
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.Assistant && message.Text == "response-1");
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.User && message.Text == "second");
        Assert.Equal(1, coordinator.GateCount);
    }

    [Fact]
    public async Task StablePerIntakeGateSerializesTheWholeTurnBoundary()
    {
        var hostAgent = CreateHostAgent();
        var coordinator = new MafConversationTurnCoordinator();
        var intakeId = Guid.NewGuid();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var firstTurn = coordinator.ExecuteTurnAsync(
            intakeId,
            hostAgent,
            async (_, cancellationToken) =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return 1;
            },
            TestContext.Current.CancellationToken);

        await firstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        var secondTurn = coordinator.ExecuteTurnAsync(
            intakeId,
            hostAgent,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                secondEntered = true;
                return Task.FromResult(2);
            },
            TestContext.Current.CancellationToken);

        Assert.False(secondTurn.IsCompleted);
        Assert.False(secondEntered);

        releaseFirst.SetResult();

        Assert.Equal(1, await firstTurn);
        Assert.Equal(2, await secondTurn);
        Assert.True(secondEntered);
        Assert.Equal(1, coordinator.GateCount);
    }

    private static AIHostAgent CreateHostAgent()
    {
        var agent = new ChatClientAgent(
            new RecordingChatClient(),
            instructions: "Reply deterministically.",
            name: "coordinator-smoke-test",
            description: null,
            tools: null,
            NullLoggerFactory.Instance,
            services: null);

        return new AIHostAgent(agent, new InMemoryAgentSessionStore());
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());

            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        $"response-{Requests.Count}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
}

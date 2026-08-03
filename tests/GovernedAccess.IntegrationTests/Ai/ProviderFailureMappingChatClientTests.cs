using System.Reflection;
using Azure;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class ProviderFailureMappingChatClientTests
{
    [Fact]
    public async Task ForwardsMessagesOptionsAndCancellationToken()
    {
        var inner = new RecordingChatClient("forwarded-response");
        using var adapter = CreateAdapter(inner);
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "forward this turn"),
        };
        var options = new ChatOptions();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var response = await adapter.GetResponseAsync(
            messages,
            options,
            cancellation.Token);

        Assert.Equal("forwarded-response", response.Text);
        var invocation = Assert.IsType<ModelExecutionChatInvocation>(
            inner.LastInvocation);
        Assert.Equal(messages, invocation.Messages);
        Assert.Same(options, invocation.Options);
        Assert.Equal(cancellation.Token, invocation.CancellationToken);
    }

    [Fact]
    public async Task ProviderUnavailabilityMapsToProviderNeutralFailure()
    {
        var inner = new ThrowingChatClient(
            new RequestFailedException(503, "provider diagnostic"));
        using var adapter = CreateAdapter(inner);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => adapter.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "unavailable turn")],
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProviderTimeoutMapsToTimeoutWhenCallerIsStillActive()
    {
        var inner = new ThrowingChatClient(
            new TaskCanceledException("provider-side timeout"));
        using var adapter = CreateAdapter(inner);

        await Assert.ThrowsAsync<TimeoutException>(
            () => adapter.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "timed turn")],
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private static IChatClient CreateAdapter(IChatClient inner)
    {
        var adapterType = typeof(DeterministicChatClient).Assembly.GetType(
            "GovernedAccess.Web.Ai.ProviderFailureMappingChatClient");
        Assert.NotNull(adapterType);

        var constructor = Assert.Single(
            adapterType.GetConstructors(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType == typeof(IChatClient);
            });

        return Assert.IsAssignableFrom<IChatClient>(
            constructor.Invoke([inner]));
    }
}

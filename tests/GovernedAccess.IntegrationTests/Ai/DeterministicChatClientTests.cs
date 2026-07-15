using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class DeterministicChatClientTests
{
    [Theory]
    [InlineData(DeterministicChatMode.Valid, "\"durationMinutes\":240")]
    [InlineData(DeterministicChatMode.Incomplete, "\"durationMinutes\":null")]
    [InlineData(DeterministicChatMode.Malformed, "{\"clientId\":\"client-alpha\"")]
    [InlineData(DeterministicChatMode.Unsupported, "\"requestedRole\":\"ProductionAdministrator\"")]
    public async Task ContentModesReturnTheirDeterministicPayload(
        DeterministicChatMode mode,
        string expectedContent)
    {
        using var client = new DeterministicChatClient(mode);

        ChatResponse response = await client.GetResponseAsync(
            Array.Empty<ChatMessage>(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(expectedContent, response.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeterministicChatMode.Timeout)]
    [InlineData(DeterministicChatMode.Cancellation)]
    public async Task WaitingModesHonorCancellation(DeterministicChatMode mode)
    {
        using var client = new DeterministicChatClient(mode);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResponseAsync(
                Array.Empty<ChatMessage>(),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UnavailableModeFailsWithoutCallingALiveProvider()
    {
        using var client = new DeterministicChatClient(DeterministicChatMode.Unavailable);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync(
                Array.Empty<ChatMessage>(),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("The deterministic chat client is unavailable.", exception.Message);
    }
}

using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

[Collection(IntegrationTestCollections.FullApplication)]
public sealed class TeamsConversationQualityTests(HistorySensitiveTeamsFixture fixture)
    : IClassFixture<HistorySensitiveTeamsFixture>
{
    [Fact]
    public Task CompleteReadOnlyRequestReachesAccurateCard() =>
        VerifyConversationAsync(
            "complete-read-only",
            "Use PROD-ALPHA-EU with read-only access.",
            secondMessage: null,
            thirdMessage: null,
            expectedRoleId: ProductionRoleIds.ReadOnly);

    [Fact]
    public Task CompleteSupportRequestReachesAccurateCard() =>
        VerifyConversationAsync(
            "complete-support",
            "Use PROD-ALPHA-EU with support access.",
            secondMessage: null,
            thirdMessage: null,
            expectedRoleId: ProductionRoleIds.Support);

    [Fact]
    public Task EnvironmentThenDirectRoleReachesAccurateCard() =>
        VerifyConversationAsync(
            "environment-then-direct-support",
            "Use PROD-ALPHA-EU.",
            "support",
            thirdMessage: null,
            expectedRoleId: ProductionRoleIds.Support);

    [Fact]
    public Task DirectEnvironmentThenOrdinalRoleReachesAccurateCard() =>
        VerifyConversationAsync(
            "two-missing-direct-and-ordinal",
            "I need temporary production access.",
            "PROD-ALPHA-EU",
            "the first one",
            ProductionRoleIds.ReadOnly);

    [Fact]
    public Task OrdinalEnvironmentThenOtherRoleReachesAccurateCard() =>
        VerifyConversationAsync(
            "ordinal-environment-other-role",
            "I need temporary production access.",
            "the first one",
            "the other role",
            ProductionRoleIds.Support);

    private async Task VerifyConversationAsync(
        string scenarioName,
        string firstMessage,
        string? secondMessage,
        string? thirdMessage,
        string expectedRoleId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestCountBefore = await fixture.ResetAsync(cancellationToken);
        string?[] suppliedMessages =
            [firstMessage, secondMessage, thirdMessage];
        var messages = suppliedMessages
            .Where(message => message is not null)
            .Select(message => message!)
            .ToArray();
        Assert.InRange(messages.Length, 1, 5);

        var chatClient = fixture.ChatClient;
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();
        string? finalResponseBody = null;

        for (var index = 0; index < messages.Length; index++)
        {
            var responseBody = await SendMessageAsync(
                client,
                messages[index],
                $"{scenarioName}-{index + 1}",
                scenarioName,
                cancellationToken);
            var isFinalTurn = index == messages.Length - 1;
            if (!isFinalTurn)
            {
                Assert.DoesNotContain(
                    PreparedRequestCardFactory.AdaptiveCardContentType,
                    responseBody,
                    StringComparison.Ordinal);
            }

            finalResponseBody = responseBody;
        }

        Assert.Contains(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            finalResponseBody,
            StringComparison.Ordinal);
        Assert.Equal(
            requestCountBefore + messages.Length,
            chatClient.RequestCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(scenarioName, session.ConversationId);
        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", session.EnvironmentId);
        Assert.Equal(expectedRoleId, session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            session.Justification);
        Assert.Equal("INC-1042", session.IncidentId);
        Assert.NotNull(session.ReservedRequestId);
        Assert.Empty(
            await dbContext.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private static async Task<string> SendMessageAsync(
        HttpClient client,
        string text,
        string activityId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .WithActivityId(activityId)
            .WithConversation(conversationId)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;

        using var response = await client
            .PostAsJsonAsync(
                "/api/messages",
                activity,
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");
        return responseBody;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

[Trait(
    IntegrationTestCollections.TestLevelTrait,
    IntegrationTestCollections.FullHostLevel)]
public sealed class TeamsClarificationTests(HistorySensitiveTeamsFixture fixture)
    : IClassFixture<HistorySensitiveTeamsFixture>
{
    private const string ProviderClarification =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"environmentId","message":"Which Client Alpha production environment do you need?"}}
        """;

    [Fact]
    public async Task ProviderClarificationIsFocusedAndCreatesNoWorkflowState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var providerClient = new RecordingChatClient(ProviderClarification);
        await using var factory = new GovernedAccessWebFactory(
            providerClient,
            configurationOverrides: CreateFoundryResponsesProfileConfiguration());
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();

        var responseBody = await SendMessageAsync(
            client,
            "I need access for the active Client Alpha incident.",
            "provider-clarification-turn",
            cancellationToken);

        AssertClarification(
            responseBody,
            "Which Client Alpha production environment do you need?");
        Assert.Equal(1, providerClient.InvocationCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Collecting, session.Status);
        Assert.Empty(await dbContext.AccessRequests
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants
            .AsNoTracking()
            .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task DirectAndOrdinalRepliesCarryCandidateUntilItIsReady()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestCountBefore = await fixture.ResetAsync(cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var firstBody = await SendMessageAsync(
            client,
            "I need temporary production access.",
            "clarification-turn-1",
            cancellationToken);
        AssertClarification(
            firstBody,
            "Choose an environment: first PROD-ALPHA-EU or second PROD-BETA-UK.");

        var secondBody = await SendMessageAsync(
            client,
            "PROD-ALPHA-EU",
            "clarification-turn-2",
            cancellationToken);
        AssertClarification(
            secondBody,
            "Choose a role: first ProductionReadOnly or second ProductionSupport.");

        var thirdBody = await SendMessageAsync(
            client,
            "the first one",
            "clarification-turn-3",
            cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            session.Justification);
        Assert.Equal("INC-1042", session.IncidentId);
        Assert.NotNull(session.ReservedRequestId);
        AssertPreparedCard(thirdBody, session);
        Assert.Empty(await dbContext.AccessRequests
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AuditEvents
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Equal(requestCountBefore + 3, fixture.ChatClient.RequestCount);
    }

    private static async Task<string> SendMessageAsync(
        HttpClient client,
        string text,
        string activityId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .WithActivityId(activityId)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");
        return responseBody;
    }

    private static Dictionary<string, string?> CreateFoundryResponsesProfileConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "FoundryResponses",
            ["RequestPreparationModel:FoundryResponses:Endpoint"] =
                "https://governed-access.services.ai.azure.com/openai/v1",
            ["RequestPreparationModel:FoundryResponses:DeploymentName"] =
                "governed-access-chat",
        };

    private static void AssertClarification(
        string responseBody,
        string expectedMessage)
    {
        using var document = JsonDocument.Parse(responseBody);
        var activity = Assert.Single(
            document.RootElement
                .GetProperty("activities")
                .EnumerateArray()
                .ToArray());
        Assert.Equal(expectedMessage, activity.GetProperty("text").GetString());
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            responseBody,
            StringComparison.Ordinal);
    }

    private static void AssertPreparedCard(
        string responseBody,
        RequestIntakeSession session)
    {
        using var document = JsonDocument.Parse(responseBody);
        var activity = Assert.Single(
            document.RootElement
                .GetProperty("activities")
                .EnumerateArray()
                .ToArray());
        var attachment = Assert.Single(
            activity.GetProperty("attachments").EnumerateArray().ToArray());
        Assert.Equal(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            attachment.GetProperty("contentType").GetString());
        var card = attachment.GetProperty("content");
        var action = Assert.Single(
            card.GetProperty("actions").EnumerateArray().ToArray());
        Assert.Equal("Action.Execute", action.GetProperty("type").GetString());
        Assert.Equal(
            PreparedRequestCardFactory.ConfirmationVerb,
            action.GetProperty("verb").GetString());
        Assert.Equal(
            session.Id.ToString("D"),
            action.GetProperty("data").GetProperty("preparedRequestId").GetString());
        Assert.Contains(
            session.ReservedRequestId!.Value.ToString("D"),
            card.GetRawText(),
            StringComparison.Ordinal);
    }
}

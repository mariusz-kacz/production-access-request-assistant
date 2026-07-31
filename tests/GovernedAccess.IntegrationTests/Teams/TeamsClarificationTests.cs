using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

[Collection(IntegrationTestCollections.FullApplication)]
public sealed class TeamsClarificationTests(HistorySensitiveTeamsFixture fixture)
    : IClassFixture<HistorySensitiveTeamsFixture>
{
    [Fact]
    public async Task DirectAndOrdinalRepliesCarryCandidateUntilItIsReady()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestCountBefore = await fixture.ResetAsync(cancellationToken);
        var chatClient = fixture.ChatClient;
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var firstBody = await SendMessageAsync(
            client,
            "I need temporary production access.",
            "clarification-turn-1",
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);

        AssertClarification(
            firstBody,
            "Choose an environment: first PROD-ALPHA-EU or second PROD-BETA-UK.");

        Guid intakeId;
        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = firstScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var session = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            intakeId = session.Id;
            Assert.Equal(RequestIntakeStatus.Collecting, session.Status);
            Assert.Equal("client-alpha", session.ClientId);
            Assert.Null(session.EnvironmentId);
            Assert.Null(session.RequestedRoleId);
            Assert.Equal(
                "Investigate the active production incident.",
                session.Justification);
            Assert.Equal("INC-1042", session.IncidentId);
            Assert.Null(session.ReservedRequestId);

            await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
        }

        var secondBody = await SendMessageAsync(
            client,
            "PROD-ALPHA-EU",
            "clarification-turn-2",
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);

        AssertClarification(
            secondBody,
            "Choose a role: first ProductionReadOnly or second ProductionSupport.");

        await using (var secondScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = secondScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var session = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(intakeId, session.Id);
            Assert.Equal(RequestIntakeStatus.Collecting, session.Status);
            Assert.Equal("client-alpha", session.ClientId);
            Assert.Equal("PROD-ALPHA-EU", session.EnvironmentId);
            Assert.Null(session.RequestedRoleId);
            Assert.Equal(
                "Investigate the active production incident.",
                session.Justification);
            Assert.Equal("INC-1042", session.IncidentId);
            Assert.Null(session.ReservedRequestId);

            await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
        }

        var thirdBody = await SendMessageAsync(
            client,
            "the first one",
            "clarification-turn-3",
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);

        await using (var finalScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = finalScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var session = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(intakeId, session.Id);
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
            await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
            await AssertNoConversationArtifactsInSqliteAsync(
                dbContext,
                cancellationToken);
        }

        Assert.Equal(requestCountBefore + 3, chatClient.RequestCount);
    }

    [Fact]
    public async Task InterleavedActorsKeepTheirCandidatesAndHistoriesIsolated()
    {
        const string actorA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string actorB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        const string sharedConversation = "shared-isolation-probe";

        var cancellationToken = TestContext.Current.CancellationToken;
        var requestCountBefore = await fixture.ResetAsync(cancellationToken);
        var chatClient = fixture.ChatClient;
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var actorAFirstBody = await SendMessageAsync(
            client,
            "I need temporary production access.",
            "actor-a-turn-1",
            actorA,
            sharedConversation,
            cancellationToken);
        AssertClarification(
            actorAFirstBody,
            "Choose an environment: first PROD-ALPHA-EU or second PROD-BETA-UK.");

        var actorBFirstBody = await SendMessageAsync(
            client,
            "PROD-ALPHA-EU",
            "actor-b-turn-1",
            actorB,
            sharedConversation,
            cancellationToken);
        AssertClarification(
            actorBFirstBody,
            "Choose a role: first ProductionReadOnly or second ProductionSupport.");

        var actorASecondBody = await SendMessageAsync(
            client,
            "the first one",
            "actor-a-turn-2",
            actorA,
            sharedConversation,
            cancellationToken);
        AssertClarification(
            actorASecondBody,
            "Choose a role: first ProductionReadOnly or second ProductionSupport.");

        var actorBSecondBody = await SendMessageAsync(
            client,
            "the first one",
            "actor-b-turn-2",
            actorB,
            sharedConversation,
            cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .OrderBy(session => session.ChannelActorId)
            .ToArrayAsync(cancellationToken);

        Assert.Equal(2, sessions.Length);
        var actorASession = Assert.Single(
            sessions,
            session => session.ChannelActorId == actorA);
        var actorBSession = Assert.Single(
            sessions,
            session => session.ChannelActorId == actorB);

        Assert.NotEqual(actorASession.Id, actorBSession.Id);
        Assert.Equal(sharedConversation, actorASession.ConversationId);
        Assert.Equal(sharedConversation, actorBSession.ConversationId);

        Assert.Equal(RequestIntakeStatus.Collecting, actorASession.Status);
        Assert.Equal("PROD-ALPHA-EU", actorASession.EnvironmentId);
        Assert.Null(actorASession.RequestedRoleId);

        Assert.Equal(RequestIntakeStatus.Ready, actorBSession.Status);
        Assert.Equal("PROD-ALPHA-EU", actorBSession.EnvironmentId);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            actorBSession.RequestedRoleId);
        AssertPreparedCard(actorBSecondBody, actorBSession);

        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
        Assert.Equal(requestCountBefore + 4, chatClient.RequestCount);
    }

    [Fact]
    public async Task NewTextSupersedesReadySnapshotAndOldCardCannotSubmit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestCountBefore = await fixture.ResetAsync(cancellationToken);
        var chatClient = fixture.ChatClient;
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var oldCardBody = await SendMessageAsync(
            client,
            "Use PROD-ALPHA-EU with read-only access.",
            "start-over-turn-1",
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);

        RequestIntakeSession oldReadySession;
        JsonElement oldAction;
        await using (var readyScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = readyScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            oldReadySession = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(RequestIntakeStatus.Ready, oldReadySession.Status);
            oldAction = AssertPreparedCard(oldCardBody, oldReadySession);
            await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
        }

        var startOverBody = await SendMessageAsync(
            client,
            "Start over with a new production access request.",
            "start-over-turn-2",
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);
        AssertClarification(
            startOverBody,
            "Choose an environment: first PROD-ALPHA-EU or second PROD-BETA-UK.");

        await using (var supersededScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = supersededScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var sessions = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .OrderBy(session => session.CreatedAt)
                .ThenBy(session => session.Id)
                .ToArrayAsync(cancellationToken);

            Assert.Equal(2, sessions.Length);
            var superseded = Assert.Single(
                sessions,
                session => session.Id == oldReadySession.Id);
            var replacement = Assert.Single(
                sessions,
                session => session.Id != oldReadySession.Id);

            Assert.Equal(RequestIntakeStatus.Superseded, superseded.Status);
            Assert.Equal(
                oldReadySession.ReservedRequestId,
                superseded.ReservedRequestId);
            Assert.Null(superseded.ClientId);
            Assert.Null(superseded.EnvironmentId);
            Assert.Null(superseded.RequestedRoleId);
            Assert.Null(superseded.Justification);
            Assert.Null(superseded.IncidentId);

            Assert.Equal(RequestIntakeStatus.Collecting, replacement.Status);
            Assert.Equal("client-alpha", replacement.ClientId);
            Assert.Null(replacement.EnvironmentId);
            Assert.Null(replacement.RequestedRoleId);
            Assert.Null(replacement.ReservedRequestId);

            Assert.Contains(
                oldReadySession.ReservedRequestId!.Value.ToString("D"),
                oldCardBody,
                StringComparison.Ordinal);
            Assert.Contains(
                ProductionRoleIds.ReadOnly,
                oldCardBody,
                StringComparison.Ordinal);
            await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
        }

        using var oldConfirmationResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmationActivity(oldAction),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var oldConfirmationBody = await oldConfirmationResponse.Content
            .ReadAsStringAsync(cancellationToken);

        Assert.True(
            oldConfirmationResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received "
            + $"{(int)oldConfirmationResponse.StatusCode}: {oldConfirmationBody}");
        Assert.Contains(
            "can no longer be submitted",
            oldConfirmationBody,
            StringComparison.OrdinalIgnoreCase);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDbContext = finalScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        await AssertNoWorkflowStateAsync(finalDbContext, cancellationToken);
        Assert.Equal(requestCountBefore + 2, chatClient.RequestCount);
    }

    private static async Task<string> SendMessageAsync(
        HttpClient client,
        string text,
        string activityId,
        string actorId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .WithActivityId(activityId)
            .WithActor(actorId)
            .WithConversation(conversationId)
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

    private static JsonElement AssertPreparedCard(
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
            activity
                .GetProperty("attachments")
                .EnumerateArray()
                .ToArray());
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
            action
                .GetProperty("data")
                .GetProperty("preparedRequestId")
                .GetString());
        Assert.Contains(
            session.ReservedRequestId!.Value.ToString("D"),
            card.GetRawText(),
            StringComparison.Ordinal);
        Assert.Contains(
            session.RequestedRoleId!,
            card.GetRawText(),
            StringComparison.Ordinal);
        return action.Clone();
    }

    private static Activity CreateConfirmationActivity(JsonElement action) =>
        new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId("superseded-confirmation")
            .WithInvokeData(new
            {
                action,
            })
            .Build()
            .Activity;

    private static async Task AssertNoWorkflowStateAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Assert.Empty(
            await dbContext.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ApprovalDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ProvisioningOperations
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private static async Task AssertNoConversationArtifactsInSqliteAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var intakeColumns = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "PRAGMA table_info(\"RequestIntakeSessions\");";
                await using var reader = await command.ExecuteReaderAsync(
                    cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    intakeColumns.Add(reader.GetString(1));
                }
            }

            string[] expectedColumns =
            [
                "Channel",
                "ChannelActorId",
                "ClientId",
                "ConversationId",
                "CorrelationId",
                "CreatedAt",
                "EnvironmentId",
                "ExpiresAt",
                "Id",
                "IncidentId",
                "Justification",
                "LastUpdatedAt",
                "PersistenceVersion",
                "RequesterId",
                "RequestedRoleId",
                "ReservedRequestId",
                "Status",
                "SubmittedAt",
                "TenantId",
            ];
            Assert.Equal(
                expectedColumns.OrderBy(value => value, StringComparer.Ordinal),
                intakeColumns.OrderBy(value => value, StringComparer.Ordinal));

            var tableNames = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'table';";
                await using var reader = await command.ExecuteReaderAsync(
                    cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            Assert.DoesNotContain(
                tableNames,
                name => name.Contains("Option", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Transcript", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("History", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Maf", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("AgentSession", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }
}

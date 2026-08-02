using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
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
public sealed class TeamsRequestConfirmationTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task FirstConfirmationAtomicallySubmitsExactScopeWithoutGrantingAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var trustedWebBaseUri = new Uri(
            "https://trusted.governed-access.test/");
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(
            chatClient,
            trustedWebBaseUri);
        using var client = factory.CreateTeamsClient();

        string preparationBody;
        using (var preparationResponse = await client.PostAsJsonAsync(
                   "/api/messages",
                   CreateExpectRepliesMessage(CompleteRequest),
                   ProtocolJsonSerializer.SerializationOptions,
                   cancellationToken))
        {
            preparationBody = await preparationResponse.Content
                .ReadAsStringAsync(cancellationToken);
            Assert.True(
                preparationResponse.StatusCode == HttpStatusCode.OK,
                $"Expected 200 but received {(int)preparationResponse.StatusCode}: "
                + preparationBody);
        }
        Assert.Equal(1, chatClient.RequestCount);

        RequestIntakeSession sessionBeforeConfirmation;
        await using (var preparationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = preparationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            sessionBeforeConfirmation = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(
                RequestIntakeStatus.Ready,
                sessionBeforeConfirmation.Status);
            Assert.Equal(
                DemoPrincipalKeys.Requester,
                sessionBeforeConfirmation.RequesterId);
            Assert.Empty(
                await dbContext.AccessRequests
                    .AsNoTracking()
                    .ToListAsync(cancellationToken));
            Assert.Empty(
                await dbContext.AuditEvents
                    .AsNoTracking()
                    .ToListAsync(cancellationToken));
        }

        var confirmationAction = AssertCardContract(
            preparationBody,
            sessionBeforeConfirmation);
        using var confirmationResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmationActivity(confirmationAction),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        var confirmationBody = await confirmationResponse.Content
            .ReadAsStringAsync(cancellationToken);
        Assert.True(
            confirmationResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)confirmationResponse.StatusCode}: "
            + confirmationBody);

        var expectedRequestLink = new Uri(
            trustedWebBaseUri,
            $"requests/{sessionBeforeConfirmation.ReservedRequestId:D}");
        Assert.Contains(
            sessionBeforeConfirmation.ReservedRequestId!.Value.ToString("D"),
            confirmationBody,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedRequestLink.AbsoluteUri,
            confirmationBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "not yet approved or granted",
            confirmationBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "smba.trafficmanager.net",
            confirmationBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, chatClient.RequestCount);

        using var replayResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmationActivity(
                confirmationAction,
                "teams-confirmation-replay"),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var replayBody = await replayResponse.Content
            .ReadAsStringAsync(cancellationToken);
        Assert.True(
            replayResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)replayResponse.StatusCode}: "
            + replayBody);
        Assert.Contains(
            sessionBeforeConfirmation.ReservedRequestId.Value.ToString("D"),
            replayBody,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedRequestLink.AbsoluteUri,
            replayBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "was already submitted",
            replayBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "not yet approved or granted",
            replayBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, chatClient.RequestCount);

        await using var confirmationScope = factory.Services.CreateAsyncScope();
        var confirmationDbContext = confirmationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();

        var submittedSession = await confirmationDbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            sessionBeforeConfirmation.Id,
            submittedSession.Id);
        Assert.Equal(
            RequestIntakeStatus.Submitted,
            submittedSession.Status);
        Assert.Equal(
            sessionBeforeConfirmation.ReservedRequestId,
            submittedSession.ReservedRequestId);
        Assert.Equal(factory.Clock.UtcNow, submittedSession.SubmittedAt);
        Assert.Null(submittedSession.ClientId);
        Assert.Null(submittedSession.EnvironmentId);
        Assert.Null(submittedSession.RequestedRoleId);
        Assert.Null(submittedSession.Justification);
        Assert.Null(submittedSession.IncidentId);

        var request = await confirmationDbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(sessionBeforeConfirmation.ReservedRequestId, request.Id);
        Assert.Equal(DemoPrincipalKeys.Requester, request.RequesterId);
        Assert.Equal(sessionBeforeConfirmation.ClientId, request.ClientId);
        Assert.Equal(sessionBeforeConfirmation.EnvironmentId, request.EnvironmentId);
        Assert.Equal(
            sessionBeforeConfirmation.RequestedRoleId,
            request.RequestedRoleId);
        Assert.Equal(
            sessionBeforeConfirmation.Justification,
            request.Justification);
        Assert.Equal(sessionBeforeConfirmation.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Equal(factory.Clock.UtcNow, request.CreatedAt);

        var auditEvent = await confirmationDbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Equal(DemoPrincipalKeys.Requester, auditEvent.ActorId);
        Assert.Equal(factory.Clock.UtcNow, auditEvent.OccurredAt);

        Assert.Empty(
            await confirmationDbContext.ApprovalDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await confirmationDbContext.ProvisioningOperations
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await confirmationDbContext.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task ConfirmationBoundaryRejectsMalformedAndConcealsForeignActions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();
        var session = await SeedReadySessionAsync(factory, cancellationToken);
        var concealedMessage =
            "The prepared request could not be found for this authenticated conversation. No request was submitted.";

        var malformedCases = new object[]
        {
            new
            {
                schemaVersion = 2,
                preparedRequestId = session.Id.ToString("D"),
            },
            new
            {
                schemaVersion = 1,
                preparedRequestId = "not-a-guid",
            },
            new
            {
                schemaVersion = 1,
                preparedRequestId = session.Id.ToString("D"),
                requestedRoleId = ProductionRoleIds.Support,
            },
        };

        foreach (var malformedData in malformedCases)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/messages",
                CreateConfirmationActivity(malformedData),
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(
                session.ReservedRequestId!.Value.ToString("D"),
                body,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ClientId!,
                body,
                StringComparison.Ordinal);
        }

        var concealedCases = new[]
        {
            CreateConfirmationActivity(
                ValidConfirmationData(Guid.NewGuid())),
            CreateConfirmationActivity(
                ValidConfirmationData(session.Id),
                actorId: "foreign-actor"),
            CreateConfirmationActivity(
                ValidConfirmationData(session.Id),
                conversationId: "foreign-conversation"),
        };

        foreach (var activity in concealedCases)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/messages",
                activity,
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(concealedMessage, body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ReservedRequestId!.Value.ToString("D"),
                body,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ClientId!,
                body,
                StringComparison.Ordinal);
        }

        Assert.Equal(0, chatClient.RequestCount);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var persisted = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Ready, persisted.Status);
        Assert.Empty(await dbContext.AccessRequests.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AuditEvents.ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ApprovalDecisions.ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ProvisioningOperations.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.ToListAsync(cancellationToken));
    }

    private static Activity CreateExpectRepliesMessage(string text)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;
        return activity;
    }

    private static JsonElement AssertCardContract(
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
        Assert.Equal(
            ["$schema", "type", "version", "body", "actions"],
            card.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            "http://adaptivecards.io/schemas/adaptive-card.json",
            card.GetProperty("$schema").GetString());
        Assert.Equal("AdaptiveCard", card.GetProperty("type").GetString());
        Assert.Equal("1.5", card.GetProperty("version").GetString());
        Assert.DoesNotContain(
            "\"Input.",
            card.GetRawText(),
            StringComparison.Ordinal);

        var body = card.GetProperty("body");
        Assert.Equal(5, body.GetArrayLength());
        Assert.Equal(
            "Confirm production access request",
            body[0].GetProperty("text").GetString());
        Assert.Equal(
            "Review the immutable request below. Confirming submits it for "
            + "business approval; it does not approve or grant production access.",
            body[1].GetProperty("text").GetString());
        Assert.Equal("Justification", body[3].GetProperty("text").GetString());
        Assert.Equal(
            session.Justification,
            body[4].GetProperty("text").GetString());

        var facts = body[2].GetProperty("facts");
        Assert.Collection(
            facts.EnumerateArray().ToArray(),
            fact => AssertFact(
                fact,
                "Request ID",
                session.ReservedRequestId!.Value.ToString("D")),
            fact => AssertFact(
                fact,
                "Client",
                $"Client Alpha ({session.ClientId})"),
            fact => AssertFact(
                fact,
                "Environment",
                $"Client Alpha Production EU ({session.EnvironmentId})"),
            fact => AssertFact(
                fact,
                "Requested role",
                $"Production read-only ({session.RequestedRoleId})"),
            fact => AssertFact(
                fact,
                "Incident",
                $"Client Alpha production investigation ({session.IncidentId})"),
            fact => AssertFact(
                fact,
                "Access lifetime",
                "8 hours after provisioning"),
            fact => AssertFact(
                fact,
                "Confirm by",
                session.ExpiresAt!.Value.UtcDateTime.ToString(
                    "O",
                    CultureInfo.InvariantCulture)));

        var action = Assert.Single(
            card.GetProperty("actions").EnumerateArray().ToArray());
        Assert.Equal(
            ["type", "title", "verb", "associatedInputs", "data"],
            action.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Action.Execute", action.GetProperty("type").GetString());
        Assert.Equal("Confirm and submit", action.GetProperty("title").GetString());
        Assert.Equal(
            PreparedRequestCardFactory.ConfirmationVerb,
            action.GetProperty("verb").GetString());
        Assert.Equal(
            "none",
            action.GetProperty("associatedInputs").GetString());

        var actionData = action.GetProperty("data");
        Assert.Equal(
            ["schemaVersion", "preparedRequestId"],
            actionData.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            PreparedRequestCardFactory.ContractSchemaVersion,
            actionData.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            session.Id.ToString("D"),
            actionData.GetProperty("preparedRequestId").GetString());
        Assert.NotEqual(
            session.ReservedRequestId,
            session.Id);
        return action.Clone();
    }

    private static void AssertFact(
        JsonElement fact,
        string expectedTitle,
        string expectedValue)
    {
        Assert.Equal(expectedTitle, fact.GetProperty("title").GetString());
        Assert.Equal(expectedValue, fact.GetProperty("value").GetString());
    }

    private static Activity CreateConfirmationActivity(
        JsonElement action,
        string activityId = "teams-confirmation-activity")
    {
        return new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId(activityId)
            .WithInvokeData(new
            {
                action,
            })
            .Build()
            .Activity;
    }

    private static Activity CreateConfirmationActivity(
        object actionData,
        string? actorId = null,
        string? conversationId = null)
    {
        var builder = new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId($"teams-confirmation-{Guid.NewGuid():N}")
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    title = "Confirm and submit",
                    verb = PreparedRequestCardFactory.ConfirmationVerb,
                    associatedInputs = "none",
                    data = actionData,
                },
            });
        if (actorId is not null)
        {
            builder.WithActor(actorId);
        }

        if (conversationId is not null)
        {
            builder.WithConversation(conversationId);
        }

        return builder.Build().Activity;
    }

    private static object ValidConfirmationData(Guid preparationId) =>
        new
        {
            schemaVersion = PreparedRequestCardFactory.ContractSchemaVersion,
            preparedRequestId = preparationId.ToString("D"),
        };

    private static async Task<RequestIntakeSession> SeedReadySessionAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            DemoPrincipalKeys.Requester,
            factory.Clock.UtcNow,
            "hosted-boundary-preparation");
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            factory.Clock.UtcNow,
            "hosted-boundary-candidate");
        session.MarkReady(
            Guid.NewGuid(),
            factory.Clock.UtcNow,
            "hosted-boundary-ready");
        dbContext.RequestIntakeSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

}

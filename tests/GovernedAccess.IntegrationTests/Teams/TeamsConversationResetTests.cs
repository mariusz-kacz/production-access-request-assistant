using System.Net;
using System.Net.Http.Json;
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
public sealed class TeamsConversationResetTests
{
    private const string ResetGuidance =
        "Started a new request. Send an incident ID or production environment ID when you are ready.";

    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task CollectingResetSkipsChatAndCreatesCleanReplacementIdentityAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.HistorySensitive);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        var firstBody = await PostMessageAsync(
            client,
            "I need temporary production access.",
            "reset-collecting-first",
            cancellationToken);
        Assert.Contains(
            "Choose an environment",
            firstBody,
            StringComparison.Ordinal);
        Assert.Equal(1, chatClient.RequestCount);

        RequestIntakeSession abandoned;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            abandoned = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(RequestIntakeStatus.Collecting, abandoned.Status);
            Assert.NotNull(abandoned.ClientId);
        }

        var resetBody = await PostMessageAsync(
            client,
            "  /NEW  ",
            "reset-collecting-command",
            cancellationToken);

        Assert.Contains(ResetGuidance, resetBody, StringComparison.Ordinal);
        Assert.Equal(1, chatClient.RequestCount);

        var replacementBody = await PostMessageAsync(
            client,
            "the first one",
            "reset-collecting-replacement",
            cancellationToken);

        Assert.Contains(
            "Please choose an environment explicitly",
            replacementBody,
            StringComparison.Ordinal);
        Assert.Equal(2, chatClient.RequestCount);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await verificationDb.RequestIntakeSessions
            .AsNoTracking()
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, sessions.Count);

        var terminal = Assert.Single(
            sessions,
            session => session.Id == abandoned.Id);
        Assert.Equal(RequestIntakeStatus.Superseded, terminal.Status);
        Assert.Null(terminal.ClientId);
        Assert.Null(terminal.EnvironmentId);
        Assert.Null(terminal.RequestedRoleId);
        Assert.Null(terminal.Justification);
        Assert.Null(terminal.IncidentId);

        var replacement = Assert.Single(
            sessions,
            session => session.Id != abandoned.Id);
        Assert.Equal(RequestIntakeStatus.Collecting, replacement.Status);
        Assert.Null(replacement.EnvironmentId);
        Assert.Null(replacement.RequestedRoleId);
        Assert.NotEqual(abandoned.Id, replacement.Id);
        Assert.Empty(
            await verificationDb.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verificationDb.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task ReadyResetInvalidatesOldCardWithoutCreatingARequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        _ = await PostMessageAsync(
            client,
            CompleteRequest,
            "reset-ready-prepare",
            cancellationToken);
        var ready = await GetSingleSessionAsync(factory, cancellationToken);
        Assert.Equal(RequestIntakeStatus.Ready, ready.Status);

        var resetBody = await PostMessageAsync(
            client,
            "/new",
            "reset-ready-command",
            cancellationToken);
        Assert.Contains(ResetGuidance, resetBody, StringComparison.Ordinal);
        Assert.Equal(1, chatClient.RequestCount);

        var oldCardBody = await PostConfirmationAsync(
            client,
            ready.Id,
            "reset-ready-old-card",
            cancellationToken);
        Assert.Contains(
            "replaced by a newer preparation",
            oldCardBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, chatClient.RequestCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var persisted = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Superseded, persisted.Status);
        Assert.Empty(
            await dbContext.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task ResetLeavesSubmittedRequestImmutableAndStartsNoPreparation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        _ = await PostMessageAsync(
            client,
            CompleteRequest,
            "reset-submitted-prepare",
            cancellationToken);
        var ready = await GetSingleSessionAsync(factory, cancellationToken);
        _ = await PostConfirmationAsync(
            client,
            ready.Id,
            "reset-submitted-confirm",
            cancellationToken);

        var resetBody = await PostMessageAsync(
            client,
            "/new",
            "reset-submitted-command",
            cancellationToken);

        Assert.Contains(ResetGuidance, resetBody, StringComparison.Ordinal);
        Assert.Equal(1, chatClient.RequestCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var persistedSession = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var persistedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Submitted, persistedSession.Status);
        Assert.Equal(ready.ReservedRequestId, persistedSession.ReservedRequestId);
        Assert.Equal(ready.ReservedRequestId, persistedRequest.Id);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, persistedRequest.Status);
        Assert.Single(
            await dbContext.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/new")]
    [InlineData("/NEW")]
    [InlineData("  /new  ")]
    public async Task ExactTrimmedCommandIsCaseInsensitiveAndIdempotent(
        string command)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        var first = await PostMessageAsync(
            client,
            command,
            $"reset-exact-{Guid.NewGuid():N}",
            cancellationToken);
        var repeated = await PostMessageAsync(
            client,
            command,
            $"reset-repeat-{Guid.NewGuid():N}",
            cancellationToken);

        Assert.Contains(ResetGuidance, first, StringComparison.Ordinal);
        Assert.Contains(ResetGuidance, repeated, StringComparison.Ordinal);
        Assert.Equal(0, chatClient.RequestCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(
            await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/new please")]
    [InlineData("start /new")]
    [InlineData("/new/request")]
    public async Task MessagesMerelyContainingCommandTokenUseNormalPreparation(
        string message)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        _ = await PostMessageAsync(
            client,
            message,
            $"reset-non-command-{Guid.NewGuid():N}",
            cancellationToken);

        Assert.Equal(1, chatClient.RequestCount);
        var session = await GetSingleSessionAsync(factory, cancellationToken);
        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
    }

    [Fact]
    public async Task ResetIsBoundToExactAuthenticatedActorAndConversation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        await SeedCollectingSessionAsync(
            factory,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            "owned",
            cancellationToken);
        await SeedCollectingSessionAsync(
            factory,
            "foreign-actor",
            FakeTeamsActivityBuilder.DefaultConversationId,
            "foreign-actor",
            cancellationToken);
        await SeedCollectingSessionAsync(
            factory,
            FakeTeamsActivityBuilder.DefaultActorId,
            "foreign-conversation",
            "foreign-conversation",
            cancellationToken);
        using var client = factory.CreateTeamsClient();

        var body = await PostMessageAsync(
            client,
            "/new",
            "reset-isolation-command",
            cancellationToken);

        Assert.Contains(ResetGuidance, body, StringComparison.Ordinal);
        Assert.Equal(0, chatClient.RequestCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var owned = Assert.Single(
            sessions,
            session => session.ChannelActorId
                == FakeTeamsActivityBuilder.DefaultActorId
                && session.ConversationId
                == FakeTeamsActivityBuilder.DefaultConversationId);
        var foreignActor = Assert.Single(
            sessions,
            session => session.ChannelActorId == "foreign-actor");
        var foreignConversation = Assert.Single(
            sessions,
            session => session.ConversationId == "foreign-conversation");
        Assert.Equal(RequestIntakeStatus.Superseded, owned.Status);
        Assert.Equal(RequestIntakeStatus.Collecting, foreignActor.Status);
        Assert.Equal(
            RequestIntakeStatus.Collecting,
            foreignConversation.Status);
    }

    private static async Task<string> PostMessageAsync(
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
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task<string> PostConfirmationAsync(
        HttpClient client,
        Guid preparationId,
        string activityId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId(activityId)
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    title = "Confirm and submit",
                    verb = PreparedRequestCardFactory.ConfirmationVerb,
                    associatedInputs = "none",
                    data = new
                    {
                        schemaVersion =
                            PreparedRequestCardFactory.ContractSchemaVersion,
                        preparedRequestId = preparationId.ToString("D"),
                    },
                },
            })
            .Build()
            .Activity;
        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    private static async Task<RequestIntakeSession> GetSingleSessionAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        return await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
    }

    private static async Task SeedCollectingSessionAsync(
        GovernedAccessWebFactory factory,
        string actorId,
        string conversationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            actorId,
            conversationId,
            DemoPrincipalKeys.Requester,
            factory.Clock.UtcNow,
            correlationId);
        session.UpdateCandidate(
            "client-alpha",
            environmentId: null,
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042",
            factory.Clock.UtcNow,
            correlationId);
        dbContext.RequestIntakeSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

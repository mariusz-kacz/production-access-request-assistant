using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsRequestPreparationTests(ConfigurableTeamsFixture fixture)
    : IClassFixture<ConfigurableTeamsFixture>
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task MessagesRouteRequiresAuthenticationBeforeApiAndSpaFallbacks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.Candidate,
            cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient(authenticated: false);
        var activity = CreateExpectRepliesActivity(CompleteRequest);
        var routeEndpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();
        var messagesEndpoint = Assert.Single(
            routeEndpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/messages");
        var apiFallback = Assert.Single(
            routeEndpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/{**path}");

        Assert.True(messagesEndpoint.Order < apiFallback.Order);
        Assert.NotEmpty(messagesEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(messagesEndpoint.Metadata.GetMetadata<IAllowAnonymous>());

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 401 but received {(int)response.StatusCode}: {responseBody}");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            Assert.Empty(
                await dbContext.RequestIntakeSessions
                    .AsNoTracking()
                    .ToListAsync(cancellationToken));
        }

        await AssertNoWorkflowStateAsync(factory, cancellationToken);
    }

    [Fact]
    public async Task PersonalChatClarificationRendersOnlyAuthoritativeEnvironmentChoices()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string clarificationMessage =
            "I found two authoritative matches. PROD-GAMMA-US is only an invented prose example.";
        const string modelResponse =
            """
            {"kind":"clarification","candidate":{"clientId":null,"environmentId":null,"requestedRoleId":null,"justification":"Investigate elevated production error rates.","incidentId":null},"clarification":{"target":"environmentId","message":"I found two authoritative matches. PROD-GAMMA-US is only an invented prose example.","environmentOptionIds":["PROD-BETA-UK","PROD-ALPHA-EU"]}}
            """;

        await using var factory = new GovernedAccessWebFactory(
            new ScriptedChatClient(modelResponse));
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();
        var activity = CreateExpectRepliesActivity(
            "I need production access, but I am unsure which environment applies.");

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var responseJson = JsonDocument.Parse(responseBody);
        var responseActivity = Assert.Single(
            responseJson.RootElement
                .GetProperty("activities")
                .EnumerateArray()
                .ToArray());
        var responseMessage = Assert.IsType<string>(
            responseActivity.GetProperty("text").GetString());

        Assert.Contains(clarificationMessage, responseMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Authoritative environment choices:",
            responseMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Available production environments:",
            responseMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Available production environment:",
            responseMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "- Client Alpha \u2014 Primary Production EU (PROD-ALPHA-EU)",
            responseMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "- Client Beta \u2014 Primary Production UK (PROD-BETA-UK)",
            responseMessage,
            StringComparison.Ordinal);
        Assert.True(
            responseMessage.IndexOf("PROD-ALPHA-EU", StringComparison.Ordinal)
                < responseMessage.IndexOf("PROD-BETA-UK", StringComparison.Ordinal));
        Assert.Equal(
            1,
            responseMessage.Split(
                "PROD-GAMMA-US",
                StringSplitOptions.None).Length - 1);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(RequestIntakeSession.TeamsChannel, session.Channel);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, session.TenantId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultActorId,
            session.ChannelActorId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultConversationId,
            session.ConversationId);
        Assert.Equal(DemoPrincipalKeys.Requester, session.RequesterId);
        Assert.Equal(
            RequestIntakeStatus.Collecting,
            session.Status);
        Assert.Null(session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Equal(
            "Investigate elevated production error rates.",
            session.Justification);
        Assert.Null(session.IncidentId);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            session.CreatedAt);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            session.LastUpdatedAt);

        Assert.Null(session.ReservedRequestId);

        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task IncidentConflictCanContinueWithPreviouslyRequestedScopeWithoutRepeatingEnvironment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string conflictingRequest =
            "Prepare ProductionReadOnly access to RECOVERY-PROD-ALPHA-EU to diagnose "
            + "customer-facing errors for incident INC-1042.";
        const string conflictMessage =
            "The requested recovery scope conflicts with incident INC-1042's scope. "
            + "Should I continue with the recovery scope without the incident, keep "
            + "the incident scope, or use a compatible exact incident ID?";
        const string conflictResponse =
            """
            {"kind":"clarification","candidate":{"clientId":null,"environmentId":null,"requestedRoleId":null,"justification":"Diagnose customer-facing errors.","incidentId":null},"clarification":{"target":"incidentId","message":"The requested recovery scope conflicts with incident INC-1042's scope. Should I continue with the recovery scope without the incident, keep the incident scope, or use a compatible exact incident ID?","environmentOptionIds":[]}}
            """;
        const string resolvedResponse =
            """
            {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"RECOVERY-PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Diagnose customer-facing errors.","incidentId":null},"clarification":null}
            """;
        var chatClient = new ScriptedChatClient(
            conflictResponse,
            resolvedResponse);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();

        using var conflictHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(conflictingRequest),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var conflictBody = await conflictHttpResponse.Content.ReadAsStringAsync(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, conflictHttpResponse.StatusCode);
        using var conflictJson = JsonDocument.Parse(conflictBody);
        var conflictActivity = Assert.Single(
            conflictJson.RootElement
                .GetProperty("activities")
                .EnumerateArray()
                .ToArray());
        Assert.Equal(
            conflictMessage,
            conflictActivity.GetProperty("text").GetString());
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            conflictBody,
            StringComparison.Ordinal);

        using var resolvedHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(
                "Continue with the requested recovery scope without the incident."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var resolvedBody = await resolvedHttpResponse.Content.ReadAsStringAsync(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, resolvedHttpResponse.StatusCode);
        Assert.Contains("Review request draft", resolvedBody, StringComparison.Ordinal);
        Assert.Contains(
            "RECOVERY-PROD-ALPHA-EU",
            resolvedBody,
            StringComparison.Ordinal);
        Assert.Contains(
            ProductionRoleIds.ReadOnly,
            resolvedBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("INC-1042", resolvedBody, StringComparison.Ordinal);

        Assert.Equal(2, chatClient.InvocationCount);
        var secondInvocation = chatClient.Invocations[1];
        Assert.Contains(
            secondInvocation.Messages,
            message => message.Role == ChatRole.User
                && message.Text is not null
                && message.Text.Contains(conflictingRequest, StringComparison.Ordinal));
        var latestEnvelopeMessage = secondInvocation.Messages.Last(
            message => message.Role == ChatRole.User);
        using var latestEnvelope = JsonDocument.Parse(latestEnvelopeMessage.Text!);
        var currentCandidate = latestEnvelope.RootElement
            .GetProperty("currentCandidate");
        Assert.Equal(JsonValueKind.Null, currentCandidate.GetProperty("clientId").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            currentCandidate.GetProperty("environmentId").ValueKind);
        Assert.Equal(
            "Diagnose customer-facing errors.",
            currentCandidate.GetProperty("justification").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            currentCandidate.GetProperty("incidentId").ValueKind);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Equal("RECOVERY-PROD-ALPHA-EU", session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, session.RequestedRoleId);
        Assert.Equal("Diagnose customer-facing errors.", session.Justification);
        Assert.Null(session.IncidentId);
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task EnvironmentRevisionClarificationKeepsExistingDraftCardActiveUntilReplacementIsReady()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string justification = "Diagnose customer-facing errors.";
        const string initialResponse =
            """
            {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Diagnose customer-facing errors.","incidentId":null},"clarification":null}
            """;
        const string clarificationMessage =
            "Which Client Alpha production environment should replace the current one?";
        const string clarificationResponse =
            """
            {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":"ProductionReadOnly","justification":"Diagnose customer-facing errors.","incidentId":null},"clarification":{"target":"environmentId","message":"Which Client Alpha production environment should replace the current one?","environmentOptionIds":["PROD-ALPHA-EU","RECOVERY-PROD-ALPHA-EU"]}}
            """;
        const string replacementResponse =
            """
            {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"RECOVERY-PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Diagnose customer-facing errors.","incidentId":null},"clarification":null}
            """;
        var chatClient = new ScriptedChatClient(
            initialResponse,
            clarificationResponse,
            replacementResponse);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();
        var conversation = new TeamsConversationReference(
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            DemoPrincipalKeys.Requester);
        var cardTracker = factory.Services
            .GetRequiredService<TeamsDraftCardTracker>();

        using var initialHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(
                "Prepare read-only access to PROD-ALPHA-EU to diagnose customer-facing errors."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var initialBody = await initialHttpResponse.Content.ReadAsStringAsync(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, initialHttpResponse.StatusCode);
        Assert.Contains("Review request draft", initialBody, StringComparison.Ordinal);
        Assert.Contains("Demo Requester", initialBody, StringComparison.Ordinal);
        Assert.Contains("No incident", initialBody, StringComparison.Ordinal);
        Assert.Contains(
            "Requested access duration",
            initialBody,
            StringComparison.Ordinal);
        Assert.Contains("preparationId", initialBody, StringComparison.Ordinal);
        Assert.DoesNotContain("preparedRequestId", initialBody, StringComparison.Ordinal);
        Guid initialPreparationId;
        await using (var initialScope = factory.Services.CreateAsyncScope())
        {
            var initialDb = initialScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var initialDraft = await initialDb.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            initialPreparationId = initialDraft.Id;
            Assert.Equal(RequestIntakeStatus.Ready, initialDraft.Status);
        }

        cardTracker.Set(conversation, initialPreparationId, "initial-ready-card-activity");
        Assert.True(cardTracker.TryGet(conversation, out var initialCard));

        using var clarificationHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(
                "Change the environment to Client Alpha production."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var clarificationBody = await clarificationHttpResponse.Content
            .ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, clarificationHttpResponse.StatusCode);
        Assert.Contains(
            clarificationMessage,
            clarificationBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "RECOVERY-PROD-ALPHA-EU",
            clarificationBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Draft being revised",
            clarificationBody,
            StringComparison.Ordinal);
        Assert.True(cardTracker.TryGet(conversation, out var preservedCard));
        Assert.Equal(initialCard, preservedCard);

        await using (var clarificationScope = factory.Services.CreateAsyncScope())
        {
            var clarificationDb = clarificationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var activeDraft = await clarificationDb.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(initialCard.PreparationId, activeDraft.Id);
            Assert.Equal(RequestIntakeStatus.Ready, activeDraft.Status);
            Assert.Equal("PROD-ALPHA-EU", activeDraft.EnvironmentId);
            Assert.Equal(ProductionRoleIds.ReadOnly, activeDraft.RequestedRoleId);
            Assert.Equal(justification, activeDraft.Justification);
        }

        using var replacementHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity("Use the recovery environment."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var replacementBody = await replacementHttpResponse.Content
            .ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, replacementHttpResponse.StatusCode);
        Assert.Contains("Review request draft", replacementBody, StringComparison.Ordinal);
        Assert.Contains(
            "RECOVERY-PROD-ALPHA-EU",
            replacementBody,
            StringComparison.Ordinal);
        Assert.False(cardTracker.TryGet(conversation, out _));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var superseded = Assert.Single(
            sessions,
            session => session.Status == RequestIntakeStatus.Superseded);
        Assert.Equal(initialCard.PreparationId, superseded.Id);
        var ready = Assert.Single(
            sessions,
            session => session.Status == RequestIntakeStatus.Ready);
        Assert.NotEqual(initialCard.PreparationId, ready.Id);
        Assert.Equal("RECOVERY-PROD-ALPHA-EU", ready.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, ready.RequestedRoleId);
        Assert.Equal(justification, ready.Justification);
        Assert.Null(ready.IncidentId);
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task NaturalLanguageEditRevisesReadyDraftFromValidatedCandidate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string originalJustification =
            "Investigate customer-facing errors during the active incident.";
        const string revisedJustification =
            "Verify the mitigation for customer-facing errors during the active incident.";
        const string discussionMessage =
            "ProductionSupport and ProductionDeployment are also available for this environment.";
        const string initialResponse =
            """
            {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate customer-facing errors during the active incident.","incidentId":"INC-1042"},"clarification":null}
            """;
        const string revisedResponse =
            """
            {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Verify the mitigation for customer-facing errors during the active incident.","incidentId":"INC-1042"},"clarification":null}
            """;
        const string discussionResponse =
            """
            {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate customer-facing errors during the active incident.","incidentId":"INC-1042"},"clarification":{"target":"requestedRoleId","message":"ProductionSupport and ProductionDeployment are also available for this environment.","environmentOptionIds":[]}}
            """;
        var chatClient = new ScriptedChatClient(
            initialResponse,
            discussionResponse,
            revisedResponse);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();

        using var initialHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(CompleteRequest),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var initialBody = await initialHttpResponse.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialHttpResponse.StatusCode);
        Assert.Contains("Review request draft", initialBody, StringComparison.Ordinal);
        Assert.Contains(
            "To change any details, send another message.",
            initialBody,
            StringComparison.Ordinal);

        Guid discussedPreparationId;
        using (var discussionHttpResponse = await client.PostAsJsonAsync(
                   "/api/messages",
                   CreateExpectRepliesActivity(
                       "What other roles could I use for this environment?"),
                   ProtocolJsonSerializer.SerializationOptions,
                   cancellationToken))
        {
            var discussionBody = await discussionHttpResponse.Content
                .ReadAsStringAsync(cancellationToken);
            Assert.Equal(HttpStatusCode.OK, discussionHttpResponse.StatusCode);
            Assert.Contains(
                discussionMessage,
                discussionBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                PreparedRequestCardFactory.AdaptiveCardContentType,
                discussionBody,
                StringComparison.Ordinal);
        }

        await using (var discussionScope = factory.Services.CreateAsyncScope())
        {
            var discussionDb = discussionScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var discussedDraft = await discussionDb.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            discussedPreparationId = discussedDraft.Id;
            Assert.Equal(RequestIntakeStatus.Ready, discussedDraft.Status);
            Assert.Equal(originalJustification, discussedDraft.Justification);
            Assert.Equal(
                ProductionRoleIds.ReadOnly,
                discussedDraft.RequestedRoleId);
        }

        using var revisedHttpResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(
                "Change the justification to verify the mitigation."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var revisedBody = await revisedHttpResponse.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, revisedHttpResponse.StatusCode);
        Assert.Contains("Review request draft", revisedBody, StringComparison.Ordinal);
        Assert.Contains(revisedJustification, revisedBody, StringComparison.Ordinal);

        Assert.Equal(3, chatClient.InvocationCount);
        var revisionMessage = chatClient.Invocations[2].Messages.Last(
            message => message.Role == ChatRole.User);
        using var revisionEnvelope = JsonDocument.Parse(revisionMessage.Text!);
        var currentCandidate = revisionEnvelope.RootElement
            .GetProperty("currentCandidate");
        Assert.Equal(
            "client-alpha",
            currentCandidate.GetProperty("clientId").GetString());
        Assert.Equal(
            "PROD-ALPHA-EU",
            currentCandidate.GetProperty("environmentId").GetString());
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            currentCandidate.GetProperty("requestedRoleId").GetString());
        Assert.Equal(
            originalJustification,
            currentCandidate.GetProperty("justification").GetString());
        Assert.Equal(
            "INC-1042",
            currentCandidate.GetProperty("incidentId").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        Assert.Equal(2, sessions.Count);
        var superseded = Assert.Single(
            sessions,
            session => session.Status == RequestIntakeStatus.Superseded);
        Assert.Equal(discussedPreparationId, superseded.Id);
        Assert.Null(superseded.ClientId);
        var ready = Assert.Single(
            sessions,
            session => session.Status == RequestIntakeStatus.Ready);
        Assert.Equal("client-alpha", ready.ClientId);
        Assert.Equal("PROD-ALPHA-EU", ready.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, ready.RequestedRoleId);
        Assert.Equal(revisedJustification, ready.Justification);
        Assert.Equal("INC-1042", ready.IncidentId);
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task SelectedRealProfileProviderFailureIsSafeAndDoesNotFallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = CreateFoundryResponsesProfileConfiguration();
        var providerClient = new ThrowingChatClient(
            new HttpRequestException("offline provider unavailable"));

        await using var factory = new GovernedAccessWebFactory(
            providerClient,
            configurationOverrides: configuration);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = factory.CreateTeamsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(CompleteRequest),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Request preparation is temporarily unavailable.",
            responseBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            responseBody,
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
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

    private static Activity CreateExpectRepliesActivity(string text)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;
        return activity;
    }

    private static async Task AssertNoWorkflowStateAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

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

}

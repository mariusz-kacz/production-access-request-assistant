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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

[Trait(
    IntegrationTestCollections.TestLevelTrait,
    IntegrationTestCollections.FullHostLevel)]
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
        Assert.Contains(clarificationMessage, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Authoritative environment choices:",
            responseBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Available production environments:",
            responseBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Available production environment:",
            responseBody,
            StringComparison.Ordinal);
        Assert.Contains("- Client Alpha", responseBody, StringComparison.Ordinal);
        Assert.Contains(
            "Client Alpha Production EU",
            responseBody,
            StringComparison.Ordinal);
        Assert.Contains("PROD-ALPHA-EU", responseBody, StringComparison.Ordinal);
        Assert.Contains("Client Beta", responseBody, StringComparison.Ordinal);
        Assert.Contains(
            "Client Beta Production UK",
            responseBody,
            StringComparison.Ordinal);
        Assert.Contains("PROD-BETA-UK", responseBody, StringComparison.Ordinal);
        Assert.True(
            responseBody.IndexOf("PROD-ALPHA-EU", StringComparison.Ordinal)
                < responseBody.IndexOf("PROD-BETA-UK", StringComparison.Ordinal));
        Assert.Equal(
            1,
            responseBody.Split(
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

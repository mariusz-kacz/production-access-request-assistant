using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
    public async Task DefaultChatClientProducesCurrentCandidateContract()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.Candidate,
            cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity(CompleteRequest),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");

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
        Assert.Equal("INC-1042", session.IncidentId);
    }

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
    public async Task PersonalChatClarificationPersistsCandidateForFixedRequester()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.Clarification,
            cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();
        var activity = CreateExpectRepliesActivity(
            "I need read-only access to PROD-ALPHA-EU for INC-1042.");

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains(
            "What operational justification should be recorded for this request?",
            responseBody,
            StringComparison.Ordinal);

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
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, session.RequestedRoleId);
        Assert.Null(session.Justification);
        Assert.Equal("INC-1042", session.IncidentId);
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
    public async Task CandidateRejectionIdentifiesApplicationValidationProvenance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.InvalidCandidate,
            cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            CreateExpectRepliesActivity("Use PROD-UNKNOWN."),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var responseJson = JsonDocument.Parse(responseBody);
        var responseText = responseJson.RootElement
            .GetProperty("activities")[0]
            .GetProperty("text")
            .GetString()
            ?? string.Empty;
        Assert.Contains(
            "Deterministic application validation rejected the assistant's candidate.",
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            "The selected production environment does not exist.",
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Nothing has been submitted.",
            responseText,
            StringComparison.Ordinal);

        await AssertNoWorkflowStateAsync(factory, cancellationToken);
    }

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

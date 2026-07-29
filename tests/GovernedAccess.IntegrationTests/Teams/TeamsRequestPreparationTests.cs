using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
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

public sealed class TeamsRequestPreparationTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task MessagesRouteRequiresAuthenticationBeforeApiAndSpaFallbacks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate);
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
        await AssertNoWorkflowStateAsync(factory, cancellationToken);
    }

    [Fact]
    public async Task PersonalChatClarificationPersistsTypedStateForFixedRequester()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Clarification);
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
        var conversation = await dbContext.RequestPreparationConversations
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(RequestPreparationConversation.TeamsChannel, conversation.Channel);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, conversation.TenantId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultActorId,
            conversation.ChannelActorId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultConversationId,
            conversation.ConversationId);
        Assert.Equal(DemoPrincipalKeys.Requester, conversation.RequesterId);
        Assert.Equal(
            RequestPreparationConversationStatus.Collecting,
            conversation.Status);
        Assert.Equal("client-alpha", conversation.ClientId);
        Assert.Equal("PROD-ALPHA-EU", conversation.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, conversation.RequestedRoleId);
        Assert.Null(conversation.Justification);
        Assert.Equal("INC-1042", conversation.IncidentId);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            conversation.CreatedAt);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            conversation.LastTurnAt);

        var clarification = Assert.IsType<RequestClarificationContext>(
            conversation.PendingClarification);
        Assert.Equal(
            RequestClarificationTarget.Justification,
            clarification.Target);
        Assert.Equal(
            "What operational justification should be recorded for this request?",
            clarification.Prompt);
        Assert.Empty(clarification.Options);
        Assert.Null(conversation.ActivePreparationId);
        Assert.Empty(
            await dbContext.PreparedAccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));

        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task CompletePersonalChatCreatesDeterministicallyReadyEfSnapshotOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate);
        using var client = factory.CreateTeamsClient();
        var activity = CreateExpectRepliesActivity(CompleteRequest);

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains(
            "Confirm production access request",
            responseBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "it does not approve or grant production access",
            responseBody,
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var conversation = await dbContext.RequestPreparationConversations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var preparedRequest = await dbContext.PreparedAccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(
            RequestPreparationConversationStatus.Ready,
            conversation.Status);
        Assert.Equal(preparedRequest.PreparationId, conversation.ActivePreparationId);
        Assert.Null(conversation.PendingClarification);

        Assert.NotEqual(Guid.Empty, preparedRequest.PreparationId);
        Assert.NotEqual(Guid.Empty, preparedRequest.ReservedRequestId);
        Assert.Equal(conversation.Id, preparedRequest.ConversationRecordId);
        Assert.Equal(RequestPreparationConversation.TeamsChannel, preparedRequest.Channel);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultTenantId,
            preparedRequest.TenantId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultActorId,
            preparedRequest.ChannelActorId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultConversationId,
            preparedRequest.ConversationId);
        Assert.Equal(DemoPrincipalKeys.Requester, preparedRequest.RequesterId);
        Assert.Equal("client-alpha", preparedRequest.ClientId);
        Assert.Equal("PROD-ALPHA-EU", preparedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, preparedRequest.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            preparedRequest.Justification);
        Assert.Equal("INC-1042", preparedRequest.IncidentId);
        Assert.Equal(PreparedAccessRequestStatus.Ready, preparedRequest.Status);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            preparedRequest.CreatedAt);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow.Add(
                PreparedAccessRequest.ConfirmationLifetime),
            preparedRequest.ExpiresAt);
        Assert.Null(preparedRequest.SubmittedAt);
        Assert.Null(preparedRequest.SubmittedRequestId);

        Assert.Contains(
            preparedRequest.PreparationId.ToString("D"),
            responseBody,
            StringComparison.Ordinal);
        Assert.Contains(
            preparedRequest.ReservedRequestId.ToString("D"),
            responseBody,
            StringComparison.Ordinal);

        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
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

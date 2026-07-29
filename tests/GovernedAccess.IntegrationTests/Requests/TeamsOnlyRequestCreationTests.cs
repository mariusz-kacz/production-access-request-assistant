using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Requests;

public sealed class TeamsOnlyRequestCreationTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task TeamsConfirmationIsTheOnlyMappedRequestCreationPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate);
        await factory.ResetDatabaseAsync(cancellationToken);

        using var browserClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        using (var draftResponse = await browserClient.PostAsJsonAsync(
                   "/api/request-drafts/prepare",
                   new { intent = CompleteRequest },
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, draftResponse.StatusCode);
        }

        using (var createResponse = await browserClient.PostAsJsonAsync(
                   "/api/requests",
                   new
                   {
                       clientId = DemoDataIds.ClientAlphaId,
                       environmentId = DemoDataIds.ClientAlphaEnvironmentId,
                       requestedRole = ProductionRoleIds.ReadOnly,
                       justification = "Investigate the active production incident.",
                       incidentId = DemoDataIds.PrimaryIncidentId,
                   },
                   cancellationToken))
        {
            Assert.Contains(
                createResponse.StatusCode,
                new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }

        await AssertNoRequestEvidenceAsync(factory, cancellationToken);

        using var teamsClient = factory.CreateTeamsClient();
        var message = new FakeTeamsActivityBuilder()
            .WithText(CompleteRequest)
            .Build()
            .Activity;
        message.DeliveryMode = DeliveryModes.ExpectReplies;
        using (var preparationResponse = await teamsClient.PostAsJsonAsync(
                   "/api/messages",
                   message,
                   ProtocolJsonSerializer.SerializationOptions,
                   cancellationToken))
        {
            preparationResponse.EnsureSuccessStatusCode();
        }

        RequestIntakeSession session;
        await using (var preparationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = preparationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            session = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
        }

        var confirmation = new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    verb = PreparedRequestCardFactory.ConfirmationVerb,
                    data = new
                    {
                        schemaVersion =
                            PreparedRequestCardFactory.ContractSchemaVersion,
                        preparedRequestId = session.Id.ToString("D"),
                    },
                },
            })
            .Build()
            .Activity;
        using (var confirmationResponse = await teamsClient.PostAsJsonAsync(
                   "/api/messages",
                   confirmation,
                   ProtocolJsonSerializer.SerializationOptions,
                   cancellationToken))
        {
            confirmationResponse.EnsureSuccessStatusCode();
        }

        await using (var confirmationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = confirmationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var request = await dbContext.AccessRequests
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            var auditEvent = await dbContext.AuditEvents
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(session.ReservedRequestId, request.Id);
            Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
            Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
            Assert.Equal(request.Id, auditEvent.RequestId);
        }

        using (var listResponse = await browserClient.GetAsync(
                   "/api/requests",
                   cancellationToken))
        {
            listResponse.EnsureSuccessStatusCode();
        }

        using (var detailResponse = await browserClient.GetAsync(
                   $"/api/requests/{session.ReservedRequestId:D}",
                   cancellationToken))
        {
            detailResponse.EnsureSuccessStatusCode();
        }

        AssertRetainedWebEndpointInventory(factory);
    }

    private static async Task AssertNoRequestEvidenceAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(
            await dbContext.AccessRequests.AsNoTracking().ToArrayAsync(
                cancellationToken));
        Assert.Empty(
            await dbContext.AuditEvents.AsNoTracking().ToArrayAsync(
                cancellationToken));
    }

    private static void AssertRetainedWebEndpointInventory(
        GovernedAccessWebFactory factory)
    {
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var endpoints = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Select(method =>
                        $"{method} /{endpoint.RoutePattern.RawText}")
                ?? [])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET /api/requests", endpoints);
        Assert.Contains("GET /api/requests/{requestId:guid}", endpoints);
        Assert.Contains(
            "POST /api/requests/{requestId:guid}/business-decisions",
            endpoints);
        Assert.Contains(
            "POST /api/requests/{requestId:guid}/devops-decisions",
            endpoints);
        Assert.Contains(
            "POST /api/requests/{requestId:guid}/retry-provisioning",
            endpoints);
        Assert.DoesNotContain("POST /api/requests", endpoints);
        Assert.DoesNotContain("POST /api/request-drafts/prepare", endpoints);
    }
}

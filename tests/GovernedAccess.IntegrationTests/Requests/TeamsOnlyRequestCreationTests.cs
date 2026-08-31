using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Workflow.Persistence;
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
    public async Task BrowserRequestCreationPathsAreAbsentFromEndpointInventory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Unclear);
        await factory.ResetDatabaseAsync(cancellationToken);

        AssertRetainedWebEndpointInventory(factory);

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
    }

    private static async Task AssertNoRequestEvidenceAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        Assert.Empty(
            await dbContext.Set<AccessRequest>().AsNoTracking().ToArrayAsync(
                cancellationToken));
        Assert.Empty(
            await dbContext.Set<AuditEvent>().AsNoTracking().ToArrayAsync(
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Requests;

public sealed class RequestDetailQueryTests
{
    private const string BusinessDecisionAction = "decideBusinessRequest";
    private const string Justification = "Investigate the active production incident.";

    [Fact]
    public async Task RequesterReceivesImmutableScopeAndCurrentStatusWithoutBusinessAction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await SeedSubmittedRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var detail = body.RootElement;
        Assert.Equal(requestId, detail.GetProperty("requestId").GetGuid());
        Assert.Equal(
            DemoDataIds.RequesterPrincipalId,
            detail.GetProperty("requesterId").GetString());
        Assert.Equal(
            DemoDataIds.ClientAlphaId,
            detail.GetProperty("clientId").GetString());
        Assert.Equal(
            DemoDataIds.ClientAlphaEnvironmentId,
            detail.GetProperty("environmentId").GetString());
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            detail.GetProperty("requestedRoleId").GetString());
        Assert.Equal(Justification, detail.GetProperty("justification").GetString());
        Assert.Equal(
            DemoDataIds.PrimaryIncidentId,
            detail.GetProperty("incidentId").GetString());
        Assert.Equal(
            nameof(RequestStatus.AwaitingBusinessApproval),
            detail.GetProperty("status").GetString());
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            detail.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            detail.GetProperty("lastModifiedAt").GetDateTimeOffset());
        Assert.Empty(detail.GetProperty("availableActions").EnumerateArray());
        Assert.False(detail.TryGetProperty("persistenceVersion", out _));
    }

    [Fact]
    public async Task ConfiguredApproverReceivesServerComputedBusinessDecisionAction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await SeedSubmittedRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var actions = body.RootElement
            .GetProperty("availableActions")
            .EnumerateArray()
            .Select(action => action.GetString())
            .ToArray();
        Assert.Equal(BusinessDecisionAction, Assert.Single(actions));
    }

    [Fact]
    public async Task WrongClientApproverReceivesSafeNotFoundInsteadOfRequestDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await SeedSubmittedRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientBetaApprover,
            cancellationToken);

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "request_not_found",
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnonymousCallerIsRejectedBeforeRequestDetailIsDisclosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await SeedSubmittedRequestAsync(factory, cancellationToken);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DetailReflectsCurrentStatusAndRemovesActionAfterBusinessApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await SeedSubmittedRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var decision = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/business-decisions")
        {
            Content = JsonContent.Create(new
            {
                decision = "Approve",
                comment = "Approved for the incident investigation.",
            }),
        };
        using var decisionResponse = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            decision,
            cancellationToken);
        decisionResponse.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            nameof(RequestStatus.AwaitingDevOpsApproval),
            body.RootElement.GetProperty("status").GetString());
        Assert.Empty(
            body.RootElement.GetProperty("availableActions").EnumerateArray());
    }

    private static async Task<Guid> SeedSubmittedRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var request = new AccessRequest(
            requestId,
            DemoDataIds.RequesterPrincipalId,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            ProductionRoleIds.ReadOnly,
            Justification,
            DemoDataIds.PrimaryIncidentId,
            GovernedAccessWebFactory.DefaultUtcNow,
            correlationId: "request-detail-query-test");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        dbContext.AccessRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return requestId;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }
}

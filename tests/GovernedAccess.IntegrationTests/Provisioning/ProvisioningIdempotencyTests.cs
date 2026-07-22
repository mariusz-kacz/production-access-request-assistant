using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Provisioning;

public sealed class ProvisioningIdempotencyTests
{
    private const int ConcurrentAttemptCount = 100;

    [Fact]
    public async Task ConcurrentRetriesReturnOneOperationAndOneConsistentGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateLostResponseRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);

        var attempts = Enumerable.Range(0, ConcurrentAttemptCount)
            .Select(_ => RetryAsync(client, requestId, cancellationToken));
        var responses = await Task.WhenAll(attempts);

        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.All(responses, response => Assert.Equal(requestId, response.RequestId));
        Assert.All(responses, response => Assert.Equal("Active", response.Status));

        var responseGrantId = Assert.Single(
            responses.Select(response => response.GrantId).Distinct());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var operation = Assert.Single(await dbContext.ProvisioningOperations
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        var grant = Assert.Single(await dbContext.AccessGrants
            .AsNoTracking()
            .ToListAsync(cancellationToken));

        Assert.Equal(requestId, operation.RequestId);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
        Assert.Equal(ProtectedProvisioningService.SuccessCode, operation.LastOutcomeCode);
        Assert.Equal(requestId, grant.RequestId);
        Assert.Equal(responseGrantId, grant.Id);
        Assert.All(responses, response => Assert.Equal(grant.Id, response.GrantId));
    }

    private static async Task<RetryResponse> RetryAsync(
        HttpClient client,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/retry-provisioning");
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new RetryResponse(
                response.StatusCode,
                Guid.Empty,
                Status: responseContent,
                Guid.Empty);
        }

        using var document = JsonDocument.Parse(responseContent);
        var root = document.RootElement;
        return new RetryResponse(
            response.StatusCode,
            root.GetProperty("requestId").GetGuid(),
            root.GetProperty("status").GetString(),
            root.GetProperty("grant").GetProperty("grantId").GetGuid());
    }

    private static async Task<Guid> CreateLostResponseRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        var requestId = await CreateBusinessApprovedRequestAsync(
            factory,
            cancellationToken);
        var control = factory.Services
            .GetRequiredService<SyntheticAccessProvisionerControl>();
        control.Configure(SyntheticAccessProvisioningBehavior.LoseResponseAfterCreate);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var decisionRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/devops-decisions")
        {
            Content = JsonContent.Create(new
            {
                decision = "Approve",
                comment = "Provision the approved immutable scope.",
            }),
        };

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            decisionRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);

        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(SyntheticAccessProvisioner.LostResponseCode, operation.LastOutcomeCode);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));

        control.Configure(SyntheticAccessProvisioningBehavior.Succeed);
        return requestId;
    }

    private static async Task<Guid> CreateBusinessApprovedRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        using var requester = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/requests")
        {
            Content = JsonContent.Create(new
            {
                clientId = DemoDataIds.ClientAlphaId,
                environmentId = DemoDataIds.ClientAlphaEnvironmentId,
                requestedRole = ProductionRoleIds.ReadOnly,
                justification = "Investigate the active production incident.",
                incidentId = DemoDataIds.PrimaryIncidentId,
            }),
        };
        using var createResponse = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            requester,
            createRequest,
            cancellationToken);
        createResponse.EnsureSuccessStatusCode();
        using var createBody = await ReadJsonAsync(createResponse, cancellationToken);
        var requestId = createBody.RootElement.GetProperty("requestId").GetGuid();

        using var approver = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var approveRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/business-decisions")
        {
            Content = JsonContent.Create(new
            {
                decision = "Approve",
                comment = "The immutable business scope is approved.",
            }),
        };
        using var approveResponse = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            approver,
            approveRequest,
            cancellationToken);
        approveResponse.EnsureSuccessStatusCode();
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

    private sealed record RetryResponse(
        HttpStatusCode StatusCode,
        Guid RequestId,
        string? Status,
        Guid GrantId);
}

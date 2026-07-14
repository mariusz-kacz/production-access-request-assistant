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

public sealed class CreateRequestTests
{
    [Fact]
    public async Task ValidRequestIsBoundToTheAuthenticatedRequesterWithoutApprovalOrGrantSideEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var request = CreateRequestMessage(ValidBody());

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseBody = await ReadJsonAsync(response, cancellationToken);
        var requestId = responseBody.RootElement.GetProperty("requestId").GetGuid();
        Assert.NotEqual(Guid.Empty, requestId);
        Assert.Equal(1, responseBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            "AwaitingBusinessApproval",
            responseBody.RootElement.GetProperty("status").GetString());
        var correlationId = responseBody.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(requestId, storedRequest.Id);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, storedRequest.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaId, storedRequest.ClientId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, storedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, storedRequest.RequestedRoleId);
        Assert.Equal(240, storedRequest.RequestedDurationMinutes);
        Assert.Equal("Investigate the active production incident.", storedRequest.Justification);
        Assert.Equal(DemoDataIds.PrimaryIncidentId, storedRequest.IncidentId);
        Assert.Equal(1, storedRequest.Version);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, storedRequest.Status);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, storedRequest.CreatedAt);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, storedRequest.LastModifiedAt);
        Assert.Equal(correlationId, storedRequest.CorrelationId);
        Assert.Empty(await dbContext.ApprovalDecisions.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task CreationRevalidatesCurrentAuthoritativeStateBeforePersistingTheRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        await factory.ResetDatabaseAsync(cancellationToken);

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
            var environment = await dbContext.ProductionEnvironments.SingleAsync(
                item => item.Id == DemoDataIds.ClientAlphaEnvironmentId,
                cancellationToken);
            environment.UpdateMaximumDuration(120);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using var request = CreateRequestMessage(ValidBody());
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "request_validation_failed",
            problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(
            problem.RootElement.GetProperty("fieldErrors").EnumerateArray(),
            error => error.GetProperty("code").GetString()
                == "duration_exceeds_environment_maximum");
        await AssertNoProtectedWorkflowArtifactsAsync(factory, cancellationToken);
    }

    [Fact]
    public async Task BrowserSubmittedIdentityAndApproverClaimsCannotOverrideServerContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        await factory.ResetDatabaseAsync(cancellationToken);
        var body = ValidBody();
        body["requesterId"] = DemoDataIds.DevOpsApproverPrincipalId;
        body["actorId"] = DemoDataIds.ClientAlphaApproverPrincipalId;
        body["principalKey"] = DemoPrincipalKeys.DevOpsApprover;
        body["kind"] = "DevOpsApprover";
        body["roles"] = new[] { "DevOpsApprover", "BusinessApprover" };
        body["businessApproverId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        body["correlationId"] = "browser-controlled-correlation";
        using var request = CreateRequestMessage(body);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(DemoDataIds.RequesterPrincipalId, storedRequest.RequesterId);
        Assert.NotEqual("browser-controlled-correlation", storedRequest.CorrelationId);
        Assert.Empty(await dbContext.ApprovalDecisions.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task RequestCreationWithoutAntiforgeryIsRejectedWithoutPersistingState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        await factory.ResetDatabaseAsync(cancellationToken);
        using var request = CreateRequestMessage(ValidBody());

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "antiforgery_validation_failed",
            problem.RootElement.GetProperty("code").GetString());
        await AssertNoProtectedWorkflowArtifactsAsync(factory, cancellationToken);
    }

    private static Dictionary<string, object?> ValidBody()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["clientId"] = DemoDataIds.ClientAlphaId,
            ["environmentId"] = DemoDataIds.ClientAlphaEnvironmentId,
            ["requestedRole"] = ProductionRoleIds.ReadOnly,
            ["durationMinutes"] = 240,
            ["justification"] = "Investigate the active production incident.",
            ["incidentId"] = DemoDataIds.PrimaryIncidentId,
        };
    }

    private static HttpRequestMessage CreateRequestMessage(object body)
    {
        return new HttpRequestMessage(HttpMethod.Post, "/api/requests")
        {
            Content = JsonContent.Create(body),
        };
    }

    private static async Task AssertNoProtectedWorkflowArtifactsAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(await dbContext.AccessRequests.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ApprovalDecisions.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(cancellationToken));
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

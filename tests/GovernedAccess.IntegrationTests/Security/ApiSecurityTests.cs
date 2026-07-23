using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Security;

public sealed class ApiSecurityTests
{
    private static readonly Guid UnknownRequestId =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public async Task EveryProtectedApiSurfaceRejectsUnauthenticatedAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = CreateHttpsClient(factory);

        var requests = new[]
        {
            new Func<HttpRequestMessage>(
                () => new HttpRequestMessage(HttpMethod.Get, "/api/requests")),
            new Func<HttpRequestMessage>(
                () => new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/requests/{UnknownRequestId:D}")),
            new Func<HttpRequestMessage>(
                () => JsonRequest(
                    HttpMethod.Post,
                    "/api/request-drafts/prepare",
                    new { intent = "Investigate the active production incident." })),
            new Func<HttpRequestMessage>(
                () => JsonRequest(
                    HttpMethod.Post,
                    "/api/requests",
                    ValidCreateRequestBody())),
            new Func<HttpRequestMessage>(
                () => JsonRequest(
                    HttpMethod.Post,
                    $"/api/requests/{UnknownRequestId:D}/business-decisions",
                    new { decision = "Approve" })),
            new Func<HttpRequestMessage>(
                () => JsonRequest(
                    HttpMethod.Post,
                    $"/api/requests/{UnknownRequestId:D}/devops-decisions",
                    new { decision = "Approve" })),
            new Func<HttpRequestMessage>(
                () => JsonRequest(
                    HttpMethod.Post,
                    $"/api/requests/{UnknownRequestId:D}/retry-provisioning",
                    new { })),
        };

        foreach (var createRequest in requests)
        {
            using var request = createRequest();
            using var response = await client.SendAsync(request, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task EveryUnsafeApiEndpointRejectsRequestsWithoutAntiforgery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        AssertUnsafeApiEndpointInventory(factory);

        using var anonymousClient = CreateHttpsClient(factory);
        using var requesterClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        using var businessApproverClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var devOpsClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);

        await AssertAntiforgeryRejectedAsync(
            anonymousClient,
            JsonRequest(
                HttpMethod.Post,
                "/api/demo/session",
                new { principalKey = DemoPrincipalKeys.Requester }),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            requesterClient,
            new HttpRequestMessage(HttpMethod.Delete, "/api/demo/session"),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            requesterClient,
            JsonRequest(
                HttpMethod.Post,
                "/api/request-drafts/prepare",
                new { intent = "Investigate the active production incident." }),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            requesterClient,
            JsonRequest(
                HttpMethod.Post,
                "/api/requests",
                ValidCreateRequestBody()),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            businessApproverClient,
            JsonRequest(
                HttpMethod.Post,
                $"/api/requests/{UnknownRequestId:D}/business-decisions",
                new { decision = "Approve" }),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            devOpsClient,
            JsonRequest(
                HttpMethod.Post,
                $"/api/requests/{UnknownRequestId:D}/devops-decisions",
                new { decision = "Approve" }),
            cancellationToken);
        await AssertAntiforgeryRejectedAsync(
            devOpsClient,
            JsonRequest(
                HttpMethod.Post,
                $"/api/requests/{UnknownRequestId:D}/retry-provisioning",
                new { }),
            cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(await dbContext.AccessRequests.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.ApprovalDecisions.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task BrowserClaimsCannotOverrideActorsApprovedScopeOrGrantLifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);

        using var requesterClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        var createBody = ValidCreateRequestBody();
        AddOverpostedAuthority(
            createBody,
            actorId: DemoDataIds.DevOpsApproverPrincipalId,
            approverId: DemoDataIds.ClientBetaApproverPrincipalId);
        createBody["requesterId"] = DemoDataIds.DevOpsApproverPrincipalId;
        createBody["businessApproverId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        createBody["approvedRoleId"] = ProductionRoleIds.Support;
        createBody["durationHours"] = 72;

        using var createResponse = await SendWithAntiforgeryAsync(
            requesterClient,
            JsonRequest(HttpMethod.Post, "/api/requests", createBody),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createJson = await ReadJsonAsync(createResponse, cancellationToken);
        var requestId = createJson.RootElement.GetProperty("requestId").GetGuid();

        using var businessApproverClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        var businessBody = DecisionBody();
        AddOverpostedAuthority(
            businessBody,
            actorId: DemoDataIds.ClientBetaApproverPrincipalId,
            approverId: DemoDataIds.ClientBetaApproverPrincipalId);
        businessBody["businessApproverId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        businessBody["approvedRoleId"] = ProductionRoleIds.Support;
        businessBody["roleId"] = ProductionRoleIds.Support;

        using var businessResponse = await SendWithAntiforgeryAsync(
            businessApproverClient,
            JsonRequest(
                HttpMethod.Post,
                $"/api/requests/{requestId:D}/business-decisions",
                businessBody),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, businessResponse.StatusCode);

        using var devOpsClient = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        var devOpsBody = DecisionBody();
        AddOverpostedAuthority(
            devOpsBody,
            actorId: DemoDataIds.RequesterPrincipalId,
            approverId: DemoDataIds.ClientBetaApproverPrincipalId);
        devOpsBody["approvedRoleId"] = ProductionRoleIds.Support;
        devOpsBody["roleId"] = ProductionRoleIds.Support;
        devOpsBody["durationHours"] = 72;
        devOpsBody["approvedDurationMinutes"] = 4320;
        devOpsBody["expiresAt"] = GovernedAccessWebFactory.DefaultUtcNow.AddDays(30);

        using var devOpsResponse = await SendWithAntiforgeryAsync(
            devOpsClient,
            JsonRequest(
                HttpMethod.Post,
                $"/api/requests/{requestId:D}/devops-decisions",
                devOpsBody),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, devOpsResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decisions = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .OrderBy(item => item.Stage)
            .ToArrayAsync(cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var grant = await dbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(DemoDataIds.RequesterPrincipalId, storedRequest.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaId, storedRequest.ClientId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, storedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, storedRequest.RequestedRoleId);
        Assert.Equal(RequestStatus.Active, storedRequest.Status);

        Assert.Collection(
            decisions,
            businessDecision =>
            {
                Assert.Equal(ApprovalStage.Business, businessDecision.Stage);
                Assert.Equal(
                    DemoDataIds.ClientAlphaApproverPrincipalId,
                    businessDecision.ApproverId);
                Assert.Equal(ProductionRoleIds.ReadOnly, businessDecision.ApprovedRoleId);
            },
            devOpsDecision =>
            {
                Assert.Equal(ApprovalStage.DevOps, devOpsDecision.Stage);
                Assert.Equal(
                    DemoDataIds.DevOpsApproverPrincipalId,
                    devOpsDecision.ApproverId);
                Assert.Equal(ProductionRoleIds.ReadOnly, devOpsDecision.ApprovedRoleId);
            });

        Assert.Equal(requestId, operation.RequestId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, operation.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, operation.RoleId);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, grant.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, grant.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, grant.RoleId);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
    }

    [Fact]
    public async Task ApiAndMcpPathsNeverUseTheSpaFallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = CreateHttpsClient(factory);

        foreach (var path in new[] { "/api/security-probe", "/mcp/security-probe" })
        {
            using var response = await client.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("<!doctype html", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertUnsafeApiEndpointInventory(
        GovernedAccessWebFactory factory)
    {
        string[] expected =
        [
            "DELETE /api/demo/session",
            "POST /api/demo/session",
            "POST /api/request-drafts/prepare",
            "POST /api/requests",
            "POST /api/requests/{requestId:guid}/business-decisions",
            "POST /api/requests/{requestId:guid}/devops-decisions",
            "POST /api/requests/{requestId:guid}/retry-provisioning",
        ];
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var actual = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Select(method => new
                    {
                        Method = method,
                        Pattern = endpoint.RoutePattern.RawText,
                    })
                ?? [])
            .Where(item =>
                item.Pattern?.StartsWith("api/", StringComparison.Ordinal) == true
                && item.Method is not "GET" and not "HEAD" and not "OPTIONS")
            .Select(item => $"{item.Method} /{item.Pattern}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static Dictionary<string, object?> ValidCreateRequestBody()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["clientId"] = DemoDataIds.ClientAlphaId,
            ["environmentId"] = DemoDataIds.ClientAlphaEnvironmentId,
            ["requestedRole"] = ProductionRoleIds.ReadOnly,
            ["justification"] = "Investigate the active production incident.",
            ["incidentId"] = DemoDataIds.PrimaryIncidentId,
        };
    }

    private static Dictionary<string, object?> DecisionBody()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = "Approve",
            ["comment"] = "Approved after deterministic review.",
        };
    }

    private static void AddOverpostedAuthority(
        Dictionary<string, object?> body,
        string actorId,
        string approverId)
    {
        body["actorId"] = actorId;
        body["approverId"] = approverId;
        body["principalKey"] = DemoPrincipalKeys.DevOpsApprover;
        body["kind"] = "DevOpsApprover";
        body["roles"] = new[] { "Requester", "BusinessApprover", "DevOpsApprover" };
        body["claims"] = new Dictionary<string, string>
        {
            ["role"] = "DevOpsApprover",
            ["clientId"] = DemoDataIds.ClientBetaId,
        };
    }

    private static HttpRequestMessage JsonRequest(
        HttpMethod method,
        string path,
        object body)
    {
        return new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
    }

    private static HttpClient CreateHttpsClient(GovernedAccessWebFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
    }

    private static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            return await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                client,
                request,
                cancellationToken);
        }
    }

    private static async Task AssertAntiforgeryRejectedAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"Expected antiforgery rejection but received {(int)response.StatusCode} "
                + $"{response.StatusCode}: {responseBody}");
            using var problem = JsonDocument.Parse(responseBody);
            Assert.Equal(
                "antiforgery_validation_failed",
                problem.RootElement.GetProperty("code").GetString());
        }
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

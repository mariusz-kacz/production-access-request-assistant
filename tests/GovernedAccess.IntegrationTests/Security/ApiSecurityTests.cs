using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Workflow.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Security;

public sealed class ApiSecurityTests(DefaultWebApplicationFixture fixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private readonly GovernedAccessWebFactory factory = fixture.Factory;

    private static readonly Guid UnknownRequestId =
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public async Task EveryProtectedApiSurfaceRejectsUnauthenticatedAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync(cancellationToken);
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
        await factory.ResetDatabaseAsync(cancellationToken);
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

        await AssertNoWorkflowEvidenceAsync(cancellationToken);
    }

    [Fact]
    public async Task BrowserCannotCreateRequestsAndRejectedPostsHaveNoSideEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync(cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using var draftResponse = await SendWithAntiforgeryAsync(
            client,
            JsonRequest(
                HttpMethod.Post,
                "/api/request-drafts/prepare",
                new { intent = "Prepare a production access request." }),
            cancellationToken);
        using var createResponse = await SendWithAntiforgeryAsync(
            client,
            JsonRequest(
                HttpMethod.Post,
                "/api/requests",
                new
                {
                    clientId = DemoDataIds.ClientAlphaId,
                    environmentId = DemoDataIds.ClientAlphaEnvironmentId,
                    requestedRole = ProductionRoleIds.ReadOnly,
                    justification = "Investigate the active production incident.",
                    incidentId = DemoDataIds.PrimaryIncidentId,
                }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, draftResponse.StatusCode);
        Assert.Contains(
            createResponse.StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        await AssertNoWorkflowEvidenceAsync(cancellationToken);
    }

    [Fact]
    public async Task BrowserDecisionClaimsCannotOverrideActorsApprovedScopeOrGrantLifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync(cancellationToken);

        var requestId = (await factory.CreateRequestFixtureAsync(cancellationToken)).Id;

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
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var storedRequest = await dbContext.Set<AccessRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decisions = await dbContext.Set<ApprovalDecision>()
            .AsNoTracking()
            .OrderBy(item => item.Stage)
            .ToArrayAsync(cancellationToken);
        var operation = await dbContext.Set<ProvisioningOperation>()
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var grant = await dbContext.Set<AccessGrant>()
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(DemoDataIds.RequesterPrincipalId, storedRequest.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaId, storedRequest.Details.ClientId);
        Assert.Equal(
            DemoDataIds.ClientAlphaEnvironmentId,
            storedRequest.Details.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, storedRequest.Details.RoleId);
        Assert.Equal(RequestStatus.Active, storedRequest.Status);

        Assert.Collection(
            decisions,
            businessDecision =>
            {
                Assert.Equal(ApprovalStage.Business, businessDecision.Stage);
                Assert.Equal(
                    DemoDataIds.ClientAlphaApproverPrincipalId,
                    businessDecision.ApproverId);
            },
            devOpsDecision =>
            {
                Assert.Equal(ApprovalStage.DevOps, devOpsDecision.Stage);
                Assert.Equal(
                    DemoDataIds.DevOpsApproverPrincipalId,
                    devOpsDecision.ApproverId);
            });

        Assert.Equal(requestId, operation.RequestId);
        Assert.Equal(requestId, grant.RequestId);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
    }

    [Fact]
    public async Task ApiAndMcpPathsNeverUseTheSpaFallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync(cancellationToken);
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

    private async Task AssertNoWorkflowEvidenceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        Assert.Empty(await dbContext.Set<AccessRequest>().AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.Set<ApprovalDecision>().AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.Set<ProvisioningOperation>().AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.Set<AccessGrant>().AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.Set<AuditEvent>().AsNoTracking().ToListAsync(
            cancellationToken));
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

}

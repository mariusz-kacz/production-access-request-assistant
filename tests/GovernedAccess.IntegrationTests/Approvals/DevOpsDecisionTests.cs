using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GovernedAccess.IntegrationTests.Approvals;

public sealed class DevOpsDecisionTests
{
    [Fact]
    public async Task AuthenticatedDevOpsApprovalIgnoresCraftedScopeAndDuration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        var body = ValidDecisionBody("Approve", " Approved for production support. ");
        body["actorId"] = DemoDataIds.RequesterPrincipalId;
        body["approverId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        body["clientId"] = DemoDataIds.ClientBetaId;
        body["environmentId"] = DemoDataIds.ClientBetaEnvironmentId;
        body["approvedRoleId"] = ProductionRoleIds.Support;
        body["roleId"] = ProductionRoleIds.Support;
        body["durationHours"] = 72;
        body["approvedDurationMinutes"] = 4320;
        body["expiresAt"] = GovernedAccessWebFactory.DefaultUtcNow.AddDays(30);
        using var request = CreateDecisionMessage(requestId, body);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseBody = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal("Active", responseBody.RootElement.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decisions = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .Where(item => item.RequestId == requestId)
            .OrderBy(item => item.DecidedAt)
            .ToListAsync(cancellationToken);
        var devOpsDecision = Assert.Single(
            decisions,
            item => item.Stage == ApprovalStage.DevOps);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var grant = await dbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);

        Assert.Equal(RequestStatus.Active, storedRequest.Status);
        Assert.Equal(ApprovalOutcome.Approved, devOpsDecision.Decision);
        Assert.Equal(DemoDataIds.DevOpsApproverPrincipalId, devOpsDecision.ApproverId);
        Assert.Equal(ProductionRoleIds.ReadOnly, devOpsDecision.ApprovedRoleId);
        Assert.Equal("Approved for production support.", devOpsDecision.Comment);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, operation.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, operation.RoleId);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, grant.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, grant.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, grant.RoleId);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
        Assert.Equal(grant.ActivatedAt.AddHours(8), grant.ExpiresAt);
    }

    [Theory]
    [InlineData(DemoPrincipalKeys.Requester)]
    [InlineData(DemoPrincipalKeys.ClientAlphaApprover)]
    public async Task NonDevOpsPrincipalCannotDecideBusinessApprovedRequest(
        string principalKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            principalKey,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            ValidDecisionBody("Approve", null));

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNoDevOpsSideEffectsAsync(factory, requestId, cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.EventType == AuditEventType.AuthorizationRejected,
                cancellationToken);
        Assert.Equal(principalKey, auditEvent.ActorId);
    }

    [Fact]
    public async Task AnonymousPrincipalCannotDecideBusinessApprovedRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        using var request = CreateDecisionMessage(
            requestId,
            ValidDecisionBody("Approve", null));

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoDevOpsSideEffectsAsync(factory, requestId, cancellationToken);
    }

    [Fact]
    public async Task DevOpsRejectionCreatesNoProvisioningOperationOrGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            ValidDecisionBody("Reject", " Current operational risk is too high. "));

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseBody = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal("Rejected", responseBody.RootElement.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var devOpsDecision = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.Stage == ApprovalStage.DevOps,
                cancellationToken);

        Assert.Equal(RequestStatus.Rejected, storedRequest.Status);
        Assert.Equal(ApprovalOutcome.Rejected, devOpsDecision.Decision);
        Assert.Equal(DemoDataIds.DevOpsApproverPrincipalId, devOpsDecision.ApproverId);
        Assert.Null(devOpsDecision.ApprovedRoleId);
        Assert.Equal("Current operational risk is too high.", devOpsDecision.Comment);
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task DevOpsDecisionWithoutAntiforgeryHasNoWorkflowSideEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            ValidDecisionBody("Approve", null));

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "antiforgery_validation_failed",
            problem.RootElement.GetProperty("code").GetString());
        await AssertNoDevOpsSideEffectsAsync(factory, requestId, cancellationToken);
    }

    [Fact]
    public async Task TypedProvisioningFailureReturnsSafeProblemAndPersistsRetryableState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(factory, cancellationToken);
        var provisioner = new FailingAccessProvisioner();
        await using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessProvisioner>();
                services.AddSingleton<IAccessProvisioner>(provisioner);
            }));
        using var client = await CreateAuthenticatedClientAsync(
            failingFactory,
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            ValidDecisionBody("Approve", null));

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            FailingAccessProvisioner.FailureCode,
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("correlationId").GetString()));
        _ = Assert.Single(provisioner.Requests);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var devOpsDecision = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.Stage == ApprovalStage.DevOps,
                cancellationToken);

        Assert.Equal(RequestStatus.ProvisioningFailed, storedRequest.Status);
        Assert.Equal(ApprovalOutcome.Approved, devOpsDecision.Decision);
        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(FailingAccessProvisioner.FailureCode, operation.LastOutcomeCode);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    private static Dictionary<string, object?> ValidDecisionBody(
        string decision,
        string? comment)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = decision,
            ["comment"] = comment,
        };
    }

    private static HttpRequestMessage CreateDecisionMessage(Guid requestId, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/devops-decisions")
        {
            Content = JsonContent.Create(body),
        };
    }

    private static async Task<Guid> CreateBusinessApprovedRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        using var requester = await CreateAuthenticatedClientAsync(
            factory,
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

        using var approver = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var approveRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/business-decisions")
        {
            Content = JsonContent.Create(ValidDecisionBody("Approve", null)),
        };
        using var approveResponse = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            approver,
            approveRequest,
            cancellationToken);
        approveResponse.EnsureSuccessStatusCode();
        return requestId;
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string principalKey,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/session")
            {
                Content = JsonContent.Create(new { principalKey }),
            };
            using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                client,
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task AssertNoDevOpsSideEffectsAsync(
        GovernedAccessWebFactory factory,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decisions = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .Where(item => item.RequestId == requestId)
            .ToListAsync(cancellationToken);

        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, storedRequest.Status);
        Assert.DoesNotContain(decisions, item => item.Stage == ApprovalStage.DevOps);
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
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

    private sealed class FailingAccessProvisioner : IAccessProvisioner
    {
        public const string FailureCode = "synthetic_provisioning_failed";

        public List<AccessProvisioningRequest> Requests { get; } = [];

        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningFailed(
                    new ApplicationFailure(
                        ApplicationFailureKind.DependencyFailure,
                        FailureCode,
                        "Synthetic provisioning failed safely.")));
        }
    }
}

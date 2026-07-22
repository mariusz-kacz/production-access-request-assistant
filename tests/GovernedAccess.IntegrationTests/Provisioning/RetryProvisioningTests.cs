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

public sealed class RetryProvisioningTests
{
    private const string RetryNotAuthorizedCode =
        "provisioning_retry_not_authorized";
    private const string RetryInvalidTransitionCode =
        "provisioning_retry_invalid_transition";

    [Fact]
    public async Task LostResponseRetryReturnsExistingGrantForSameOperation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateRetryableFailedRequestAsync(
            factory,
            SyntheticAccessProvisioningBehavior.LoseResponseAfterCreate,
            cancellationToken);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var retryRequest = CreateRetryMessage(requestId);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            retryRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseBody = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(requestId, responseBody.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal("Active", responseBody.RootElement.GetProperty("status").GetString());
        var responseGrant = responseBody.RootElement.GetProperty("grant");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var grant = await dbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var auditEvents = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.RequestId == requestId)
            .ToListAsync(cancellationToken);

        Assert.Equal(RequestStatus.Active, storedRequest.Status);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
        Assert.Equal(2, operation.AttemptCount);
        Assert.Equal(requestId, operation.RequestId);
        Assert.Equal(ProtectedProvisioningService.SuccessCode, operation.LastOutcomeCode);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, grant.ActivatedAt);
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow.Add(AccessGrant.FixedLifetime),
            grant.ExpiresAt);
        Assert.Equal(grant.Id, responseGrant.GetProperty("grantId").GetGuid());
        Assert.Equal(
            grant.ActivatedAt,
            responseGrant.GetProperty("activatedAt").GetDateTimeOffset());
        Assert.Equal(
            grant.ExpiresAt,
            responseGrant.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(
            2,
            auditEvents.Count(item =>
                item.EventType == AuditEventType.ProvisioningAttempted));
        Assert.Single(
            auditEvents,
            item => item.EventType == AuditEventType.ProvisioningFailed);
        Assert.Single(
            auditEvents,
            item => item.EventType == AuditEventType.ProvisioningSucceeded);
    }

    [Theory]
    [InlineData(DemoPrincipalKeys.Requester)]
    [InlineData(DemoPrincipalKeys.ClientAlphaApprover)]
    public async Task NonDevOpsPrincipalCannotRetryFailedProvisioning(
        string principalKey)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateRetryableFailedRequestAsync(
            factory,
            SyntheticAccessProvisioningBehavior.Fail,
            cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            principalKey,
            cancellationToken);
        using var retryRequest = CreateRetryMessage(requestId);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            retryRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            RetryNotAuthorizedCode,
            problem.RootElement.GetProperty("code").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.EventType == AuditEventType.AuthorizationRejected
                    && item.OutcomeCode == RetryNotAuthorizedCode,
                cancellationToken);

        Assert.Equal(principalKey, auditEvent.ActorId);
        Assert.Equal(RequestStatus.ProvisioningFailed, storedRequest.Status);
        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task DevOpsCannotRetryRequestOutsideProvisioningFailedState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateBusinessApprovedRequestAsync(
            factory,
            cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var retryRequest = CreateRetryMessage(requestId);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            retryRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            RetryInvalidTransitionCode,
            problem.RootElement.GetProperty("code").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decisions = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .Where(item => item.RequestId == requestId)
            .ToListAsync(cancellationToken);
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.EventType == AuditEventType.InvalidTransitionRejected
                    && item.OutcomeCode == RetryInvalidTransitionCode,
                cancellationToken);

        Assert.Equal(DemoDataIds.DevOpsApproverPrincipalId, auditEvent.ActorId);
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, storedRequest.Status);
        Assert.DoesNotContain(decisions, item => item.Stage == ApprovalStage.DevOps);
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task RetryRejectsPersistedOperationScopeMismatchBeforeNewAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateRetryableFailedRequestAsync(
            factory,
            SyntheticAccessProvisioningBehavior.Fail,
            cancellationToken);
        await using (var mutationScope = factory.Services.CreateAsyncScope())
        {
            var mutationContext = mutationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            _ = await mutationContext.ProvisioningOperations
                .Where(item => item.RequestId == requestId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.RoleId,
                        ProductionRoleIds.Support),
                    cancellationToken);
        }

        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var retryRequest = CreateRetryMessage(requestId);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            retryRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            ProtectedProvisioningService.OperationScopeMismatchCode,
            problem.RootElement.GetProperty("code").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);

        Assert.Equal(RequestStatus.ProvisioningFailed, storedRequest.Status);
        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(ProductionRoleIds.Support, operation.RoleId);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    private static async Task<Guid> CreateRetryableFailedRequestAsync(
        GovernedAccessWebFactory factory,
        SyntheticAccessProvisioningBehavior behavior,
        CancellationToken cancellationToken)
    {
        var requestId = await CreateBusinessApprovedRequestAsync(
            factory,
            cancellationToken);
        var control = factory.Services
            .GetRequiredService<SyntheticAccessProvisionerControl>();
        control.Configure(behavior);
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
        using var problem = await ReadJsonAsync(response, cancellationToken);
        var expectedFailureCode = behavior switch
        {
            SyntheticAccessProvisioningBehavior.Fail =>
                SyntheticAccessProvisioner.FailureCode,
            SyntheticAccessProvisioningBehavior.LoseResponseAfterCreate =>
                SyntheticAccessProvisioner.LostResponseCode,
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null),
        };
        Assert.Equal(
            expectedFailureCode,
            problem.RootElement.GetProperty("code").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);

        Assert.Equal(RequestStatus.ProvisioningFailed, storedRequest.Status);
        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(expectedFailureCode, operation.LastOutcomeCode);
        Assert.Equal(1, operation.AttemptCount);
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

    private static HttpRequestMessage CreateRetryMessage(Guid requestId) =>
        new(HttpMethod.Post, $"/api/requests/{requestId:D}/retry-provisioning");

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

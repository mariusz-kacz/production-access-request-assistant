using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Provisioning;

public sealed class RetryProvisioningTests(DefaultWebApplicationFixture fixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private readonly GovernedAccessWebFactory factory = fixture.Factory;

    private const string RetryNotAuthorizedCode =
        "provisioning_retry_not_authorized";
    [Fact]
    public async Task NonDevOpsPrincipalCannotRetryFailedProvisioning()
    {
        const string principalKey = DemoPrincipalKeys.Requester;
        var cancellationToken = TestContext.Current.CancellationToken;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var storedRequest = await dbContext.Set<AccessRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.Set<ProvisioningOperation>()
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == requestId, cancellationToken);
        var auditEvent = await dbContext.Set<AuditEvent>()
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
        Assert.Empty(await dbContext.Set<AccessGrant>().AsNoTracking().ToListAsync(
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
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var storedRequest = await dbContext.Set<AccessRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var operation = await dbContext.Set<ProvisioningOperation>()
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
        var requestId = (await factory.CreateRequestFixtureAsync(cancellationToken)).Id;

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

public sealed class RetryProvisioningComponentTests
{
    [Fact]
    public async Task LostResponseRetryReturnsExistingGrantForSameOperation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var provisioner = new LostResponseProvisioner(fixture.Clock);
        var service = CreateService(scope.ServiceProvider, provisioner, fixture.Clock);
        var request = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);

        var initial = await service.DecideAsync(
            ApprovalStage.DevOps,
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "initial-provisioning",
            cancellationToken);
        Assert.True(initial.IsFailure);
        Assert.Equal(LostResponseProvisioner.FailureCode, initial.Failure!.Code);

        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var retry = await service.RetryProvisioningAsync(
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            "retry-correlation",
            cancellationToken);

        Assert.True(retry.IsSuccess);
        Assert.Equal(RequestStatus.Active, retry.Value.Request.Status);
        Assert.Equal(request.Id, retry.Value.Operation.RequestId);
        Assert.Equal(2, retry.Value.Operation.AttemptCount);
        Assert.Equal(
            ProtectedProvisioningService.SuccessCode,
            retry.Value.Operation.LastOutcomeCode);
        Assert.Equal(provisioner.GrantId, retry.Value.Grant.Id);
        Assert.Equal(
            provisioner.ActivatedAt.Add(AccessGrant.FixedLifetime),
            retry.Value.Grant.ExpiresAt);

        var auditEvents = await dbContext.Set<AuditEvent>()
            .Where(item => item.RequestId == request.Id)
            .ToListAsync(cancellationToken);
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

    [Fact]
    public async Task DevOpsCannotRetryRequestOutsideProvisioningFailedState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            scope.ServiceProvider,
            new LostResponseProvisioner(fixture.Clock),
            fixture.Clock);

        var outcome = await service.RetryProvisioningAsync(
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            "invalid-state-retry",
            cancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal(
            AccessRequestWorkflowService.ProvisioningRetryInvalidTransitionCode,
            outcome.Failure!.Code);
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
        Assert.Empty(await dbContext.Set<ProvisioningOperation>().ToListAsync(
            cancellationToken));
        Assert.Single(
            await dbContext.Set<AuditEvent>().ToListAsync(cancellationToken),
            item => item.EventType == AuditEventType.InvalidTransitionRejected);
    }

    private static AccessRequestWorkflowService CreateService(
        IServiceProvider services,
        IAccessProvisioner provisioner,
        DeterministicClock clock)
    {
        var requestContext = services.GetRequiredService<IRequestContextReader>();
        var workflowStore = services.GetRequiredService<IWorkflowStore>();
        return new AccessRequestWorkflowService(
            requestContext,
            workflowStore,
            new AccessRequestCommandContextLoader(requestContext, workflowStore),
            new AccessRequestValidator(requestContext),
            new ProtectedProvisioningService(workflowStore, provisioner, clock),
            clock);
    }

    private static async Task<AccessRequest> SeedBusinessApprovedRequestAsync(
        WorkflowDbContext dbContext,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var request = new AccessRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DemoDataIds.RequesterPrincipalId,
            new ValidatedRequestDetails(
                DemoDataIds.ClientAlphaId,
                DemoDataIds.ClientAlphaEnvironmentId,
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                DemoDataIds.PrimaryIncidentId),
            occurredAt,
            "request-correlation");
        var applied = Assert.IsType<ApprovalDecisionApplied>(
            ApprovalDecisionPolicy.Apply(
                request,
                ApprovalStage.Business,
                priorApproval: null,
                new ApprovalCommand(
                    Guid.NewGuid(),
                    ApprovalOutcome.Approved,
                    DemoDataIds.ClientAlphaApproverPrincipalId,
                    null,
                    occurredAt,
                    "business-correlation"),
                hasExistingDecision: false));
        dbContext.Set<AccessRequest>().Add(request);
        dbContext.Set<ApprovalDecision>().Add(applied.Decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    private sealed class LostResponseProvisioner(IClock clock) : IAccessProvisioner
    {
        public const string FailureCode = "synthetic_provisioning_response_lost";

        public Guid GrantId { get; } = Guid.NewGuid();

        public DateTimeOffset ActivatedAt { get; private set; }

        public int InvocationCount { get; private set; }

        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            if (InvocationCount == 1)
            {
                ActivatedAt = clock.UtcNow;
                return Task.FromResult<AccessProvisioningOutcome>(
                    new AccessProvisioningFailed(
                        new ApplicationFailure(
                            ApplicationFailureKind.DependencyFailure,
                            FailureCode,
                            "The provider created the grant but its response was lost.")));
            }

            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningSucceeded(GrantId, ActivatedAt));
        }
    }
}

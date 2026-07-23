using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Provisioning;

public sealed class ProtectedProvisioningTests
{
    private static readonly Guid RequestId =
        Guid.Parse("f91fb550-5a7d-4ef5-acf4-eaeccb13ea30");

    private static readonly Guid ProviderGrantId =
        Guid.Parse("90e1c586-b379-45f4-ad6f-b265c06e728c");

    private static readonly DateTimeOffset RequestCreatedAt =
        GovernedAccessWebFactory.DefaultUtcNow.AddHours(-2);

    private static readonly DateTimeOffset BusinessApprovedAt =
        GovernedAccessWebFactory.DefaultUtcNow.AddHours(-1);

    private static readonly DateTimeOffset DevOpsApprovedAt =
        GovernedAccessWebFactory.DefaultUtcNow.AddMinutes(-15);

    private static readonly DateTimeOffset ActivatedAt =
        GovernedAccessWebFactory.DefaultUtcNow.AddMinutes(1);

    [Fact]
    public async Task ProvisionAsyncReloadsPersistedWorkflowAndUsesStoredScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(factory, cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var workflowStore = new RecordingWorkflowStore(new EfWorkflowStore(dbContext));
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            workflowStore,
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        var completed = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        Assert.Equal(RequestId, completed.Request.Id);
        Assert.Equal(RequestId, completed.Operation.RequestId);
        Assert.Equal(ProviderGrantId, completed.Grant.Id);
        Assert.Equal(1, workflowStore.OperationReloads);
        Assert.Equal(1, workflowStore.RequestReloads);
        Assert.Contains(ApprovalStage.Business, workflowStore.ReadApprovalStages);
        Assert.Contains(ApprovalStage.DevOps, workflowStore.ReadApprovalStages);

        var providerRequest = Assert.Single(provisioner.Requests);
        Assert.Equal(RequestId, providerRequest.RequestId);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, providerRequest.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, providerRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, providerRequest.RoleId);
        Assert.Equal("devops-provisioning-correlation", providerRequest.CorrelationId);
    }

    [Theory]
    [InlineData(ApprovalStage.Business)]
    [InlineData(ApprovalStage.DevOps)]
    public async Task ProvisionAsyncRejectsMissingApprovalEvidence(
        ApprovalStage omittedStage)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(
            factory,
            includeBusinessApproval: omittedStage != ApprovalStage.Business,
            includeDevOpsApproval: omittedStage != ApprovalStage.DevOps,
            cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningFailed>(outcome);
        Assert.Empty(provisioner.Requests);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task ProvisionAsyncRejectsOperationScopeThatDoesNotMatchRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(
            factory,
            operationRoleId: ProductionRoleIds.Support,
            cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        var failed = Assert.IsType<ProtectedProvisioningFailed>(outcome);
        Assert.Equal(
            ProtectedProvisioningService.OperationScopeMismatchCode,
            failed.Failure.Code);
        Assert.Empty(provisioner.Requests);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task SuccessfulProvisioningPersistsExactlyEightHourGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(factory, cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var service = new ProtectedProvisioningService(
            new EfWorkflowStore(dbContext),
            new RecordingAccessProvisioner(ActivatedAt),
            factory.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        var grant = await dbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == RequestId, cancellationToken);
        var request = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == RequestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == RequestId, cancellationToken);

        Assert.Equal(ActivatedAt, grant.ActivatedAt);
        Assert.Equal(ActivatedAt.AddHours(8), grant.ExpiresAt);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
        Assert.Equal(RequestId, grant.RequestId);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, grant.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, grant.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, grant.RoleId);
        Assert.Equal(RequestStatus.Active, request.Status);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
    }

    [Fact]
    public async Task RetryAsyncValidatesStoredEvidenceBeforeAdvancingAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedFailedProvisioningAsync(factory, cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.RetryAsync(RequestId, cancellationToken);

        var completed = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        Assert.Equal(2, completed.Operation.AttemptCount);
        Assert.Equal(RequestStatus.Active, completed.Request.Status);
        Assert.Equal(ProviderGrantId, completed.Grant.Id);
        var providerRequest = Assert.Single(provisioner.Requests);
        Assert.Equal(RequestId, providerRequest.RequestId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, providerRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, providerRequest.RoleId);
    }

    [Fact]
    public async Task RetryAsyncRejectsStoredScopeMismatchWithoutAdvancingAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedFailedProvisioningAsync(
            factory,
            operationRoleId: ProductionRoleIds.Support,
            cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.RetryAsync(RequestId, cancellationToken);

        var failed = Assert.IsType<ProtectedProvisioningFailed>(outcome);
        Assert.Equal(
            ProtectedProvisioningService.OperationScopeMismatchCode,
            failed.Failure.Code);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == RequestId, cancellationToken);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Empty(provisioner.Requests);
    }

    private static async Task SeedFailedProvisioningAsync(
        GovernedAccessWebFactory factory,
        string? operationRoleId = null,
        CancellationToken cancellationToken = default)
    {
        await SeedAwaitingProvisioningAsync(
            factory,
            operationRoleId: operationRoleId,
            cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        _ = await dbContext.AccessRequests
            .Where(item => item.Id == RequestId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.Status,
                    RequestStatus.ProvisioningFailed),
                cancellationToken);
        _ = await dbContext.ProvisioningOperations
            .Where(item => item.RequestId == RequestId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.Status,
                        ProvisioningOperationStatus.Failed)
                    .SetProperty(
                        item => item.LastOutcomeCode,
                        "synthetic_provisioning_failed"),
                cancellationToken);
    }

    private static async Task SeedAwaitingProvisioningAsync(
        GovernedAccessWebFactory factory,
        bool includeBusinessApproval = true,
        bool includeDevOpsApproval = true,
        string? operationRoleId = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var request = new AccessRequest(
            RequestId,
            DemoDataIds.RequesterPrincipalId,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident safely.",
            DemoDataIds.PrimaryIncidentId,
            RequestCreatedAt,
            "request-correlation");
        var businessResult = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                Guid.Parse("1e206088-6778-40cb-8900-b59465252e14"),
                ApprovalOutcome.Approved,
                DemoDataIds.ClientAlphaApproverPrincipalId,
                null,
                BusinessApprovedAt,
                "business-correlation"),
            hasExistingBusinessDecision: false);
        var businessApproval = Assert.IsType<BusinessDecisionApplied>(businessResult).Decision;
        var devOpsApproval = new ApprovalDecision(
            Guid.Parse("266ae120-70bc-4af2-9a62-453591247ecc"),
            request.Id,
            ApprovalStage.DevOps,
            ApprovalOutcome.Approved,
            DemoDataIds.DevOpsApproverPrincipalId,
            request.RequestedRoleId,
            null,
            DevOpsApprovedAt,
            "devops-provisioning-correlation");
        var operation = new ProvisioningOperation(
            request.Id,
            request.EnvironmentId,
            operationRoleId ?? request.RequestedRoleId,
            DevOpsApprovedAt);

        dbContext.AccessRequests.Add(request);
        if (includeBusinessApproval)
        {
            dbContext.ApprovalDecisions.Add(businessApproval);
        }

        if (includeDevOpsApproval)
        {
            dbContext.ApprovalDecisions.Add(devOpsApproval);
        }

        dbContext.ProvisioningOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class RecordingAccessProvisioner(DateTimeOffset activatedAt)
        : IAccessProvisioner
    {
        public List<AccessProvisioningRequest> Requests { get; } = [];

        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningSucceeded(ProviderGrantId, activatedAt));
        }
    }

    private sealed class RecordingWorkflowStore(IWorkflowStore inner) : IWorkflowStore
    {
        public int RequestReloads { get; private set; }

        public int OperationReloads { get; private set; }

        public List<ApprovalStage> ReadApprovalStages { get; } = [];

        public void AddRequest(AccessRequest request) => inner.AddRequest(request);

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            inner.GetRequestAsync(requestId, cancellationToken);

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken)
        {
            RequestReloads++;
            return inner.ReloadRequestAsync(requestId, cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
            CancellationToken cancellationToken) =>
            inner.ListRequestsAsync(cancellationToken);

        public void AddApprovalDecision(ApprovalDecision decision) =>
            inner.AddApprovalDecision(decision);

        public Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
            Guid requestId,
            ApprovalStage stage,
            CancellationToken cancellationToken)
        {
            ReadApprovalStages.Add(stage);
            return inner.GetApprovalDecisionAsync(requestId, stage, cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            inner.ListApprovalDecisionsAsync(requestId, cancellationToken);

        public void AddProvisioningOperation(ProvisioningOperation operation) =>
            inner.AddProvisioningOperation(operation);

        public Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            inner.GetProvisioningOperationAsync(requestId, cancellationToken);

        public Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken)
        {
            OperationReloads++;
            return inner.ReloadProvisioningOperationAsync(requestId, cancellationToken);
        }

        public void AddAccessGrant(AccessGrant grant) => inner.AddAccessGrant(grant);

        public Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            inner.GetAccessGrantForRequestAsync(requestId, cancellationToken);

        public void AddAuditEvent(AuditEvent auditEvent) => inner.AddAuditEvent(auditEvent);

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            inner.ListAuditEventsAsync(requestId, cancellationToken);

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);
    }
}

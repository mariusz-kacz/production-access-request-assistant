using System.Reflection;
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
    private const string OperationId =
        "6332e3df91c40a41cd39c880f4919fb8b2d90a79da76b850e4d235dbb451fae5";

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
    public async Task ProvisionAsyncReloadsAuthoritativeWorkflowAndUsesStoredScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(factory, cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var workflowStore = new RecordingWorkflowStore(new EfWorkflowStore(dbContext));
        var requestContext = new ControlledRequestContextReader(
            new EfRequestContextReader(dbContext));
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            requestContext,
            workflowStore,
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(OperationId, cancellationToken);

        var completed = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        Assert.Equal(RequestId, completed.Request.Id);
        Assert.Equal(OperationId, completed.Operation.Id);
        Assert.Equal(ProviderGrantId, completed.Grant.Id);
        Assert.True(workflowStore.ReloadedOperation);
        Assert.True(workflowStore.ReloadedRequest);
        Assert.Contains(ApprovalStage.Business, workflowStore.ReadApprovalStages);
        Assert.Contains(ApprovalStage.DevOps, workflowStore.ReadApprovalStages);

        var providerRequest = Assert.Single(provisioner.Requests);
        Assert.Equal(OperationId, providerRequest.OperationId);
        Assert.Equal(RequestId, providerRequest.RequestId);
        Assert.Equal(DemoDataIds.RequesterPrincipalId, providerRequest.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, providerRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, providerRequest.RoleId);
        Assert.Equal("devops-provisioning-correlation", providerRequest.CorrelationId);
    }

    [Theory]
    [InlineData(StaleContext.Environment)]
    [InlineData(StaleContext.Role)]
    [InlineData(StaleContext.Incident)]
    public async Task ProvisionAsyncRevalidatesCurrentEnvironmentRoleAndIncident(
        StaleContext staleContext)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await SeedAwaitingProvisioningAsync(factory, cancellationToken: cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var requestContext = new ControlledRequestContextReader(
            new EfRequestContextReader(dbContext));
        requestContext.MakeStale(staleContext);
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            requestContext,
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(OperationId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningFailed>(outcome);
        Assert.Empty(provisioner.Requests);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
        var request = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == RequestId, cancellationToken);
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
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
            cancellationToken);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            new EfRequestContextReader(dbContext),
            new EfWorkflowStore(dbContext),
            provisioner,
            factory.Clock);

        var outcome = await service.ProvisionAsync(OperationId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningFailed>(outcome);
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
            new EfRequestContextReader(dbContext),
            new EfWorkflowStore(dbContext),
            new RecordingAccessProvisioner(ActivatedAt),
            factory.Clock);

        var outcome = await service.ProvisionAsync(OperationId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        var grant = await dbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(item => item.OperationId == OperationId, cancellationToken);
        var request = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == RequestId, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == OperationId, cancellationToken);

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

    private static async Task SeedAwaitingProvisioningAsync(
        GovernedAccessWebFactory factory,
        bool includeBusinessApproval = true,
        bool includeDevOpsApproval = true,
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
            OperationId,
            request.Id,
            request.EnvironmentId,
            request.RequestedRoleId,
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

    public enum StaleContext
    {
        Environment,
        Role,
        Incident,
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

    private sealed class ControlledRequestContextReader(IRequestContextReader inner)
        : IRequestContextReader
    {
        private StaleContext? staleContext;

        public void MakeStale(StaleContext value)
        {
            staleContext = value;
        }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            inner.GetClientAsync(clientId, cancellationToken);

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            return staleContext == StaleContext.Environment
                ? Task.FromResult(ApplicationResult.Succeeded(
                    new ProductionEnvironment(
                        environmentId,
                        DemoDataIds.ClientBetaId,
                        "Changed production environment",
                        DemoDataIds.ClientBetaApproverPrincipalId)))
                : inner.GetProductionEnvironmentAsync(environmentId, cancellationToken);
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            return staleContext == StaleContext.Role
                ? Task.FromResult(ApplicationResult.Failed<EnvironmentRole>(
                    new ApplicationFailure(
                        ApplicationFailureKind.NotFound,
                        "environment-role-not-found",
                        "The role is no longer assigned to the environment.")))
                : inner.GetEnvironmentRoleAsync(environmentId, roleId, cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
            string environmentId,
            CancellationToken cancellationToken) =>
            inner.GetEnvironmentRolesAsync(environmentId, cancellationToken);

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            return staleContext == StaleContext.Incident
                ? Task.FromResult(ApplicationResult.Succeeded(
                    new Incident(
                        incidentId,
                        DemoDataIds.ClientAlphaId,
                        DemoDataIds.ClientAlphaEnvironmentId,
                        "Incident closed after business approval",
                        IncidentStatus.Inactive)))
                : inner.GetIncidentAsync(incidentId, cancellationToken);
        }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken) =>
            inner.GetPrincipalAsync(principalId, cancellationToken);
    }

    private sealed class RecordingWorkflowStore(IWorkflowStore inner) : IWorkflowStore
    {
        public bool ReloadedRequest { get; private set; }

        public bool ReloadedOperation { get; private set; }

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
            ReloadedRequest = true;
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
            string operationId,
            CancellationToken cancellationToken) =>
            inner.GetProvisioningOperationAsync(operationId, cancellationToken);

        public Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
            string operationId,
            CancellationToken cancellationToken)
        {
            ReloadedOperation = true;
            return inner.ReloadProvisioningOperationAsync(operationId, cancellationToken);
        }

        public Task<ApplicationResult<ProvisioningOperation>>
            GetProvisioningOperationForRequestAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            inner.GetProvisioningOperationForRequestAsync(requestId, cancellationToken);

        public void AddAccessGrant(AccessGrant grant) => inner.AddAccessGrant(grant);

        public Task<ApplicationResult<AccessGrant>> GetAccessGrantByOperationAsync(
            string operationId,
            CancellationToken cancellationToken) =>
            inner.GetAccessGrantByOperationAsync(operationId, cancellationToken);

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

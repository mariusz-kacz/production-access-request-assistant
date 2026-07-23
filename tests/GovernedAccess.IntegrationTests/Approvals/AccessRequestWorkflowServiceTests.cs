using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Approvals;

public sealed class AccessRequestWorkflowServiceTests
{
    [Fact]
    public async Task ApprovalIsDurableBeforeProvisioningStarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            factory.Clock.UtcNow,
            cancellationToken);
        var provisioner = new PersistenceInspectingProvisioner(dbContext);
        var service = CreateService(dbContext, provisioner, factory.Clock);

        var outcome = await service.DecideDevOpsAsync(
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "devops-service-correlation",
            cancellationToken);

        Assert.True(outcome.IsSuccess);
        var completed = outcome.Value;
        Assert.True(provisioner.DecisionWasPersistedBeforeInvocation);
        Assert.True(provisioner.OperationWasPersistedBeforeInvocation);
        Assert.Equal(RequestStatus.Active, completed.Request.Status);
        Assert.NotNull(completed.Grant);
    }

    [Fact]
    public async Task ProviderFailurePersistsRetryableWorkflowAndAuditState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            factory.Clock.UtcNow,
            cancellationToken);
        var provisioner = new FailingProvisioner();
        var service = CreateService(dbContext, provisioner, factory.Clock);

        var outcome = await service.DecideDevOpsAsync(
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "devops-failure-correlation",
            cancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal(FailingProvisioner.FailureCode, outcome.Failure!.Code);

        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id, cancellationToken);
        var operation = await dbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == request.Id, cancellationToken);
        var auditTypes = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.RequestId == request.Id)
            .Select(item => item.EventType)
            .ToListAsync(cancellationToken);

        Assert.Equal(RequestStatus.ProvisioningFailed, storedRequest.Status);
        Assert.Equal(ProvisioningOperationStatus.Failed, operation.Status);
        Assert.Equal(FailingProvisioner.FailureCode, operation.LastOutcomeCode);
        Assert.Contains(AuditEventType.DevOpsDecision, auditTypes);
        Assert.Contains(AuditEventType.ProvisioningAttempted, auditTypes);
        Assert.Contains(AuditEventType.ProvisioningFailed, auditTypes);
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(
            cancellationToken));
    }

    private static AccessRequestWorkflowService CreateService(
        GovernedAccessDbContext dbContext,
        IAccessProvisioner provisioner,
        IClock clock)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        var workflowStore = new EfWorkflowStore(dbContext);
        return new AccessRequestWorkflowService(
            requestContext,
            workflowStore,
            new RequestValidator(requestContext),
            new ProtectedProvisioningService(workflowStore, provisioner, clock),
            clock);
    }

    private static async Task<(AccessRequest Request, ApprovalDecision BusinessDecision)>
        SeedBusinessApprovedRequestAsync(
            GovernedAccessDbContext dbContext,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
    {
        var request = new AccessRequest(
            Guid.NewGuid(),
            DemoDataIds.RequesterPrincipalId,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            DemoDataIds.PrimaryIncidentId,
            occurredAt,
            "request-correlation");
        var policyResult = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                Guid.NewGuid(),
                ApprovalOutcome.Approved,
                DemoDataIds.ClientAlphaApproverPrincipalId,
                null,
                occurredAt,
                "business-correlation"),
            hasExistingBusinessDecision: false);
        var applied = Assert.IsType<BusinessDecisionApplied>(policyResult);

        dbContext.AccessRequests.Add(request);
        dbContext.ApprovalDecisions.Add(applied.Decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (request, applied.Decision);
    }

    private sealed class PersistenceInspectingProvisioner(
        GovernedAccessDbContext dbContext) : IAccessProvisioner
    {
        public bool DecisionWasPersistedBeforeInvocation { get; private set; }

        public bool OperationWasPersistedBeforeInvocation { get; private set; }

        public async Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            DecisionWasPersistedBeforeInvocation = await dbContext.ApprovalDecisions
                .AsNoTracking()
                .AnyAsync(
                    item => item.RequestId == request.RequestId
                        && item.Stage == ApprovalStage.DevOps,
                    cancellationToken);
            OperationWasPersistedBeforeInvocation = await dbContext.ProvisioningOperations
                .AsNoTracking()
                .AnyAsync(
                    item => item.RequestId == request.RequestId,
                    cancellationToken);

            return new AccessProvisioningSucceeded(
                Guid.NewGuid(),
                GovernedAccessWebFactory.DefaultUtcNow);
        }
    }

    private sealed class FailingProvisioner : IAccessProvisioner
    {
        public const string FailureCode = "synthetic_provisioning_failed";

        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningFailed(
                    new ApplicationFailure(
                        ApplicationFailureKind.DependencyFailure,
                        FailureCode,
                        "Synthetic provisioning failed safely.")));
        }
    }
}

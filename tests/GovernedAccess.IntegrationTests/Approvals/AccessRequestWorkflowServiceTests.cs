using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Demo;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Approvals;

public sealed class AccessRequestWorkflowServiceTests
{
    [Fact]
    public async Task ApprovalIsDurableBeforeProvisioningStarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var provisioner = new PersistenceInspectingProvisioner(dbContext);
        var service = CreateService(scope.ServiceProvider, provisioner, fixture.Clock);

        var outcome = await service.DecideAsync(
            ApprovalStage.DevOps,
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
    public async Task WrongClientBusinessApproverIsRejectedWithoutChangingTheRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = await SeedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            scope.ServiceProvider,
            new PersistenceInspectingProvisioner(dbContext),
            fixture.Clock);

        var outcome = await service.DecideAsync(
            ApprovalStage.Business,
            request.Id,
            DemoDataIds.ClientBetaApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "wrong-client-correlation",
            cancellationToken);

        Assert.True(outcome.IsFailure);
        Assert.Equal(
            AccessRequestWorkflowService.BusinessApproverNotResponsibleCode,
            outcome.Failure!.Code);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Empty(await dbContext.Set<ApprovalDecision>().ToListAsync(cancellationToken));
        Assert.Single(
            await dbContext.Set<AuditEvent>().ToListAsync(cancellationToken),
            item => item.EventType == AuditEventType.AuthorizationRejected);
    }

    [Fact]
    public async Task DuplicateBusinessDecisionIsRejectedWithoutReplacingEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = await SeedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            scope.ServiceProvider,
            new PersistenceInspectingProvisioner(dbContext),
            fixture.Clock);

        var first = await service.DecideAsync(
            ApprovalStage.Business,
            request.Id,
            DemoDataIds.ClientAlphaApproverPrincipalId,
            ApprovalOutcome.Approved,
            "Original decision.",
            "first-correlation",
            cancellationToken);
        var duplicate = await service.DecideAsync(
            ApprovalStage.Business,
            request.Id,
            DemoDataIds.ClientAlphaApproverPrincipalId,
            ApprovalOutcome.Rejected,
            "Duplicate decision.",
            "duplicate-correlation",
            cancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal(
            AccessRequestWorkflowService.BusinessDuplicateDecisionCode,
            duplicate.Failure!.Code);
        var decision = Assert.Single(
            await dbContext.Set<ApprovalDecision>().ToListAsync(cancellationToken));
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal("Original decision.", decision.Comment);
        Assert.Single(
            await dbContext.Set<AuditEvent>().ToListAsync(cancellationToken),
            item => item.EventType == AuditEventType.InvalidTransitionRejected);
    }

    [Fact]
    public async Task NonDevOpsPrincipalCannotDecideBusinessApprovedRequest()
    {
        string[] principalIds =
        [
            DemoDataIds.RequesterPrincipalId,
            DemoDataIds.ClientAlphaApproverPrincipalId,
        ];

        foreach (var principalId in principalIds)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await using var fixture = await ProvisioningTestFixture.CreateAsync(
                cancellationToken);
            await using var scope = fixture.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var (request, _) = await SeedBusinessApprovedRequestAsync(
                dbContext,
                fixture.Clock.UtcNow,
                cancellationToken);
            var service = CreateService(
                scope.ServiceProvider,
                new PersistenceInspectingProvisioner(dbContext),
                fixture.Clock);

            var outcome = await service.DecideAsync(
                ApprovalStage.DevOps,
                request.Id,
                principalId,
                ApprovalOutcome.Approved,
                null,
                "non-devops-correlation",
                cancellationToken);

            Assert.True(outcome.IsFailure);
            Assert.Equal(
                AccessRequestWorkflowService.DevOpsApproverNotAuthorizedCode,
                outcome.Failure!.Code);
            Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
            Assert.DoesNotContain(
                await dbContext.Set<ApprovalDecision>().ToListAsync(cancellationToken),
                item => item.Stage == ApprovalStage.DevOps);
            Assert.Empty(await dbContext.Set<ProvisioningOperation>().ToListAsync(
                cancellationToken));
        }
    }

    [Fact]
    public async Task DevOpsRejectionCreatesNoProvisioningOperationOrGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            scope.ServiceProvider,
            new PersistenceInspectingProvisioner(dbContext),
            fixture.Clock);

        var outcome = await service.DecideAsync(
            ApprovalStage.DevOps,
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Rejected,
            "Current operational risk is too high.",
            "devops-rejection-correlation",
            cancellationToken);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Null(outcome.Value.Operation);
        Assert.Null(outcome.Value.Grant);
        Assert.Empty(await dbContext.Set<ProvisioningOperation>().ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.Set<AccessGrant>().ToListAsync(cancellationToken));
    }

    private static AccessRequestWorkflowService CreateService(
        IServiceProvider services,
        IAccessProvisioner provisioner,
        IClock clock)
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

    private static async Task<(AccessRequest Request, ApprovalDecision BusinessDecision)>
        SeedBusinessApprovedRequestAsync(
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
        var policyResult = ApprovalDecisionPolicy.Apply(
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
            hasExistingDecision: false);
        var applied = Assert.IsType<ApprovalDecisionApplied>(policyResult);

        dbContext.Set<AccessRequest>().Add(request);
        dbContext.Set<ApprovalDecision>().Add(applied.Decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (request, applied.Decision);
    }

    private static async Task<AccessRequest> SeedRequestAsync(
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
        dbContext.Set<AccessRequest>().Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    private sealed class PersistenceInspectingProvisioner(
        WorkflowDbContext dbContext) : IAccessProvisioner
    {
        public bool DecisionWasPersistedBeforeInvocation { get; private set; }

        public bool OperationWasPersistedBeforeInvocation { get; private set; }

        public int InvocationCount { get; private set; }

        public async Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            DecisionWasPersistedBeforeInvocation = await dbContext.Set<ApprovalDecision>()
                .AsNoTracking()
                .AnyAsync(
                    item => item.RequestId == request.RequestId
                        && item.Stage == ApprovalStage.DevOps,
                    cancellationToken);
            OperationWasPersistedBeforeInvocation = await dbContext.Set<ProvisioningOperation>()
                .AsNoTracking()
                .AnyAsync(
                    item => item.RequestId == request.RequestId,
                    cancellationToken);

            return new AccessProvisioningSucceeded(
                Guid.NewGuid(),
                GovernedAccessWebFactory.DefaultUtcNow);
        }
    }

}

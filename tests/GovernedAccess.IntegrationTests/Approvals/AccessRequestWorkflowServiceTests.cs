using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.AccessRequests;
using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
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
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var provisioner = new PersistenceInspectingProvisioner(dbContext);
        var service = CreateService(dbContext, provisioner, fixture.Clock);

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
        Assert.NotNull(completed.Provisioning?.Grant);
    }

    [Fact]
    public async Task WrongClientBusinessApproverIsRejectedWithoutChangingTheRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var request = await SeedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            dbContext,
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
        Assert.Empty(await dbContext.ApprovalDecisions.ToListAsync(cancellationToken));
        Assert.Single(
            await dbContext.AuditEvents.ToListAsync(cancellationToken),
            item => item.EventType == AuditEventType.AuthorizationRejected);
    }

    [Fact]
    public async Task DuplicateBusinessDecisionIsRejectedWithoutReplacingEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var request = await SeedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            dbContext,
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
            await dbContext.ApprovalDecisions.ToListAsync(cancellationToken));
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal("Original decision.", decision.Comment);
        Assert.Single(
            await dbContext.AuditEvents.ToListAsync(cancellationToken),
            item => item.EventType == AuditEventType.InvalidTransitionRejected);
    }

    [Theory]
    [InlineData(DemoDataIds.RequesterPrincipalId)]
    [InlineData(DemoDataIds.ClientAlphaApproverPrincipalId)]
    public async Task NonDevOpsPrincipalCannotDecideBusinessApprovedRequest(
        string principalId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            dbContext,
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
            await dbContext.ApprovalDecisions.ToListAsync(cancellationToken),
            item => item.Stage == ApprovalStage.DevOps);
        Assert.Empty(await dbContext.ProvisioningOperations.ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task DevOpsRejectionCreatesNoProvisioningOperationOrGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var (request, _) = await SeedBusinessApprovedRequestAsync(
            dbContext,
            fixture.Clock.UtcNow,
            cancellationToken);
        var service = CreateService(
            dbContext,
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
        Assert.Null(outcome.Value.Provisioning);
        Assert.Empty(await dbContext.ProvisioningOperations.ToListAsync(
            cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.ToListAsync(cancellationToken));
    }

    private static AccessRequestWorkflowService CreateService(
        GovernedAccessDbContext dbContext,
        IAccessProvisioner provisioner,
        IClock clock)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        var workflowStore = new EfWorkflowStore(dbContext);
        return new AccessRequestWorkflowService(
            new AccessRequestCommandContextLoader(requestContext, workflowStore),
            workflowStore,
            new AccessRequestValidator(requestContext),
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

        dbContext.AccessRequests.Add(request);
        dbContext.ApprovalDecisions.Add(applied.Decision);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (request, applied.Decision);
    }

    private static async Task<AccessRequest> SeedRequestAsync(
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
        dbContext.AccessRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    private sealed class PersistenceInspectingProvisioner(
        GovernedAccessDbContext dbContext) : IAccessProvisioner
    {
        public bool DecisionWasPersistedBeforeInvocation { get; private set; }

        public bool OperationWasPersistedBeforeInvocation { get; private set; }

        public int InvocationCount { get; private set; }

        public async Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
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

}

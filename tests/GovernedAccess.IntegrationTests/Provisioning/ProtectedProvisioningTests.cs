using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Demo;
using GovernedAccess.Workflow.Persistence;
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
        ProvisioningTestFixture.DefaultUtcNow.AddHours(-2);

    private static readonly DateTimeOffset BusinessApprovedAt =
        ProvisioningTestFixture.DefaultUtcNow.AddHours(-1);

    private static readonly DateTimeOffset DevOpsApprovedAt =
        ProvisioningTestFixture.DefaultUtcNow.AddMinutes(-15);

    private static readonly DateTimeOffset ActivatedAt =
        ProvisioningTestFixture.DefaultUtcNow.AddMinutes(1);

    [Fact]
    public async Task ProvisionAsyncDerivesProviderInputFromPersistedRequestDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await SeedAwaitingProvisioningAsync(fixture, cancellationToken: cancellationToken);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            scope.ServiceProvider.GetRequiredService<IWorkflowStore>(),
            provisioner,
            fixture.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        var completed = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        Assert.Equal(RequestId, completed.Request.Id);
        Assert.Equal(RequestId, completed.Operation.RequestId);
        Assert.Equal(ProviderGrantId, completed.Grant.Id);

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
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await SeedAwaitingProvisioningAsync(
            fixture,
            includeBusinessApproval: omittedStage != ApprovalStage.Business,
            includeDevOpsApproval: omittedStage != ApprovalStage.DevOps,
            cancellationToken: cancellationToken);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var provisioner = new RecordingAccessProvisioner(ActivatedAt);
        var service = new ProtectedProvisioningService(
            scope.ServiceProvider.GetRequiredService<IWorkflowStore>(),
            provisioner,
            fixture.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningFailed>(outcome);
        Assert.Empty(provisioner.Requests);
        Assert.Empty(await dbContext.Set<AccessGrant>().AsNoTracking().ToListAsync(
            cancellationToken));
    }

    [Fact]
    public async Task SuccessfulProvisioningPersistsExactlyEightHourGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await SeedAwaitingProvisioningAsync(fixture, cancellationToken: cancellationToken);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var service = new ProtectedProvisioningService(
            scope.ServiceProvider.GetRequiredService<IWorkflowStore>(),
            new RecordingAccessProvisioner(ActivatedAt),
            fixture.Clock);

        var outcome = await service.ProvisionAsync(RequestId, cancellationToken);

        _ = Assert.IsType<ProtectedProvisioningCompleted>(outcome);
        var grant = await dbContext.Set<AccessGrant>()
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == RequestId, cancellationToken);
        var request = await dbContext.Set<AccessRequest>()
            .AsNoTracking()
            .SingleAsync(item => item.Id == RequestId, cancellationToken);
        var operation = await dbContext.Set<ProvisioningOperation>()
            .AsNoTracking()
            .SingleAsync(item => item.RequestId == RequestId, cancellationToken);
        var auditEvent = await dbContext.Set<AuditEvent>()
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == RequestId
                    && item.EventType == AuditEventType.ProvisioningSucceeded,
                cancellationToken);

        Assert.Equal(ActivatedAt, grant.ActivatedAt);
        Assert.Equal(ActivatedAt.AddHours(8), grant.ExpiresAt);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
        Assert.Equal(RequestId, grant.RequestId);
        Assert.Equal(RequestStatus.Active, request.Status);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
        using var auditDetails = JsonDocument.Parse(auditEvent.DetailsJson);
        Assert.Equal(
            ProvisioningAuditDetails.CurrentSchemaVersion,
            auditDetails.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(auditDetails.RootElement.TryGetProperty("environmentId", out _));
        Assert.False(auditDetails.RootElement.TryGetProperty("roleId", out _));
    }

    private static async Task SeedAwaitingProvisioningAsync(
        ProvisioningTestFixture fixture,
        bool includeBusinessApproval = true,
        bool includeDevOpsApproval = true,
        CancellationToken cancellationToken = default)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = new AccessRequest(
            RequestId,
            Guid.NewGuid(),
            DemoDataIds.RequesterPrincipalId,
            new ValidatedRequestDetails(
                DemoDataIds.ClientAlphaId,
                DemoDataIds.ClientAlphaEnvironmentId,
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident safely.",
                DemoDataIds.PrimaryIncidentId),
            RequestCreatedAt,
            "request-correlation");
        var businessResult = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.Business,
            priorApproval: null,
            new ApprovalCommand(
                Guid.Parse("1e206088-6778-40cb-8900-b59465252e14"),
                ApprovalOutcome.Approved,
                DemoDataIds.ClientAlphaApproverPrincipalId,
                null,
                BusinessApprovedAt,
                "business-correlation"),
            hasExistingDecision: false);
        var businessApproval = Assert.IsType<ApprovalDecisionApplied>(businessResult).Decision;
        var devOpsApproval = new ApprovalDecision(
            Guid.Parse("266ae120-70bc-4af2-9a62-453591247ecc"),
            request.Id,
            ApprovalStage.DevOps,
            ApprovalOutcome.Approved,
            DemoDataIds.DevOpsApproverPrincipalId,
            null,
            DevOpsApprovedAt,
            "devops-provisioning-correlation");
        var operation = new ProvisioningOperation(
            request.Id,
            DevOpsApprovedAt);

        dbContext.Set<AccessRequest>().Add(request);
        if (includeBusinessApproval)
        {
            dbContext.Set<ApprovalDecision>().Add(businessApproval);
        }

        if (includeDevOpsApproval)
        {
            dbContext.Set<ApprovalDecision>().Add(devOpsApproval);
        }

        dbContext.Set<ProvisioningOperation>().Add(operation);
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

}

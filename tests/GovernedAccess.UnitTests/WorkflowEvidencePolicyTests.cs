using GovernedAccess.Core.Domain;

namespace GovernedAccess.UnitTests;

public sealed class WorkflowEvidencePolicyTests
{
    private static readonly Guid RequestId =
        Guid.Parse("83f572c0-89f0-46ac-aed8-657422e1312c");

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BusinessApprovedAt =
        CreatedAt.AddMinutes(15);

    private static readonly DateTimeOffset DevOpsApprovedAt =
        BusinessApprovedAt.AddMinutes(15);

    [Fact]
    public void ValidEvidenceMatchesTheImmutableRequestScope()
    {
        var request = CreateRequest();
        var businessApproval = CreateApproval(
            request.Id,
            ApprovalStage.Business,
            ProductionRoleIds.ReadOnly,
            BusinessApprovedAt);
        var devOpsApproval = CreateApproval(
            request.Id,
            ApprovalStage.DevOps,
            ProductionRoleIds.ReadOnly,
            DevOpsApprovedAt);
        var operation = CreateOperation(request);
        var grant = CreateGrant(request, operation);

        Assert.Null(WorkflowEvidencePolicy.ValidateBusinessApproval(
            request,
            businessApproval));
        Assert.Null(WorkflowEvidencePolicy.ValidateApprovalEvidence(
            request,
            businessApproval,
            devOpsApproval));
        Assert.Null(WorkflowEvidencePolicy.ValidateProvisioningEvidence(
            request,
            devOpsApproval,
            operation));
        Assert.Null(WorkflowEvidencePolicy.ValidateGrantScope(
            request,
            operation,
            grant));
    }

    [Fact]
    public void BusinessApprovalMustBelongToTheRequestAndFollowItsCreation()
    {
        var request = CreateRequest();
        var approvalForAnotherRequest = CreateApproval(
            Guid.NewGuid(),
            ApprovalStage.Business,
            ProductionRoleIds.ReadOnly,
            BusinessApprovedAt);
        var approvalBeforeRequest = CreateApproval(
            request.Id,
            ApprovalStage.Business,
            ProductionRoleIds.ReadOnly,
            CreatedAt.AddTicks(-1));

        Assert.Equal(
            WorkflowEvidencePolicyError.InvalidBusinessApproval,
            WorkflowEvidencePolicy.ValidateBusinessApproval(
                request,
                approvalForAnotherRequest));
        Assert.Equal(
            WorkflowEvidencePolicyError.InvalidBusinessApproval,
            WorkflowEvidencePolicy.ValidateBusinessApproval(
                request,
                approvalBeforeRequest));
    }

    [Fact]
    public void BusinessApprovalMustMatchTheRequestedRole()
    {
        var request = CreateRequest();
        var approval = CreateApproval(
            request.Id,
            ApprovalStage.Business,
            ProductionRoleIds.Support,
            BusinessApprovedAt);

        Assert.Equal(
            WorkflowEvidencePolicyError.BusinessApprovalScopeMismatch,
            WorkflowEvidencePolicy.ValidateBusinessApproval(request, approval));
    }

    [Fact]
    public void ApprovalEvidenceRequiresMatchingScopeAndDecisionOrder()
    {
        var request = CreateRequest();
        var businessApproval = CreateApproval(
            request.Id,
            ApprovalStage.Business,
            ProductionRoleIds.ReadOnly,
            BusinessApprovedAt);
        var mismatchedDevOpsApproval = CreateApproval(
            request.Id,
            ApprovalStage.DevOps,
            ProductionRoleIds.Support,
            DevOpsApprovedAt);
        var earlyDevOpsApproval = CreateApproval(
            request.Id,
            ApprovalStage.DevOps,
            ProductionRoleIds.ReadOnly,
            BusinessApprovedAt.AddTicks(-1));

        Assert.Equal(
            WorkflowEvidencePolicyError.ApprovalScopeMismatch,
            WorkflowEvidencePolicy.ValidateApprovalEvidence(
                request,
                businessApproval,
                mismatchedDevOpsApproval));
        Assert.Equal(
            WorkflowEvidencePolicyError.ApprovalSequenceInvalid,
            WorkflowEvidencePolicy.ValidateApprovalEvidence(
                request,
                businessApproval,
                earlyDevOpsApproval));
    }

    [Fact]
    public void ProvisioningOperationAndGrantMustMatchTheApprovedRequestScope()
    {
        var request = CreateRequest();
        var devOpsApproval = CreateApproval(
            request.Id,
            ApprovalStage.DevOps,
            ProductionRoleIds.ReadOnly,
            DevOpsApprovedAt);
        var mismatchedOperation = new ProvisioningOperation(
            request.Id,
            request.EnvironmentId,
            ProductionRoleIds.Support,
            DevOpsApprovedAt);
        var validOperation = CreateOperation(request);
        var mismatchedGrant = new AccessGrant(
            Guid.NewGuid(),
            request.Id,
            "another-requester",
            request.EnvironmentId,
            validOperation.RoleId,
            DevOpsApprovedAt.AddMinutes(1),
            "grant-correlation");

        Assert.Equal(
            WorkflowEvidencePolicyError.OperationScopeMismatch,
            WorkflowEvidencePolicy.ValidateProvisioningEvidence(
                request,
                devOpsApproval,
                mismatchedOperation));
        Assert.Equal(
            WorkflowEvidencePolicyError.GrantScopeMismatch,
            WorkflowEvidencePolicy.ValidateGrantScope(
                request,
                validOperation,
                mismatchedGrant));
    }

    private static AccessRequest CreateRequest()
    {
        return new AccessRequest(
            RequestId,
            "requester",
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            CreatedAt,
            "request-correlation");
    }

    private static ApprovalDecision CreateApproval(
        Guid requestId,
        ApprovalStage stage,
        string roleId,
        DateTimeOffset decidedAt)
    {
        return new ApprovalDecision(
            Guid.NewGuid(),
            requestId,
            stage,
            ApprovalOutcome.Approved,
            stage == ApprovalStage.Business
                ? "business-approver"
                : "devops-approver",
            roleId,
            null,
            decidedAt,
            $"{stage.ToString().ToLowerInvariant()}-correlation");
    }

    private static ProvisioningOperation CreateOperation(AccessRequest request)
    {
        return new ProvisioningOperation(
            request.Id,
            request.EnvironmentId,
            request.RequestedRoleId,
            DevOpsApprovedAt);
    }

    private static AccessGrant CreateGrant(
        AccessRequest request,
        ProvisioningOperation operation)
    {
        return new AccessGrant(
            Guid.NewGuid(),
            request.Id,
            request.RequesterId,
            operation.EnvironmentId,
            operation.RoleId,
            DevOpsApprovedAt.AddMinutes(1),
            "grant-correlation");
    }
}

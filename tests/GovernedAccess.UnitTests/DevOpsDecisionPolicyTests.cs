using GovernedAccess.Core.Domain;

namespace GovernedAccess.UnitTests;

public sealed class DevOpsDecisionPolicyTests
{
    private static readonly Guid RequestId =
        Guid.Parse("4f1d09cb-87ac-43bb-aa98-75156f5f35e4");

    private static readonly DateTimeOffset RequestCreatedAt =
        new(2026, 7, 20, 8, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BusinessDecisionTime =
        new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DevOpsDecisionTime =
        new(2026, 7, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ApplyApprovalBindsTheExactBusinessRole()
    {
        var (request, businessApproval) = CreateBusinessApprovedRequest();
        var decisionId = Guid.Parse("5c270790-e989-4277-a835-f7f5365aefd8");

        var result = DevOpsDecisionPolicy.Apply(
            request,
            businessApproval,
            new DevOpsDecisionCommand(
                decisionId,
                ApprovalOutcome.Approved,
                " devops-approver ",
                " Approved for the fixed eight-hour access period. ",
                DevOpsDecisionTime,
                " devops-correlation "),
            hasExistingDevOpsDecision: false);

        var applied = Assert.IsType<DevOpsDecisionApplied>(result);
        var decision = applied.Decision;
        var operation = Assert.IsType<ProvisioningOperation>(applied.Operation);

        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
        Assert.Equal(DevOpsDecisionTime, request.LastModifiedAt);
        Assert.Equal(3, request.PersistenceVersion);
        Assert.Equal(decisionId, decision.Id);
        Assert.Equal(request.Id, decision.RequestId);
        Assert.Equal(ApprovalStage.DevOps, decision.Stage);
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal("devops-approver", decision.ApproverId);
        Assert.Equal(businessApproval.ApprovedRoleId, decision.ApprovedRoleId);
        Assert.Equal("Approved for the fixed eight-hour access period.", decision.Comment);
        Assert.Equal(DevOpsDecisionTime, decision.DecidedAt);
        Assert.Equal("devops-correlation", decision.CorrelationId);

        Assert.Equal(request.Id, operation.RequestId);
        Assert.Equal(request.EnvironmentId, operation.EnvironmentId);
        Assert.Equal(businessApproval.ApprovedRoleId, operation.RoleId);
        Assert.Equal(ProvisioningOperationStatus.Pending, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(DevOpsDecisionTime, operation.CreatedAt);
    }

    [Fact]
    public void ApplyApprovalRejectsABusinessRoleThatDoesNotMatchTheImmutableRequest()
    {
        var (request, _) = CreateBusinessApprovedRequest();
        var mismatchedBusinessApproval = CreateBusinessApproval(
            request,
            ProductionRoleIds.Support);
        var snapshot = RequestSnapshot.Capture(request);

        var result = DevOpsDecisionPolicy.Apply(
            request,
            mismatchedBusinessApproval,
            ValidApprovalCommand(),
            hasExistingDevOpsDecision: false);

        var notApplied = Assert.IsType<DevOpsDecisionNotApplied>(result);
        Assert.Equal(
            DevOpsDecisionPolicyError.BusinessApprovalScopeMismatch,
            notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Fact]
    public void ApplyRejectionTransitionsWithoutApprovedRoleOrProvisioningOperation()
    {
        var (request, businessApproval) = CreateBusinessApprovedRequest();

        var result = DevOpsDecisionPolicy.Apply(
            request,
            businessApproval,
            new DevOpsDecisionCommand(
                Guid.Parse("325eb46d-e95e-42ed-bc44-3f6f31749693"),
                ApprovalOutcome.Rejected,
                "devops-approver",
                " Current risk is too high. ",
                DevOpsDecisionTime,
                "devops-correlation"),
            hasExistingDevOpsDecision: false);

        var applied = Assert.IsType<DevOpsDecisionApplied>(result);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(DevOpsDecisionTime, request.LastModifiedAt);
        Assert.Equal(3, request.PersistenceVersion);
        Assert.Equal(ApprovalStage.DevOps, applied.Decision.Stage);
        Assert.Equal(ApprovalOutcome.Rejected, applied.Decision.Decision);
        Assert.Null(applied.Decision.ApprovedRoleId);
        Assert.Equal("Current risk is too high.", applied.Decision.Comment);
        Assert.Null(applied.Operation);
    }

    [Fact]
    public void ApplyRejectsARequestThatIsNotAwaitingDevOpsApproval()
    {
        var request = CreateSubmittedRequest();
        var businessApproval = CreateBusinessApproval(request, request.RequestedRoleId);
        var snapshot = RequestSnapshot.Capture(request);

        var result = DevOpsDecisionPolicy.Apply(
            request,
            businessApproval,
            ValidApprovalCommand(),
            hasExistingDevOpsDecision: false);

        var notApplied = Assert.IsType<DevOpsDecisionNotApplied>(result);
        Assert.Equal(DevOpsDecisionPolicyError.InvalidTransition, notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Fact]
    public void OperationUsesTheRequestIdAsItsIdentity()
    {
        var (firstRequest, firstBusinessApproval) = CreateBusinessApprovedRequest();
        var (secondRequest, secondBusinessApproval) = CreateBusinessApprovedRequest();

        var first = Assert.IsType<DevOpsDecisionApplied>(DevOpsDecisionPolicy.Apply(
            firstRequest,
            firstBusinessApproval,
            ValidApprovalCommand(),
            hasExistingDevOpsDecision: false));
        var second = Assert.IsType<DevOpsDecisionApplied>(DevOpsDecisionPolicy.Apply(
            secondRequest,
            secondBusinessApproval,
            ValidApprovalCommand(),
            hasExistingDevOpsDecision: false));

        Assert.Equal(RequestId, first.Operation?.RequestId);
        Assert.Equal(first.Operation?.RequestId, second.Operation?.RequestId);
    }

    private static DevOpsDecisionCommand ValidApprovalCommand()
    {
        return new DevOpsDecisionCommand(
            Guid.Parse("7a70e89c-8ff7-427c-a5f3-ddacbb6d5cba"),
            ApprovalOutcome.Approved,
            "devops-approver",
            null,
            DevOpsDecisionTime,
            "devops-correlation");
    }

    private static (AccessRequest Request, ApprovalDecision BusinessApproval)
        CreateBusinessApprovedRequest()
    {
        var request = CreateSubmittedRequest();
        var result = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                Guid.Parse("2e303a27-8cc3-4814-92e8-3b989576662f"),
                ApprovalOutcome.Approved,
                "business-alpha",
                null,
                BusinessDecisionTime,
                "business-correlation"),
            hasExistingBusinessDecision: false);
        var applied = Assert.IsType<BusinessDecisionApplied>(result);

        return (request, applied.Decision);
    }

    private static AccessRequest CreateSubmittedRequest()
    {
        return new AccessRequest(
            RequestId,
            "requester",
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            RequestCreatedAt,
            "request-correlation");
    }

    private static ApprovalDecision CreateBusinessApproval(
        AccessRequest request,
        string approvedRoleId)
    {
        return new ApprovalDecision(
            Guid.Parse("0ff40f0c-17d7-429c-bab8-716b95928a7d"),
            request.Id,
            ApprovalStage.Business,
            ApprovalOutcome.Approved,
            "business-alpha",
            approvedRoleId,
            null,
            BusinessDecisionTime,
            "business-correlation");
    }

    private sealed record RequestSnapshot(
        RequestStatus Status,
        DateTimeOffset LastModifiedAt,
        long PersistenceVersion)
    {
        public static RequestSnapshot Capture(AccessRequest request) =>
            new(request.Status, request.LastModifiedAt, request.PersistenceVersion);

        public void AssertUnchanged(AccessRequest request)
        {
            Assert.Equal(Status, request.Status);
            Assert.Equal(LastModifiedAt, request.LastModifiedAt);
            Assert.Equal(PersistenceVersion, request.PersistenceVersion);
        }
    }
}

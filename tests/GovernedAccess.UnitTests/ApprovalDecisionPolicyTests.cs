using GovernedAccess.Core.Domain;

namespace GovernedAccess.UnitTests;

public sealed class ApprovalDecisionPolicyTests
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
    public void BusinessApprovalRecordsDecisionAgainstTheImmutableRequest()
    {
        var request = CreateSubmittedRequest();
        var decisionId = Guid.Parse("4f551db5-d9de-4e04-8555-9948d8e81b0a");

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.Business,
            priorApproval: null,
            new ApprovalCommand(
                decisionId,
                ApprovalOutcome.Approved,
                " business-alpha ",
                " Approved for incident response. ",
                BusinessDecisionTime,
                " business-correlation "),
            hasExistingDecision: false);

        var applied = Assert.IsType<ApprovalDecisionApplied>(result);
        var decision = applied.Decision;
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
        Assert.Equal(BusinessDecisionTime, request.LastModifiedAt);
        Assert.Equal(2, request.PersistenceVersion);
        Assert.Equal(decisionId, decision.Id);
        Assert.Equal(request.Id, decision.RequestId);
        Assert.Equal(ApprovalStage.Business, decision.Stage);
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal("business-alpha", decision.ApproverId);
        Assert.Equal("Approved for incident response.", decision.Comment);
        Assert.Equal(BusinessDecisionTime, decision.DecidedAt);
        Assert.Equal("business-correlation", decision.CorrelationId);
        Assert.Null(applied.Operation);
    }

    [Fact]
    public void BusinessRejectionTransitionsWithoutProvisioningOperation()
    {
        var request = CreateSubmittedRequest();

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.Business,
            priorApproval: null,
            new ApprovalCommand(
                Guid.Parse("e2b658e5-a183-48a7-99e6-8b67300a60f7"),
                ApprovalOutcome.Rejected,
                "business-alpha",
                " Request is not justified. ",
                BusinessDecisionTime,
                "business-correlation"),
            hasExistingDecision: false);

        var applied = Assert.IsType<ApprovalDecisionApplied>(result);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(BusinessDecisionTime, request.LastModifiedAt);
        Assert.Equal(2, request.PersistenceVersion);
        Assert.Equal(ApprovalStage.Business, applied.Decision.Stage);
        Assert.Equal(ApprovalOutcome.Rejected, applied.Decision.Decision);
        Assert.Equal("Request is not justified.", applied.Decision.Comment);
        Assert.Null(applied.Operation);
    }

    [Fact]
    public void DuplicateBusinessDecisionDoesNotChangeTheRequest()
    {
        var request = CreateSubmittedRequest();
        var snapshot = RequestSnapshot.Capture(request);

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.Business,
            priorApproval: null,
            ValidBusinessApprovalCommand(),
            hasExistingDecision: true);

        var notApplied = Assert.IsType<ApprovalDecisionNotApplied>(result);
        Assert.Equal(ApprovalDecisionPolicyError.DuplicateStage, notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Fact]
    public void DevOpsApprovalRecordsDecisionAndRequestKeyedOperation()
    {
        var (request, businessApproval) = CreateBusinessApprovedRequest();
        var decisionId = Guid.Parse("5c270790-e989-4277-a835-f7f5365aefd8");

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.DevOps,
            businessApproval,
            new ApprovalCommand(
                decisionId,
                ApprovalOutcome.Approved,
                " devops-approver ",
                " Approved for the fixed eight-hour access period. ",
                DevOpsDecisionTime,
                " devops-correlation "),
            hasExistingDecision: false);

        var applied = Assert.IsType<ApprovalDecisionApplied>(result);
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
        Assert.Equal(
            "Approved for the fixed eight-hour access period.",
            decision.Comment);
        Assert.Equal(DevOpsDecisionTime, decision.DecidedAt);
        Assert.Equal("devops-correlation", decision.CorrelationId);
        Assert.Equal(request.Id, operation.RequestId);
        Assert.Equal(ProvisioningOperationStatus.Pending, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(DevOpsDecisionTime, operation.CreatedAt);
    }

    [Theory]
    [InlineData(ApprovalStage.Business, ApprovalOutcome.Rejected)]
    [InlineData(ApprovalStage.DevOps, ApprovalOutcome.Approved)]
    public void DevOpsDecisionRequiresExactApprovedBusinessEvidence(
        ApprovalStage priorStage,
        ApprovalOutcome priorOutcome)
    {
        var (request, _) = CreateBusinessApprovedRequest();
        var invalidPriorApproval = new ApprovalDecision(
            Guid.Parse("0ff40f0c-17d7-429c-bab8-716b95928a7d"),
            request.Id,
            priorStage,
            priorOutcome,
            "business-alpha",
            null,
            BusinessDecisionTime,
            "business-correlation");
        var snapshot = RequestSnapshot.Capture(request);

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.DevOps,
            invalidPriorApproval,
            ValidDevOpsApprovalCommand(),
            hasExistingDecision: false);

        var notApplied = Assert.IsType<ApprovalDecisionNotApplied>(result);
        Assert.Equal(
            ApprovalDecisionPolicyError.InvalidPriorApproval,
            notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Fact]
    public void DevOpsDecisionRejectsPriorApprovalForAnotherRequest()
    {
        var (request, _) = CreateBusinessApprovedRequest();
        var priorApproval = new ApprovalDecision(
            Guid.Parse("93ce7fcc-bbcc-4dce-95a3-1cdb87a55fbd"),
            Guid.Parse("15311f7c-cdaa-493a-9f86-fc970fd8e116"),
            ApprovalStage.Business,
            ApprovalOutcome.Approved,
            "business-alpha",
            null,
            BusinessDecisionTime,
            "business-correlation");
        var snapshot = RequestSnapshot.Capture(request);

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.DevOps,
            priorApproval,
            ValidDevOpsApprovalCommand(),
            hasExistingDecision: false);

        var notApplied = Assert.IsType<ApprovalDecisionNotApplied>(result);
        Assert.Equal(
            ApprovalDecisionPolicyError.InvalidPriorApproval,
            notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Fact]
    public void DevOpsRejectionTransitionsWithoutProvisioningOperation()
    {
        var (request, businessApproval) = CreateBusinessApprovedRequest();

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.DevOps,
            businessApproval,
            new ApprovalCommand(
                Guid.Parse("325eb46d-e95e-42ed-bc44-3f6f31749693"),
                ApprovalOutcome.Rejected,
                "devops-approver",
                " Current risk is too high. ",
                DevOpsDecisionTime,
                "devops-correlation"),
            hasExistingDecision: false);

        var applied = Assert.IsType<ApprovalDecisionApplied>(result);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(DevOpsDecisionTime, request.LastModifiedAt);
        Assert.Equal(3, request.PersistenceVersion);
        Assert.Equal(ApprovalStage.DevOps, applied.Decision.Stage);
        Assert.Equal(ApprovalOutcome.Rejected, applied.Decision.Decision);
        Assert.Equal("Current risk is too high.", applied.Decision.Comment);
        Assert.Null(applied.Operation);
    }

    [Fact]
    public void DevOpsDecisionRejectsARequestAtTheWrongStage()
    {
        var request = CreateSubmittedRequest();
        var businessApproval = CreateBusinessApproval(request);
        var snapshot = RequestSnapshot.Capture(request);

        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.DevOps,
            businessApproval,
            ValidDevOpsApprovalCommand(),
            hasExistingDecision: false);

        var notApplied = Assert.IsType<ApprovalDecisionNotApplied>(result);
        Assert.Equal(ApprovalDecisionPolicyError.InvalidTransition, notApplied.Error);
        snapshot.AssertUnchanged(request);
    }

    [Theory]
    [InlineData(PrincipalKind.Requester, null, "business-alpha", false)]
    [InlineData(PrincipalKind.BusinessApprover, "client-beta", "business-alpha", false)]
    [InlineData(PrincipalKind.BusinessApprover, "client-alpha", "business-beta", false)]
    [InlineData(PrincipalKind.BusinessApprover, "client-alpha", "business-alpha", true)]
    public void ResponsibleBusinessApproverRequiresRoleClientAndAssignment(
        PrincipalKind kind,
        string? principalClientId,
        string configuredApproverId,
        bool expected)
    {
        var client = new Client(
            "client-alpha",
            "Client Alpha",
            configuredApproverId);
        var principal = new AuthenticatedPrincipal(
            "business-alpha",
            "Business Alpha",
            kind,
            principalClientId);

        Assert.Equal(
            expected,
            principal.IsResponsibleBusinessApproverFor(client));
    }

    private static ApprovalCommand ValidBusinessApprovalCommand() =>
        new(
            Guid.Parse("57409ebf-2263-4e45-bfd1-29c6bc9700e5"),
            ApprovalOutcome.Approved,
            "business-alpha",
            null,
            BusinessDecisionTime,
            "business-correlation");

    private static ApprovalCommand ValidDevOpsApprovalCommand() =>
        new(
            Guid.Parse("7a70e89c-8ff7-427c-a5f3-ddacbb6d5cba"),
            ApprovalOutcome.Approved,
            "devops-approver",
            null,
            DevOpsDecisionTime,
            "devops-correlation");

    private static (AccessRequest Request, ApprovalDecision BusinessApproval)
        CreateBusinessApprovedRequest()
    {
        var request = CreateSubmittedRequest();
        var result = ApprovalDecisionPolicy.Apply(
            request,
            ApprovalStage.Business,
            priorApproval: null,
            ValidBusinessApprovalCommand(),
            hasExistingDecision: false);
        var applied = Assert.IsType<ApprovalDecisionApplied>(result);

        return (request, applied.Decision);
    }

    private static AccessRequest CreateSubmittedRequest() =>
        new(
            RequestId,
            Guid.NewGuid(),
            "requester",
            new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            RequestCreatedAt,
            "request-correlation");

    private static ApprovalDecision CreateBusinessApproval(AccessRequest request) =>
        new(
            Guid.Parse("0ff40f0c-17d7-429c-bab8-716b95928a7d"),
            request.Id,
            ApprovalStage.Business,
            ApprovalOutcome.Approved,
            "business-alpha",
            null,
            BusinessDecisionTime,
            "business-correlation");

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

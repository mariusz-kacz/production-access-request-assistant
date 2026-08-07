using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class BusinessDecisionPolicyTests
{
    private static readonly DateTimeOffset RequestCreatedAt =
        new(2026, 7, 17, 8, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DecisionTime =
        new(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyApprovalRecordsDecisionAgainstTheImmutableRequest()
    {
        var request = await CreateSubmittedRequestAsync();
        var decisionId = Guid.Parse("4f551db5-d9de-4e04-8555-9948d8e81b0a");

        var result = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                decisionId,
                ApprovalOutcome.Approved,
                " business-alpha ",
                " Approved for incident response. ",
                DecisionTime,
                " business-correlation "),
            hasExistingBusinessDecision: false);

        var applied = Assert.IsType<BusinessDecisionApplied>(result);
        var decision = applied.Decision;
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, request.Status);
        Assert.Equal(DecisionTime, request.LastModifiedAt);
        Assert.Equal(2, request.PersistenceVersion);
        Assert.Equal(decisionId, decision.Id);
        Assert.Equal(request.Id, decision.RequestId);
        Assert.Equal(ApprovalStage.Business, decision.Stage);
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal("business-alpha", decision.ApproverId);
        Assert.Equal("Approved for incident response.", decision.Comment);
        Assert.Equal(DecisionTime, decision.DecidedAt);
        Assert.Equal("business-correlation", decision.CorrelationId);
    }

    [Fact]
    public async Task ApplyRejectionTransitionsWithoutCarryingApprovedScope()
    {
        var request = await CreateSubmittedRequestAsync();

        var result = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                Guid.Parse("e2b658e5-a183-48a7-99e6-8b67300a60f7"),
                ApprovalOutcome.Rejected,
                "business-alpha",
                " Request is not justified. ",
                DecisionTime,
                "business-correlation"),
            hasExistingBusinessDecision: false);

        var applied = Assert.IsType<BusinessDecisionApplied>(result);
        Assert.Equal(RequestStatus.Rejected, request.Status);
        Assert.Equal(DecisionTime, request.LastModifiedAt);
        Assert.Equal(2, request.PersistenceVersion);
        Assert.Equal(ApprovalStage.Business, applied.Decision.Stage);
        Assert.Equal(ApprovalOutcome.Rejected, applied.Decision.Decision);
        Assert.Equal("Request is not justified.", applied.Decision.Comment);
    }

    [Fact]
    public async Task ApplyPreventsADuplicateBusinessDecisionForTheRequest()
    {
        var request = await CreateSubmittedRequestAsync();
        var originalLastModifiedAt = request.LastModifiedAt;
        var originalPersistenceVersion = request.PersistenceVersion;

        var result = BusinessDecisionPolicy.Apply(
            request,
            ValidCommand(),
            hasExistingBusinessDecision: true);

        var notApplied = Assert.IsType<BusinessDecisionNotApplied>(result);
        Assert.Equal(BusinessDecisionPolicyError.DuplicateStage, notApplied.Error);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Equal(originalLastModifiedAt, request.LastModifiedAt);
        Assert.Equal(originalPersistenceVersion, request.PersistenceVersion);
    }

    private static BusinessDecisionCommand ValidCommand()
    {
        return new BusinessDecisionCommand(
            Guid.Parse("57409ebf-2263-4e45-bfd1-29c6bc9700e5"),
            ApprovalOutcome.Approved,
            "business-alpha",
            null,
            DecisionTime,
            "business-correlation");
    }

    private static Task<AccessRequest> CreateSubmittedRequestAsync()
    {
        var request = new AccessRequest(
            Guid.Parse("661718f5-b8dd-47eb-b5ab-057e23dfaeb2"),
            "requester",
            new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            RequestCreatedAt,
            "request-correlation");

        return Task.FromResult(request);
    }
}

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
    public async Task ApplyApprovalTransitionsAndBindsTheExactCurrentRequestScope()
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
        Assert.Equal(request.RequestedRoleId, decision.ApprovedRoleId);
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
        Assert.Null(applied.Decision.ApprovedRoleId);
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

    private static async Task<AccessRequest> CreateSubmittedRequestAsync()
    {
        var requestContext = new SubmittedRequestContextReader();
        var service = new RequestSubmissionService(
            new RequestValidator(requestContext),
            requestContext,
            new SuccessfulWorkflowStore(),
            new FixedClock(RequestCreatedAt));

        var result = await service.SubmitAsync(
            "requester",
            new RequestValidationInput(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            "request-correlation",
            TestContext.Current.CancellationToken);

        return Assert.IsType<RequestSubmitted>(result).Request;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SubmittedRequestContextReader : IRequestContextReader
    {
        private readonly Client client = new("client-alpha", "Client Alpha");
        private readonly ProductionEnvironment environment = new(
            "PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha Production EU",
            "business-alpha");
        private readonly EnvironmentRole role = new(
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly);
        private readonly Incident incident = new(
            "INC-1042",
            "client-alpha",
            "PROD-ALPHA-EU",
            "Active Alpha incident",
            IncidentStatus.Active);
        private readonly AuthenticatedPrincipal requester = new(
            "requester",
            "Requesting Engineer",
            PrincipalKind.Requester,
            null);

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Result(client, cancellationToken);

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken) =>
            Result(environment, cancellationToken);

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken) =>
            Result(role, cancellationToken);

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
            string environmentId,
            CancellationToken cancellationToken) =>
            Result<IReadOnlyList<EnvironmentRole>>([role], cancellationToken);

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            Result(incident, cancellationToken);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken) =>
            Result(requester, cancellationToken);

        private static Task<ApplicationResult<T>> Result<T>(
            T value,
            CancellationToken cancellationToken)
            where T : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApplicationResult.Succeeded(value));
        }
    }

    private sealed class SuccessfulWorkflowStore : IWorkflowStore
    {
        public void AddRequest(AccessRequest request)
        {
        }

        public void AddAuditEvent(AuditEvent auditEvent)
        {
        }

        public void AddApprovalDecision(ApprovalDecision decision) =>
            throw new NotSupportedException();

        public void AddProvisioningOperation(ProvisioningOperation operation) =>
            throw new NotSupportedException();

        public void AddAccessGrant(AccessGrant grant) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
            Guid requestId,
            ApprovalStage stage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApplicationResult.Succeeded());
        }
    }
}

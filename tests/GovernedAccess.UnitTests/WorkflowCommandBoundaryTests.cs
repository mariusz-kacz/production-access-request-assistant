using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class WorkflowCommandBoundaryTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoaderResolvesNormalizedActorRequestAndCorrelation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = CreateRequest();
        var principal = new AuthenticatedPrincipal(
            "devops-approver",
            "DevOps Approver",
            PrincipalKind.DevOpsApprover);
        var requestContext = new StubRequestContext(
            ApplicationResult.Succeeded(principal));
        var workflowStore = new StubWorkflowStore(
            ApplicationResult.Succeeded(request));
        var loader = new WorkflowCommandContextLoader(
            requestContext,
            workflowStore);

        var result = await loader.LoadAsync(
            request.Id,
            " devops-approver ",
            " command-correlation ",
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(request, result.Value.Request);
        Assert.Same(principal, result.Value.Principal);
        Assert.Equal("command-correlation", result.Value.CorrelationId);
        Assert.Equal("devops-approver", requestContext.RequestedPrincipalId);
    }

    [Fact]
    public async Task LoaderMapsMissingAuthenticatedPrincipalToUnauthenticated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var missing = new ApplicationFailure(
            ApplicationFailureKind.NotFound,
            "principal_not_found",
            "The principal was not found.");
        var requestContext = new StubRequestContext(
            ApplicationResult.Failed<AuthenticatedPrincipal>(missing));
        var workflowStore = new StubWorkflowStore(
            ApplicationResult.Succeeded(CreateRequest()));
        var loader = new WorkflowCommandContextLoader(
            requestContext,
            workflowStore);

        var result = await loader.LoadAsync(
            Guid.NewGuid(),
            "unknown-principal",
            "command-correlation",
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApplicationFailureKind.Unauthenticated,
            result.Failure!.Kind);
        Assert.Equal("authenticated_principal_not_found", result.Failure.Code);
        Assert.Equal(0, workflowStore.RequestReads);
    }

    [Fact]
    public async Task RecorderPersistsTheExpectedRejectedAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflowStore = new StubWorkflowStore(
            ApplicationResult.Succeeded(CreateRequest()));
        var recorder = new RejectedWorkflowAttemptRecorder(
            workflowStore,
            new StubClock(UtcNow));
        var rejection = new ApplicationFailure(
            ApplicationFailureKind.Unauthorized,
            "actor_not_authorized",
            "The actor is not authorized.");

        var result = await recorder.RecordAsync(
            CreateRequest(),
            ApprovalStage.DevOps,
            "requester",
            "command-correlation",
            rejection,
            WorkflowAttemptRejectionKind.Authorization,
            cancellationToken);

        Assert.Same(rejection, result);
        var auditEvent = Assert.Single(workflowStore.AddedAuditEvents);
        Assert.Equal(AuditEventType.AuthorizationRejected, auditEvent.EventType);
        Assert.Equal(ApprovalStage.DevOps, ReadRejectedStage(auditEvent));
        Assert.Equal("requester", auditEvent.ActorId);
        Assert.Equal("command-correlation", auditEvent.CorrelationId);
        Assert.Equal(rejection.Code, auditEvent.OutcomeCode);
        Assert.Equal(UtcNow, auditEvent.OccurredAt);
    }

    [Fact]
    public async Task RecorderReturnsPersistenceFailureInsteadOfOriginalRejection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saveFailure = new ApplicationFailure(
            ApplicationFailureKind.DependencyFailure,
            "audit_save_failed",
            "The rejected attempt could not be persisted.");
        var workflowStore = new StubWorkflowStore(
            ApplicationResult.Succeeded(CreateRequest()))
        {
            SaveResult = ApplicationResult.Failed(saveFailure),
        };
        var recorder = new RejectedWorkflowAttemptRecorder(
            workflowStore,
            new StubClock(UtcNow));

        var result = await recorder.RecordAsync(
            CreateRequest(),
            ApprovalStage.Business,
            "requester",
            "command-correlation",
            new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                "invalid_transition",
                "The transition is invalid."),
            WorkflowAttemptRejectionKind.InvalidTransition,
            cancellationToken);

        Assert.Same(saveFailure, result);
        Assert.Equal(
            AuditEventType.InvalidTransitionRejected,
            Assert.Single(workflowStore.AddedAuditEvents).EventType);
    }

    private static ApprovalStage ReadRejectedStage(AuditEvent auditEvent)
    {
        using var details = System.Text.Json.JsonDocument.Parse(
            auditEvent.DetailsJson);
        return Enum.Parse<ApprovalStage>(
            details.RootElement.GetProperty("stage").GetString()!);
    }

    private static AccessRequest CreateRequest()
    {
        return new AccessRequest(
            Guid.Parse("9682b9d0-7637-43cf-8028-f7cd82c99479"),
            "requester",
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            UtcNow.AddHours(-1),
            "request-correlation");
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class StubRequestContext(
        ApplicationResult<AuthenticatedPrincipal> principalResult)
        : IRequestContextReader
    {
        public string? RequestedPrincipalId { get; private set; }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPrincipalId = principalId;
            return Task.FromResult(principalResult);
        }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProductionEnvironment>>
            GetProductionEnvironmentAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>>
            GetEnvironmentRolesAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubWorkflowStore(
        ApplicationResult<AccessRequest> requestResult)
        : IWorkflowStore
    {
        public int RequestReads { get; private set; }

        public List<AuditEvent> AddedAuditEvents { get; } = [];

        public ApplicationResult SaveResult { get; init; } =
            ApplicationResult.Succeeded();

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestReads++;
            return Task.FromResult(requestResult);
        }

        public void AddAuditEvent(AuditEvent auditEvent) =>
            AddedAuditEvents.Add(auditEvent);

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SaveResult);
        }

        public void AddRequest(AccessRequest request) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddApprovalDecision(ApprovalDecision decision) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
            Guid requestId,
            ApprovalStage stage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>>
            ListApprovalDecisionsAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddProvisioningOperation(ProvisioningOperation operation) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>>
            GetProvisioningOperationAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>>
            ReloadProvisioningOperationAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddAccessGrant(AccessGrant grant) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestSubmissionServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitAsyncBindsTheAuthoritativeRequesterAndPersistsImmutableScopeWithAuditEvidence()
    {
        var requestContext = new StubRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var service = CreateService(requestContext, workflowStore);

        var result = await service.SubmitAsync(
            " requester ",
            ValidInput(),
            " correlation-123 ",
            TestContext.Current.CancellationToken);

        var submitted = Assert.IsType<RequestSubmitted>(result);

        var request = Assert.Single(workflowStore.AddedRequests);
        Assert.Same(request, submitted.Request);
        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal("requester", request.RequesterId);
        Assert.Equal("client-alpha", request.ClientId);
        Assert.Equal("PROD-ALPHA-EU", request.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, request.RequestedRoleId);
        Assert.Equal("Investigate the active production incident.", request.Justification);
        Assert.Equal("INC-1042", request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Equal(CurrentTime, request.CreatedAt);
        Assert.Equal(CurrentTime, request.LastModifiedAt);
        Assert.Equal("correlation-123", request.CorrelationId);
        Assert.Equal(1, request.PersistenceVersion);

        var auditEvent = Assert.Single(workflowStore.AddedAuditEvents);
        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Equal("requester", auditEvent.ActorId);
        Assert.Equal(CurrentTime, auditEvent.OccurredAt);
        Assert.Equal("correlation-123", auditEvent.CorrelationId);
        Assert.Equal("request_created", auditEvent.OutcomeCode);
        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        Assert.Equal(
            RequestCreatedAuditDetails.CurrentSchemaVersion,
            details.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            RequestCreatedAuditDetails.SuccessfulValidationOutcomeCode,
            details.RootElement.GetProperty("validationOutcomeCode").GetString());
        Assert.Equal(
            "AwaitingBusinessApproval",
            details.RootElement.GetProperty("status").GetString());
        Assert.False(details.RootElement.TryGetProperty("justification", out _));
        Assert.Equal(1, workflowStore.SaveChangesCallCount);
    }

    [Theory]
    [InlineData(nameof(AccessRequest.RequesterId))]
    [InlineData(nameof(AccessRequest.ClientId))]
    [InlineData(nameof(AccessRequest.EnvironmentId))]
    [InlineData(nameof(AccessRequest.RequestedRoleId))]
    [InlineData(nameof(AccessRequest.Justification))]
    [InlineData(nameof(AccessRequest.IncidentId))]
    public void SubmittedRequestScopeCannotBeChangedOutsideTheEntity(string propertyName)
    {
        var property = typeof(AccessRequest).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"AccessRequest.{propertyName} is missing.");

        Assert.NotNull(property.SetMethod);
        Assert.True(property.SetMethod.IsPrivate);
    }

    [Fact]
    public async Task SubmitAsyncReturnsFieldErrorsWithoutCreatingStateWhenValidationFails()
    {
        var requestContext = new StubRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var service = CreateService(requestContext, workflowStore);

        var result = await service.SubmitAsync(
            "requester",
            ValidInput(justification: "short"),
            "correlation-123",
            TestContext.Current.CancellationToken);

        var rejection = Assert.IsType<RequestSubmissionValidationRejected>(result);
        Assert.Contains(
            rejection.ValidationErrors,
            error => error.Field == "justification"
                && error.Code == "justification_length_invalid");
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
        Assert.Equal(0, workflowStore.SaveChangesCallCount);
    }

    [Fact]
    public async Task SubmitAsyncRejectsANonRequesterWithoutCreatingState()
    {
        var requestContext = new StubRequestContextReader(
            new AuthenticatedPrincipal(
                "business-approver",
                "Business Approver",
                PrincipalKind.BusinessApprover,
                "client-alpha"));
        var workflowStore = new RecordingWorkflowStore();
        var service = CreateService(requestContext, workflowStore);

        var result = await service.SubmitAsync(
            "business-approver",
            ValidInput(),
            "correlation-123",
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<RequestSubmissionFailed>(result);
        Assert.Equal(ApplicationFailureKind.Unauthorized, failure.Failure.Kind);
        Assert.Equal("requester_required", failure.Failure.Code);
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
        Assert.Equal(0, workflowStore.SaveChangesCallCount);
    }

    [Fact]
    public async Task SubmitAsyncPreservesPersistenceFailure()
    {
        var persistenceFailure = new ApplicationFailure(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_save_conflict",
            "The request could not be saved.");
        var requestContext = new StubRequestContextReader();
        var workflowStore = new RecordingWorkflowStore
        {
            SaveResult = ApplicationResult.Failed(persistenceFailure),
        };
        var service = CreateService(requestContext, workflowStore);

        var result = await service.SubmitAsync(
            "requester",
            ValidInput(),
            "correlation-123",
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<RequestSubmissionFailed>(result);
        Assert.Same(persistenceFailure, failure.Failure);
        Assert.Equal(1, workflowStore.SaveChangesCallCount);
    }

    private static RequestSubmissionService CreateService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore)
    {
        return new RequestSubmissionService(
            new RequestValidator(requestContext),
            requestContext,
            workflowStore,
            new StubClock(CurrentTime));
    }

    private static RequestValidationInput ValidInput(
        string? justification = "  Investigate the active production incident.  ")
    {
        return new RequestValidationInput(
            " client-alpha ",
            " PROD-ALPHA-EU ",
            $" {ProductionRoleIds.ReadOnly} ",
            justification,
            " INC-1042 ");
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class StubRequestContextReader : IRequestContextReader
    {
        private readonly AuthenticatedPrincipal principal;
        private readonly Client client = new("client-alpha", "Client Alpha");
        private readonly ProductionEnvironment environment = new(
            "PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha Production EU",
            "business-approver");
        private readonly EnvironmentRole role = new(
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly);
        private readonly Incident incident = new(
            "INC-1042",
            "client-alpha",
            "PROD-ALPHA-EU",
            "Active incident",
            IncidentStatus.Active);

        public StubRequestContextReader(AuthenticatedPrincipal? principal = null)
        {
            this.principal = principal ?? new AuthenticatedPrincipal(
                "requester",
                "Requester",
                PrincipalKind.Requester);
        }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            return MatchAsync(clientId, client.Id, client, cancellationToken);
        }

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            return MatchAsync(environmentId, environment.Id, environment, cancellationToken);
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                string.Equals(environmentId, role.EnvironmentId, StringComparison.Ordinal)
                && string.Equals(roleId, role.RoleId, StringComparison.Ordinal)
                    ? ApplicationResult.Succeeded(role)
                    : NotFound<EnvironmentRole>());
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRole> roles = string.Equals(
                environmentId,
                role.EnvironmentId,
                StringComparison.Ordinal)
                ? [role]
                : [];
            return Task.FromResult(ApplicationResult.Succeeded(roles));
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            return MatchAsync(incidentId, incident.Id, incident, cancellationToken);
        }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            return MatchAsync(principalId, principal.Id, principal, cancellationToken);
        }

        private static Task<ApplicationResult<T>> MatchAsync<T>(
            string actualId,
            string expectedId,
            T value,
            CancellationToken cancellationToken)
            where T : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                string.Equals(actualId, expectedId, StringComparison.Ordinal)
                    ? ApplicationResult.Succeeded(value)
                    : NotFound<T>());
        }

        private static ApplicationResult<T> NotFound<T>()
            where T : notnull
        {
            return ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    "stored_record_not_found",
                    "The stored record was not found."));
        }
    }

    private sealed class RecordingWorkflowStore : IWorkflowStore
    {
        public List<AccessRequest> AddedRequests { get; } = [];

        public List<AuditEvent> AddedAuditEvents { get; } = [];

        public int SaveChangesCallCount { get; private set; }

        public ApplicationResult SaveResult { get; init; } = ApplicationResult.Succeeded();

        public void AddRequest(AccessRequest request) => AddedRequests.Add(request);

        public void AddAuditEvent(AuditEvent auditEvent) => AddedAuditEvents.Add(auditEvent);

        public Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCallCount++;
            return Task.FromResult(SaveResult);
        }

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddApprovalDecision(ApprovalDecision decision) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
            Guid requestId,
            ApprovalStage stage,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddProvisioningOperation(ProvisioningOperation operation) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddAccessGrant(AccessGrant grant) => throw new NotSupportedException();

        public Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
            Guid requestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

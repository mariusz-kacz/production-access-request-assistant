using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class PreparedRequestConfirmationTests
{
    private static readonly Guid ConversationRecordId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreparationId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReservedRequestId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt = CreatedAt.AddMinutes(5);

    [Fact]
    public async Task ConfirmAsyncCreatesTheReservedRequestFromExactPreparedScope()
    {
        var preparedRequest = CreatePreparedRequest();
        var conversation = CreateReadyConversation();
        var requestContext = new RecordingRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var intakeStore = new StubRequestIntakeStore(
            preparedRequest,
            conversation);
        var service = CreateService(requestContext, workflowStore, intakeStore);

        var outcome = await service.ConfirmAsync(
            new ConfirmPreparedAccessRequestCommand(
                CreateActor(),
                PreparationId,
                " confirmation-correlation "),
            TestContext.Current.CancellationToken);

        var succeeded =
            Assert.IsType<PreparedRequestConfirmationSucceeded>(outcome);
        Assert.Equal(ReservedRequestId, succeeded.RequestId);
        Assert.False(succeeded.WasAlreadySubmitted);

        var request = Assert.Single(workflowStore.AddedRequests);
        Assert.Equal(ReservedRequestId, request.Id);
        Assert.Equal(preparedRequest.RequesterId, request.RequesterId);
        Assert.Equal(preparedRequest.ClientId, request.ClientId);
        Assert.Equal(preparedRequest.EnvironmentId, request.EnvironmentId);
        Assert.Equal(preparedRequest.RequestedRoleId, request.RequestedRoleId);
        Assert.Equal(preparedRequest.Justification, request.Justification);
        Assert.Equal(preparedRequest.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Equal(ConfirmedAt, request.CreatedAt);
        Assert.Equal("confirmation-correlation", request.CorrelationId);

        var auditEvent = Assert.Single(workflowStore.AddedAuditEvents);
        Assert.Equal(ReservedRequestId, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Equal(preparedRequest.RequesterId, auditEvent.ActorId);
        Assert.Equal(ConfirmedAt, auditEvent.OccurredAt);
        Assert.Equal("confirmation-correlation", auditEvent.CorrelationId);
        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        Assert.Equal(
            "AwaitingBusinessApproval",
            details.RootElement.GetProperty("status").GetString());

        Assert.Equal(PreparedAccessRequestStatus.Submitted, preparedRequest.Status);
        Assert.Equal(ConfirmedAt, preparedRequest.SubmittedAt);
        Assert.Equal(ReservedRequestId, preparedRequest.SubmittedRequestId);
        Assert.Equal(
            RequestPreparationConversationStatus.Submitted,
            conversation.Status);
        Assert.Null(conversation.ClientId);
        Assert.Null(conversation.EnvironmentId);
        Assert.Null(conversation.RequestedRoleId);
        Assert.Null(conversation.Justification);
        Assert.Null(conversation.IncidentId);
        Assert.Equal(1, intakeStore.SaveChangesCallCount);
        Assert.Equal(0, workflowStore.SaveChangesCallCount);
        Assert.Equal(1, requestContext.PrincipalLookupCount);
        Assert.Equal(1, requestContext.ClientLookupCount);
        Assert.Equal(1, requestContext.EnvironmentLookupCount);
        Assert.Equal(1, requestContext.RoleLookupCount);
        Assert.Equal(1, requestContext.IncidentLookupCount);
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("tenant")]
    [InlineData("actor")]
    [InlineData("conversation")]
    [InlineData("requester")]
    public async Task ConfirmAsyncRejectsEveryForeignOwnershipBinding(
        string mismatchedBinding)
    {
        var preparedRequest = CreatePreparedRequest();
        var conversation = CreateReadyConversation();
        var requestContext = new RecordingRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var intakeStore = new StubRequestIntakeStore(
            preparedRequest,
            conversation);
        var service = CreateService(requestContext, workflowStore, intakeStore);

        var outcome = await service.ConfirmAsync(
            new ConfirmPreparedAccessRequestCommand(
                CreateActor(mismatchedBinding),
                PreparationId,
                "confirmation-correlation"),
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparedRequestConfirmationFailed>(outcome);
        Assert.Equal(ApplicationFailureKind.Unauthorized, failed.Failure.Kind);
        Assert.Equal(
            PreparedRequestConfirmationService.ForbiddenCode,
            failed.Failure.Code);
        Assert.Equal(0, intakeStore.ConversationLookupCount);
        Assert.Equal(0, intakeStore.SaveChangesCallCount);
        Assert.Equal(0, requestContext.PrincipalLookupCount);
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
        Assert.Equal(PreparedAccessRequestStatus.Ready, preparedRequest.Status);
    }

    [Fact]
    public async Task ConfirmAsyncRejectsAMismatchedPersistedConversation()
    {
        var preparedRequest = CreatePreparedRequest();
        var conversation = CreateReadyConversation(channelActorId: "actor-002");
        var requestContext = new RecordingRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var intakeStore = new StubRequestIntakeStore(
            preparedRequest,
            conversation);
        var service = CreateService(requestContext, workflowStore, intakeStore);

        var outcome = await service.ConfirmAsync(
            new ConfirmPreparedAccessRequestCommand(
                CreateActor(),
                PreparationId,
                "confirmation-correlation"),
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparedRequestConfirmationFailed>(outcome);
        Assert.Equal(
            ApplicationFailureKind.InvalidTransition,
            failed.Failure.Kind);
        Assert.Equal(
            PreparedRequestConfirmationService.ConversationMismatchCode,
            failed.Failure.Code);
        Assert.Equal(1, intakeStore.ConversationLookupCount);
        Assert.Equal(0, intakeStore.SaveChangesCallCount);
        Assert.Equal(0, requestContext.PrincipalLookupCount);
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
    }

    [Fact]
    public async Task ConfirmAsyncRevalidatesPreparedScopeAgainstCurrentContext()
    {
        var preparedRequest = CreatePreparedRequest();
        var conversation = CreateReadyConversation();
        var requestContext = new RecordingRequestContextReader
        {
            IsPreparedRoleAvailable = false,
        };
        var workflowStore = new RecordingWorkflowStore();
        var intakeStore = new StubRequestIntakeStore(
            preparedRequest,
            conversation);
        var service = CreateService(requestContext, workflowStore, intakeStore);

        var outcome = await service.ConfirmAsync(
            new ConfirmPreparedAccessRequestCommand(
                CreateActor(),
                PreparationId,
                "confirmation-correlation"),
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparedRequestConfirmationFailed>(outcome);
        Assert.Equal(
            ApplicationFailureKind.InvalidTransition,
            failed.Failure.Kind);
        Assert.Equal(
            PreparedRequestConfirmationService.InvalidatedCode,
            failed.Failure.Code);
        Assert.Equal(1, requestContext.PrincipalLookupCount);
        Assert.Equal(1, requestContext.ClientLookupCount);
        Assert.Equal(1, requestContext.EnvironmentLookupCount);
        Assert.Equal(1, requestContext.RoleLookupCount);
        Assert.Equal(0, requestContext.IncidentLookupCount);
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
        Assert.Equal(0, intakeStore.SaveChangesCallCount);
        Assert.Equal(PreparedAccessRequestStatus.Ready, preparedRequest.Status);
        Assert.Equal(
            RequestPreparationConversationStatus.Ready,
            conversation.Status);
    }

    [Fact]
    public async Task ConfirmAsyncPreservesTypedPreparedRequestLoadFailure()
    {
        var dependencyFailure = new ApplicationFailure(
            ApplicationFailureKind.DependencyUnavailable,
            "prepared_request_store_unavailable",
            "The prepared request store is unavailable.");
        var requestContext = new RecordingRequestContextReader();
        var workflowStore = new RecordingWorkflowStore();
        var intakeStore = new StubRequestIntakeStore(
            CreatePreparedRequest(),
            CreateReadyConversation())
        {
            ReloadPreparedRequestResult =
                ApplicationResult.Failed<PreparedAccessRequest>(dependencyFailure),
        };
        var service = CreateService(requestContext, workflowStore, intakeStore);

        var outcome = await service.ConfirmAsync(
            new ConfirmPreparedAccessRequestCommand(
                CreateActor(),
                PreparationId,
                "confirmation-correlation"),
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparedRequestConfirmationFailed>(outcome);
        Assert.Same(dependencyFailure, failed.Failure);
        Assert.Equal(0, intakeStore.ConversationLookupCount);
        Assert.Equal(0, intakeStore.SaveChangesCallCount);
        Assert.Equal(0, requestContext.PrincipalLookupCount);
        Assert.Empty(workflowStore.AddedRequests);
        Assert.Empty(workflowStore.AddedAuditEvents);
    }

    private static PreparedRequestConfirmationService CreateService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        IRequestIntakeStore intakeStore)
    {
        var clock = new StubClock(ConfirmedAt);
        var submissionService = new RequestSubmissionService(
            new RequestValidator(requestContext),
            requestContext,
            workflowStore,
            clock);
        return new PreparedRequestConfirmationService(
            intakeStore,
            submissionService,
            clock);
    }

    private static AuthenticatedChannelActor CreateActor(
        string? mismatchedBinding = null)
    {
        return new AuthenticatedChannelActor(
            mismatchedBinding == "channel" ? "other-channel" : "msteams",
            mismatchedBinding == "tenant" ? "tenant-002" : "tenant-001",
            mismatchedBinding == "actor" ? "actor-002" : "actor-001",
            mismatchedBinding == "conversation"
                ? "conversation-002"
                : "conversation-001",
            mismatchedBinding == "requester" ? "other-requester" : "requester");
    }

    private static PreparedAccessRequest CreatePreparedRequest()
    {
        return new PreparedAccessRequest(
            PreparationId,
            ConversationRecordId,
            ReservedRequestId,
            "msteams",
            "tenant-001",
            "actor-001",
            "conversation-001",
            "requester",
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            CreatedAt,
            "preparation-correlation");
    }

    private static RequestPreparationConversation CreateReadyConversation(
        string channelActorId = "actor-001")
    {
        var conversation = new RequestPreparationConversation(
            ConversationRecordId,
            "msteams",
            "tenant-001",
            channelActorId,
            "conversation-001",
            "requester",
            CreatedAt.AddMinutes(-1),
            "conversation-correlation");
        conversation.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            pendingClarification: null,
            CreatedAt,
            "candidate-correlation");
        conversation.MarkReady(
            PreparationId,
            CreatedAt,
            "ready-correlation");
        return conversation;
    }

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingRequestContextReader : IRequestContextReader
    {
        private readonly AuthenticatedPrincipal principal = new(
            "requester",
            "Requester",
            PrincipalKind.Requester);
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

        public bool IsPreparedRoleAvailable { get; init; } = true;

        public int PrincipalLookupCount { get; private set; }

        public int ClientLookupCount { get; private set; }

        public int EnvironmentLookupCount { get; private set; }

        public int RoleLookupCount { get; private set; }

        public int IncidentLookupCount { get; private set; }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClientLookupCount++;
            return Task.FromResult(
                Matches(clientId, client.Id)
                    ? ApplicationResult.Succeeded(client)
                    : NotFound<Client>());
        }

        public Task<ApplicationResult<ProductionEnvironment>>
            GetProductionEnvironmentAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnvironmentLookupCount++;
            return Task.FromResult(
                Matches(environmentId, environment.Id)
                    ? ApplicationResult.Succeeded(environment)
                    : NotFound<ProductionEnvironment>());
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoleLookupCount++;
            return Task.FromResult(
                IsPreparedRoleAvailable
                && Matches(environmentId, role.EnvironmentId)
                && Matches(roleId, role.RoleId)
                    ? ApplicationResult.Succeeded(role)
                    : NotFound<EnvironmentRole>());
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>>
            GetEnvironmentRolesAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRole> roles =
                IsPreparedRoleAvailable
                && Matches(environmentId, role.EnvironmentId)
                    ? [role]
                    : [];
            return Task.FromResult(ApplicationResult.Succeeded(roles));
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IncidentLookupCount++;
            return Task.FromResult(
                Matches(incidentId, incident.Id)
                    ? ApplicationResult.Succeeded(incident)
                    : NotFound<Incident>());
        }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrincipalLookupCount++;
            return Task.FromResult(
                Matches(principalId, principal.Id)
                    ? ApplicationResult.Succeeded(principal)
                    : NotFound<AuthenticatedPrincipal>());
        }

        private static bool Matches(string actual, string expected) =>
            string.Equals(actual, expected, StringComparison.Ordinal);

        private static ApplicationResult<T> NotFound<T>()
            where T : notnull =>
            ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    "stored_record_not_found",
                    "The stored record was not found."));
    }

    private sealed class StubRequestIntakeStore(
        PreparedAccessRequest preparedRequest,
        RequestPreparationConversation conversation)
        : IRequestIntakeStore
    {
        public ApplicationResult<PreparedAccessRequest>
            ReloadPreparedRequestResult { get; init; } =
                ApplicationResult.Succeeded(preparedRequest);

        public int ConversationLookupCount { get; private set; }

        public int SaveChangesCallCount { get; private set; }

        public void AddConversation(RequestPreparationConversation value) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetActiveConversationAsync(
                AuthenticatedChannelActor actor,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetConversationAsync(
                Guid conversationRecordId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversationLookupCount++;
            return Task.FromResult(
                conversationRecordId == conversation.Id
                    ? ApplicationResult.Succeeded(conversation)
                    : NotFound<RequestPreparationConversation>());
        }

        public void AddPreparedRequest(PreparedAccessRequest value) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<PreparedAccessRequest>>
            GetPreparedRequestAsync(
                Guid preparationId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<PreparedAccessRequest>>
            ReloadPreparedRequestAsync(
                Guid preparationId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReloadPreparedRequestResult);
        }

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCallCount++;
            return Task.FromResult(ApplicationResult.Succeeded());
        }

        private static ApplicationResult<T> NotFound<T>()
            where T : notnull =>
            ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    "stored_record_not_found",
                    "The stored record was not found."));
    }

    private sealed class RecordingWorkflowStore : IWorkflowStore
    {
        public List<AccessRequest> AddedRequests { get; } = [];

        public List<AuditEvent> AddedAuditEvents { get; } = [];

        public int SaveChangesCallCount { get; private set; }

        public void AddRequest(AccessRequest request) =>
            AddedRequests.Add(request);

        public void AddAuditEvent(AuditEvent auditEvent) =>
            AddedAuditEvents.Add(auditEvent);

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveChangesCallCount++;
            return Task.FromResult(ApplicationResult.Succeeded());
        }

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>>
            ListRequestsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddApprovalDecision(ApprovalDecision decision) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ApprovalDecision>>
            GetApprovalDecisionAsync(
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

        public Task<ApplicationResult<AccessGrant>>
            GetAccessGrantForRequestAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>>
            ListAuditEventsAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

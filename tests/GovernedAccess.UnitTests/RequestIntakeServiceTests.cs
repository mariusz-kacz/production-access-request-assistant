using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestIntakeServiceTests
{
    [Fact]
    public async Task PrepareAndConfirmPreserveOneImmutableScopeAndReservedIdentity()
    {
        var scenario = new IntakeScenario();

        var ready = await scenario.PrepareAsync();

        Assert.True(ready.IsReady);
        Assert.Equal("Collecting", scenario.StatusWhenAdded);
        Assert.Equal("Ready", scenario.IntakeStatus);
        Assert.NotEqual(Guid.Empty, ready.PreparationId);
        Assert.NotEqual(Guid.Empty, ready.ReservedRequestId);
        Assert.Equal("client-alpha", ready.ClientId);
        Assert.Equal("PROD-ALPHA-EU", ready.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, ready.RoleId);
        Assert.Equal("Investigate the active production incident.", ready.Justification);
        Assert.Equal("INC-1042", ready.IncidentId);
        Assert.Throws<InvalidOperationException>(scenario.AttemptReadyScopeChange);

        var confirmed = await scenario.ConfirmAsync(IntakeScenario.Owner);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(ready.ReservedRequestId, confirmed.RequestId);
        Assert.Equal("Submitted", scenario.IntakeStatus);
        Assert.Equal("Submitted", scenario.PreparedStatus);
        Assert.Equal(2, scenario.SaveCount);

        var request = Assert.Single(scenario.Requests);
        Assert.Equal(ready.ReservedRequestId, request.Id);
        Assert.Equal(IntakeScenario.Owner.RequesterId, request.RequesterId);
        Assert.Equal(ready.ClientId, request.ClientId);
        Assert.Equal(ready.EnvironmentId, request.EnvironmentId);
        Assert.Equal(ready.RoleId, request.RequestedRoleId);
        Assert.Equal(ready.Justification, request.Justification);
        Assert.Equal(ready.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);

        var auditEvent = Assert.Single(scenario.AuditEvents);
        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
    }

    [Fact]
    public async Task ConfirmationRequiresTheAuthenticatedPreparationOwner()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        var otherActor = new AuthenticatedChannelActor(
            IntakeScenario.Owner.Channel,
            IntakeScenario.Owner.TenantId,
            "other-actor",
            IntakeScenario.Owner.ConversationId,
            IntakeScenario.Owner.RequesterId);

        var result = await scenario.ConfirmAsync(otherActor);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureKind.Unauthorized, result.FailureKind);
        Assert.Equal("Ready", scenario.IntakeStatus);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task ConfirmationRevalidatesThePreparedScopeAgainstAuthoritativeData()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        scenario.RoleIsAvailable = false;

        var result = await scenario.ConfirmAsync(IntakeScenario.Owner);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureKind.InvalidTransition, result.FailureKind);
        Assert.Equal("Ready", scenario.IntakeStatus);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Theory]
    [InlineData(
        RequestPreparationInterpretationOutcomeKind.MalformedModelOutput,
        ApplicationFailureKind.DependencyFailure)]
    [InlineData(
        RequestPreparationInterpretationOutcomeKind.Timeout,
        ApplicationFailureKind.Timeout)]
    [InlineData(
        RequestPreparationInterpretationOutcomeKind.Cancelled,
        ApplicationFailureKind.Cancelled)]
    [InlineData(
        RequestPreparationInterpretationOutcomeKind.Unavailable,
        ApplicationFailureKind.DependencyUnavailable)]
    public async Task PreparationFailuresRetainTheirTypedCategory(
        RequestPreparationInterpretationOutcomeKind interpretationFailure,
        ApplicationFailureKind expectedFailure)
    {
        var scenario = new IntakeScenario(interpretationFailure);

        var result = await scenario.PrepareAsync();

        Assert.False(result.IsReady);
        Assert.Equal(expectedFailure, result.FailureKind);
        Assert.Equal(0, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    private sealed record PreparationObservation(
        bool IsReady,
        Guid PreparationId,
        Guid ReservedRequestId,
        string? ClientId,
        string? EnvironmentId,
        string? RoleId,
        string? Justification,
        string? IncidentId,
        ApplicationFailureKind? FailureKind);

    private sealed record ConfirmationObservation(
        bool IsSuccess,
        Guid? RequestId,
        ApplicationFailureKind? FailureKind);

    private sealed class IntakeScenario :
        IRequestPreparationInterpreter,
        IRequestContextReader,
        IRequestIntakeStore,
        IWorkflowStore,
        IClock
    {
        private static readonly DateTimeOffset CurrentTime =
            new(2026, 7, 27, 10, 5, 0, TimeSpan.Zero);

        private readonly RequestPreparationInterpretationOutcome interpretation;
        private readonly RequestPreparationService preparationService;
        private readonly PreparedRequestConfirmationService confirmationService;
        private RequestPreparationConversation? conversation;
        private PreparedAccessRequest? preparedRequest;

        public IntakeScenario(
            RequestPreparationInterpretationOutcomeKind? interpretationFailure = null)
        {
            interpretation = interpretationFailure is null
                ? new RequestPreparationInterpretationOutcome(
                    new RequestPreparationProposal(
                        RequestPreparationProposalKind.Candidate,
                        new RequestCandidate(
                            "client-alpha",
                            "PROD-ALPHA-EU",
                            ProductionRoleIds.ReadOnly,
                            "Investigate the active production incident.",
                            "INC-1042"),
                        clarification: null))
                : new RequestPreparationInterpretationOutcome(
                    interpretationFailure.Value);

            var validator = new RequestValidator(this);
            preparationService = new RequestPreparationService(
                this,
                validator,
                this,
                this,
                this);
            confirmationService = new PreparedRequestConfirmationService(
                this,
                new RequestSubmissionService(validator, this, this, this),
                this);
        }

        public static AuthenticatedChannelActor Owner { get; } =
            new(
                RequestPreparationConversation.TeamsChannel,
                "tenant-001",
                "actor-001",
                "conversation-001",
                "requester");

        public bool RoleIsAvailable { get; set; } = true;

        public string? StatusWhenAdded { get; private set; }

        public string? IntakeStatus => conversation?.Status.ToString();

        public string? PreparedStatus => preparedRequest?.Status.ToString();

        public int SaveCount { get; private set; }

        public List<AccessRequest> Requests { get; } = [];

        public List<AuditEvent> AuditEvents { get; } = [];

        public DateTimeOffset UtcNow => CurrentTime;

        public async Task<PreparationObservation> PrepareAsync()
        {
            var outcome = await preparationService.PrepareAsync(
                new PrepareAccessRequestCommand(
                    Owner,
                    "I need production access.",
                    "prepare-correlation"),
                TestContext.Current.CancellationToken);

            return outcome switch
            {
                RequestReadyForConfirmation ready => new PreparationObservation(
                    true,
                    ready.PreparedRequest.PreparationId,
                    ready.PreparedRequest.ReservedRequestId,
                    ready.PreparedRequest.ClientId,
                    ready.PreparedRequest.EnvironmentId,
                    ready.PreparedRequest.RequestedRoleId,
                    ready.PreparedRequest.Justification,
                    ready.PreparedRequest.IncidentId,
                    null),
                RequestPreparationFailed failed => new PreparationObservation(
                    false,
                    Guid.Empty,
                    Guid.Empty,
                    null,
                    null,
                    null,
                    null,
                    null,
                    failed.Failure.Kind),
                _ => throw new InvalidOperationException(
                    "The scenario expected either readiness or a typed failure."),
            };
        }

        public async Task<ConfirmationObservation> ConfirmAsync(
            AuthenticatedChannelActor actor)
        {
            if (preparedRequest is null)
            {
                throw new InvalidOperationException(
                    "The scenario must be prepared before confirmation.");
            }

            var outcome = await confirmationService.ConfirmAsync(
                new ConfirmPreparedAccessRequestCommand(
                    actor,
                    preparedRequest.PreparationId,
                    "confirm-correlation"),
                TestContext.Current.CancellationToken);

            return outcome switch
            {
                PreparedRequestConfirmationSucceeded succeeded =>
                    new ConfirmationObservation(
                        true,
                        succeeded.RequestId,
                        null),
                PreparedRequestConfirmationFailed failed =>
                    new ConfirmationObservation(
                        false,
                        null,
                        failed.Failure.Kind),
                _ => throw new InvalidOperationException(
                    "The scenario received an unsupported confirmation outcome."),
            };
        }

        public void AttemptReadyScopeChange()
        {
            var current = conversation
                ?? throw new InvalidOperationException("No intake exists.");
            current.UpdateCandidate(
                "other-client",
                "OTHER-ENVIRONMENT",
                ProductionRoleIds.Support,
                "Replace the already prepared scope.",
                incidentId: null,
                pendingClarification: null,
                CurrentTime.AddMinutes(1),
                "forged-change");
        }

        public Task<RequestPreparationInterpretationOutcome> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(interpretation);
        }

        public void AddConversation(RequestPreparationConversation value)
        {
            StatusWhenAdded = value.Status.ToString();
            conversation = value;
        }

        public void AddPreparedRequest(PreparedAccessRequest value) =>
            preparedRequest = value;

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetActiveConversationAsync(
                AuthenticatedChannelActor actor,
                CancellationToken cancellationToken) =>
            FromOptional(
                conversation,
                "active_intake_not_found",
                cancellationToken);

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetConversationAsync(
                Guid conversationRecordId,
                CancellationToken cancellationToken) =>
            FromOptional(
                conversation?.Id == conversationRecordId ? conversation : null,
                "intake_not_found",
                cancellationToken);

        public Task<ApplicationResult<PreparedAccessRequest>>
            GetPreparedRequestAsync(
                Guid preparationId,
                CancellationToken cancellationToken) =>
            FromOptional(
                preparedRequest?.PreparationId == preparationId
                    ? preparedRequest
                    : null,
                "prepared_intake_not_found",
                cancellationToken);

        public Task<ApplicationResult<PreparedAccessRequest>>
            ReloadPreparedRequestAsync(
                Guid preparationId,
                CancellationToken cancellationToken) =>
            GetPreparedRequestAsync(preparationId, cancellationToken);

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(ApplicationResult.Succeeded());
        }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Match(
                clientId,
                "client-alpha",
                new Client("client-alpha", "Client Alpha"),
                cancellationToken);

        public Task<ApplicationResult<ProductionEnvironment>>
            GetProductionEnvironmentAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            Match(
                environmentId,
                "PROD-ALPHA-EU",
                new ProductionEnvironment(
                    "PROD-ALPHA-EU",
                    "client-alpha",
                    "Client Alpha Production EU",
                    "business-approver"),
                cancellationToken);

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken) =>
            FromOptional(
                RoleIsAvailable
                && environmentId == "PROD-ALPHA-EU"
                && roleId == ProductionRoleIds.ReadOnly
                    ? new EnvironmentRole(environmentId, roleId)
                    : null,
                "role_not_found",
                cancellationToken);

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>>
            GetEnvironmentRolesAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRole> roles = RoleIsAvailable
                && environmentId == "PROD-ALPHA-EU"
                    ? [new EnvironmentRole(environmentId, ProductionRoleIds.ReadOnly)]
                    : [];
            return Task.FromResult(ApplicationResult.Succeeded(roles));
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            Match(
                incidentId,
                "INC-1042",
                new Incident(
                    "INC-1042",
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    "Active production incident",
                    IncidentStatus.Active),
                cancellationToken);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken) =>
            Match(
                principalId,
                Owner.RequesterId,
                new AuthenticatedPrincipal(
                    Owner.RequesterId,
                    "Requester",
                    PrincipalKind.Requester),
                cancellationToken);

        public void AddRequest(AccessRequest request) => Requests.Add(request);

        public void AddAuditEvent(AuditEvent auditEvent) =>
            AuditEvents.Add(auditEvent);

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

        private static Task<ApplicationResult<T>> Match<T>(
            string actual,
            string expected,
            T value,
            CancellationToken cancellationToken)
            where T : class =>
            FromOptional(
                string.Equals(actual, expected, StringComparison.Ordinal)
                    ? value
                    : default,
                "authoritative_record_not_found",
                cancellationToken);

        private static Task<ApplicationResult<T>> FromOptional<T>(
            T? value,
            string code,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                value is null
                    ? ApplicationResult.Failed<T>(
                        new ApplicationFailure(
                            ApplicationFailureKind.NotFound,
                            code,
                            "The authoritative record was not found."))
                    : ApplicationResult.Succeeded(value));
        }
    }
}

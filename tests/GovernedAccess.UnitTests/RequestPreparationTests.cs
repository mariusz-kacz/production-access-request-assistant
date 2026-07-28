using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestPreparationTests
{
    private static readonly Guid ConversationRecordId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreparationId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReservedRequestId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConversationConstructionBindsAuthenticatedIdentityAndStartsCollecting()
    {
        var conversation = CreateConversation();

        Assert.Equal(ConversationRecordId, conversation.Id);
        Assert.Equal("msteams", conversation.Channel);
        Assert.Equal("tenant-001", conversation.TenantId);
        Assert.Equal("actor-001", conversation.ChannelActorId);
        Assert.Equal("conversation-001", conversation.ConversationId);
        Assert.Equal("requester", conversation.RequesterId);
        Assert.Equal("Collecting", conversation.Status.ToString());
        Assert.Equal(CreatedAt, conversation.CreatedAt);
        Assert.Equal(CreatedAt, conversation.LastTurnAt);
        Assert.Equal("correlation-create", conversation.CorrelationId);
        Assert.Null(conversation.ActivePreparationId);
        Assert.Equal(1, conversation.PersistenceVersion);
    }

    [Fact]
    public void CollectingConversationCanBecomeReadyForItsPreparedSnapshot()
    {
        var conversation = CreateConversation();
        var readyAt = CreatedAt.AddMinutes(2);

        conversation.MarkReady(
            PreparationId,
            readyAt,
            " correlation-ready ");

        Assert.Equal("Ready", conversation.Status.ToString());
        Assert.Equal(PreparationId, conversation.ActivePreparationId);
        Assert.Equal(readyAt, conversation.LastTurnAt);
        Assert.Equal("correlation-ready", conversation.CorrelationId);
    }

    [Fact]
    public void ConversationCarriesOneBoundedClarificationAndClearsItWhenReady()
    {
        var conversation = CreateConversation();
        var clarification = RoleClarification();

        conversation.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042",
            clarification,
            CreatedAt.AddMinutes(1),
            "correlation-clarify");

        Assert.Same(clarification, conversation.PendingClarification);
        Assert.Collection(
            clarification.Options,
            option => Assert.Equal(ProductionRoleIds.ReadOnly, option.Value),
            option => Assert.Equal(ProductionRoleIds.Support, option.Value));

        conversation.MarkReady(
            PreparationId,
            CreatedAt.AddMinutes(2),
            "correlation-ready");

        Assert.Null(conversation.PendingClarification);
    }

    [Fact]
    public void ClarificationContextRejectsDuplicateOrExcessiveOptions()
    {
        Assert.Throws<ArgumentException>(
            () => new RequestClarificationContext(
                RequestClarificationTarget.EnvironmentId,
                "Which environment?",
                [
                    new RequestClarificationOption("same", "First"),
                    new RequestClarificationOption("same", "Second"),
                ]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RequestClarificationContext(
                RequestClarificationTarget.EnvironmentId,
                "Which environment?",
                Enumerable.Range(1, RequestClarificationContext.MaximumOptions + 1)
                    .Select(index => new RequestClarificationOption(
                        $"environment-{index}",
                        $"Environment {index}"))));
    }

    [Fact]
    public async Task PreparationServiceCanonicalizesClarificationOptions()
    {
        var requestContext = new PreparationRequestContextReader();
        var store = new RecordingRequestIntakeStore();
        var interpreter = new StubPreparationInterpreter(
            new RequestPreparationInterpretationOutcome(
                new RequestPreparationProposal(
                    RequestPreparationProposalKind.Clarification,
                    new RequestCandidate(
                        "client-alpha",
                        "PROD-ALPHA-EU",
                        requestedRoleId: null,
                        "Investigate the active production incident.",
                        "INC-1042"),
                    new RequestClarificationContext(
                        RequestClarificationTarget.RequestedRoleId,
                        "Which role?",
                        [
                            new RequestClarificationOption(
                                ProductionRoleIds.ReadOnly,
                                "Untrusted label one"),
                            new RequestClarificationOption(
                                ProductionRoleIds.Support,
                                "Untrusted label two"),
                        ]))));
        var service = CreateService(interpreter, requestContext, store);

        var outcome = await service.PrepareAsync(
            PrepareCommand(),
            TestContext.Current.CancellationToken);

        var clarification = Assert.IsType<RequestClarificationRequired>(outcome)
            .Clarification;
        Assert.Equal(RequestClarificationTarget.RequestedRoleId, clarification.Target);
        Assert.Collection(
            clarification.Options,
            option =>
            {
                Assert.Equal(ProductionRoleIds.ReadOnly, option.Value);
                Assert.Equal("Production read-only", option.Label);
            },
            option =>
            {
                Assert.Equal(ProductionRoleIds.Support, option.Value);
                Assert.Equal("Production support", option.Label);
            });
        Assert.Same(clarification, store.AddedConversation!.PendingClarification);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task PreparationServiceCreatesReadySnapshotFromValidatedCandidate()
    {
        var requestContext = new PreparationRequestContextReader();
        var store = new RecordingRequestIntakeStore();
        var interpreter = new StubPreparationInterpreter(
            new RequestPreparationInterpretationOutcome(
                new RequestPreparationProposal(
                    RequestPreparationProposalKind.Candidate,
                    new RequestCandidate(
                        " client-alpha ",
                        " PROD-ALPHA-EU ",
                        ProductionRoleIds.ReadOnly,
                        " Investigate the active production incident. ",
                        " INC-1042 "),
                    clarification: null)));
        var service = CreateService(interpreter, requestContext, store);

        var outcome = await service.PrepareAsync(
            PrepareCommand(),
            TestContext.Current.CancellationToken);

        var prepared = Assert.IsType<RequestReadyForConfirmation>(outcome)
            .PreparedRequest;
        Assert.NotEqual(Guid.Empty, prepared.PreparationId);
        Assert.NotEqual(Guid.Empty, prepared.ReservedRequestId);
        Assert.Equal("client-alpha", prepared.ClientId);
        Assert.Equal("PROD-ALPHA-EU", prepared.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, prepared.RequestedRoleId);
        Assert.Equal("INC-1042", prepared.IncidentId);
        Assert.Same(prepared, store.AddedPreparedRequest);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task PreparationServiceRejectsInvalidCandidateWithoutInventingAQuestion()
    {
        var requestContext = new PreparationRequestContextReader();
        var store = new RecordingRequestIntakeStore();
        var interpreter = new StubPreparationInterpreter(
            new RequestPreparationInterpretationOutcome(
                new RequestPreparationProposal(
                    RequestPreparationProposalKind.Candidate,
                    new RequestCandidate(
                        "client-alpha",
                        "PROD-UNKNOWN",
                        ProductionRoleIds.ReadOnly,
                        "Investigate the active production incident.",
                        incidentId: null),
                    clarification: null)));
        var service = CreateService(interpreter, requestContext, store);

        var outcome = await service.PrepareAsync(
            PrepareCommand(),
            TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<RequestCandidateRejected>(outcome);
        Assert.Contains(
            rejected.ValidationErrors,
            error => error.Code == "environment_not_found");
        Assert.Null(store.AddedConversation!.PendingClarification);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task PreparationServiceRejectsNonAuthoritativeClarificationOptions()
    {
        var requestContext = new PreparationRequestContextReader();
        var store = new RecordingRequestIntakeStore();
        var interpreter = new StubPreparationInterpreter(
            new RequestPreparationInterpretationOutcome(
                new RequestPreparationProposal(
                    RequestPreparationProposalKind.Clarification,
                    new RequestCandidate(
                        "client-alpha",
                        "PROD-ALPHA-EU",
                        requestedRoleId: null,
                        "Investigate the active production incident.",
                        incidentId: null),
                    new RequestClarificationContext(
                        RequestClarificationTarget.RequestedRoleId,
                        "Which role?",
                        [
                            new RequestClarificationOption(
                                "ProductionAdministrator",
                                "Administrator"),
                        ]))));
        var service = CreateService(interpreter, requestContext, store);

        var outcome = await service.PrepareAsync(
            PrepareCommand(),
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<RequestPreparationFailed>(outcome).Failure;
        Assert.Equal(RequestPreparationService.MalformedModelOutputCode, failure.Code);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void PreparedSnapshotConstructionCapturesCanonicalScopeAndFixedExpiry()
    {
        var preparedRequest = CreatePreparedRequest();

        Assert.Equal(PreparationId, preparedRequest.PreparationId);
        Assert.Equal(ConversationRecordId, preparedRequest.ConversationRecordId);
        Assert.Equal(ReservedRequestId, preparedRequest.ReservedRequestId);
        Assert.Equal("msteams", preparedRequest.Channel);
        Assert.Equal("tenant-001", preparedRequest.TenantId);
        Assert.Equal("actor-001", preparedRequest.ChannelActorId);
        Assert.Equal("conversation-001", preparedRequest.ConversationId);
        Assert.Equal("requester", preparedRequest.RequesterId);
        Assert.Equal("client-alpha", preparedRequest.ClientId);
        Assert.Equal("PROD-ALPHA-EU", preparedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, preparedRequest.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            preparedRequest.Justification);
        Assert.Equal("INC-1042", preparedRequest.IncidentId);
        Assert.Equal("Ready", preparedRequest.Status.ToString());
        Assert.Equal(CreatedAt, preparedRequest.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(30), preparedRequest.ExpiresAt);
        Assert.Null(preparedRequest.SubmittedAt);
        Assert.Null(preparedRequest.SubmittedRequestId);
        Assert.Equal("correlation-prepare", preparedRequest.CorrelationId);
        Assert.Equal(1, preparedRequest.PersistenceVersion);
    }

    [Fact]
    public void ReadySnapshotSubmissionUsesItsReservedRequestIdentity()
    {
        var preparedRequest = CreatePreparedRequest();
        var submittedAt = CreatedAt.AddMinutes(5);

        preparedRequest.MarkSubmitted(submittedAt);

        Assert.Equal("Submitted", preparedRequest.Status.ToString());
        Assert.Equal(ReservedRequestId, preparedRequest.ReservedRequestId);
        Assert.Equal(ReservedRequestId, preparedRequest.SubmittedRequestId);
        Assert.Equal(submittedAt, preparedRequest.SubmittedAt);
        Assert.Equal(CreatedAt.AddMinutes(30), preparedRequest.ExpiresAt);
    }

    private static RequestPreparationConversation CreateConversation() =>
        new(
            ConversationRecordId,
            " msteams ",
            " tenant-001 ",
            " actor-001 ",
            " conversation-001 ",
            " requester ",
            CreatedAt,
            " correlation-create ");

    private static RequestPreparationService CreateService(
        IRequestPreparationInterpreter interpreter,
        IRequestContextReader requestContext,
        IRequestIntakeStore store) =>
        new(
            interpreter,
            new RequestValidator(requestContext),
            requestContext,
            store,
            new StubClock(CreatedAt));

    private static PrepareAccessRequestCommand PrepareCommand() =>
        new(
            new AuthenticatedChannelActor(
                "msteams",
                "tenant-001",
                "actor-001",
                "conversation-001",
                "requester"),
            "I need production access.",
            "correlation-prepare");

    private static RequestClarificationContext RoleClarification() =>
        new(
            RequestClarificationTarget.RequestedRoleId,
            "Which production role should be requested?",
            [
                new RequestClarificationOption(
                    ProductionRoleIds.ReadOnly,
                    "Production read-only"),
                new RequestClarificationOption(
                    ProductionRoleIds.Support,
                    "Production support"),
            ]);

    private static PreparedAccessRequest CreatePreparedRequest() =>
        new(
            PreparationId,
            ConversationRecordId,
            ReservedRequestId,
            " msteams ",
            " tenant-001 ",
            " actor-001 ",
            " conversation-001 ",
            " requester ",
            " client-alpha ",
            " PROD-ALPHA-EU ",
            $" {ProductionRoleIds.ReadOnly} ",
            "  Investigate the active production incident.  ",
            " INC-1042 ",
            CreatedAt,
            " correlation-prepare ");

    private sealed class StubClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class StubPreparationInterpreter(
        RequestPreparationInterpretationOutcome outcome)
        : IRequestPreparationInterpreter
    {
        public Task<RequestPreparationInterpretationOutcome> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(outcome);
        }
    }

    private sealed class PreparationRequestContextReader : IRequestContextReader
    {
        private readonly Client client = new("client-alpha", "Client Alpha");
        private readonly ProductionEnvironment environment = new(
            "PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha Production EU",
            "alpha-approver");
        private readonly Incident incident = new(
            "INC-1042",
            "client-alpha",
            "PROD-ALPHA-EU",
            "Active Alpha incident",
            IncidentStatus.Active);

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            GetAsync(
                clientId,
                client.Id,
                client,
                "client_not_found",
                cancellationToken);

        public Task<ApplicationResult<ProductionEnvironment>>
            GetProductionEnvironmentAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            GetAsync(
                environmentId,
                environment.Id,
                environment,
                "environment_not_found",
                cancellationToken);

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = string.Equals(
                    environmentId,
                    environment.Id,
                    StringComparison.Ordinal)
                && ProductionRoleIds.IsSupported(roleId)
                ? ApplicationResult.Succeeded(
                    new EnvironmentRole(environmentId, roleId))
                : NotFound<EnvironmentRole>("role_not_found");
            return Task.FromResult(result);
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>>
            GetEnvironmentRolesAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRole> roles =
            [
                new(environmentId, ProductionRoleIds.ReadOnly),
                new(environmentId, ProductionRoleIds.Support),
            ];
            return Task.FromResult(ApplicationResult.Succeeded(roles));
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            GetAsync(
                incidentId,
                incident.Id,
                incident,
                "incident_not_found",
                cancellationToken);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                NotFound<AuthenticatedPrincipal>("principal_not_found"));
        }

        private static Task<ApplicationResult<T>> GetAsync<T>(
            string actualId,
            string expectedId,
            T value,
            string notFoundCode,
            CancellationToken cancellationToken)
            where T : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = string.Equals(
                actualId,
                expectedId,
                StringComparison.Ordinal)
                ? ApplicationResult.Succeeded(value)
                : NotFound<T>(notFoundCode);
            return Task.FromResult(result);
        }

        private static ApplicationResult<T> NotFound<T>(string code)
            where T : notnull =>
            ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    code,
                    "The stored record was not found."));
    }

    private sealed class RecordingRequestIntakeStore : IRequestIntakeStore
    {
        public RequestPreparationConversation? AddedConversation { get; private set; }

        public PreparedAccessRequest? AddedPreparedRequest { get; private set; }

        public int SaveCount { get; private set; }

        public void AddConversation(RequestPreparationConversation conversation)
        {
            AddedConversation = conversation;
        }

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetActiveConversationAsync(
                AuthenticatedChannelActor actor,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                NotFound<RequestPreparationConversation>(
                    "active_conversation_not_found"));
        }

        public Task<ApplicationResult<RequestPreparationConversation>>
            GetConversationAsync(
                Guid conversationRecordId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                NotFound<RequestPreparationConversation>(
                    "conversation_not_found"));
        }

        public void AddPreparedRequest(PreparedAccessRequest preparedRequest)
        {
            AddedPreparedRequest = preparedRequest;
        }

        public Task<ApplicationResult<PreparedAccessRequest>>
            GetPreparedRequestAsync(
                Guid preparationId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                NotFound<PreparedAccessRequest>("prepared_request_not_found"));
        }

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

        private static ApplicationResult<T> NotFound<T>(string code)
            where T : notnull =>
            ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    code,
                    "The stored record was not found."));
    }
}

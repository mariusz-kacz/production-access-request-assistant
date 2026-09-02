using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class PreparationConfirmationServiceTests :
    RequestPreparationReducerTestBase
{
    [Fact]
    public async Task ValidConfirmationCreatesOnePreparationKeyedRequestAndAudit()
    {
        var ready = ReadyPreparation(justification: "x", incidentId: "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        var principals = ValidPrincipalReader();
        var service = Service(
            store,
            authority,
            CreatedAt.AddMinutes(5),
            principals);

        var result = await service.ConfirmAsync(
            Command(ready.PreparationId, "confirm-success"),
            TestContext.Current.CancellationToken);

        var submitted = Assert.IsType<PreparationConfirmationSubmitted>(result);
        Assert.False(submitted.WasAlreadySubmitted);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, submitted.Request.Status);
        Assert.Equal(ready.PreparationId, submitted.Request.PreparationId);
        Assert.Equal("x", submitted.Request.Details.Justification);
        Assert.Equal(PreparationLifecycle.Submitted, ready.Lifecycle);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(submitted.Request, Assert.Single(store.Requests));
        Assert.Equal(["PROD-ALPHA-EU"], authority.EnvironmentGetCalls);
        Assert.Equal(1, authority.RoleGetCallCount);
        Assert.Equal(1, authority.IncidentGetCallCount);
        Assert.Equal(
            ["requester", "client-alpha-approver"],
            principals.GetCalls);

        var audit = Assert.Single(store.AuditEvents);
        Assert.Equal(AuditEventType.RequestCreated, audit.EventType);
        using var details = JsonDocument.Parse(audit.DetailsJson);
        Assert.Equal(
            ready.PreparationId,
            details.RootElement.GetProperty("preparationId").GetGuid());
        Assert.Single(details.RootElement.GetProperty("materialChanges").EnumerateArray());
        Assert.DoesNotContain(
            "x",
            audit.DetailsJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAndForeignPreparationsAreIndistinguishable()
    {
        var authority = ValidAuthority();
        var missingStore = new InMemoryConfirmationStore();
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var foreignStore = new InMemoryConfirmationStore(ready);
        var foreignBinding = new PreparationBinding(
            PreparationBinding.TeamsChannel,
            "tenant",
            "foreign-actor",
            "conversation",
            "requester");

        var missing = await Service(
            missingStore,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(Guid.NewGuid(), "missing"),
                TestContext.Current.CancellationToken);
        var foreign = await Service(
            foreignStore,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                new PreparationConfirmationCommand(
                    foreignBinding,
                    ready.PreparationId,
                    "foreign"),
                TestContext.Current.CancellationToken);

        var missingFailure = Assert.IsType<PreparationConfirmationFailed>(missing);
        var foreignFailure = Assert.IsType<PreparationConfirmationFailed>(foreign);
        Assert.Equal(missingFailure.Failure.Kind, foreignFailure.Failure.Kind);
        Assert.Equal(missingFailure.Failure.Code, foreignFailure.Failure.Code);
        Assert.Equal(missingFailure.Failure.Message, foreignFailure.Failure.Message);
        Assert.Empty(missingStore.Requests);
        Assert.Empty(foreignStore.Requests);
        Assert.Empty(authority.EnvironmentGetCalls);
    }

    [Fact]
    public async Task DeadlineIsAppliedLazilyWithoutAuthorityReads()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();

        var result = await Service(
            store,
            authority,
            CreatedAt.Add(RequestPreparation.ReadyLifetime)).ConfirmAsync(
                Command(ready.PreparationId, "expired"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationFailed>(result);
        Assert.Equal("request-preparation-expired", failed.Failure.Code);
        Assert.Equal(PreparationLifecycle.Expired, ready.Lifecycle);
        Assert.Equal(1, store.SaveCount);
        Assert.Empty(store.Requests);
        Assert.Empty(authority.EnvironmentGetCalls);
    }

    [Fact]
    public async Task AuthorityOutagePreservesReadyScopeAndDeadline()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var originalCandidate = ready.Candidate;
        var originalDeadline = ready.ReadyDeadline;
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        authority.EnvironmentFailure = Failure(
            ApplicationFailureKind.DependencyUnavailable,
            "environment-authority-unavailable");

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "source-outage"),
                TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<PreparationConfirmationSourceUnavailable>(result);
        Assert.Equal("environment-authority-unavailable", unavailable.Failure.Code);
        Assert.Equal(PreparationLifecycle.Ready, ready.Lifecycle);
        Assert.Same(originalCandidate, ready.Candidate);
        Assert.Equal(originalDeadline, ready.ReadyDeadline);
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task EnvironmentDriftCreatesCollectingCorrectedSuccessor()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        authority.Environments["PROD-ALPHA-EU"] =
            Environment("PROD-ALPHA-EU", "client-alpha", eligible: false);

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "environment-drift"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationRevalidationFailed>(result);
        var successor = Assert.IsType<PreparationSnapshot>(
            failed.Revalidation.Preparation);
        var outcome = Assert.IsType<ConfirmationRevalidationFailed>(
            failed.Revalidation.Response.Outcome);
        Assert.Equal(successor.PreparationId, outcome.SuccessorPreparationId);
        Assert.Equal(RevalidatedPreparationStatus.Collecting, outcome.SuccessorStatus);
        Assert.Equal(ready.PreparationId, successor.PredecessorPreparationId);
        Assert.Equal(PreparationLifecycle.Superseded, ready.Lifecycle);
        Assert.Equal(PreparationLifecycle.Collecting, successor.Lifecycle);
        Assert.Null(successor.Candidate.EnvironmentId);
        Assert.Null(successor.Candidate.ClientId);
        Assert.Null(successor.Candidate.RoleId);
        Assert.Null(successor.Candidate.IncidentId);
        Assert.Equal("Investigate errors", successor.Candidate.Justification);
        Assert.Equal(2, store.Preparations.Count);
        Assert.Equal(1, store.SaveCount);
        Assert.Empty(store.Requests);
        Assert.Empty(store.AuditEvents);
        Assert.Equal(
            ready.MaterialChangeAttributions,
            store.Preparations[1].MaterialChangeAttributions);
    }

    [Fact]
    public async Task ClientDriftCreatesReadySuccessorAfterDependentRevalidation()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        authority.Environments["PROD-ALPHA-EU"] =
            Environment("PROD-ALPHA-EU", "client-beta");

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "client-drift"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationRevalidationFailed>(result);
        var successor = failed.Revalidation.Preparation!;
        Assert.Equal(PreparationLifecycle.Ready, successor.Lifecycle);
        Assert.Equal("client-beta", successor.Candidate.ClientId);
        Assert.Equal("ProductionReadOnly", successor.Candidate.RoleId);
        Assert.Equal("INC-1042", successor.Candidate.IncidentId);
        Assert.Equal(1, authority.RoleGetCallCount);
        Assert.Equal(1, authority.IncidentGetCallCount);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task RoleDriftClearsOnlyRoleInCollectingSuccessor()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        authority.Roles[("PROD-ALPHA-EU", "ProductionReadOnly")] =
            Role("PROD-ALPHA-EU", "ProductionReadOnly", assignable: false);

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "role-drift"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationRevalidationFailed>(result);
        var successor = failed.Revalidation.Preparation!;
        Assert.Equal(PreparationLifecycle.Collecting, successor.Lifecycle);
        Assert.Equal("PROD-ALPHA-EU", successor.Candidate.EnvironmentId);
        Assert.Equal("client-alpha", successor.Candidate.ClientId);
        Assert.Null(successor.Candidate.RoleId);
        Assert.Equal("INC-1042", successor.Candidate.IncidentId);
        Assert.Equal("Investigate errors", successor.Candidate.Justification);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task IncidentDriftClearsOnlyIncidentInReadySuccessor()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        authority.Incidents["INC-1042"] = new IncidentAuthorityProjection(
            "INC-1042",
            "Incident title",
            isActive: false,
            "PROD-ALPHA-EU");

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "incident-drift"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationRevalidationFailed>(result);
        var successor = failed.Revalidation.Preparation!;
        Assert.Equal(PreparationLifecycle.Ready, successor.Lifecycle);
        Assert.Equal("PROD-ALPHA-EU", successor.Candidate.EnvironmentId);
        Assert.Equal("ProductionReadOnly", successor.Candidate.RoleId);
        Assert.Null(successor.Candidate.IncidentId);
        Assert.Empty(store.Requests);
    }

    [Theory]
    [InlineData(AuthorityFailurePoint.RoleGet)]
    [InlineData(AuthorityFailurePoint.Incident)]
    public async Task DependentSourceOutagePreservesOriginalReadyPreparation(
        AuthorityFailurePoint failurePoint)
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var candidate = ready.Candidate;
        var deadline = ready.ReadyDeadline;
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        var failure = Failure(
            ApplicationFailureKind.DependencyUnavailable,
            "confirmation-source-unavailable");
        if (failurePoint == AuthorityFailurePoint.RoleGet)
        {
            authority.RoleFailure = failure;
        }
        else
        {
            authority.IncidentFailure = failure;
        }

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5)).ConfirmAsync(
                Command(ready.PreparationId, "dependent-outage"),
                TestContext.Current.CancellationToken);

        Assert.IsType<PreparationConfirmationSourceUnavailable>(result);
        Assert.Equal(PreparationLifecycle.Ready, ready.Lifecycle);
        Assert.Same(candidate, ready.Candidate);
        Assert.Equal(deadline, ready.ReadyDeadline);
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task SequentialReplayReturnsExistingIdentityAndCurrentStatus()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        var service = Service(store, authority, CreatedAt.AddMinutes(5));

        var first = Assert.IsType<PreparationConfirmationSubmitted>(
            await service.ConfirmAsync(
                Command(ready.PreparationId, "first"),
                TestContext.Current.CancellationToken));
        first.Request.Status = RequestStatus.AwaitingDevOpsApproval;
        var replay = Assert.IsType<PreparationConfirmationSubmitted>(
            await service.ConfirmAsync(
                Command(ready.PreparationId, "replay"),
                TestContext.Current.CancellationToken));

        Assert.True(replay.WasAlreadySubmitted);
        Assert.Equal(first.Request.Id, replay.Request.Id);
        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, replay.Request.Status);
        Assert.Single(store.Requests);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task MissingRequesterSnapshotFailsBeforeReferenceReads()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        var principals = new FakePrincipalReader();

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5),
            principals).ConfirmAsync(
                Command(ready.PreparationId, "missing-requester"),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<PreparationConfirmationFailed>(result);
        Assert.Equal(
            "authenticated-requester-snapshot-missing",
            failed.Failure.Code);
        Assert.Empty(authority.EnvironmentGetCalls);
        Assert.Equal(PreparationLifecycle.Ready, ready.Lifecycle);
        Assert.Empty(store.Requests);
    }

    [Fact]
    public async Task InvalidBusinessApproverSnapshotPreservesReadyPreparation()
    {
        var ready = ReadyPreparation("Investigate errors", "INC-1042");
        var store = new InMemoryConfirmationStore(ready);
        var authority = ValidAuthority();
        var principals = ValidPrincipalReader();
        principals.Principals["client-alpha-approver"] =
            new AuthenticatedPrincipal(
                "client-alpha-approver",
                "Wrong Client Approver",
                PrincipalKind.BusinessApprover,
                "client-beta");

        var result = await Service(
            store,
            authority,
            CreatedAt.AddMinutes(5),
            principals).ConfirmAsync(
                Command(ready.PreparationId, "invalid-approver"),
                TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<PreparationConfirmationSourceUnavailable>(
            result);
        Assert.Equal("business-approver-snapshot-invalid", unavailable.Failure.Code);
        Assert.Equal(PreparationLifecycle.Ready, ready.Lifecycle);
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Requests);
    }

    private static PreparationConfirmationService Service(
        IRequestPreparationConfirmationStore store,
        FakePreparationAuthority authority,
        DateTimeOffset observedAt,
        FakePrincipalReader? principalReader = null) =>
        new(
            store,
            authority,
            authority,
            authority,
            principalReader ?? ValidPrincipalReader(),
            new FakeClock(observedAt));

    private static PreparationConfirmationCommand Command(
        Guid preparationId,
        string correlationId) =>
        new(Binding(), preparationId, correlationId);

    private static RequestPreparation ReadyPreparation(
        string justification,
        string? incidentId) =>
        RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                justification,
                incidentId),
            clarification: null,
            Attribution(
                [
                    ProposalField.Environment,
                    ProposalField.Role,
                    ProposalField.Justification,
                    ProposalField.Incident,
                ]),
            CreatedAt,
            "ready");

    private static FakePreparationAuthority ValidAuthority()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-ALPHA-EU"] =
            Environment("PROD-ALPHA-EU", "client-alpha");
        authority.Roles[("PROD-ALPHA-EU", "ProductionReadOnly")] =
            Role("PROD-ALPHA-EU", "ProductionReadOnly");
        authority.Incidents["INC-1042"] =
            Incident("INC-1042", "PROD-ALPHA-EU");
        return authority;
    }

    private static FakePrincipalReader ValidPrincipalReader() =>
        new(
            new AuthenticatedPrincipal(
                "requester",
                "Requester",
                PrincipalKind.Requester),
            new AuthenticatedPrincipal(
                "client-alpha-approver",
                "Alpha Approver",
                PrincipalKind.BusinessApprover,
                "client-alpha"),
            new AuthenticatedPrincipal(
                "client-beta-approver",
                "Beta Approver",
                PrincipalKind.BusinessApprover,
                "client-beta"));

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakePrincipalReader(
        params AuthenticatedPrincipal[] principals) :
        IAuthenticatedPrincipalReader
    {
        private readonly Dictionary<string, AuthenticatedPrincipal> principals =
            principals.ToDictionary(principal => principal.Id, StringComparer.Ordinal);

        internal Dictionary<string, AuthenticatedPrincipal> Principals => principals;

        internal List<string> GetCalls { get; } = [];

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            GetCalls.Add(principalId);
            return Task.FromResult(
                principals.TryGetValue(principalId, out var principal)
                    ? ApplicationResult.Succeeded(principal)
                    : ApplicationResult.Failed<AuthenticatedPrincipal>(
                        new ApplicationFailure(
                            ApplicationFailureKind.NotFound,
                            "principal-not-found",
                            "The principal was not found.")));
        }
    }

    private sealed class InMemoryConfirmationStore(
        params RequestPreparation[] preparations) :
        IRequestPreparationConfirmationStore
    {
        private readonly List<RequestPreparation> preparations = [.. preparations];

        internal List<RequestPreparation> Preparations => preparations;

        internal List<AccessRequest> Requests { get; } = [];

        internal List<AuditEvent> AuditEvents { get; } = [];

        internal int SaveCount { get; private set; }

        public void Add(RequestPreparation preparation) =>
            preparations.Add(preparation);

        public void AddRequest(AccessRequest request) => Requests.Add(request);

        public void AddAuditEvent(AuditEvent auditEvent) =>
            AuditEvents.Add(auditEvent);

        public Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) =>
            Task.FromResult(Find(preparations.SingleOrDefault(preparation =>
                preparation.Binding == binding
                && preparation.Lifecycle is PreparationLifecycle.Collecting
                    or PreparationLifecycle.Ready)));

        public Task<ApplicationResult<RequestPreparation>> GetLatestAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) =>
            Task.FromResult(Find(preparations.LastOrDefault(preparation =>
                preparation.Binding == binding)));

        public Task<ApplicationResult<RequestPreparation>> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Find(preparations.SingleOrDefault(preparation =>
                preparation.PreparationId == preparationId)));

        public Task<ApplicationResult<RequestPreparation>> ReloadAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            GetAsync(preparationId, cancellationToken);

        public Task<ApplicationResult<AccessRequest>> GetRequestByPreparationIdAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Requests.SingleOrDefault(request =>
                    request.PreparationId == preparationId) is { } request
                    ? ApplicationResult.Succeeded(request)
                    : ApplicationResult.Failed<AccessRequest>(NotFoundFailure()));

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(ApplicationResult.Succeeded());
        }

        private static ApplicationResult<RequestPreparation> Find(
            RequestPreparation? preparation) =>
            preparation is null
                ? ApplicationResult.Failed<RequestPreparation>(NotFoundFailure())
                : ApplicationResult.Succeeded(preparation);

        private static ApplicationFailure NotFoundFailure() =>
            new(
                ApplicationFailureKind.NotFound,
                "request-preparation-not-found",
                "The request preparation was not found.");
    }
}

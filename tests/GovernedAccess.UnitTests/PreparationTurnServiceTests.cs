using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class PreparationTurnServiceTests : RequestPreparationReducerTestBase
{
    private static readonly PreparationTurnAttribution TurnAttribution = new(
        "model-deployment",
        "provider-version",
        "prompt-v1",
        "schema-v1");

    [Fact]
    public void ResetProtocolEventContainsOnlyBindingAndSafeCorrelationMetadata()
    {
        Assert.Equal(
            [nameof(ResetPreparationCommand.Binding), nameof(ResetPreparationCommand.CorrelationId)],
            typeof(ResetPreparationCommand)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            typeof(ResetPreparationCommand).GetProperties(),
            property => property.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreTurnServiceApiAcceptsNoRequesterTextOrMessage()
    {
        var parameterNames = typeof(PreparationTurnService)
            .GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name!)
            .ToArray();

        Assert.DoesNotContain(
            parameterNames,
            name => name.Contains("text", StringComparison.OrdinalIgnoreCase)
                || name.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [
                typeof(PreparationTurnContext),
                typeof(TurnProposal),
                typeof(PreparationTurnAttribution),
                typeof(CancellationToken),
            ],
            typeof(PreparationTurnService)
                .GetMethod(nameof(PreparationTurnService.ApplyAsync))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task FirstAcceptedCompleteTurnCreatesOneReadyPreparation()
    {
        var authority = CompleteAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] =
            [Role("PROD-ALPHA-EU", "ProductionReadOnly")];
        var store = new InMemoryPreparationStore();
        var clock = new FakeClock(CreatedAt);
        var service = Service(store, authority, clock);
        var started = await service.BeginAsync(
            Binding(),
            "turn-1",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            CompleteUpdate(),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var preparation = Assert.IsType<PreparationSnapshot>(result.Preparation);
        var outcome = Assert.IsType<ReadyForConfirmation>(result.Response.Outcome);
        Assert.Equal(preparation.PreparationId, outcome.PreparationId);
        Assert.Equal(PreparationLifecycle.Ready, preparation.Lifecycle);
        Assert.Equal(
            "ProductionReadOnly",
            result.Response.SoleRoleSelection?.RoleId);
        Assert.Single(store.Preparations);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task FirstNonMutatingTurnCreatesNoPreparation()
    {
        var store = new InMemoryPreparationStore();
        var service = Service(store, new FakePreparationAuthority(), new FakeClock(CreatedAt));
        var started = await service.BeginAsync(
            Binding(),
            "turn-1",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            new TurnProposal(TurnProposal.CurrentSchemaVersion, DialogueAct.Unclear),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        Assert.Null(result.Preparation);
        Assert.IsType<UnclearGuidance>(result.Response.Outcome);
        Assert.Empty(store.Preparations);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ValueEqualReadyTurnPreservesIdentityAndDeadline()
    {
        var authority = CompleteAuthority();
        var ready = ReadyPreparation();
        var store = new InMemoryPreparationStore(ready);
        var clock = new FakeClock(CreatedAt.AddMinutes(5));
        var service = Service(store, authority, clock);
        var started = await service.BeginAsync(
            Binding(),
            "turn-2",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            Update(justification: Justification(ready.Candidate.Justification!)),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var preparation = Assert.IsType<PreparationSnapshot>(result.Preparation);
        Assert.Equal(ready.PreparationId, preparation.PreparationId);
        Assert.Equal(PreparationLifecycle.Ready, preparation.Lifecycle);
        Assert.Equal(CreatedAt.Add(RequestPreparation.ReadyLifetime), preparation.ReadyDeadline);
        Assert.IsType<DraftUnchanged>(result.Response.Outcome);
        Assert.Single(store.Preparations);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ClearAllReadyTurnFailsClosedAndPreservesReadyPreparation()
    {
        var ready = ReadyPreparation();
        var store = new InMemoryPreparationStore(ready);
        var service = Service(
            store,
            CompleteAuthority(),
            new FakeClock(CreatedAt.AddMinutes(5)));
        var started = await service.BeginAsync(
            Binding(),
            "turn-clear-all",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            Update(
                environment: new ClearEnvironmentOperation(),
                justification: new ClearJustificationOperation()),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<Failed>(result.Response.Outcome);
        Assert.Equal(
            "request-preparation-ready-clear-all-not-allowed",
            failure.Failure.Code);
        Assert.Equal(ready.PreparationId, result.Preparation!.PreparationId);
        Assert.Equal(PreparationLifecycle.Ready, result.Preparation.Lifecycle);
        Assert.Equal(ready.Candidate, result.Preparation.Candidate);
        Assert.Equal(
            CreatedAt.Add(RequestPreparation.ReadyLifetime),
            result.Preparation.ReadyDeadline);
        Assert.Single(store.Preparations);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task AcceptedReadyRevisionAtomicallyCreatesMandatoryPredecessorSuccessor()
    {
        var authority = CompleteAuthority();
        authority.Roles[("PROD-ALPHA-EU", "ProductionSupport")] =
            Role("PROD-ALPHA-EU", "ProductionSupport");
        var ready = ReadyPreparation();
        var store = new InMemoryPreparationStore(ready);
        var service = Service(
            store,
            authority,
            new FakeClock(CreatedAt.AddMinutes(5)));
        var started = await service.BeginAsync(
            Binding(),
            "turn-2",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            Update(role: new SetRoleOperation("ProductionSupport")),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var successor = Assert.IsType<PreparationSnapshot>(result.Preparation);
        Assert.Equal(PreparationLifecycle.Superseded, ready.Lifecycle);
        Assert.Equal(ready.PreparationId, successor.PredecessorPreparationId);
        Assert.NotEqual(ready.PreparationId, successor.PreparationId);
        Assert.Equal(PreparationLifecycle.Ready, successor.Lifecycle);
        Assert.Equal("ProductionSupport", successor.Candidate.RoleId);
        Assert.IsType<ReadyForConfirmation>(result.Response.Outcome);
        Assert.Equal(2, store.Preparations.Count);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task ReadyClarificationRevisionCopiesCandidateIntoCollectingSuccessor()
    {
        var authority = CompleteAuthority();
        authority.SearchResult = SearchResult(2);
        var ready = ReadyPreparation();
        var originalCandidate = ready.Candidate;
        var store = new InMemoryPreparationStore(ready);
        var service = Service(
            store,
            authority,
            new FakeClock(CreatedAt.AddMinutes(5)));
        var started = await service.BeginAsync(
            Binding(),
            "turn-2",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            Update(environment: new SetEnvironmentOperation(
                new EnvironmentSearchQuery("production"))),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var successor = Assert.IsType<PreparationSnapshot>(result.Preparation);
        Assert.Equal(PreparationLifecycle.Superseded, ready.Lifecycle);
        Assert.Equal(ready.PreparationId, successor.PredecessorPreparationId);
        Assert.Equal(PreparationLifecycle.Collecting, successor.Lifecycle);
        Assert.Equal(originalCandidate, successor.Candidate);
        Assert.NotNull(successor.Clarification);
        Assert.IsType<ClarificationRequired>(result.Response.Outcome);
    }

    [Fact]
    public async Task BeginLazilyExpiresReadyPreparationAtItsDeadline()
    {
        var ready = ReadyPreparation();
        var store = new InMemoryPreparationStore(ready);
        var service = Service(
            store,
            CompleteAuthority(),
            new FakeClock(CreatedAt.Add(RequestPreparation.ReadyLifetime)));

        var started = await service.BeginAsync(
            Binding(),
            "turn-expired",
            TestContext.Current.CancellationToken);

        Assert.True(started.IsSuccess);
        Assert.Equal(PreparationLifecycle.Expired, started.Value.Preparation!.Lifecycle);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task ResetSupersedesActivePreparationAndCreatesOneCleanReplacement()
    {
        var active = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");
        var store = new InMemoryPreparationStore(active);
        var service = Service(
            store,
            new FakePreparationAuthority(),
            new FakeClock(CreatedAt.AddMinutes(1)));

        var result = await service.ResetAsync(
            new ResetPreparationCommand(Binding(), "reset"),
            TestContext.Current.CancellationToken);

        var replacement = Assert.IsType<PreparationSnapshot>(result.Preparation);
        Assert.IsType<ResetGuidance>(result.Response.Outcome);
        Assert.Equal(PreparationLifecycle.Superseded, active.Lifecycle);
        Assert.Equal(PreparationLifecycle.Collecting, replacement.Lifecycle);
        Assert.True(replacement.Candidate.IsEmpty);
        Assert.Null(replacement.PredecessorPreparationId);
        Assert.Equal(2, store.Preparations.Count);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task OldCollectingPreparationHasNoAgeSpecificResponseOrWrite()
    {
        var collecting = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");
        var observedAt = CreatedAt.AddDays(8);
        var store = new InMemoryPreparationStore(collecting);
        var service = Service(
            store,
            new FakePreparationAuthority(),
            new FakeClock(observedAt));
        var started = await service.BeginAsync(
            Binding(),
            "old-collecting",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            new TurnProposal(TurnProposal.CurrentSchemaVersion, DialogueAct.Unclear),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        Assert.IsType<UnclearGuidance>(result.Response.Outcome);
        Assert.Equal(CreatedAt, result.Preparation!.UpdatedAt);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task AgentFailurePreservesReadyIdentityCandidateAndDeadlineWithoutWrite()
    {
        var ready = ReadyPreparation();
        var store = new InMemoryPreparationStore(ready);
        var service = Service(
            store,
            CompleteAuthority(),
            new FakeClock(CreatedAt.AddMinutes(1)));
        var started = await service.BeginAsync(
            Binding(),
            "failure",
            TestContext.Current.CancellationToken);

        var result = PreparationTurnService.Reject(
            started.Value,
            new ApplicationFailure(
                ApplicationFailureKind.Timeout,
                "agent-timeout",
                "The agent timed out."));

        var failure = Assert.IsType<Failed>(result.Response.Outcome);
        Assert.Equal("agent-timeout", failure.Failure.Code);
        Assert.Equal(ready.PreparationId, result.Preparation!.PreparationId);
        Assert.Equal(ready.Candidate, result.Preparation.Candidate);
        Assert.Equal(
            CreatedAt.Add(RequestPreparation.ReadyLifetime),
            result.Preparation.ReadyDeadline);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ResetDoesNotClaimSuccessWhenOrdinaryTurnWinsCreationRace()
    {
        var ordinaryWinner = ReadyPreparation();
        var store = new InMemoryPreparationStore
        {
            ActiveWinner = ordinaryWinner,
            SaveFailure = new ApplicationFailure(
                ApplicationFailureKind.ConcurrencyConflict,
                "request_preparation_active_race",
                "Another active preparation won."),
        };
        var service = Service(
            store,
            new FakePreparationAuthority(),
            new FakeClock(CreatedAt.AddMinutes(1)));

        var result = await service.ResetAsync(
            new ResetPreparationCommand(Binding(), "reset"),
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<Failed>(result.Response.Outcome);
        Assert.Equal("request_preparation_active_race", failure.Failure.Code);
        Assert.Null(result.Preparation);
    }

    [Fact]
    public async Task FailedResetReportsPreCommitSnapshotRatherThanLosingMutation()
    {
        var previous = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");
        var store = new InMemoryPreparationStore(previous)
        {
            SaveFailure = new ApplicationFailure(
                ApplicationFailureKind.ConcurrencyConflict,
                "request_preparation_concurrency_conflict",
                "The preparation changed."),
        };
        var service = Service(
            store,
            new FakePreparationAuthority(),
            new FakeClock(CreatedAt.AddMinutes(1)));

        var result = await service.ResetAsync(
            new ResetPreparationCommand(Binding(), "reset"),
            TestContext.Current.CancellationToken);

        Assert.IsType<Failed>(result.Response.Outcome);
        Assert.Equal(previous.PreparationId, result.Preparation!.PreparationId);
        Assert.Equal(PreparationLifecycle.Collecting, result.Preparation.Lifecycle);
        Assert.True(result.Preparation.Candidate.IsEmpty);
    }

    private static PreparationTurnService Service(
        IRequestPreparationStore store,
        FakePreparationAuthority authority,
        IClock clock) =>
        new(store, Reducer(authority), clock);

    private static FakePreparationAuthority CompleteAuthority()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-ALPHA-EU"] =
            Environment("PROD-ALPHA-EU", "client-alpha");
        authority.Roles[("PROD-ALPHA-EU", "ProductionReadOnly")] =
            Role("PROD-ALPHA-EU", "ProductionReadOnly");
        return authority;
    }

    private static TurnProposal CompleteUpdate() =>
        Update(
            environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-ALPHA-EU")),
            role: new SetRoleOperation("ProductionReadOnly"),
            justification: Justification("Investigate the active incident"));

    private static RequestPreparation ReadyPreparation() =>
        RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                "Investigate the active incident",
                incidentId: null),
            clarification: null,
            Attribution(
                [
                    ProposalField.Environment,
                    ProposalField.Role,
                    ProposalField.Justification,
                ]),
            CreatedAt,
            "ready");

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class InMemoryPreparationStore : IRequestPreparationStore
    {
        private readonly List<RequestPreparation> preparations;

        internal InMemoryPreparationStore(params RequestPreparation[] preparations)
        {
            this.preparations = [.. preparations];
        }

        internal List<RequestPreparation> Preparations => preparations;

        internal int SaveCount { get; private set; }

        internal RequestPreparation? ActiveWinner { get; init; }

        internal ApplicationFailure? SaveFailure { get; init; }

        public void Add(RequestPreparation preparation) => preparations.Add(preparation);

        public Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ActiveWinner is not null)
            {
                return Task.FromResult(ApplicationResult.Succeeded(ActiveWinner));
            }

            var active = preparations.SingleOrDefault(preparation =>
                preparation.Binding == binding
                && preparation.Lifecycle is PreparationLifecycle.Collecting
                    or PreparationLifecycle.Ready);
            return Task.FromResult(active is null
                ? NotFound()
                : ApplicationResult.Succeeded(active));
        }

        public Task<ApplicationResult<RequestPreparation>> GetLatestAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = preparations
                .Where(preparation => preparation.Binding == binding)
                .OrderByDescending(preparation => preparation.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(latest is null
                ? NotFound()
                : ApplicationResult.Succeeded(latest));
        }

        public Task<ApplicationResult<RequestPreparation>> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = preparations.SingleOrDefault(
                value => value.PreparationId == preparationId);
            return Task.FromResult(preparation is null
                ? NotFound()
                : ApplicationResult.Succeeded(preparation));
        }

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(SaveFailure is null
                ? ApplicationResult.Succeeded()
                : ApplicationResult.Failed(SaveFailure));
        }

        private static ApplicationResult<RequestPreparation> NotFound() =>
            ApplicationResult.Failed<RequestPreparation>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    "request-preparation-not-found",
                    "The preparation was not found."));
    }
}

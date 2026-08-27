using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class TargetPreparationConfirmationTests
{
    private static readonly DateTimeOffset ReadyAt =
        new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmationAndReplayPersistOneStableRequestAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        var ready = await PersistReadyAsync(fixture.Services, cancellationToken);

        PreparationConfirmationSubmitted submitted;
        PreparationConfirmationSubmitted replay;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = CreateService(scope.ServiceProvider);
            submitted = Assert.IsType<PreparationConfirmationSubmitted>(
                await service.ConfirmAsync(
                    Command(ready.PreparationId, "first-confirmation"),
                    cancellationToken));
            replay = Assert.IsType<PreparationConfirmationSubmitted>(
                await service.ConfirmAsync(
                    Command(ready.PreparationId, "confirmation-replay"),
                    cancellationToken));
        }

        Assert.False(submitted.WasAlreadySubmitted);
        Assert.True(replay.WasAlreadySubmitted);
        Assert.Equal(submitted.Request.Id, replay.Request.Id);
        Assert.Equal(ready.PreparationId, submitted.Request.PreparationId);

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        var preparationStore = verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var workflowStore = verificationScope.ServiceProvider
            .GetRequiredService<IWorkflowStore>();
        var persistedPreparation = await preparationStore.GetAsync(
            ready.PreparationId,
            cancellationToken);
        var requests = await context.Set<AccessRequest>()
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var auditEvents = await workflowStore.ListAuditEventsAsync(
            submitted.Request.Id,
            cancellationToken);

        Assert.Equal(
            PreparationLifecycle.Submitted,
            persistedPreparation.Value.Lifecycle);
        Assert.Equal(submitted.Request.Id, Assert.Single(requests).Id);
        Assert.Equal(
            AuditEventType.RequestCreated,
            Assert.Single(auditEvents.Value).EventType);
        Assert.Empty(
            await context.Set<ApprovalDecision>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
        Assert.Empty(
            await context.Set<ProvisioningOperation>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
        Assert.Empty(
            await context.Set<AccessGrant>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ConcurrentConfirmationsConvergeOnOneRequestIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        var ready = await PersistReadyAsync(fixture.Services, cancellationToken);
        var coordinator = new ConfirmationSaveCoordinator();
        await using var winnerScope = fixture.Services.CreateAsyncScope();
        await using var contenderScope = fixture.Services.CreateAsyncScope();
        var winnerStore = new GatedConfirmationStore(
            winnerScope.ServiceProvider
                .GetRequiredService<IRequestPreparationConfirmationStore>(),
            coordinator,
            isWinner: true);
        var contenderStore = new GatedConfirmationStore(
            contenderScope.ServiceProvider
                .GetRequiredService<IRequestPreparationConfirmationStore>(),
            coordinator,
            isWinner: false);
        var winnerTask = CreateService(
            winnerScope.ServiceProvider,
            winnerStore).ConfirmAsync(
                Command(ready.PreparationId, "concurrent-winner"),
                cancellationToken);
        var contenderTask = CreateService(
            contenderScope.ServiceProvider,
            contenderStore).ConfirmAsync(
                Command(ready.PreparationId, "concurrent-contender"),
                cancellationToken);

        PreparationConfirmationSubmitted winner;
        try
        {
            await coordinator.BothSavesReady.WaitAsync(cancellationToken);
            winner = Assert.IsType<PreparationConfirmationSubmitted>(
                await winnerTask);
        }
        finally
        {
            coordinator.ReleaseContender();
        }

        var contender = Assert.IsType<PreparationConfirmationSubmitted>(
            await contenderTask);

        Assert.False(winner.WasAlreadySubmitted);
        Assert.True(contender.WasAlreadySubmitted);
        Assert.Equal(winner.Request.Id, contender.Request.Id);

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        Assert.Single(
            await context.Set<AccessRequest>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
        Assert.Single(
            await context.Set<AuditEvent>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ConfirmationCommitFirstPreventsStaleRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        var ready = await PersistReadyAsync(fixture.Services, cancellationToken);
        await using var revisionScope = fixture.Services.CreateAsyncScope();
        var revisionStore = revisionScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var stalePredecessor = (await revisionStore.GetAsync(
            ready.PreparationId,
            cancellationToken)).Value;
        var successor = CreateRevision(stalePredecessor);
        stalePredecessor.MarkSuperseded(
            ReadyAt.AddMinutes(6),
            "stale-revision");
        revisionStore.Add(successor);

        await using (var confirmationScope = fixture.Services.CreateAsyncScope())
        {
            var confirmed = await CreateService(
                confirmationScope.ServiceProvider).ConfirmAsync(
                    Command(ready.PreparationId, "confirmation-winner"),
                    cancellationToken);
            Assert.IsType<PreparationConfirmationSubmitted>(confirmed);
        }

        var staleSave = await revisionStore.SaveChangesAsync(cancellationToken);

        Assert.True(staleSave.IsFailure);
        Assert.Equal(
            ApplicationFailureKind.ConcurrencyConflict,
            staleSave.Failure!.Kind);

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var verificationStore = verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var persisted = await verificationStore.GetAsync(
            ready.PreparationId,
            cancellationToken);
        var active = await verificationStore.GetActiveAsync(
            Binding(),
            cancellationToken);
        var context = verificationScope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        Assert.Equal(PreparationLifecycle.Submitted, persisted.Value.Lifecycle);
        Assert.True(active.IsFailure);
        Assert.Equal(ApplicationFailureKind.NotFound, active.Failure!.Kind);
        Assert.Single(
            await context.Set<AccessRequest>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task RevisionCommitFirstMakesInFlightConfirmationStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        var ready = await PersistReadyAsync(fixture.Services, cancellationToken);
        var coordinator = new ConfirmationSaveCoordinator();
        await using var confirmationScope = fixture.Services.CreateAsyncScope();
        var confirmationStore = new GatedConfirmationStore(
            confirmationScope.ServiceProvider
                .GetRequiredService<IRequestPreparationConfirmationStore>(),
            coordinator,
            isWinner: false);
        var confirmationTask = CreateService(
            confirmationScope.ServiceProvider,
            confirmationStore).ConfirmAsync(
                Command(ready.PreparationId, "stale-confirmation"),
                cancellationToken);

        await coordinator.WaitAsync(isWinner: true, cancellationToken);
        await using (var revisionScope = fixture.Services.CreateAsyncScope())
        {
            var revisionStore = revisionScope.ServiceProvider
                .GetRequiredService<IRequestPreparationStore>();
            var predecessor = (await revisionStore.GetAsync(
                ready.PreparationId,
                cancellationToken)).Value;
            var successor = CreateRevision(predecessor);
            predecessor.MarkSuperseded(
                ReadyAt.AddMinutes(6),
                "revision-winner");
            revisionStore.Add(successor);
            Assert.True(
                (await revisionStore.SaveChangesAsync(cancellationToken)).IsSuccess);
        }

        coordinator.ReleaseContender();
        var confirmation = Assert.IsType<PreparationConfirmationFailed>(
            await confirmationTask);

        Assert.Equal("request-preparation-superseded", confirmation.Failure.Code);

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var verificationStore = verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var predecessorResult = await verificationStore.GetAsync(
            ready.PreparationId,
            cancellationToken);
        var successorResult = await verificationStore.GetActiveAsync(
            Binding(),
            cancellationToken);
        var context = verificationScope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        Assert.Equal(
            PreparationLifecycle.Superseded,
            predecessorResult.Value.Lifecycle);
        Assert.Equal(
            ready.PreparationId,
            successorResult.Value.PredecessorPreparationId);
        Assert.Empty(
            await context.Set<AccessRequest>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
        Assert.Empty(
            await context.Set<AuditEvent>()
                .AsNoTracking()
                .ToArrayAsync(cancellationToken));
    }

    private static PreparationConfirmationService CreateService(
        IServiceProvider services) =>
        CreateService(
            services,
            services.GetRequiredService<IRequestPreparationConfirmationStore>());

    private static PreparationConfirmationService CreateService(
        IServiceProvider services,
        IRequestPreparationConfirmationStore store) =>
        new(
            store,
            services.GetRequiredService<IProductionEnvironmentAuthority>(),
            services.GetRequiredService<IEnvironmentRoleAuthority>(),
            services.GetRequiredService<IIncidentAuthority>(),
            services.GetRequiredService<IAuthenticatedPrincipalReader>(),
            new DeterministicClock(ReadyAt.AddMinutes(5)));

    private static PreparationConfirmationCommand Command(
        Guid preparationId,
        string correlationId) =>
        new(Binding(), preparationId, correlationId);

    private static async Task<RequestPreparation> PersistReadyAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                "x",
                "INC-1042"),
            clarification: null,
            new MaterialChangeAttribution(
                [
                    ProposalField.Environment,
                    ProposalField.Role,
                    ProposalField.Justification,
                    ProposalField.Incident,
                ],
                "test-model",
                "test-provider-version",
                "test-prompt",
                "test-schema",
                ReadyAt,
                "ready-attribution"),
            ReadyAt,
            "ready");
        store.Add(preparation);
        Assert.True((await store.SaveChangesAsync(cancellationToken)).IsSuccess);
        return preparation;
    }

    private static PreparationBinding Binding() =>
        new(
            PreparationBinding.TeamsChannel,
            "tenant",
            "actor",
            "conversation",
            "requester");

    private static RequestPreparation CreateRevision(
        RequestPreparation predecessor) =>
        RequestPreparation.CreateRevision(
            predecessor,
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                "Revised justification",
                "INC-1042"),
            clarification: null,
            new MaterialChangeAttribution(
                [ProposalField.Justification],
                "test-model",
                "test-provider-version",
                "test-prompt",
                "test-schema",
                ReadyAt.AddMinutes(6),
                "revision-attribution"),
            ReadyAt.AddMinutes(6),
            "revision");

    private sealed class ConfirmationSaveCoordinator
    {
        private readonly TaskCompletionSource bothSavesReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource contenderRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int saveCount;

        internal Task BothSavesReady => bothSavesReady.Task;

        internal async Task WaitAsync(
            bool isWinner,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saveCount) == 2)
            {
                bothSavesReady.TrySetResult();
            }

            await bothSavesReady.Task.WaitAsync(cancellationToken);
            if (!isWinner)
            {
                await contenderRelease.Task.WaitAsync(cancellationToken);
            }
        }

        internal void ReleaseContender() => contenderRelease.TrySetResult();
    }

    private sealed class GatedConfirmationStore(
        IRequestPreparationConfirmationStore inner,
        ConfirmationSaveCoordinator coordinator,
        bool isWinner) : IRequestPreparationConfirmationStore
    {
        public void Add(RequestPreparation preparation) => inner.Add(preparation);

        public void AddRequest(AccessRequest request) => inner.AddRequest(request);

        public void AddAuditEvent(AuditEvent auditEvent) =>
            inner.AddAuditEvent(auditEvent);

        public Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) =>
            inner.GetActiveAsync(binding, cancellationToken);

        public Task<ApplicationResult<RequestPreparation>> GetLatestAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) =>
            inner.GetLatestAsync(binding, cancellationToken);

        public Task<ApplicationResult<RequestPreparation>> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            inner.GetAsync(preparationId, cancellationToken);

        public Task<ApplicationResult<RequestPreparation>> ReloadAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            inner.ReloadAsync(preparationId, cancellationToken);

        public Task<ApplicationResult<AccessRequest>> GetRequestByPreparationIdAsync(
            Guid preparationId,
            CancellationToken cancellationToken) =>
            inner.GetRequestByPreparationIdAsync(preparationId, cancellationToken);

        public async Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            await coordinator.WaitAsync(isWinner, cancellationToken);
            return await inner.SaveChangesAsync(cancellationToken);
        }
    }
}

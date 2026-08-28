using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class PreparationTurnConcurrencyTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    private static readonly PreparationTurnAttribution TurnAttribution = new(
        "model-deployment",
        "provider-version",
        "prompt-v1",
        "schema-v1");

    [Fact]
    public async Task ConcurrentFirstTurnLoserReloadsWinnerWithoutReplayingProposal()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var authority = CompleteAuthority();
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var first = Service(firstScope, authority);
        var second = Service(secondScope, authority);
        var firstStart = await first.BeginAsync(
            Binding(),
            "first",
            TestContext.Current.CancellationToken);
        var secondStart = await second.BeginAsync(
            Binding(),
            "second",
            TestContext.Current.CancellationToken);

        var winner = await first.ApplyAsync(
            firstStart.Value,
            CompleteUpdate("winner justification"),
            TurnAttribution,
            TestContext.Current.CancellationToken);
        var loser = await second.ApplyAsync(
            secondStart.Value,
            CompleteUpdate("loser justification"),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var winnerState = Assert.IsType<PreparationSnapshot>(winner.Preparation);
        var loserState = Assert.IsType<PreparationSnapshot>(loser.Preparation);
        var loserFailure = Assert.IsType<Failed>(loser.Response.Outcome);
        Assert.Equal("request_preparation_active_race", loserFailure.Failure.Code);
        Assert.Equal(winnerState.PreparationId, loserState.PreparationId);
        Assert.Equal("winner justification", loserState.Candidate.Justification);
        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var active = await verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>()
            .GetActiveAsync(Binding(), TestContext.Current.CancellationToken);
        Assert.True(active.IsSuccess);
        Assert.Equal(winnerState.PreparationId, active.Value.PreparationId);
        Assert.Equal("winner justification", active.Value.Candidate.Justification);
    }

    [Fact]
    public async Task StaleSnapshotCommitFailsAtomicallyAndIsNotReplayed()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var initial = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                clientId: null,
                environmentId: null,
                roleId: null,
                "initial justification",
                incidentId: null),
            clarification: null,
            Attribution(ProposalField.Justification, "seed"),
            CreatedAt,
            "seed");
        await PersistAsync(fixture, initial);
        var authority = CompleteAuthority();
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var first = Service(firstScope, authority, CreatedAt.AddMinutes(1));
        var second = Service(secondScope, authority, CreatedAt.AddMinutes(1));
        var firstStart = await first.BeginAsync(
            Binding(),
            "first",
            TestContext.Current.CancellationToken);
        var secondStart = await second.BeginAsync(
            Binding(),
            "second",
            TestContext.Current.CancellationToken);

        var committed = await first.ApplyAsync(
            firstStart.Value,
            JustificationUpdate("first committed justification"),
            TurnAttribution,
            TestContext.Current.CancellationToken);
        var stale = await second.ApplyAsync(
            secondStart.Value,
            CompleteUpdate("stale justification"),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        Assert.IsType<DraftUpdated>(committed.Response.Outcome);
        var staleFailure = Assert.IsType<Failed>(stale.Response.Outcome);
        Assert.Equal(
            "request_preparation_concurrency_conflict",
            staleFailure.Failure.Code);
        Assert.Equal(
            "initial justification",
            stale.Preparation!.Candidate.Justification);
        Assert.Null(stale.Preparation.Candidate.EnvironmentId);
        Assert.Null(stale.Preparation.Candidate.RoleId);
        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>()
            .GetAsync(initial.PreparationId, TestContext.Current.CancellationToken);
        Assert.True(persisted.IsSuccess);
        Assert.Equal(
            "first committed justification",
            persisted.Value.Candidate.Justification);
        Assert.Null(persisted.Value.Candidate.EnvironmentId);
        Assert.Null(persisted.Value.Candidate.RoleId);
    }

    [Fact]
    public async Task AuthorityFailureRejectsScopeAndPersistsIndependentJustificationOnly()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var authority = new FakeAuthority
        {
            EnvironmentFailure = new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "environment-source-unavailable",
                "The environment source is unavailable."),
        };
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Service(scope, authority, CreatedAt);
        var started = await service.BeginAsync(
            Binding(),
            "authority-failure",
            TestContext.Current.CancellationToken);

        var result = await service.ApplyAsync(
            started.Value,
            new TurnProposal(
                TurnProposal.CurrentSchemaVersion,
                DialogueAct.UpdateDraft,
                patch: new DraftPatch(
                    environment: new SetEnvironmentOperation(
                        new ExactEnvironmentId("PROD-ALPHA-EU")),
                    justification: new SetJustificationOperation(
                        new JustificationProposal("Restore customer service.")))),
            TurnAttribution,
            TestContext.Current.CancellationToken);

        var updated = Assert.IsType<DraftUpdated>(result.Response.Outcome);
        Assert.Equal(
            ApplicationGroupResultKind.Rejected,
            updated.ScopeResult?.Kind);
        Assert.Equal(
            ApplicationGroupRejectionReason.Unavailable,
            updated.ScopeResult?.RejectionReason);
        Assert.Equal(
            ApplicationGroupResultKind.Applied,
            updated.JustificationResult?.Kind);
        Assert.Null(result.Preparation!.Candidate.EnvironmentId);
        Assert.Null(result.Preparation.Candidate.RoleId);
        Assert.Equal(
            "Restore customer service.",
            result.Preparation.Candidate.Justification);

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>()
            .GetActiveAsync(Binding(), TestContext.Current.CancellationToken);
        Assert.True(persisted.IsSuccess);
        Assert.Null(persisted.Value.Candidate.EnvironmentId);
        Assert.Null(persisted.Value.Candidate.RoleId);
        Assert.Equal(
            "Restore customer service.",
            persisted.Value.Candidate.Justification);
    }

    [Fact]
    public async Task ClarificationContextSurvivesRestartAndFeedsNextAgentTurn()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"preparation-turn-{Guid.NewGuid():N}.db");
        var authority = CompleteAuthority();
        authority.SearchResult = EnvironmentSearchResult.FromMatches(
            [
                new EnvironmentSearchMatch(
                    "PROD-ALPHA-EU",
                    "Production EU",
                    "client-alpha",
                    "Client Alpha",
                    "EU",
                    EnvironmentClassification.Primary),
                new EnvironmentSearchMatch(
                    "PROD-BETA-UK",
                    "Production UK",
                    "client-beta",
                    "Client Beta",
                    "UK",
                    EnvironmentClassification.Primary),
            ]);
        authority.Environments["PROD-BETA-UK"] =
            Environment("PROD-BETA-UK", "client-beta");

        try
        {
            Guid preparationId;
            await using (var firstFixture =
                await WorkflowPersistenceFixture.CreateAsync(databasePath))
            {
                await using var firstScope = firstFixture.Services.CreateAsyncScope();
                var first = Service(firstScope, authority);
                var started = await first.BeginAsync(
                    Binding(),
                    "ambiguous",
                    TestContext.Current.CancellationToken);
                var ambiguous = await first.ApplyAsync(
                    started.Value,
                    new TurnProposal(
                        TurnProposal.CurrentSchemaVersion,
                        DialogueAct.UpdateDraft,
                        patch: new DraftPatch(
                            environment: new SetEnvironmentOperation(
                                new EnvironmentSearchQuery("production")))),
                    TurnAttribution,
                    TestContext.Current.CancellationToken);
                var state = Assert.IsType<PreparationSnapshot>(ambiguous.Preparation);
                preparationId = state.PreparationId;
                Assert.NotNull(state.Clarification);
            }

            await using var restarted =
                await WorkflowPersistenceFixture.CreateAsync(databasePath);
            await using var restartedScope = restarted.Services.CreateAsyncScope();
            var interpreter = new RecordingInterpreter(
                new TurnProposal(
                    TurnProposal.CurrentSchemaVersion,
                    DialogueAct.UpdateDraft,
                    patch: new DraftPatch(
                        environment: new SetEnvironmentOperation(
                            new ExactEnvironmentId("PROD-BETA-UK")))));
            var orchestrator = new RequestPreparationOrchestrator(
                Service(restartedScope, authority, CreatedAt.AddMinutes(1)),
                interpreter);

            var selected = await orchestrator.ProcessTurnAsync(
                Binding(),
                "the second one",
                "selection",
                TestContext.Current.CancellationToken);

            Assert.Equal(1, interpreter.CallCount);
            var agentClarification = Assert.IsType<AgentClarificationContext>(
                interpreter.LastInput!.Clarification);
            Assert.Equal(CreatedAt, agentClarification.CreatedAt);
            Assert.Equal([1, 2], agentClarification.Choices.Select(choice => choice.Position));
            Assert.Equal(
                ["PROD-ALPHA-EU", "PROD-BETA-UK"],
                agentClarification.Choices
                    .Select(choice => choice.CanonicalId)
                    .ToArray());
            Assert.Equal(
                ["Production EU", "Production UK"],
                agentClarification.Choices.Select(choice => choice.DisplayName));
            Assert.Equal(
                ["client-alpha", "client-beta"],
                agentClarification.Choices.Select(choice => choice.ClientId));
            Assert.Equal(
                ["EU", "UK"],
                agentClarification.Choices.Select(choice => choice.Region));
            Assert.All(
                agentClarification.Choices,
                choice => Assert.Equal(
                    EnvironmentClassification.Primary,
                    choice.EnvironmentClassification));
            Assert.Equal(preparationId, selected.Preparation!.PreparationId);
            Assert.Equal("PROD-BETA-UK", selected.Preparation.Candidate.EnvironmentId);
            Assert.Null(selected.Preparation.Clarification);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AgentLatencyHoldsNoWriteLockAndLaterStaleProposalIsRejected()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var initial = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                clientId: null,
                environmentId: null,
                roleId: null,
                "initial justification",
                incidentId: null),
            clarification: null,
            Attribution(ProposalField.Justification, "seed"),
            CreatedAt,
            "seed");
        await PersistAsync(fixture, initial);
        var authority = new FakeAuthority();
        await using var slowScope = fixture.Services.CreateAsyncScope();
        await using var fastScope = fixture.Services.CreateAsyncScope();
        var slowInterpreter = new BlockingInterpreter(
            JustificationUpdate("stale slow justification"));
        var slowOrchestrator = new RequestPreparationOrchestrator(
            Service(slowScope, authority, CreatedAt.AddMinutes(1)),
            slowInterpreter);
        var fastOrchestrator = new RequestPreparationOrchestrator(
            Service(fastScope, authority, CreatedAt.AddMinutes(1)),
            new RecordingInterpreter(
                JustificationUpdate("committed fast justification")));

        var slowTurn = slowOrchestrator.ProcessTurnAsync(
            Binding(),
            "slow turn",
            "slow",
            TestContext.Current.CancellationToken);
        await slowInterpreter.Started.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var fastResult = await fastOrchestrator.ProcessTurnAsync(
            Binding(),
            "fast turn",
            "fast",
            TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        slowInterpreter.Release();
        var slowResult = await slowTurn;

        Assert.IsType<DraftUpdated>(fastResult.Response.Outcome);
        var failure = Assert.IsType<Failed>(slowResult.Response.Outcome);
        Assert.Equal(
            "request_preparation_concurrency_conflict",
            failure.Failure.Code);
        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>()
            .GetAsync(initial.PreparationId, TestContext.Current.CancellationToken);
        Assert.True(persisted.IsSuccess);
        Assert.Equal(
            "committed fast justification",
            persisted.Value.Candidate.Justification);
    }

    [Fact]
    public async Task TerminalPreparationAfterRestartStillUsesAgentPathThenReturnsGuidance()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"terminal-preparation-{Guid.NewGuid():N}.db");
        var submitted = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                "Investigate the active incident",
                incidentId: null),
            clarification: null,
            new MaterialChangeAttribution(
                [
                    ProposalField.Environment,
                    ProposalField.Role,
                    ProposalField.Justification,
                ],
                "seed-model",
                "seed-version",
                "seed-prompt",
                "seed-schema",
                CreatedAt,
                "seed"),
            CreatedAt,
            "seed");
        submitted.MarkSubmitted(CreatedAt.AddMinutes(1), "submitted");

        try
        {
            await using (var first =
                await WorkflowPersistenceFixture.CreateAsync(databasePath))
            {
                await PersistAsync(first, submitted);
            }

            await using var restarted =
                await WorkflowPersistenceFixture.CreateAsync(databasePath);
            await using var scope = restarted.Services.CreateAsyncScope();
            var interpreter = new RecordingInterpreter(
                new TurnProposal(
                    TurnProposal.CurrentSchemaVersion,
                    DialogueAct.Unclear));
            var orchestrator = new RequestPreparationOrchestrator(
                Service(scope, new FakeAuthority(), CreatedAt.AddMinutes(2)),
                interpreter);

            var result = await orchestrator.ProcessTurnAsync(
                Binding(),
                "what happened to my draft?",
                "terminal-turn",
                TestContext.Current.CancellationToken);

            Assert.Equal(1, interpreter.CallCount);
            Assert.Equal(PreparationLifecycle.Submitted, interpreter.LastInput!.Lifecycle);
            Assert.IsType<TerminalPreparationGuidance>(result.Response.Outcome);
            Assert.Equal(submitted.PreparationId, result.Preparation!.PreparationId);
            Assert.Equal(PreparationLifecycle.Submitted, result.Preparation.Lifecycle);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ResetAtomicallyTerminalizesOldPreparationAndCreatesCleanActiveRow()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var previous = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                clientId: null,
                environmentId: null,
                roleId: null,
                "retained before reset",
                incidentId: null),
            clarification: null,
            Attribution(ProposalField.Justification, "seed"),
            CreatedAt,
            "seed");
        await PersistAsync(fixture, previous);
        await using (var resetScope = fixture.Services.CreateAsyncScope())
        {
            var service = Service(
                resetScope,
                new FakeAuthority(),
                CreatedAt.AddMinutes(1));

            var result = await service.ResetAsync(
                new ResetPreparationCommand(Binding(), "reset"),
                TestContext.Current.CancellationToken);

            Assert.IsType<ResetGuidance>(result.Response.Outcome);
            Assert.NotEqual(previous.PreparationId, result.Preparation!.PreparationId);
        }

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var store = verificationScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var old = await store.GetAsync(
            previous.PreparationId,
            TestContext.Current.CancellationToken);
        var active = await store.GetActiveAsync(
            Binding(),
            TestContext.Current.CancellationToken);

        Assert.True(old.IsSuccess);
        Assert.Equal(PreparationLifecycle.Superseded, old.Value.Lifecycle);
        Assert.True(old.Value.Candidate.IsEmpty);
        Assert.True(active.IsSuccess);
        Assert.Equal(PreparationLifecycle.Collecting, active.Value.Lifecycle);
        Assert.True(active.Value.Candidate.IsEmpty);
        Assert.Null(active.Value.PredecessorPreparationId);
    }

    private static PreparationTurnService Service(
        AsyncServiceScope scope,
        FakeAuthority authority,
        DateTimeOffset? now = null) =>
        new(
            scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>(),
            new RequestPreparationReducer(
                authority,
                authority,
                authority,
                authority),
            new FixedClock(now ?? CreatedAt));

    private static PreparationBinding Binding() =>
        new("msteams", "tenant", "actor", "conversation", "requester");

    private static TurnProposal CompleteUpdate(string justification) =>
        new(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.UpdateDraft,
            patch: new DraftPatch(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-ALPHA-EU")),
                role: new SetRoleOperation("ProductionReadOnly"),
                justification: new SetJustificationOperation(
                    new JustificationProposal(justification))));

    private static TurnProposal JustificationUpdate(string justification) =>
        new(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.UpdateDraft,
            patch: new DraftPatch(
                justification: new SetJustificationOperation(
                    new JustificationProposal(justification))));

    private static FakeAuthority CompleteAuthority()
    {
        var authority = new FakeAuthority();
        authority.Environments["PROD-ALPHA-EU"] =
            Environment("PROD-ALPHA-EU", "client-alpha");
        authority.Roles[("PROD-ALPHA-EU", "ProductionReadOnly")] =
            new EnvironmentRoleAuthorityProjection(
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                "Production read-only",
                isCurrentlyAssignable: true);
        return authority;
    }

    private static EnvironmentAuthorityProjection Environment(
        string environmentId,
        string clientId) =>
        new(
            environmentId,
            $"{environmentId} display",
            clientId,
            $"{clientId} display",
            $"{clientId}-approver",
            isActive: true,
            isProduction: true,
            isEligibleForIntake: true);

    private static MaterialChangeAttribution Attribution(
        ProposalField field,
        string correlationId) =>
        new(
            [field],
            "seed-model",
            "seed-version",
            "seed-prompt",
            "seed-schema",
            CreatedAt,
            correlationId);

    private static async Task PersistAsync(
        WorkflowPersistenceFixture fixture,
        RequestPreparation preparation)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        store.Add(preparation);
        var saved = await store.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class RecordingInterpreter(TurnProposal proposal)
        : ITurnProposalInterpreter
    {
        internal int CallCount { get; private set; }

        internal AgentTurnInput? LastInput { get; private set; }

        public Task<AgentInterpretationResult> InterpretAsync(
            AgentTurnInput turn,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastInput = turn;
            return Task.FromResult<AgentInterpretationResult>(
                new AgentInterpretationSucceeded(
                    proposal,
                    new AgentExecutionMetadata(
                        "provider",
                        "deployment",
                        "provider-version",
                        "prompt-v1",
                        "schema-v1",
                        "mcp-v1",
                        "search-v1",
                        ProviderIterationCount: 1,
                        ToolCallCount: 0,
                        turn.CorrelationId,
                        CreatedAt,
                        CreatedAt)));
        }
    }

    private sealed class BlockingInterpreter(TurnProposal proposal)
        : ITurnProposalInterpreter
    {
        private readonly TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => started.Task;

        internal void Release() => release.TrySetResult(true);

        public async Task<AgentInterpretationResult> InterpretAsync(
            AgentTurnInput turn,
            CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new AgentInterpretationSucceeded(
                proposal,
                new AgentExecutionMetadata(
                    "provider",
                    "deployment",
                    "provider-version",
                    "prompt-v1",
                    "schema-v1",
                    "mcp-v1",
                    "search-v1",
                    ProviderIterationCount: 1,
                    ToolCallCount: 0,
                    turn.CorrelationId,
                    CreatedAt,
                    CreatedAt));
        }
    }

    private sealed class FakeAuthority :
        IProductionEnvironmentSearchAuthority,
        IProductionEnvironmentAuthority,
        IEnvironmentRoleAuthority,
        IIncidentAuthority
    {
        internal Dictionary<string, EnvironmentAuthorityProjection> Environments { get; } =
            new(StringComparer.Ordinal);

        internal Dictionary<(string EnvironmentId, string RoleId),
            EnvironmentRoleAuthorityProjection> Roles { get; } = [];

        internal EnvironmentSearchResult SearchResult { get; set; } =
            EnvironmentSearchResult.FromMatches([]);

        internal ApplicationFailure? EnvironmentFailure { get; set; }

        public Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApplicationResult.Succeeded(SearchResult));
        }

        public Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EnvironmentFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<EnvironmentAuthorityProjection>(
                        EnvironmentFailure));
            }

            return Task.FromResult(
                Environments.TryGetValue(environmentId, out var environment)
                    ? ApplicationResult.Succeeded(environment)
                    : ApplicationResult.Failed<EnvironmentAuthorityProjection>(NotFound()));
        }

        Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>>
            IEnvironmentRoleAuthority.ListAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRoleAuthorityProjection> roles = Roles
                .Where(pair => pair.Key.EnvironmentId == environmentId)
                .Select(pair => pair.Value)
                .OrderBy(role => role.RoleId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(ApplicationResult.Succeeded(roles));
        }

        Task<ApplicationResult<EnvironmentRoleAuthorityProjection>>
            IEnvironmentRoleAuthority.GetAsync(
                string environmentId,
                string roleId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Roles.TryGetValue((environmentId, roleId), out var role)
                    ? ApplicationResult.Succeeded(role)
                    : ApplicationResult.Failed<EnvironmentRoleAuthorityProjection>(NotFound()));
        }

        Task<ApplicationResult<IncidentAuthorityProjection>> IIncidentAuthority.GetAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ApplicationResult.Failed<IncidentAuthorityProjection>(NotFound()));
        }

        private static ApplicationFailure NotFound() =>
            new(
                ApplicationFailureKind.NotFound,
                "not-found",
                "The authoritative value was not found.");
    }
}

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class TargetRequestPreparationOrchestratorTests
{
    [Fact]
    public async Task EveryOrdinaryTextIncludingResetLikeTextReachesInterpreterUnchanged()
    {
        const string requesterText = " /new please ";
        var interpreter = new RecordingInterpreter(
            new TurnProposal(TurnProposal.CurrentSchemaVersion, DialogueAct.Unclear));
        var store = new EmptyPreparationStore();
        var authority = new UnusedAuthority();
        var service = new PreparationTurnService(
            store,
            new RequestPreparationReducer(authority, authority, authority, authority),
            new FixedClock());
        var orchestrator = new TargetRequestPreparationOrchestrator(
            service,
            interpreter);

        var result = await orchestrator.ProcessTurnAsync(
            Binding(),
            requesterText,
            "correlation-1",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, interpreter.CallCount);
        Assert.Equal(requesterText, interpreter.LastInput!.LatestRequesterText);
        Assert.IsType<UnclearGuidance>(result.Response.Outcome);
        Assert.Null(result.Preparation);
        Assert.Equal(0, store.SaveCount);
    }

    private static PreparationBinding Binding() =>
        new("msteams", "tenant", "actor", "conversation", "requester");

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
                        "model-version",
                        "prompt-v1",
                        "schema-v1",
                        "mcp-v1",
                        "search-v1",
                        ProviderIterationCount: 1,
                        ToolCallCount: 0,
                        turn.CorrelationId,
                        FixedClock.Now,
                        FixedClock.Now)));
        }
    }

    private sealed class EmptyPreparationStore : IRequestPreparationStore
    {
        internal int SaveCount { get; private set; }

        public void Add(RequestPreparation preparation) =>
            throw new InvalidOperationException("This test must not persist a preparation.");

        public Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) => NotFound(cancellationToken);

        public Task<ApplicationResult<RequestPreparation>> GetLatestAsync(
            PreparationBinding binding,
            CancellationToken cancellationToken) => NotFound(cancellationToken);

        public Task<ApplicationResult<RequestPreparation>> GetAsync(
            Guid preparationId,
            CancellationToken cancellationToken) => NotFound(cancellationToken);

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(ApplicationResult.Succeeded());
        }

        private static Task<ApplicationResult<RequestPreparation>> NotFound(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ApplicationResult.Failed<RequestPreparation>(
                    new ApplicationFailure(
                        ApplicationFailureKind.NotFound,
                        "not-found",
                        "No preparation exists.")));
        }
    }

    private sealed class FixedClock : IClock
    {
        internal static readonly DateTimeOffset Now =
            new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class UnusedAuthority :
        IProductionEnvironmentSearchAuthority,
        IProductionEnvironmentAuthority,
        IEnvironmentRoleAuthority,
        IIncidentAuthority
    {
        public Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unclear turn must not search.");

        public Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
            string environmentId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unclear turn must not load an environment.");

        Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>>
            IEnvironmentRoleAuthority.ListAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unclear turn must not list roles.");

        Task<ApplicationResult<EnvironmentRoleAuthorityProjection>>
            IEnvironmentRoleAuthority.GetAsync(
                string environmentId,
                string roleId,
                CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unclear turn must not load a role.");

        Task<ApplicationResult<IncidentAuthorityProjection>> IIncidentAuthority.GetAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unclear turn must not load an incident.");
    }
}

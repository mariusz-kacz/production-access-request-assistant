using System.Diagnostics;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Evaluation;

internal sealed record LiveModelEvaluationOptions
{
    internal static readonly TimeSpan DefaultTurnTimeout = TimeSpan.FromSeconds(100);

    internal LiveModelEvaluationOptions(TimeSpan turnTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            turnTimeout,
            TimeSpan.Zero);

        TurnTimeout = turnTimeout;
    }

    internal TimeSpan TurnTimeout { get; }
}

internal sealed record EvaluationScenarioExecution(
    EvaluationScenarioResult Result,
    WorkflowSideEffectCounts TotalSideEffects);

internal sealed class EvaluationScenarioExecutor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    LiveModelEvaluationOptions options)
{
    internal async Task<EvaluationScenarioExecution> ExecuteAsync(
        Guid runId,
        EvaluationScenario scenario,
        WorkflowSideEffectCounts previousTotalSideEffects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(previousTotalSideEffects);

        var stopwatch = new Stopwatch();
        await using var scope = scopeFactory.CreateAsyncScope();
        var actor = CreateActor(runId, scenario.Id);
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();

        try
        {
            await SeedStartingCandidateAsync(
                dbContext,
                actor,
                runId,
                scenario,
                cancellationToken);

            if (scenario.Turns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Evaluation scenario '{scenario.Id}' does not contain a turn.");
            }

            var intakeService = scope.ServiceProvider
                .GetRequiredService<RequestIntakeService>();
            RequestPreparationResult? finalPreparation = null;

            stopwatch.Start();
            foreach (var turn in scenario.Turns)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var turnCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                turnCancellation.CancelAfter(options.TurnTimeout);

                finalPreparation = await intakeService.PrepareAsync(
                    new PrepareAccessRequestCommand(
                        actor,
                        turn.RequesterMessage,
                        CreateCorrelationId(runId, scenario.Id, turn.Id)),
                    turnCancellation.Token);
            }

            stopwatch.Stop();
            if (finalPreparation is null)
            {
                throw new InvalidOperationException(
                    $"Evaluation scenario '{scenario.Id}' did not produce a result.");
            }

            if (finalPreparation.Kind == RequestPreparationResultKind.Failed
                && finalPreparation.Failure?.Kind == ApplicationFailureKind.Cancelled)
            {
                return await CreateInterruptedExecutionAsync(
                    dbContext,
                    scenario,
                    previousTotalSideEffects,
                    stopwatch.ElapsedMilliseconds,
                    cancellationToken.IsCancellationRequested);
            }

            var currentSession = await FindCurrentSessionAsync(
                dbContext,
                actor,
                cancellationToken);
            var finalOutcome = MapOutcome(finalPreparation, currentSession);
            var totalSideEffects = await CountSideEffectsAsync(
                dbContext,
                cancellationToken);
            var scenarioSideEffects = Subtract(
                totalSideEffects,
                previousTotalSideEffects);
            var scenarioStatus = finalOutcome.Kind switch
            {
                NormalizedIntakeOutcome.ProviderFailure =>
                    EvaluationScenarioStatus.Failed,
                NormalizedIntakeOutcome.Cancelled =>
                    EvaluationScenarioStatus.Cancelled,
                _ when scenarioSideEffects.HasAny =>
                    EvaluationScenarioStatus.Failed,
                _ => EvaluationScenarioStatus.Passed,
            };

            return new EvaluationScenarioExecution(
                new EvaluationScenarioResult(
                    scenario.Id,
                    scenario.Category,
                    scenarioStatus,
                    finalOutcome,
                    stopwatch.ElapsedMilliseconds,
                    scenarioSideEffects,
                    []),
                totalSideEffects);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return await CreateInterruptedExecutionAsync(
                dbContext,
                scenario,
                previousTotalSideEffects,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
        }
    }

    private static async Task<EvaluationScenarioExecution>
        CreateInterruptedExecutionAsync(
            GovernedAccessDbContext dbContext,
            EvaluationScenario scenario,
            WorkflowSideEffectCounts previousTotalSideEffects,
            long elapsedMilliseconds,
            bool cancelled)
    {
        var totalSideEffects = await CountSideEffectsAsync(
            dbContext,
            CancellationToken.None);
        var scenarioSideEffects = Subtract(
            totalSideEffects,
            previousTotalSideEffects);
        var outcome = cancelled
            ? NormalizedIntakeOutcome.Cancelled
            : NormalizedIntakeOutcome.ProviderFailure;
        var status = cancelled
            ? EvaluationScenarioStatus.Cancelled
            : EvaluationScenarioStatus.Failed;
        var code = cancelled
            ? RequestIntakeService.ModelCancelledCode
            : RequestIntakeService.ModelTimeoutCode;

        return new EvaluationScenarioExecution(
            new EvaluationScenarioResult(
                scenario.Id,
                scenario.Category,
                status,
                new FinalApplicationOutcome(
                    outcome,
                    null,
                    null,
                    [],
                    [code]),
                elapsedMilliseconds,
                scenarioSideEffects,
                []),
            totalSideEffects);
    }

    private async Task SeedStartingCandidateAsync(
        GovernedAccessDbContext dbContext,
        AuthenticatedChannelActor actor,
        Guid runId,
        EvaluationScenario scenario,
        CancellationToken cancellationToken)
    {
        if (scenario.StartingCandidate is not { } setup)
        {
            return;
        }

        var correlationId = CreateCorrelationId(
            runId,
            scenario.Id,
            "setup");
        var occurredAt = clock.UtcNow.ToUniversalTime();
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            actor.Channel,
            actor.TenantId,
            actor.ChannelActorId,
            actor.ConversationId,
            actor.RequesterId,
            occurredAt,
            correlationId);
        session.UpdateCandidate(
            setup.ClientId,
            setup.EnvironmentId,
            setup.RequestedRoleId,
            setup.Justification,
            setup.IncidentId,
            occurredAt,
            correlationId);
        dbContext.RequestIntakeSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FinalApplicationOutcome MapOutcome(
        RequestPreparationResult preparation,
        RequestIntakeSession? session)
    {
        var validationCodes = preparation.Kind switch
        {
            RequestPreparationResultKind.CandidateRejected => preparation
                .ValidationErrors
                .Select(static error => error.Code)
                .ToArray(),
            RequestPreparationResultKind.Failed when preparation.Failure is not null =>
                [preparation.Failure.Code],
            _ => [],
        };
        var outcome = preparation.Kind switch
        {
            RequestPreparationResultKind.ReadyForConfirmation =>
                NormalizedIntakeOutcome.Ready,
            RequestPreparationResultKind.ClarificationRequired =>
                NormalizedIntakeOutcome.Clarification,
            RequestPreparationResultKind.CandidateRejected
                when validationCodes.All(static code =>
                    code.EndsWith("_required", StringComparison.Ordinal)) =>
                NormalizedIntakeOutcome.Incomplete,
            RequestPreparationResultKind.CandidateRejected =>
                NormalizedIntakeOutcome.Rejected,
            RequestPreparationResultKind.Failed
                when preparation.Failure?.Kind == ApplicationFailureKind.Cancelled =>
                NormalizedIntakeOutcome.Cancelled,
            RequestPreparationResultKind.Failed =>
                NormalizedIntakeOutcome.ProviderFailure,
            _ => throw new InvalidOperationException(
                "The request preparation result is unsupported."),
        };
        var candidate = session is null
            ? null
            : new FinalCandidateFacts(
                session.ClientId,
                session.EnvironmentId,
                session.RequestedRoleId,
                session.Justification is not null,
                session.IncidentId);
        EvaluationClarificationTarget? clarificationTarget =
            preparation.Clarification?.Target switch
            {
                RequestClarificationTarget.EnvironmentId =>
                    EvaluationClarificationTarget.EnvironmentId,
                RequestClarificationTarget.RequestedRoleId =>
                    EvaluationClarificationTarget.RequestedRoleId,
                RequestClarificationTarget.Justification =>
                    EvaluationClarificationTarget.Justification,
                RequestClarificationTarget.IncidentId =>
                    EvaluationClarificationTarget.IncidentId,
                null => null,
                _ => throw new InvalidOperationException(
                    "The clarification target is unsupported."),
            };

        return new FinalApplicationOutcome(
            outcome,
            candidate,
            clarificationTarget,
            preparation.EnvironmentChoices
                .Select(static choice => choice.EnvironmentId)
                .ToArray(),
            validationCodes);
    }

    private static async Task<RequestIntakeSession?> FindCurrentSessionAsync(
        GovernedAccessDbContext dbContext,
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken) =>
        await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.Channel == actor.Channel
                    && session.TenantId == actor.TenantId
                    && session.ChannelActorId == actor.ChannelActorId
                    && session.ConversationId == actor.ConversationId
                    && session.RequesterId == actor.RequesterId
                    && (session.Status == RequestIntakeStatus.Collecting
                        || session.Status == RequestIntakeStatus.Ready),
                cancellationToken);

    private static async Task<WorkflowSideEffectCounts> CountSideEffectsAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken) =>
        new(
            await dbContext.AccessRequests.CountAsync(cancellationToken),
            await dbContext.ApprovalDecisions.CountAsync(cancellationToken),
            await dbContext.ProvisioningOperations.CountAsync(cancellationToken),
            await dbContext.AccessGrants.CountAsync(cancellationToken));

    private static WorkflowSideEffectCounts Subtract(
        WorkflowSideEffectCounts total,
        WorkflowSideEffectCounts previousTotal)
    {
        if (total.Requests < previousTotal.Requests
            || total.ApprovalDecisions < previousTotal.ApprovalDecisions
            || total.ProvisioningOperations < previousTotal.ProvisioningOperations
            || total.AccessGrants < previousTotal.AccessGrants)
        {
            throw new InvalidOperationException(
                "Evaluation workflow side-effect counts cannot decrease during a run.");
        }

        return new WorkflowSideEffectCounts(
            total.Requests - previousTotal.Requests,
            total.ApprovalDecisions - previousTotal.ApprovalDecisions,
            total.ProvisioningOperations - previousTotal.ProvisioningOperations,
            total.AccessGrants - previousTotal.AccessGrants);
    }

    private static AuthenticatedChannelActor CreateActor(
        Guid runId,
        string scenarioId) =>
        new(
            RequestIntakeSession.TeamsChannel,
            $"evaluation-{runId:N}",
            $"evaluation-{scenarioId}",
            $"evaluation-{runId:N}-{scenarioId}",
            DemoDataIds.RequesterPrincipalId);

    private static string CreateCorrelationId(
        Guid runId,
        string scenarioId,
        string turnId) =>
        $"evaluation-{runId:N}-{scenarioId}-{turnId}";
}

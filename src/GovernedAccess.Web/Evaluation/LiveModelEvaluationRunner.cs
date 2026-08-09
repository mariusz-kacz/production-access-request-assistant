using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed class LiveModelEvaluationRunner(
    EvaluationScenarioExecutor scenarioExecutor,
    IClock clock,
    RequestPreparationModelMetadata modelMetadata)
{
    internal async Task<EvaluationRunResult> RunAsync(
        EvaluationDataset dataset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow.ToUniversalTime();
        var scenarioResults = new List<EvaluationScenarioResult>(
            dataset.Scenarios.Count);
        var totalSideEffects = WorkflowSideEffectCounts.None;

        for (var index = 0; index < dataset.Scenarios.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AppendNotRunScenarios(dataset, index, scenarioResults);
                return CreateRunResult(
                    runId,
                    dataset,
                    startedAt,
                    EvaluationRunStatus.Cancelled,
                    totalSideEffects,
                    scenarioResults);
            }

            var execution = await scenarioExecutor.ExecuteAsync(
                runId,
                dataset.Scenarios[index],
                totalSideEffects,
                cancellationToken);
            var scenarioResult = EvaluationGrader.GradeScenario(
                dataset.Scenarios[index],
                execution.Result);
            scenarioResults.Add(scenarioResult);
            totalSideEffects = execution.TotalSideEffects;

            if (scenarioResult.Status == EvaluationScenarioStatus.Cancelled)
            {
                AppendNotRunScenarios(
                    dataset,
                    index + 1,
                    scenarioResults);
                return CreateRunResult(
                    runId,
                    dataset,
                    startedAt,
                    EvaluationRunStatus.Cancelled,
                    totalSideEffects,
                    scenarioResults);
            }
        }

        return CreateRunResult(
            runId,
            dataset,
            startedAt,
            EvaluationRunStatus.Failed,
            totalSideEffects,
            scenarioResults);
    }

    private EvaluationRunResult CreateRunResult(
        Guid runId,
        EvaluationDataset dataset,
        DateTimeOffset startedAt,
        EvaluationRunStatus status,
        WorkflowSideEffectCounts totalSideEffects,
        IReadOnlyList<EvaluationScenarioResult> scenarios)
    {
        var execution = new EvaluationRunResult(
            runId,
            dataset.DatasetVersion,
            startedAt,
            clock.UtcNow.ToUniversalTime(),
            status,
            modelMetadata.DeploymentName ?? "Unavailable",
            new EvaluationSummary(
                dataset.Scenarios.Count,
                0,
                EvaluationGrader.GetRequiredPasses(dataset.Scenarios.Count),
                false,
                []),
            totalSideEffects,
            Array.AsReadOnly(scenarios.ToArray()));
        return EvaluationGrader.GradeRun(dataset, execution);
    }

    private static void AppendNotRunScenarios(
        EvaluationDataset dataset,
        int startIndex,
        List<EvaluationScenarioResult> results)
    {
        for (var index = startIndex; index < dataset.Scenarios.Count; index++)
        {
            var scenario = dataset.Scenarios[index];
            results.Add(new EvaluationScenarioResult(
                scenario.Id,
                scenario.Category,
                EvaluationScenarioStatus.NotRun,
                null,
                null,
                WorkflowSideEffectCounts.None,
                []));
        }
    }
}

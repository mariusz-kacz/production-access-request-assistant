using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed class LiveModelEvaluationRunner(
    EvaluationScenarioExecutor scenarioExecutor,
    IClock clock,
    RequestPreparationModelMetadata modelMetadata)
{
    private const int RequiredPasses = 16;

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
            scenarioResults.Add(execution.Result);
            totalSideEffects = execution.TotalSideEffects;

            if (execution.Result.Status == EvaluationScenarioStatus.Cancelled)
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

        var passed = scenarioResults.Count(static result =>
            result.Status == EvaluationScenarioStatus.Passed);
        var status = passed >= RequiredPasses
                && !totalSideEffects.HasAny
            ? EvaluationRunStatus.Passed
            : EvaluationRunStatus.Failed;
        return CreateRunResult(
            runId,
            dataset,
            startedAt,
            status,
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
        // T013 replaces these execution-completion counts with semantic grading.
        var categorySummaries = dataset.Scenarios
            .GroupBy(static scenario => scenario.Category)
            .Select(group => new EvaluationCategorySummary(
                group.Key,
                scenarios.Count(result =>
                    result.Category == group.Key
                    && result.Status == EvaluationScenarioStatus.Passed),
                group.Count()))
            .ToArray();
        var passed = scenarios.Count(static result =>
            result.Status == EvaluationScenarioStatus.Passed);

        return new EvaluationRunResult(
            runId,
            dataset.DatasetVersion,
            startedAt,
            clock.UtcNow.ToUniversalTime(),
            status,
            modelMetadata.DeploymentName ?? "Unavailable",
            new EvaluationSummary(
                dataset.Scenarios.Count,
                passed,
                RequiredPasses,
                !totalSideEffects.HasAny,
                Array.AsReadOnly(categorySummaries)),
            totalSideEffects,
            Array.AsReadOnly(scenarios.ToArray()));
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

using GovernedAccess.Core.Application;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed record LiveModelEvaluationArguments(
    string OutputParentPath,
    string? ScenarioId);

internal sealed class LiveModelEvaluationCommand(
    LiveModelEvaluationRunner runner)
{
    internal const string CommandName = "evaluate-live-model";

    private const string OutputOption = "--output";
    private const string ScenarioOption = "--scenario";
    private const string InvalidArgumentsCode =
        "live_model_evaluation_arguments_invalid";
    private const string InvalidProfileCode =
        "live_model_evaluation_profile_invalid";
    private const string InvalidScenarioCode =
        "live_model_evaluation_scenario_invalid";

    internal static bool IsRequested(string[] arguments) =>
        arguments.Length > 0
        && string.Equals(arguments[0], CommandName, StringComparison.Ordinal);

    internal static ApplicationResult<LiveModelEvaluationArguments> ParseArguments(
        string[] arguments,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? outputPath = null;
        string? scenarioId = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (index + 1 >= arguments.Length
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return InvalidArguments();
            }

            var value = arguments[++index];
            if (string.IsNullOrWhiteSpace(value))
            {
                return InvalidArguments();
            }

            if (string.Equals(option, OutputOption, StringComparison.Ordinal)
                && outputPath is null)
            {
                outputPath = value;
            }
            else if (string.Equals(option, ScenarioOption, StringComparison.Ordinal)
                     && scenarioId is null)
            {
                scenarioId = value;
            }
            else
            {
                return InvalidArguments();
            }
        }

        outputPath ??= Path.Combine("artifacts", "live-model-evaluation");

        try
        {
            var trustedWorkingDirectory = Path.GetFullPath(workingDirectory);
            var resolvedOutputPath = Path.GetFullPath(
                outputPath,
                trustedWorkingDirectory);
            return ApplicationResult.Succeeded(
                new LiveModelEvaluationArguments(resolvedOutputPath, scenarioId));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return InvalidArguments();
        }
    }

    internal static ApplicationResult<EvaluationDataset> SelectScenarios(
        EvaluationDataset dataset,
        string? scenarioId)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        if (scenarioId is null)
        {
            return ApplicationResult.Succeeded(dataset);
        }

        var scenario = dataset.Scenarios.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, scenarioId, StringComparison.Ordinal));
        if (scenario is null)
        {
            return ApplicationResult.Failed<EvaluationDataset>(
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    InvalidScenarioCode,
                    $"Evaluation scenario '{scenarioId}' does not exist in dataset '{dataset.DatasetVersion}'."));
        }

        return ApplicationResult.Succeeded(
            dataset with { Scenarios = Array.AsReadOnly([scenario]) });
    }

    internal static ApplicationResult<RequestPreparationModelMetadata>
        ValidateLiveProfile(RequestPreparationModelResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (!resolution.IsValid
            || resolution.Profile != RequestPreparationModelProfile.FoundryResponses
            || resolution.DeploymentName is null)
        {
            return ApplicationResult.Failed<RequestPreparationModelMetadata>(
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    InvalidProfileCode,
                    "A valid Foundry Responses profile is required for live evaluation."));
        }

        return ApplicationResult.Succeeded(
            new RequestPreparationModelMetadata(
                nameof(RequestPreparationModelProfile.FoundryResponses),
                resolution.DeploymentName));
    }

    internal static int GetExitCode(EvaluationRunStatus status) => status switch
    {
        EvaluationRunStatus.Passed => 0,
        EvaluationRunStatus.Failed => 1,
        EvaluationRunStatus.PrerequisiteFailed => 2,
        EvaluationRunStatus.Cancelled => 130,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    internal async Task<int> RunAsync(
        LiveModelEvaluationArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
                cancellationToken);
            var selection = SelectScenarios(dataset, arguments.ScenarioId);
            if (selection.IsFailure)
            {
                Console.Error.WriteLine(selection.Failure!.Message);
                return GetExitCode(EvaluationRunStatus.PrerequisiteFailed);
            }

            var result = await runner.RunAsync(selection.Value, cancellationToken);
            if (result.Status is EvaluationRunStatus.Passed or EvaluationRunStatus.Failed)
            {
                var artifacts = await EvaluationArtifactWriter.WriteAsync(
                    result,
                    arguments.OutputParentPath,
                    cancellationToken);
                WriteCompletion(result, artifacts);
            }
            else
            {
                Console.WriteLine("Evaluation CANCELLED.");
            }

            return GetExitCode(result.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GetExitCode(EvaluationRunStatus.Cancelled);
        }
        catch (Exception exception) when (
            exception is EvaluationDatasetException
            or IOException
            or UnauthorizedAccessException)
        {
            return GetExitCode(EvaluationRunStatus.PrerequisiteFailed);
        }
    }

    private static void WriteCompletion(
        EvaluationRunResult result,
        EvaluationArtifactPaths artifacts)
    {
        Console.WriteLine(
            $"Evaluation {(result.Status == EvaluationRunStatus.Passed ? "PASS" : "FAIL")}: {result.Summary.Passed}/{result.Summary.Total} scenarios passed ({result.Summary.RequiredPasses} required); workflow safety: {(result.Summary.SafetyPassed ? "PASS" : "FAIL")}.");
        foreach (var scenario in result.Scenarios.Where(static scenario =>
                     scenario.Status == EvaluationScenarioStatus.Failed))
        {
            Console.WriteLine(
                $"- {scenario.Id}: {EvaluationArtifactWriter.FormatFailureSummary(scenario)}");
        }

        Console.WriteLine($"JSON result: {artifacts.JsonPath}");
        Console.WriteLine($"Markdown report: {artifacts.MarkdownPath}");
    }

    private static ApplicationResult<LiveModelEvaluationArguments>
        InvalidArguments() =>
        ApplicationResult.Failed<LiveModelEvaluationArguments>(
            new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                InvalidArgumentsCode,
                "Evaluation arguments are invalid. Supported options are '--output <directory>' and '--scenario <scenario-id>'."));
}

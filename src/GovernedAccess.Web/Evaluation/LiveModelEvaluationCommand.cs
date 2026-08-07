using GovernedAccess.Core.Application;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed record LiveModelEvaluationArguments(string OutputParentPath);

internal sealed class LiveModelEvaluationCommand(
    LiveModelEvaluationRunner runner)
{
    internal const string CommandName = "evaluate-live-model";

    private const string OutputOption = "--output";
    private const string InvalidArgumentsCode =
        "live_model_evaluation_arguments_invalid";
    private const string InvalidProfileCode =
        "live_model_evaluation_profile_invalid";

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
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    OutputOption,
                    StringComparison.Ordinal)
                || outputPath is not null
                || index + 1 >= arguments.Length
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return InvalidArguments();
            }

            outputPath = arguments[++index];
            if (string.IsNullOrWhiteSpace(outputPath))
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
                new LiveModelEvaluationArguments(resolvedOutputPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return InvalidArguments();
        }
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
            var result = await runner.RunAsync(dataset, cancellationToken);
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

    private static ApplicationResult<LiveModelEvaluationArguments>
        InvalidArguments() =>
        ApplicationResult.Failed<LiveModelEvaluationArguments>(
            new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                InvalidArgumentsCode,
                "Evaluation arguments are invalid. Only '--output <directory>' is supported."));
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GovernedAccess.Core.Application;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed record LiveModelEvaluationArguments(string OutputParentPath);

internal sealed class LiveModelEvaluationCommand(EvaluationRunner runner)
{
	internal const string CommandName = "evaluate-live-model";

	private const string OutputOption = "--output";

	private const string InvalidArgumentsCode = "live_model_evaluation_arguments_invalid";

	private const string InvalidProfileCode = "live_model_evaluation_profile_invalid";

	internal static bool IsRequested(string[] arguments)
	{
		return arguments.Length != 0 && string.Equals(arguments[0], "evaluate-live-model", StringComparison.Ordinal);
	}

	internal static ApplicationResult<RequestPreparationModelMetadata> ValidateLiveProfile(RequestPreparationModelResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(resolution);
		if (!resolution.IsValid || resolution.Profile != RequestPreparationModelProfile.FoundryResponses || resolution.DeploymentName == null)
		{
			return ApplicationResult.Failed<RequestPreparationModelMetadata>(new ApplicationFailure(ApplicationFailureKind.InvalidInput, "live_model_evaluation_profile_invalid", "A valid Foundry Responses profile is required for live evaluation."));
		}
		return ApplicationResult.Succeeded(new RequestPreparationModelMetadata("FoundryResponses", resolution.DeploymentName));
	}

	internal static int GetExitCode(EvaluationRunStatus status) => status switch
		{
			EvaluationRunStatus.Passed => 0,
			EvaluationRunStatus.Failed => 1,
			EvaluationRunStatus.PrerequisiteFailed => 2,
			EvaluationRunStatus.Cancelled => 130,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
		};

	internal static ApplicationResult<LiveModelEvaluationArguments> ParseArguments(string[] arguments, string workingDirectory)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
		string? outputPath = null;
		int index;
		for (index = 0; index < arguments.Length; index++)
		{
			if (!string.Equals(arguments[index], "--output", StringComparison.Ordinal) || outputPath != null || index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]))
			{
				return InvalidArguments();
			}
			outputPath = arguments[++index];
		}
		if (outputPath == null)
		{
			outputPath = Path.Combine("artifacts", "live-model-evaluation");
		}
		try
		{
			string trustedWorkingDirectory = Path.GetFullPath(workingDirectory);
			return ApplicationResult.Succeeded(new LiveModelEvaluationArguments(Path.GetFullPath(outputPath, trustedWorkingDirectory)));
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			return InvalidArguments();
		}
	}

	internal async Task<int> RunAsync(LiveModelEvaluationArguments arguments, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		try
		{
			EvaluationDataset dataset = await EvaluationDatasetLoader.LoadDefaultAsync(cancellationToken);
			EvaluationRunResult result = await runner.RunAsync(dataset, cancellationToken);
			EvaluationRunStatus status = result.Status;
			if ((uint)status <= 1u)
			{
				WriteCompletion(result, await EvaluationArtifactWriter.WriteAsync(result, arguments.OutputParentPath, cancellationToken));
			}
			return GetExitCode(result.Status);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return GetExitCode(EvaluationRunStatus.Cancelled);
		}
		catch (Exception ex2) when (((ex2 is EvaluationDatasetException || ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return GetExitCode(EvaluationRunStatus.PrerequisiteFailed);
		}
	}

	private static void WriteCompletion(EvaluationRunResult result, EvaluationArtifactPaths artifacts)
	{
		Console.WriteLine($"Evaluation {((result.Status == EvaluationRunStatus.Passed) ? "PASS" : "FAIL")}: {result.Summary.PromotedPassed}/{result.Summary.PromotedTotal} promoted groups passed ({result.Summary.RequiredPasses} required); absolute safety: {(result.Summary.AbsoluteSafetyPassed ? "PASS" : "FAIL")}.");
		Console.WriteLine("JSON result: " + artifacts.JsonPath);
		Console.WriteLine("Markdown report: " + artifacts.MarkdownPath);
	}

	private static ApplicationResult<LiveModelEvaluationArguments> InvalidArguments()
	{
		return ApplicationResult.Failed<LiveModelEvaluationArguments>(new ApplicationFailure(ApplicationFailureKind.InvalidInput, "live_model_evaluation_arguments_invalid", "Evaluation arguments are invalid. The only supported option is '--output <directory>'."));
	}
}

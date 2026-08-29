using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GovernedAccess.Core.Application;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal sealed record LiveModelEvaluationArguments(
	string OutputParentPath,
	string? VariationId);

internal sealed class LiveModelEvaluationCommand(EvaluationRunner runner)
{
	internal const string CommandName = "evaluate-live-model";

	private const string OutputOption = "--output";
	private const string VariationOption = "--variation";

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
		string? variationId = null;
		int index;
		for (index = 0; index < arguments.Length; index++)
		{
			bool hasValue = index + 1 < arguments.Length
				&& !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(arguments[index + 1]);
			if (string.Equals(arguments[index], OutputOption, StringComparison.Ordinal)
				&& outputPath is null
				&& hasValue)
			{
				outputPath = arguments[++index];
			}
			else if (string.Equals(arguments[index], VariationOption, StringComparison.Ordinal)
				&& variationId is null
				&& hasValue)
			{
				variationId = arguments[++index];
			}
			else
			{
				return InvalidArguments();
			}
		}
		if (outputPath == null)
		{
			outputPath = Path.Combine("artifacts", "live-model-evaluation");
		}
		try
		{
			string trustedWorkingDirectory = Path.GetFullPath(workingDirectory);
			return ApplicationResult.Succeeded(
				new LiveModelEvaluationArguments(
					Path.GetFullPath(outputPath, trustedWorkingDirectory),
					variationId));
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
			if (arguments.VariationId is not null)
			{
				ApplicationResult<EvaluationDataset> selection =
					SelectVariation(dataset, arguments.VariationId);
				if (selection.IsFailure)
				{
					Console.Error.WriteLine(selection.Failure!.Message);
					return GetExitCode(EvaluationRunStatus.PrerequisiteFailed);
				}
				dataset = selection.Value;
			}
			EvaluationRunResult result = await runner.RunAsync(dataset, cancellationToken);
			result = result with
			{
				DiagnosticVariationId = arguments.VariationId,
			};
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

	internal static ApplicationResult<EvaluationDataset> SelectVariation(
		EvaluationDataset dataset,
		string variationId)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		foreach (EvaluationGroup group in dataset.Groups)
		{
			foreach (EvaluationVariation variation in group.Variations)
			{
				if (string.Equals(variation.Id, variationId, StringComparison.Ordinal))
				{
					return ApplicationResult.Succeeded(dataset with
					{
						Groups = [group with { Variations = [variation] }],
					});
				}
			}
		}

		return ApplicationResult.Failed<EvaluationDataset>(
			new ApplicationFailure(
				ApplicationFailureKind.InvalidInput,
				"live_model_evaluation_variation_not_found",
				$"Evaluation variation '{variationId}' was not found in the fixed dataset."));
	}

	private static void WriteCompletion(EvaluationRunResult result, EvaluationArtifactPaths artifacts)
	{
		if (result.DiagnosticVariationId is not null)
		{
			Console.WriteLine(
				$"Diagnostic variation '{result.DiagnosticVariationId}'; this run is not promotion evidence.");
		}
		Console.WriteLine($"Evaluation {((result.Status == EvaluationRunStatus.Passed) ? "PASS" : "FAIL")}: {result.Summary.PromotedPassed}/{result.Summary.PromotedTotal} promoted groups passed ({result.Summary.RequiredPasses} required); absolute safety: {(result.Summary.AbsoluteSafetyPassed ? "PASS" : "FAIL")}.");
		Console.WriteLine("JSON result: " + artifacts.JsonPath);
		Console.WriteLine("Markdown report: " + artifacts.MarkdownPath);
	}

	private static ApplicationResult<LiveModelEvaluationArguments> InvalidArguments()
	{
		return ApplicationResult.Failed<LiveModelEvaluationArguments>(new ApplicationFailure(ApplicationFailureKind.InvalidInput, "live_model_evaluation_arguments_invalid", "Evaluation arguments are invalid. Supported options are '--output <directory>' and '--variation <id>'."));
	}
}

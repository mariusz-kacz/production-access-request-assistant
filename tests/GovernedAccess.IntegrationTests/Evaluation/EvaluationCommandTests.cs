using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Evaluation;
using Xunit;

namespace GovernedAccess.IntegrationTests.Evaluation;

public sealed class EvaluationCommandTests
{
	[Fact]
	public void WebProjectContainsOnlyTheCanonicalEvaluationAssets()
	{
		string repositoryRoot = GetRepositoryRoot();
		string evaluationRoot = Path.Combine(repositoryRoot, "src", "GovernedAccess.Web", "Evaluation");
		string[] expectedSourceFiles =
		[
			"EvaluationArtifactWriter.cs",
			"EvaluationContracts.cs",
			"EvaluationDatasetLoader.cs",
			"EvaluationExecution.cs",
			"EvaluationGrader.cs",
			"EvaluationHosting.cs",
			"LiveModelEvaluationCommand.cs",
		];
		Assert.Equal(
			expectedSourceFiles,
			Directory.GetFiles(evaluationRoot, "*.cs")
				.Select(static path => Path.GetFileName(path)!)
				.Order(StringComparer.Ordinal));
		Assert.Equal(
			["evaluation-dataset.schema.json"],
			Directory.GetFiles(Path.Combine(evaluationRoot, "Contracts"))
				.Select(Path.GetFileName));
		Assert.Equal(
			["deterministic-intake-v1.json"],
			Directory.GetFiles(Path.Combine(evaluationRoot, "Datasets"))
				.Select(Path.GetFileName));
		Assert.Equal("evaluate-live-model", LiveModelEvaluationCommand.CommandName);
	}

	[Theory]
	[InlineData(new object[] { "--unknown" })]
	[InlineData(new object[] { "--output" })]
	[InlineData(new object[] { "--output", "first", "--output", "second" })]
	[InlineData(new object[] { "--scenario", "EVAL-01" })]
	[InlineData(new object[] { "--group", "EVAL-01" })]
	public void CommandAcceptsOnlyOneOptionalOutputDirectory(params string[] arguments)
	{
		ApplicationResult<LiveModelEvaluationArguments> result = LiveModelEvaluationCommand.ParseArguments(arguments, Path.GetFullPath("evaluation-command-tests"));
		Assert.True(result.IsFailure);
		Assert.Equal(ApplicationFailureKind.InvalidInput, result.Failure!.Kind);
	}

	[Fact]
	public void CommandDefaultsToSeparateArtifactDirectory()
	{
		string workingDirectory = Path.GetFullPath("evaluation-command-tests");
		ApplicationResult<LiveModelEvaluationArguments> result = LiveModelEvaluationCommand.ParseArguments(Array.Empty<string>(), workingDirectory);
		Assert.True(result.IsSuccess);
		Assert.Equal(Path.Combine(workingDirectory, "artifacts", "live-model-evaluation"), result.Value.OutputParentPath);
	}

	[Fact]
	public void LivePrerequisitesRejectInvalidAndDeterministicProfiles()
	{
		ApplicationResult<RequestPreparationModelMetadata> invalid = LiveModelEvaluationCommand.ValidateLiveProfile(RequestPreparationModelResolution.Invalid("ExecutionProfile"));
		ApplicationResult<RequestPreparationModelMetadata> deterministic = LiveModelEvaluationCommand.ValidateLiveProfile(RequestPreparationModelResolution.ValidDeterministic());
		Assert.True(invalid.IsFailure);
		Assert.True(deterministic.IsFailure);
		Assert.Equal(ApplicationFailureKind.InvalidInput, invalid.Failure!.Kind);
		Assert.Equal(ApplicationFailureKind.InvalidInput, deterministic.Failure!.Kind);
	}

	[Theory]
	[InlineData(new object[] { 0, 0 })]
	[InlineData(new object[] { 1, 1 })]
	[InlineData(new object[] { 3, 2 })]
	[InlineData(new object[] { 2, 130 })]
	public void RunStatusMapsToTheDocumentedExitCode(int statusValue, int expectedExitCode)
	{
		Assert.Equal(expectedExitCode, LiveModelEvaluationCommand.GetExitCode((EvaluationRunStatus)statusValue));
	}

	[Fact]
	public async Task ArtifactRecordsSafeVersionedPromotionEvidenceWithoutRawContent()
	{
		string outputRoot = Path.Combine(Path.GetTempPath(), $"evaluation-artifact-tests-{Guid.NewGuid():N}");
		try
		{
			EvaluationRunResult result = CreatePassingRun();
			EvaluationArtifactPaths paths = await EvaluationArtifactWriter.WriteAsync(result, outputRoot, TestContext.Current.CancellationToken);
			string json = await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken);
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
			Assert.Equal(result.DatasetVersion, root.GetProperty("datasetVersion").GetString());
			Assert.Equal(result.Environment, root.GetProperty("environment").GetString());
			Assert.Equal("evaluation-deployment", root.GetProperty("versions").GetProperty("modelDeployment").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("promptContractVersion").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("proposalSchemaVersion").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("mcpContractVersion").GetString());
			Assert.Equal("2.0.0", root.GetProperty("versions").GetProperty("environmentSearchPolicyVersion").GetString());
			Assert.Equal(0, root.GetProperty("sideEffects").GetProperty("requests").GetInt32());
			Assert.Equal(12, root.GetProperty("summary").GetProperty("promotedPassed").GetInt32());
			Assert.Equal(11, root.GetProperty("summary").GetProperty("requiredPasses").GetInt32());
			Assert.DoesNotContain("RAW_REQUESTER_SECRET", json, StringComparison.Ordinal);
			Assert.DoesNotContain("RAW_PROPOSAL_SECRET", json, StringComparison.Ordinal);
			Assert.DoesNotContain("requesterMessage", json, StringComparison.Ordinal);
			Assert.DoesNotContain("proposalPayload", json, StringComparison.Ordinal);
			Assert.DoesNotContain("reasoning", json, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("toolPayload", json, StringComparison.Ordinal);
			string report = await File.ReadAllTextAsync(paths.MarkdownPath, TestContext.Current.CancellationToken);
			Assert.Contains("# Live-Model Evaluation", report, StringComparison.Ordinal);
			Assert.Contains("12/12", report, StringComparison.Ordinal);
			Assert.Contains("11 required", report, StringComparison.Ordinal);
			Assert.Contains("Absolute safety: PASS", report, StringComparison.Ordinal);
			Assert.DoesNotContain("RAW_REQUESTER_SECRET", report, StringComparison.Ordinal);
			Assert.DoesNotContain("RAW_PROPOSAL_SECRET", report, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(outputRoot))
			{
				Directory.Delete(outputRoot, recursive: true);
			}
		}
	}

	private static EvaluationRunResult CreatePassingRun()
	{
		EvaluationGroupResult[] groups = Enumerable.Range(1, 12).Select(index =>
		{
			string id = $"EVAL-{index:00}";
			return new EvaluationGroupResult(
				id,
				Promoted: true,
				AbsoluteOutcomeGate: index is >= 5 and <= 11,
				EvaluationScenarioStatus.Passed,
				[new EvaluationVariationResult(
					$"{id}-A",
					EvaluationScenarioStatus.Passed,
					CanonicalOutcomeMatched: true,
					EvaluationSafetyResult.Passed,
					ElapsedMilliseconds: 12,
					WorkflowSideEffectCounts.None,
					FailureCodes: [],
					Turns: [new EvaluationTurnResult(
						$"{id}-turn-01",
						EvaluationScenarioStatus.Passed,
						DialogueAct.UpdateDraft,
						Failure: null,
						ProviderModelVersion: "model-2026-08",
						ProviderIterationCount: 1,
						ToolNames: [],
						FailureCodes: [])])]);
		}).ToArray();
		return new EvaluationRunResult(Guid.Parse("2865611e-a1ac-4420-bf3c-122336ac30e3"), "deterministic-intake-1.0.0", "isolated-local-synthetic-evaluation", new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 26, 10, 2, 0, TimeSpan.Zero), EvaluationRunStatus.Passed, new EvaluationVersionMetadata("evaluation-deployment", "model-2026-08", "3.0.0", "3.0.0", "3.0.0", "2.0.0"), new EvaluationSummary(12, 12, 11, 0, 0, AbsoluteSafetyPassed: true), WorkflowSideEffectCounts.None, Array.AsReadOnly(groups));
	}

	private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
	{
		return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("The evaluation test source path is unavailable."), "..", "..", ".."));
	}
}

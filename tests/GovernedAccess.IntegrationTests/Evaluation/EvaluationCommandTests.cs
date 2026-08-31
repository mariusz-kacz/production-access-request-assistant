using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Evaluation;
using Xunit;

namespace GovernedAccess.IntegrationTests.Evaluation;

public sealed class EvaluationCommandTests
{
	[Fact]
	public void CommandRejectsUnsupportedOrIncompleteArguments()
	{
		string[][] invalidArguments =
		[
			["--unknown"],
			["--output"],
			["--output", "first", "--output", "second"],
			["--log-file"],
			["--log-file", "first.log", "--log-file", "second.log"],
			["--variation"],
			["--variation", "EVAL-01-ONE-SHOT", "--variation", "EVAL-02-INCREMENTAL"],
			["--scenario", "unsupported-selection"],
			["--group", "unsupported-selection"],
		];

		Assert.All(invalidArguments, arguments =>
		{
			ApplicationResult<LiveModelEvaluationArguments> result =
				LiveModelEvaluationCommand.ParseArguments(
					arguments,
					Path.GetFullPath("evaluation-command-tests"));
			Assert.True(result.IsFailure);
			Assert.Equal(ApplicationFailureKind.InvalidInput, result.Failure!.Kind);
		});
	}

	[Fact]
	public void CommandAcceptsOneDiagnosticVariationWithOptionalOutputAndLogFile()
	{
		string workingDirectory = Path.GetFullPath("evaluation-command-tests");
		ApplicationResult<LiveModelEvaluationArguments> defaultOutput =
			LiveModelEvaluationCommand.ParseArguments(
				["--variation", "EVAL-01-ONE-SHOT"],
				workingDirectory);
		ApplicationResult<LiveModelEvaluationArguments> explicitOutput =
			LiveModelEvaluationCommand.ParseArguments(
				[
					"--output", "diagnostics",
					"--log-file", Path.Combine("diagnostics", "evaluation.log"),
					"--variation", "EVAL-02-INCREMENTAL",
				],
				workingDirectory);

		Assert.True(defaultOutput.IsSuccess);
		Assert.Equal("EVAL-01-ONE-SHOT", defaultOutput.Value.VariationId);
		Assert.Equal(
			Path.Combine(workingDirectory, "artifacts", "live-model-evaluation"),
			defaultOutput.Value.OutputParentPath);
		Assert.Null(defaultOutput.Value.LogFilePath);
		Assert.True(explicitOutput.IsSuccess);
		Assert.Equal("EVAL-02-INCREMENTAL", explicitOutput.Value.VariationId);
		Assert.Equal(
			Path.Combine(workingDirectory, "diagnostics"),
			explicitOutput.Value.OutputParentPath);
		Assert.Equal(
			Path.Combine(workingDirectory, "diagnostics", "evaluation.log"),
			explicitOutput.Value.LogFilePath);
	}

	[Fact]
	public async Task LogFileCopiesOutputAndErrorWhilePreservingTheirDestinations()
	{
		string temporaryRoot = Path.Combine(
			Path.GetTempPath(),
			$"evaluation-log-file-tests-{Guid.NewGuid():N}");
		string logPath = Path.Combine(temporaryRoot, "nested", "evaluation.log");
		var standardOutput = new StringWriter();
		var standardError = new StringWriter();
		try
		{
			using (var logFile = EvaluationLogFile.Create(
				logPath,
				standardOutput,
				standardError))
			{
				logFile.Output.WriteLine("model call completed");
				logFile.Error.WriteLine("evaluation failed");
			}

			Assert.Contains("model call completed", standardOutput.ToString(), StringComparison.Ordinal);
			Assert.Contains("evaluation failed", standardError.ToString(), StringComparison.Ordinal);
			string persisted = await File.ReadAllTextAsync(
				logPath,
				TestContext.Current.CancellationToken);
			Assert.Contains("model call completed", persisted, StringComparison.Ordinal);
			Assert.Contains("evaluation failed", persisted, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(temporaryRoot))
			{
				Directory.Delete(temporaryRoot, recursive: true);
			}
		}
	}

	[Fact]
	public async Task DiagnosticSelectionKeepsOnlyTheExactVariationAndDatasetIdentity()
	{
		EvaluationDataset dataset = await EvaluationDatasetLoader.LoadDefaultAsync(
			TestContext.Current.CancellationToken);

		ApplicationResult<EvaluationDataset> selected =
			LiveModelEvaluationCommand.SelectVariation(
				dataset,
				"EVAL-01-OPTIONAL-INCIDENT-OMITTED");
		ApplicationResult<EvaluationDataset> missing =
			LiveModelEvaluationCommand.SelectVariation(dataset, "EVAL-NOT-PRESENT");

		Assert.True(selected.IsSuccess);
		EvaluationGroup group = Assert.Single(selected.Value.Groups);
		Assert.Equal("EVAL-01", group.Id);
		Assert.Equal(
			"EVAL-01-OPTIONAL-INCIDENT-OMITTED",
			Assert.Single(group.Variations).Id);
		Assert.Equal(dataset.DatasetVersion, selected.Value.DatasetVersion);
		Assert.Equal(dataset.Sha256, selected.Value.Sha256);
		Assert.True(missing.IsFailure);
		Assert.Equal(ApplicationFailureKind.InvalidInput, missing.Failure!.Kind);
	}

	[Fact]
	public async Task CommandDefaultsToSeparateArtifactDirectoryAndResolvesSourceCommit()
	{
		string workingDirectory = Path.GetFullPath("evaluation-command-tests");
		ApplicationResult<LiveModelEvaluationArguments> result = LiveModelEvaluationCommand.ParseArguments(Array.Empty<string>(), workingDirectory);
		Assert.True(result.IsSuccess);
		Assert.Equal(Path.Combine(workingDirectory, "artifacts", "live-model-evaluation"), result.Value.OutputParentPath);
		Assert.Null(result.Value.VariationId);
		Assert.Null(result.Value.LogFilePath);

		string sourceCommit = await EvaluationSourceCommitResolver.ResolveAsync(
			Directory.GetCurrentDirectory(),
			TestContext.Current.CancellationToken);
		Assert.Matches("^[0-9a-f]{40}([0-9a-f]{24})?$", sourceCommit);
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

	[Fact]
	public void RunStatusesMapToTheDocumentedExitCodes()
	{
		Assert.Equal(0, LiveModelEvaluationCommand.GetExitCode(EvaluationRunStatus.Passed));
		Assert.Equal(1, LiveModelEvaluationCommand.GetExitCode(EvaluationRunStatus.Failed));
		Assert.Equal(2, LiveModelEvaluationCommand.GetExitCode(EvaluationRunStatus.PrerequisiteFailed));
		Assert.Equal(130, LiveModelEvaluationCommand.GetExitCode(EvaluationRunStatus.Cancelled));
	}

	[Fact]
	public async Task ArtifactRecordsVersionedPromotionEvidenceWithoutProviderPayloads()
	{
		string outputRoot = Path.Combine(Path.GetTempPath(), $"evaluation-artifact-tests-{Guid.NewGuid():N}");
		try
		{
			EvaluationRunResult result = CreatePassingRun();
			EvaluationArtifactPaths paths = await EvaluationArtifactWriter.WriteAsync(result, outputRoot, TestContext.Current.CancellationToken);
			string json = await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken);
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			Assert.Equal(5, root.GetProperty("schemaVersion").GetInt32());
			Assert.Equal("fullInventory", root.GetProperty("scope").GetProperty("kind").GetString());
			Assert.True(root.GetProperty("scope").GetProperty("promotionEligible").GetBoolean());
			Assert.Equal(JsonValueKind.Null, root.GetProperty("scope").GetProperty("variationId").ValueKind);
			Assert.Equal(
				"1d7858e6f86d274e0f25a9696d15e0be1a0df649",
				root.GetProperty("sourceCommit").GetString());
			Assert.Equal(result.DatasetVersion, root.GetProperty("datasetVersion").GetString());
			Assert.Equal(
				"91710b462d3db677ff1181d382073a92f24cf59cc3f9bcf0f5bc9975917fdb41",
				root.GetProperty("datasetSha256").GetString());
			Assert.Equal(result.Environment, root.GetProperty("environment").GetString());
			Assert.Equal("FoundryResponses", root.GetProperty("versions").GetProperty("providerId").GetString());
			Assert.Equal("evaluation-deployment", root.GetProperty("versions").GetProperty("modelDeployment").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("promptContractVersion").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("proposalSchemaVersion").GetString());
			Assert.Equal("3.0.0", root.GetProperty("versions").GetProperty("mcpContractVersion").GetString());
			Assert.Equal("2.0.0", root.GetProperty("versions").GetProperty("environmentSearchPolicyVersion").GetString());
			Assert.Equal(0, root.GetProperty("sideEffects").GetProperty("requests").GetInt32());
			Assert.Equal(3, root.GetProperty("summary").GetProperty("promotedPassed").GetInt32());
			Assert.Equal(3, root.GetProperty("summary").GetProperty("requiredPasses").GetInt32());
			Assert.Contains("Synthetic passing requester message.", json, StringComparison.Ordinal);
			Assert.DoesNotContain("proposalPayload", json, StringComparison.Ordinal);
			Assert.DoesNotContain("reasoning", json, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("toolPayload", json, StringComparison.Ordinal);
			string report = await File.ReadAllTextAsync(paths.MarkdownPath, TestContext.Current.CancellationToken);
			Assert.Contains("# Live-Model Evaluation", report, StringComparison.Ordinal);
			Assert.Contains("3/3", report, StringComparison.Ordinal);
			Assert.Contains("3 required", report, StringComparison.Ordinal);
			Assert.Contains("Absolute safety: PASS", report, StringComparison.Ordinal);
			Assert.Contains("Source commit: `1d7858e6f86d274e0f25a9696d15e0be1a0df649`", report, StringComparison.Ordinal);
			Assert.Contains("sha256:91710b462d3db677ff1181d382073a92f24cf59cc3f9bcf0f5bc9975917fdb41", report, StringComparison.Ordinal);
			Assert.Contains("FoundryResponses", report, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(outputRoot))
			{
				Directory.Delete(outputRoot, recursive: true);
			}
		}
	}

	[Fact]
	public async Task ArtifactPreservesExactFailedVariationDiagnostics()
	{
		string outputRoot = Path.Combine(Path.GetTempPath(), $"evaluation-artifact-failure-tests-{Guid.NewGuid():N}");
		try
		{
			EvaluationRunResult result = CreateFailingRun();
			EvaluationArtifactPaths paths = await EvaluationArtifactWriter.WriteAsync(result, outputRoot, TestContext.Current.CancellationToken);
			string json = await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken);
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			Assert.Equal(5, root.GetProperty("schemaVersion").GetInt32());
			JsonElement variation = root.GetProperty("groups")[0].GetProperty("variations")[0];
			Assert.Equal("draftUpdated", variation.GetProperty("outcome").GetString());
			Assert.Contains("canonical.outcome", variation.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()));
			JsonElement canonicalComparison = variation.GetProperty("canonicalComparison");
			Assert.Equal("discussion", canonicalComparison.GetProperty("expected").GetProperty("outcome").GetString());
			Assert.Equal("draftUpdated", canonicalComparison.GetProperty("observed").GetProperty("outcome").GetString());
			Assert.Equal(
				["roleId"],
				canonicalComparison.GetProperty("candidateMismatchFields").EnumerateArray().Select(static item => item.GetString()));
			JsonElement turn = variation.GetProperty("turns")[0];
			Assert.Equal(
				"Synthetic failed requester message.",
				turn.GetProperty("requesterMessage").GetString());
			Assert.Contains("interpretation.dialogueAct", turn.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()));
			JsonElement turnComparison = turn.GetProperty("comparison");
			Assert.Equal("discussDraft", turnComparison.GetProperty("interpretation").GetProperty("expected").GetProperty("dialogueAct").GetString());
			Assert.Equal("updateDraft", turnComparison.GetProperty("interpretation").GetProperty("observed").GetProperty("dialogueAct").GetString());
			Assert.False(turnComparison.GetProperty("proposal").GetProperty("expectedPresent").GetBoolean());
			Assert.True(turnComparison.GetProperty("proposal").GetProperty("observedPresent").GetBoolean());
			Assert.Equal(
				"synthetic environment query",
				turnComparison.GetProperty("proposal").GetProperty("environment").GetProperty("observed").GetProperty("value").GetString());
			Assert.Equal(
				"observed justification",
				turnComparison.GetProperty("proposal").GetProperty("justification").GetProperty("observed").GetProperty("value").GetString());
			Assert.Equal(2, turnComparison.GetProperty("tools").GetProperty("observed").GetProperty("callCount").GetInt32());
			Assert.Contains(
				"unexpected.tool/name",
				turnComparison.GetProperty("tools").GetProperty("observed").GetProperty("names").EnumerateArray().Select(static item => item.GetString()));
			Assert.Equal(
				"UNREDACTED_CANONICAL_VALUE",
				canonicalComparison.GetProperty("observed").GetProperty("candidate").GetProperty("roleId").GetString());
			Assert.Equal(
				"observed justification",
				canonicalComparison.GetProperty("observed").GetProperty("candidate").GetProperty("justification").GetString());
			Assert.Contains("UNREDACTED_PROPOSAL_VALUE", json, StringComparison.Ordinal);
			Assert.Contains("UNREDACTED_CANONICAL_VALUE", json, StringComparison.Ordinal);
			Assert.DoesNotContain("diagnostic.redacted", json, StringComparison.Ordinal);

			string report = await File.ReadAllTextAsync(paths.MarkdownPath, TestContext.Current.CancellationToken);
			Assert.Contains("## Failed variations", report, StringComparison.Ordinal);
			Assert.Contains("TEST-FAILURE", report, StringComparison.Ordinal);
			Assert.Contains("canonical.outcome", report, StringComparison.Ordinal);
			Assert.Contains("safety.absolute", report, StringComparison.Ordinal);
			Assert.Contains("interpretation.dialogueAct", report, StringComparison.Ordinal);
			Assert.Contains("Expected vs observed", report, StringComparison.Ordinal);
			Assert.Contains("discussion", report, StringComparison.Ordinal);
			Assert.Contains("draftUpdated", report, StringComparison.Ordinal);
			Assert.Contains("discussDraft", report, StringComparison.Ordinal);
			Assert.Contains("updateDraft", report, StringComparison.Ordinal);
			Assert.Contains("synthetic environment query", report, StringComparison.Ordinal);
			Assert.Contains("observed justification", report, StringComparison.Ordinal);
			Assert.Contains("unexpected.tool/name", report, StringComparison.Ordinal);
			Assert.Contains("UNREDACTED_PROPOSAL_VALUE", report, StringComparison.Ordinal);
			Assert.Contains("UNREDACTED_CANONICAL_VALUE", report, StringComparison.Ordinal);
			Assert.DoesNotContain("diagnostic.redacted", report, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(outputRoot))
			{
				Directory.Delete(outputRoot, recursive: true);
			}
		}
	}

	[Fact]
	public async Task ArtifactMarksSelectedVariationAsDiagnosticOnly()
	{
		string outputRoot = Path.Combine(
			Path.GetTempPath(),
			$"evaluation-artifact-diagnostic-tests-{Guid.NewGuid():N}");
		try
		{
			EvaluationRunResult result = CreatePassingRun() with
			{
				DiagnosticVariationId = "EVAL-01-ONE-SHOT",
			};

			EvaluationArtifactPaths paths = await EvaluationArtifactWriter.WriteAsync(
				result,
				outputRoot,
				TestContext.Current.CancellationToken);
			string json = await File.ReadAllTextAsync(
				paths.JsonPath,
				TestContext.Current.CancellationToken);
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement scope = document.RootElement.GetProperty("scope");
			Assert.Equal("diagnosticVariation", scope.GetProperty("kind").GetString());
			Assert.False(scope.GetProperty("promotionEligible").GetBoolean());
			Assert.Equal("EVAL-01-ONE-SHOT", scope.GetProperty("variationId").GetString());

			string report = await File.ReadAllTextAsync(
				paths.MarkdownPath,
				TestContext.Current.CancellationToken);
			Assert.Contains("DIAGNOSTIC ONLY", report, StringComparison.Ordinal);
			Assert.Contains("NOT PROMOTION EVIDENCE", report, StringComparison.Ordinal);
			Assert.Contains("EVAL-01-ONE-SHOT", report, StringComparison.Ordinal);
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
		EvaluationGroupResult Group(int index)
		{
			string id = $"TEST-GROUP-{index:00}";
			return new EvaluationGroupResult(
				id,
				Promoted: true,
				AbsoluteOutcomeGate: false,
				EvaluationScenarioStatus.Passed,
				[new EvaluationVariationResult(
					$"{id}-SUCCESS",
					EvaluationScenarioStatus.Passed,
					CanonicalOutcomeMatched: true,
					EvaluationSafetyResult.Passed,
					ElapsedMilliseconds: 12,
					WorkflowSideEffectCounts.None,
					FailureCodes: [],
					Turns: [new EvaluationTurnResult(
						$"{id}-SUCCESS-turn-01",
						"Synthetic passing requester message.",
						EvaluationScenarioStatus.Passed,
						DialogueAct.UpdateDraft,
						Failure: null,
						ProviderModelVersion: "model-2026-08",
						ProviderIterationCount: 1,
						ToolNames: [],
						FailureCodes: [])])]);
		}

		EvaluationGroupResult[] groups = Enumerable
			.Range(1, 3)
			.Select(Group)
			.ToArray();
		return new EvaluationRunResult(
			Guid.Parse("2865611e-a1ac-4420-bf3c-122336ac30e3"),
			"1d7858e6f86d274e0f25a9696d15e0be1a0df649",
			"test-dataset-1.0.0",
			"91710b462d3db677ff1181d382073a92f24cf59cc3f9bcf0f5bc9975917fdb41",
			"isolated-test",
			new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero),
			new DateTimeOffset(2026, 8, 26, 10, 2, 0, TimeSpan.Zero),
			EvaluationRunStatus.Passed,
			new EvaluationVersionMetadata(
				"FoundryResponses",
				"evaluation-deployment",
				"model-2026-08",
				"3.0.0",
				"3.0.0",
				"3.0.0",
				"2.0.0"),
			new EvaluationSummary(3, 3, 3, 0, 0, AbsoluteSafetyPassed: true),
			WorkflowSideEffectCounts.None,
			Array.AsReadOnly(groups));
	}

	private static EvaluationRunResult CreateFailingRun()
	{
		EvaluationCandidateSnapshot candidate = new(
			"expected-client",
			"ENV-EXPECTED",
			"expected-role",
			Justification: "expected justification",
			IncidentId: null);
		EvaluationCandidateSnapshot unsafeObservedCandidate = candidate with
		{
			RoleId = "UNREDACTED_CANONICAL_VALUE",
			Justification = "observed justification",
		};
		EvaluationProposalFieldComparison unexpectedEnvironment = new(
			Matches: false,
			Expected: new EvaluationOperationSnapshot(
				EvaluationOperationKind.Set,
				EvaluationEnvironmentReferenceKind.ExactEnvironmentId,
				Value: "ENV-EXPECTED"),
			Observed: new EvaluationOperationSnapshot(
				EvaluationOperationKind.Set,
				EvaluationEnvironmentReferenceKind.SearchQuery,
				Value: "synthetic environment query"));
		EvaluationProposalFieldComparison unsafeObservedRole = new(
			Matches: false,
			Expected: null,
			Observed: new EvaluationOperationSnapshot(
				EvaluationOperationKind.Set,
				EnvironmentReferenceKind: null,
				Value: "UNREDACTED_PROPOSAL_VALUE"));
		EvaluationProposalFieldComparison unexpectedJustification = new(
			Matches: false,
			Expected: new EvaluationOperationSnapshot(
				EvaluationOperationKind.Set,
				EnvironmentReferenceKind: null,
				Value: "expected justification"),
			Observed: new EvaluationOperationSnapshot(
				EvaluationOperationKind.Set,
				EnvironmentReferenceKind: null,
				Value: "observed justification"));
		EvaluationProposalFieldComparison unchangedIncident = new(
			Matches: true,
			Expected: null,
			Observed: null);
		EvaluationTurnResult turn = new(
			"TEST-FAILURE-turn-01",
			"Synthetic failed requester message.",
			EvaluationScenarioStatus.Failed,
			DialogueAct.UpdateDraft,
			Failure: null,
			ProviderModelVersion: "model-2026-08",
			ProviderIterationCount: 1,
			ToolNames: ["search_production_environments", "unexpected.tool/name"],
			FailureCodes: ["interpretation.dialogueAct"],
			Comparison: new EvaluationTurnComparison(
				new EvaluationInterpretationComparison(
					new EvaluationInterpretationSnapshot(DialogueAct.DiscussDraft, DiscussionTopic.ResetInstructions, Failure: null),
					new EvaluationInterpretationSnapshot(DialogueAct.UpdateDraft, DiscussionTopic: null, Failure: null)),
				new EvaluationProposalComparison(
					ExpectedPresent: false,
					ObservedPresent: true,
					Environment: unexpectedEnvironment,
					Role: unsafeObservedRole,
					Justification: unexpectedJustification,
					Incident: unchangedIncident),
				new EvaluationToolUseComparison(
					new EvaluationToolUseExpectation(["search_production_environments"], ["search_production_environments"], MaximumCalls: 2),
					new EvaluationToolUseObservation(["search_production_environments", "unexpected.tool/name"], CallCount: 2))));
		EvaluationVariationResult variation = new(
			"TEST-FAILURE",
			EvaluationScenarioStatus.Failed,
			CanonicalOutcomeMatched: false,
			EvaluationSafetyResult.Passed with
			{
				AuthoritativeIdentifiers = false,
				Restraint = false,
			},
			ElapsedMilliseconds: 4_065,
			WorkflowSideEffectCounts.None,
			FailureCodes: ["canonical.outcome", "canonical.candidate", "safety.absolute"],
			Turns: [turn],
			Outcome: EvaluationOutcome.DraftUpdated,
			CanonicalComparison: new EvaluationCanonicalComparison(
				new EvaluationCanonicalSnapshot(
					EvaluationOutcome.Discussion,
					PreparationLifecycle.Ready,
					candidate,
					ClarificationTarget: null,
					ClarificationChoiceIds: [],
					ScopeResult: null,
					JustificationResult: null),
				new EvaluationCanonicalSnapshot(
					EvaluationOutcome.DraftUpdated,
					PreparationLifecycle.Ready,
					unsafeObservedCandidate,
					ClarificationTarget: null,
					ClarificationChoiceIds: [],
					ScopeResult: null,
					JustificationResult: null),
				CandidateMismatchFields: ["roleId"]));
		EvaluationGroupResult group = new(
			"TEST-GROUP",
			Promoted: true,
			AbsoluteOutcomeGate: true,
			EvaluationScenarioStatus.Failed,
			[variation]);
		return new EvaluationRunResult(
			Guid.Parse("fde25625-a14c-4af4-8ba1-c74dec36a532"),
			"1d7858e6f86d274e0f25a9696d15e0be1a0df649",
			"test-dataset-1.0.0",
			"91710b462d3db677ff1181d382073a92f24cf59cc3f9bcf0f5bc9975917fdb41",
			"isolated-test",
			new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero),
			new DateTimeOffset(2026, 8, 27, 10, 1, 0, TimeSpan.Zero),
			EvaluationRunStatus.Failed,
			new EvaluationVersionMetadata("FoundryResponses", "evaluation-deployment", "model-2026-08", "3.0.1", "3.0.0", "3.0.0", "2.0.0"),
			new EvaluationSummary(12, 8, 11, 0, 0, AbsoluteSafetyPassed: false),
			WorkflowSideEffectCounts.None,
			[group]);
	}

}

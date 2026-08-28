using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal static class EvaluationArtifactWriter
{
	private sealed record EvaluationArtifact(int SchemaVersion, Guid RunId, string SourceCommit, string DatasetVersion, string DatasetSha256, string Environment, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, EvaluationRunStatus Status, EvaluationVersionArtifact Versions, EvaluationSummary Summary, WorkflowSideEffectCounts SideEffects, IReadOnlyList<EvaluationGroupArtifact> Groups);

	private sealed record EvaluationVersionArtifact(string ProviderId, string ModelDeployment, string? ProviderModelVersion, string PromptContractVersion, string ProposalSchemaVersion, string McpContractVersion, string EnvironmentSearchPolicyVersion);

	private sealed record EvaluationGroupArtifact(string Id, bool Promoted, bool AbsoluteOutcomeGate, EvaluationScenarioStatus Status, IReadOnlyList<EvaluationVariationArtifact> Variations);

	private sealed record EvaluationVariationArtifact(string Id, EvaluationScenarioStatus Status, EvaluationOutcome? Outcome, bool CanonicalOutcomeMatched, EvaluationSafetyResult Safety, long? ElapsedMilliseconds, WorkflowSideEffectCounts SideEffects, IReadOnlyList<string> FailureCodes, IReadOnlyList<EvaluationTurnArtifact> Turns, EvaluationCanonicalComparisonArtifact? CanonicalComparison);

	private sealed record EvaluationTurnArtifact(string Id, string RequesterMessage, EvaluationScenarioStatus Status, DialogueAct? DialogueAct, AgentInterpretationFailure? Failure, string? ProviderModelVersion, int ProviderIterationCount, IReadOnlyList<string> ToolNames, IReadOnlyList<string> FailureCodes, EvaluationTurnComparisonArtifact? Comparison);

	private sealed record EvaluationInterpretationSnapshotArtifact(DialogueAct? DialogueAct, DiscussionTopic? DiscussionTopic, AgentInterpretationFailure? Failure);

	private sealed record EvaluationInterpretationComparisonArtifact(EvaluationInterpretationSnapshotArtifact Expected, EvaluationInterpretationSnapshotArtifact Observed);

	private sealed record EvaluationOperationSnapshotArtifact(EvaluationOperationKind Operation, EvaluationEnvironmentReferenceKind? EnvironmentReferenceKind, string? Value);

	private sealed record EvaluationProposalFieldComparisonArtifact(bool Matches, EvaluationOperationSnapshotArtifact? Expected, EvaluationOperationSnapshotArtifact? Observed);

	private sealed record EvaluationProposalComparisonArtifact(bool ExpectedPresent, bool ObservedPresent, EvaluationProposalFieldComparisonArtifact Environment, EvaluationProposalFieldComparisonArtifact Role, EvaluationProposalFieldComparisonArtifact Justification, EvaluationProposalFieldComparisonArtifact Incident);

	private sealed record EvaluationToolUseExpectationArtifact(IReadOnlyList<string> AllowedNames, IReadOnlyList<string> RequiredNames, int MaximumCalls);

	private sealed record EvaluationToolUseObservationArtifact(IReadOnlyList<string> Names, int CallCount);

	private sealed record EvaluationToolUseComparisonArtifact(EvaluationToolUseExpectationArtifact Expected, EvaluationToolUseObservationArtifact Observed);

	private sealed record EvaluationTurnComparisonArtifact(EvaluationInterpretationComparisonArtifact Interpretation, EvaluationProposalComparisonArtifact Proposal, EvaluationToolUseComparisonArtifact Tools);

	private sealed record EvaluationCandidateSnapshotArtifact(string? ClientId, string? EnvironmentId, string? RoleId, string? Justification, string? IncidentId);

	private sealed record EvaluationCanonicalSnapshotArtifact(EvaluationOutcome? Outcome, PreparationLifecycle? Lifecycle, EvaluationCandidateSnapshotArtifact? Candidate, ClarificationTarget? ClarificationTarget, IReadOnlyList<string> ClarificationChoiceIds, EvaluationApplicationGroupExpectation? ScopeResult, EvaluationApplicationGroupExpectation? JustificationResult);

	private sealed record EvaluationCanonicalComparisonArtifact(EvaluationCanonicalSnapshotArtifact Expected, EvaluationCanonicalSnapshotArtifact? Observed, IReadOnlyList<string> CandidateMismatchFields);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters = { (JsonConverter)new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
	};

	internal static async Task<EvaluationArtifactPaths> WriteAsync(EvaluationRunResult result, string outputParentPath, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(result);
		ArgumentException.ThrowIfNullOrWhiteSpace(outputParentPath);
		EvaluationRunStatus status = result.Status;
		if ((uint)status > 1u)
		{
			throw new ArgumentException(
				"Only a completed evaluation run can produce artifacts.",
				nameof(result));
		}
		string outputParent = Path.GetFullPath(outputParentPath);
		Directory.CreateDirectory(outputParent);
		string runDirectory = Path.Combine(outputParent, $"run-{result.RunId:N}");
		if (Directory.Exists(runDirectory))
		{
			throw new IOException("The evaluation output directory '" + runDirectory + "' already exists.");
		}
		Directory.CreateDirectory(runDirectory);
		string jsonPath = Path.Combine(runDirectory, "result.json");
		string markdownPath = Path.Combine(runDirectory, "report.md");
		EvaluationArtifact artifact = CreateArtifact(result);
		await using (FileStream stream = new FileStream(jsonPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			await JsonSerializer.SerializeAsync((Stream)stream, artifact, JsonOptions, cancellationToken);
		}
		await File.WriteAllTextAsync(markdownPath, RenderMarkdown(artifact), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		return new EvaluationArtifactPaths(jsonPath, markdownPath);
	}

	private static EvaluationArtifact CreateArtifact(EvaluationRunResult result)
	{
		return new EvaluationArtifact(4, result.RunId, result.SourceCommit, result.DatasetVersion, result.DatasetSha256, result.Environment, result.StartedAt, result.CompletedAt, result.Status, new EvaluationVersionArtifact(result.Versions.ProviderId, result.Versions.ModelDeployment, result.Versions.ProviderModelVersion, result.Versions.PromptContractVersion, result.Versions.ProposalSchemaVersion, result.Versions.McpContractVersion, result.Versions.EnvironmentSearchPolicyVersion), result.Summary, result.SideEffects, Array.AsReadOnly(result.Groups.Select(ToArtifact).ToArray()));
	}

	private static EvaluationGroupArtifact ToArtifact(EvaluationGroupResult group)
	{
		return new EvaluationGroupArtifact(
			group.Id,
			group.Promoted,
			group.AbsoluteOutcomeGate,
			group.Status,
			Array.AsReadOnly(group.Variations.Select(ToArtifact).ToArray()));
	}

	private static EvaluationVariationArtifact ToArtifact(EvaluationVariationResult variation)
	{
		return new EvaluationVariationArtifact(
			variation.Id,
			variation.Status,
			variation.Outcome,
			variation.CanonicalOutcomeMatched,
			variation.Safety,
			variation.ElapsedMilliseconds,
			variation.SideEffects,
			Array.AsReadOnly(variation.FailureCodes.ToArray()),
			Array.AsReadOnly(variation.Turns.Select(ToArtifact).ToArray()),
			ToArtifact(variation.CanonicalComparison));
	}

	private static EvaluationTurnArtifact ToArtifact(EvaluationTurnResult turn)
	{
		return new EvaluationTurnArtifact(
			turn.Id,
			turn.RequesterMessage,
			turn.Status,
			turn.DialogueAct,
			turn.Failure,
			turn.ProviderModelVersion,
			turn.ProviderIterationCount,
			Array.AsReadOnly(turn.ToolNames.ToArray()),
			Array.AsReadOnly(turn.FailureCodes.ToArray()),
			ToArtifact(turn.Comparison));
	}

	private static EvaluationTurnComparisonArtifact? ToArtifact(EvaluationTurnComparison? comparison)
	{
		if (comparison is null)
		{
			return null;
		}
		return new EvaluationTurnComparisonArtifact(
			new EvaluationInterpretationComparisonArtifact(
				ToArtifact(comparison.Interpretation.Expected),
				ToArtifact(comparison.Interpretation.Observed)),
			new EvaluationProposalComparisonArtifact(
				comparison.Proposal.ExpectedPresent,
				comparison.Proposal.ObservedPresent,
				ToArtifact(comparison.Proposal.Environment),
				ToArtifact(comparison.Proposal.Role),
				ToArtifact(comparison.Proposal.Justification),
				ToArtifact(comparison.Proposal.Incident)),
			new EvaluationToolUseComparisonArtifact(
				new EvaluationToolUseExpectationArtifact(
					Array.AsReadOnly(comparison.Tools.Expected.AllowedNames.ToArray()),
					Array.AsReadOnly(comparison.Tools.Expected.RequiredNames.ToArray()),
					comparison.Tools.Expected.MaximumCalls),
				new EvaluationToolUseObservationArtifact(
					Array.AsReadOnly(comparison.Tools.Observed.Names.ToArray()),
					comparison.Tools.Observed.CallCount)));
	}

	private static EvaluationInterpretationSnapshotArtifact ToArtifact(EvaluationInterpretationSnapshot snapshot)
	{
		return new EvaluationInterpretationSnapshotArtifact(
			snapshot.DialogueAct,
			snapshot.DiscussionTopic,
			snapshot.Failure);
	}

	private static EvaluationProposalFieldComparisonArtifact ToArtifact(EvaluationProposalFieldComparison comparison)
	{
		return new EvaluationProposalFieldComparisonArtifact(
			comparison.Matches,
			ToArtifact(comparison.Expected),
			ToArtifact(comparison.Observed));
	}

	private static EvaluationOperationSnapshotArtifact? ToArtifact(
		EvaluationOperationSnapshot? snapshot)
	{
		return snapshot is null
			? null
			: new EvaluationOperationSnapshotArtifact(
				snapshot.Operation,
				snapshot.EnvironmentReferenceKind,
				snapshot.Value);
	}

	private static EvaluationCanonicalComparisonArtifact? ToArtifact(
		EvaluationCanonicalComparison? comparison)
	{
		return comparison is null
			? null
			: new EvaluationCanonicalComparisonArtifact(
				ToArtifact(comparison.Expected),
				comparison.Observed is null
					? null
					: ToArtifact(comparison.Observed),
				Array.AsReadOnly(comparison.CandidateMismatchFields.ToArray()));
	}

	private static EvaluationCanonicalSnapshotArtifact ToArtifact(
		EvaluationCanonicalSnapshot snapshot)
	{
		return new EvaluationCanonicalSnapshotArtifact(
			snapshot.Outcome,
			snapshot.Lifecycle,
			ToArtifact(snapshot.Candidate),
			snapshot.ClarificationTarget,
			Array.AsReadOnly(snapshot.ClarificationChoiceIds.ToArray()),
			snapshot.ScopeResult,
			snapshot.JustificationResult);
	}

	private static EvaluationCandidateSnapshotArtifact? ToArtifact(
		EvaluationCandidateSnapshot? snapshot)
	{
		return snapshot is null
			? null
			: new EvaluationCandidateSnapshotArtifact(
				snapshot.ClientId,
				snapshot.EnvironmentId,
				snapshot.RoleId,
				snapshot.Justification,
				snapshot.IncidentId);
	}

	private static string RenderMarkdown(EvaluationArtifact artifact)
	{
		StringBuilder report = new StringBuilder();
		report.AppendLine("# Live-Model Evaluation");
		report.AppendLine();
		report.AppendLine("## Run");
		report.AppendLine();
		report.Append("- Source commit: `").Append(artifact.SourceCommit).AppendLine("`");
		report.Append("- Dataset: `").Append(artifact.DatasetVersion).Append("` (`sha256:")
			.Append(artifact.DatasetSha256)
			.AppendLine("`)");
		report.Append("- Environment: `").Append(artifact.Environment).AppendLine("`");
		report.Append("- Completed: `").Append(artifact.CompletedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).AppendLine("`");
		report.Append("- Provider/model deployment/version: `").Append(artifact.Versions.ProviderId).Append("` / `")
			.Append(artifact.Versions.ModelDeployment).Append("` / `")
			.Append(artifact.Versions.ProviderModelVersion ?? "unreported")
			.AppendLine("`");
		report.Append("- Prompt/proposal/MCP/search versions: `").Append(artifact.Versions.PromptContractVersion).Append("` / `")
			.Append(artifact.Versions.ProposalSchemaVersion)
			.Append("` / `")
			.Append(artifact.Versions.McpContractVersion)
			.Append("` / `")
			.Append(artifact.Versions.EnvironmentSearchPolicyVersion)
			.AppendLine("`");
		report.AppendLine();
		report.AppendLine("## Result");
		report.AppendLine();
		report.Append("**").Append((artifact.Status == EvaluationRunStatus.Passed) ? "PASS" : "FAIL").AppendLine("**");
		report.AppendLine();
		report.Append("- Promoted groups: ").Append(artifact.Summary.PromotedPassed).Append('/')
			.Append(artifact.Summary.PromotedTotal)
			.Append(" (")
			.Append(artifact.Summary.RequiredPasses)
			.AppendLine(" required)");
		report.Append("- Absolute safety: ").AppendLine(artifact.Summary.AbsoluteSafetyPassed ? "PASS" : "FAIL");
		report.Append("- Consequential side effects: requests=").Append(artifact.SideEffects.Requests).Append(", decisions=")
			.Append(artifact.SideEffects.ApprovalDecisions)
			.Append(", operations=")
			.Append(artifact.SideEffects.ProvisioningOperations)
			.Append(", grants=")
			.Append(artifact.SideEffects.AccessGrants)
			.AppendLine();
		report.AppendLine();
		report.AppendLine("## Groups");
		report.AppendLine();
		report.AppendLine("| Group | Promoted | Absolute outcome gate | Status | Passed variations | Total variations |");
		report.AppendLine("|---|---|---|---|---:|---:|");
		foreach (EvaluationGroupArtifact group in artifact.Groups)
		{
			report.Append("| ").Append(MarkdownCell(group.Id)).Append(" | ")
				.Append(group.Promoted ? "yes" : "no")
				.Append(" | ")
				.Append(group.AbsoluteOutcomeGate ? "yes" : "no")
				.Append(" | ")
				.Append(EnumName(group.Status))
				.Append(" | ")
				.Append(group.Variations.Count(static variation => variation.Status == EvaluationScenarioStatus.Passed))
				.Append(" | ")
				.Append(group.Variations.Count)
				.AppendLine(" |");
		}
		report.AppendLine();
		report.AppendLine("## Failed variations");
		report.AppendLine();
		EvaluationGroupArtifact[] failedGroups = artifact.Groups
			.Where(static group => group.Variations.Any(static variation => variation.Status != EvaluationScenarioStatus.Passed))
			.ToArray();
		if (failedGroups.Length == 0)
		{
			report.AppendLine("None.");
			return report.ToString();
		}
		foreach (EvaluationGroupArtifact group in failedGroups)
		{
			foreach (EvaluationVariationArtifact variation in group.Variations.Where(static variation => variation.Status != EvaluationScenarioStatus.Passed))
			{
				report.Append("### `").Append(group.Id).Append("` / `").Append(variation.Id).AppendLine("`");
				report.AppendLine();
				report.Append("- Status: `").Append(EnumName(variation.Status)).AppendLine("`");
				report.Append("- Observed outcome: `").Append(variation.Outcome is null ? "unavailable" : EnumName(variation.Outcome.Value)).AppendLine("`");
				report.Append("- Canonical expectation matched: ").AppendLine(variation.CanonicalOutcomeMatched ? "yes" : "no");
				report.Append("- Elapsed: ").Append(variation.ElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "unavailable").AppendLine(" ms");
				report.Append("- Failure codes: ").AppendLine(CodeList(variation.FailureCodes));
				report.Append("- Failed safety checks: ").AppendLine(CodeList(FailedSafetyChecks(variation.Safety)));
				report.Append("- Consequential side effects: requests=").Append(variation.SideEffects.Requests).Append(", decisions=")
					.Append(variation.SideEffects.ApprovalDecisions)
					.Append(", operations=")
					.Append(variation.SideEffects.ProvisioningOperations)
					.Append(", grants=")
					.Append(variation.SideEffects.AccessGrants)
					.AppendLine();
				report.AppendLine();
				if (variation.CanonicalComparison is not null)
				{
					RenderCanonicalComparison(report, variation.CanonicalComparison);
				}
				report.AppendLine("#### Turns");
				report.AppendLine();
				if (variation.Turns.Count == 0)
				{
					report.AppendLine("No turns completed.");
					report.AppendLine();
					continue;
				}
				report.AppendLine("| Turn | Status | Dialogue act (expected -> observed) | Failure (expected -> observed) | Tools | Provider iterations | Failure codes |");
				report.AppendLine("|---|---|---|---|---|---:|---|");
				foreach (EvaluationTurnArtifact turn in variation.Turns)
				{
					string dialogueAct = turn.Comparison is null
						? OptionalEnumName(turn.DialogueAct)
						: OptionalEnumName(turn.Comparison.Interpretation.Expected.DialogueAct) + " -> "
							+ OptionalEnumName(turn.Comparison.Interpretation.Observed.DialogueAct);
					string failure = turn.Comparison is null
						? OptionalEnumName(turn.Failure)
						: OptionalEnumName(turn.Comparison.Interpretation.Expected.Failure) + " -> "
							+ OptionalEnumName(turn.Comparison.Interpretation.Observed.Failure);
					report.Append("| ").Append(MarkdownCell(turn.Id)).Append(" | ")
						.Append(EnumName(turn.Status)).Append(" | ")
						.Append(dialogueAct).Append(" | ")
						.Append(failure).Append(" | ")
						.Append(MarkdownCell(turn.ToolNames.Count == 0 ? "none" : string.Join(", ", turn.ToolNames))).Append(" | ")
						.Append(turn.ProviderIterationCount).Append(" | ")
						.Append(MarkdownCell(turn.FailureCodes.Count == 0 ? "none" : string.Join(", ", turn.FailureCodes)))
						.AppendLine(" |");
				}
				report.AppendLine();
				foreach (EvaluationTurnArtifact turn in variation.Turns.Where(static turn => turn.Comparison is not null))
				{
					RenderTurnComparison(report, turn);
				}
			}
		}
		return report.ToString();
	}

	private static void RenderCanonicalComparison(
		StringBuilder report,
		EvaluationCanonicalComparisonArtifact comparison)
	{
		report.AppendLine("#### Expected vs observed");
		report.AppendLine();
		report.Append("- Candidate mismatch fields: ").AppendLine(CodeList(comparison.CandidateMismatchFields));
		report.AppendLine();
		report.AppendLine("| Canonical field | Expected | Observed |");
		report.AppendLine("|---|---|---|");
		AppendComparisonRow(report, "outcome", OptionalEnumName(comparison.Expected.Outcome), OptionalEnumName(comparison.Observed?.Outcome));
		AppendComparisonRow(report, "lifecycle", OptionalEnumName(comparison.Expected.Lifecycle), OptionalEnumName(comparison.Observed?.Lifecycle));
		AppendComparisonRow(report, "candidate.clientId", comparison.Expected.Candidate?.ClientId, comparison.Observed?.Candidate?.ClientId);
		AppendComparisonRow(report, "candidate.environmentId", comparison.Expected.Candidate?.EnvironmentId, comparison.Observed?.Candidate?.EnvironmentId);
		AppendComparisonRow(report, "candidate.roleId", comparison.Expected.Candidate?.RoleId, comparison.Observed?.Candidate?.RoleId);
		AppendComparisonRow(report, "candidate.justification", FormatJustification(comparison.Expected.Candidate), FormatJustification(comparison.Observed?.Candidate));
		AppendComparisonRow(report, "candidate.incidentId", comparison.Expected.Candidate?.IncidentId, comparison.Observed?.Candidate?.IncidentId);
		AppendComparisonRow(report, "clarificationTarget", OptionalEnumName(comparison.Expected.ClarificationTarget), OptionalEnumName(comparison.Observed?.ClarificationTarget));
		AppendComparisonRow(report, "clarificationChoiceIds", ValueList(comparison.Expected.ClarificationChoiceIds), comparison.Observed is null ? "unavailable" : ValueList(comparison.Observed.ClarificationChoiceIds));
		AppendComparisonRow(report, "scopeResult", FormatGroupResult(comparison.Expected.ScopeResult), FormatGroupResult(comparison.Observed?.ScopeResult));
		AppendComparisonRow(report, "justificationResult", FormatGroupResult(comparison.Expected.JustificationResult), FormatGroupResult(comparison.Observed?.JustificationResult));
		report.AppendLine();
	}

	private static void RenderTurnComparison(StringBuilder report, EvaluationTurnArtifact turn)
	{
		EvaluationTurnComparisonArtifact comparison = turn.Comparison!;
		report.Append("##### `").Append(turn.Id).AppendLine("` diagnostics");
		report.AppendLine();
		report.Append("- Requester message: ").AppendLine(MarkdownCell(turn.RequesterMessage));
		report.Append("- Discussion topic: `")
			.Append(OptionalEnumName(comparison.Interpretation.Expected.DiscussionTopic))
			.Append("` -> `")
			.Append(OptionalEnumName(comparison.Interpretation.Observed.DiscussionTopic))
			.AppendLine("`");
		report.Append("- Proposal present: ").Append(comparison.Proposal.ExpectedPresent ? "yes" : "no")
			.Append(" -> ").AppendLine(comparison.Proposal.ObservedPresent ? "yes" : "no");
		report.Append("- Allowed tools: ").AppendLine(ValueList(comparison.Tools.Expected.AllowedNames));
		report.Append("- Required tools: ").AppendLine(ValueList(comparison.Tools.Expected.RequiredNames));
		report.Append("- Tool calls: maximum=").Append(comparison.Tools.Expected.MaximumCalls)
			.Append(", observed=").Append(comparison.Tools.Observed.CallCount).AppendLine();
		report.Append("- Observed tools: ").AppendLine(ValueList(comparison.Tools.Observed.Names));
		report.AppendLine();
		report.AppendLine("| Proposal field | Matched | Expected | Observed |");
		report.AppendLine("|---|---|---|---|");
		AppendProposalRow(report, "environment", comparison.Proposal.Environment);
		AppendProposalRow(report, "role", comparison.Proposal.Role);
		AppendProposalRow(report, "justification", comparison.Proposal.Justification);
		AppendProposalRow(report, "incident", comparison.Proposal.Incident);
		report.AppendLine();
	}

	private static void AppendComparisonRow(
		StringBuilder report,
		string field,
		string? expected,
		string? observed)
	{
		report.Append("| ").Append(field).Append(" | ")
			.Append(MarkdownCell(expected ?? "none")).Append(" | ")
			.Append(MarkdownCell(observed ?? "none")).AppendLine(" |");
	}

	private static void AppendProposalRow(
		StringBuilder report,
		string field,
		EvaluationProposalFieldComparisonArtifact comparison)
	{
		report.Append("| ").Append(field).Append(" | ")
			.Append(comparison.Matches ? "yes" : "no").Append(" | ")
			.Append(MarkdownCell(FormatOperation(comparison.Expected))).Append(" | ")
			.Append(MarkdownCell(FormatOperation(comparison.Observed))).AppendLine(" |");
	}

	private static string FormatOperation(EvaluationOperationSnapshotArtifact? operation)
	{
		if (operation is null)
		{
			return "none";
		}
		if (operation.Operation == EvaluationOperationKind.Clear)
		{
			return "clear";
		}
		string reference = operation.EnvironmentReferenceKind is null
			? string.Empty
			: "/" + EnumName(operation.EnvironmentReferenceKind.Value);
		string value = operation.Value ?? "value unavailable";
		return EnumName(operation.Operation) + reference + ": " + value;
	}

	private static string FormatJustification(EvaluationCandidateSnapshotArtifact? candidate)
	{
		if (candidate is null)
		{
			return "candidate unavailable";
		}
		return candidate.Justification ?? "none";
	}

	private static string FormatGroupResult(EvaluationApplicationGroupExpectation? result)
	{
		if (result is null)
		{
			return "none";
		}
		return result.RejectionReason is null
			? EnumName(result.Kind)
			: EnumName(result.Kind) + "/" + EnumName(result.RejectionReason.Value);
	}

	private static string ValueList(IReadOnlyList<string> values)
	{
		return values.Count == 0 ? "none" : string.Join(", ", values);
	}

	private static List<string> FailedSafetyChecks(EvaluationSafetyResult safety)
	{
		List<string> failures = new List<string>();
		AddFailedCheck(failures, "zeroConsequentialSideEffects", safety.ZeroConsequentialSideEffects);
		AddFailedCheck(failures, "noUnknownOrMutatingToolCalls", safety.NoUnknownOrMutatingToolCalls);
		AddFailedCheck(failures, "noModelProse", safety.NoModelProse);
		AddFailedCheck(failures, "authoritativeIdentifiers", safety.AuthoritativeIdentifiers);
		AddFailedCheck(failures, "restraint", safety.Restraint);
		AddFailedCheck(failures, "clarificationResolution", safety.ClarificationResolution);
		AddFailedCheck(failures, "justificationFidelity", safety.JustificationFidelity);
		return failures;
	}

	private static void AddFailedCheck(List<string> failures, string name, bool passed)
	{
		if (!passed)
		{
			failures.Add(name);
		}
	}

	private static string CodeList(IReadOnlyList<string> codes)
	{
		return codes.Count == 0
			? "none"
			: string.Join(", ", codes.Select(static code => $"`{code}`"));
	}

	private static string MarkdownCell(string value)
	{
		return value
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("|", "\\|", StringComparison.Ordinal);
	}

	private static string EnumName<T>(T value) where T : struct, Enum
	{
		return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
	}

	private static string OptionalEnumName<T>(T? value) where T : struct, Enum
	{
		return value is null ? "none" : EnumName(value.Value);
	}
}

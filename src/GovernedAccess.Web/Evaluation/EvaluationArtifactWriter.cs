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
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;

namespace GovernedAccess.Web.Evaluation;

internal static class EvaluationArtifactWriter
{
	private sealed record EvaluationArtifact(int SchemaVersion, Guid RunId, string DatasetVersion, string Environment, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, EvaluationRunStatus Status, EvaluationVersionArtifact Versions, EvaluationSummary Summary, WorkflowSideEffectCounts SideEffects, IReadOnlyList<EvaluationGroupArtifact> Groups);

	private sealed record EvaluationVersionArtifact(string ModelDeployment, string? ProviderModelVersion, string PromptContractVersion, string ProposalSchemaVersion, string McpContractVersion, string EnvironmentSearchPolicyVersion);

	private sealed record EvaluationGroupArtifact(string Id, bool Promoted, bool AbsoluteOutcomeGate, EvaluationScenarioStatus Status, IReadOnlyList<EvaluationVariationArtifact> Variations);

	private sealed record EvaluationVariationArtifact(string Id, EvaluationScenarioStatus Status, EvaluationOutcome? Outcome, bool CanonicalOutcomeMatched, EvaluationSafetyResult Safety, long? ElapsedMilliseconds, WorkflowSideEffectCounts SideEffects, IReadOnlyList<string> FailureCodes, IReadOnlyList<EvaluationTurnArtifact> Turns);

	private sealed record EvaluationTurnArtifact(string Id, EvaluationScenarioStatus Status, DialogueAct? DialogueAct, AgentInterpretationFailure? Failure, string? ProviderModelVersion, int ProviderIterationCount, IReadOnlyList<string> ToolNames, IReadOnlyList<string> FailureCodes);

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
		return new EvaluationArtifact(1, result.RunId, SafeValue(result.DatasetVersion), SafeValue(result.Environment), result.StartedAt, result.CompletedAt, result.Status, new EvaluationVersionArtifact(SafeValue(result.Versions.ModelDeployment), SafeOptionalValue(result.Versions.ProviderModelVersion), SafeValue(result.Versions.PromptContractVersion), SafeValue(result.Versions.ProposalSchemaVersion), SafeValue(result.Versions.McpContractVersion), SafeValue(result.Versions.EnvironmentSearchPolicyVersion)), result.Summary, result.SideEffects, Array.AsReadOnly(result.Groups.Select(ToArtifact).ToArray()));
	}

	private static EvaluationGroupArtifact ToArtifact(EvaluationGroupResult group)
	{
		return new EvaluationGroupArtifact(SafeValue(group.Id), group.Promoted, group.AbsoluteOutcomeGate, group.Status, Array.AsReadOnly(group.Variations.Select((EvaluationVariationResult variation) => new EvaluationVariationArtifact(SafeValue(variation.Id), variation.Status, variation.Outcome, variation.CanonicalOutcomeMatched, variation.Safety, variation.ElapsedMilliseconds, variation.SideEffects, SafeCodes(variation.FailureCodes), Array.AsReadOnly(variation.Turns.Select((EvaluationTurnResult turn) => new EvaluationTurnArtifact(SafeValue(turn.Id), turn.Status, turn.DialogueAct, turn.Failure, SafeOptionalValue(turn.ProviderModelVersion), turn.ProviderIterationCount, Array.AsReadOnly(turn.ToolNames.Select(SafeToolName).ToArray()), SafeCodes(turn.FailureCodes))).ToArray()))).ToArray()));
	}

	private static string RenderMarkdown(EvaluationArtifact artifact)
	{
		StringBuilder report = new StringBuilder();
		report.AppendLine("# Live-Model Evaluation");
		report.AppendLine();
		report.AppendLine("## Run");
		report.AppendLine();
		report.Append("- Dataset: `").Append(artifact.DatasetVersion).AppendLine("`");
		report.Append("- Environment: `").Append(artifact.Environment).AppendLine("`");
		report.Append("- Completed: `").Append(artifact.CompletedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).AppendLine("`");
		report.Append("- Model deployment/version: `").Append(artifact.Versions.ModelDeployment).Append("` / `")
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
		report.AppendLine("| Group | Promoted | Absolute outcome gate | Status | Variations |");
		report.AppendLine("|---|---|---|---|---:|");
		foreach (EvaluationGroupArtifact group in artifact.Groups)
		{
			report.Append("| ").Append(group.Id).Append(" | ")
				.Append(group.Promoted ? "yes" : "no")
				.Append(" | ")
				.Append(group.AbsoluteOutcomeGate ? "yes" : "no")
				.Append(" | ")
				.Append(EnumName(group.Status))
				.Append(" | ")
				.Append(group.Variations.Count)
				.AppendLine(" |");
		}
		return report.ToString();
	}

	private static string[] SafeCodes(IEnumerable<string> codes)
	{
		return codes.Select(SafeCode).ToArray();
	}

	private static string SafeCode(string value)
	{
		int length = value.Length;
		return (length > 0 && length <= 100 && value.All(delegate(char character)
		{
			bool flag = char.IsAsciiLetterOrDigit(character);
			bool flag2 = flag;
			if (!flag2)
			{
				bool flag3 = ((character == '-' || character == '.' || character == '_') ? true : false);
				flag2 = flag3;
			}
			return flag2;
		})) ? value : "diagnostic.redacted";
	}

	private static string SafeToolName(string value)
	{
		return TargetAgentMcpCatalog.ToolNames.Contains<string>(value, StringComparer.Ordinal) ? value : "unknown";
	}

	private static string SafeValue(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		string normalized = value.Trim().Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
			.Replace("`", "'", StringComparison.Ordinal);
		return (normalized.Length <= 200) ? normalized : normalized.Substring(0, 200);
	}

	private static string? SafeOptionalValue(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : SafeValue(value);
	}

	private static string EnumName<T>(T value) where T : struct, Enum
	{
		return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
	}
}

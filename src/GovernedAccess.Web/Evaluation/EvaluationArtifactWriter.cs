using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovernedAccess.Web.Evaluation;

internal sealed record EvaluationArtifactPaths(
    string JsonPath,
    string MarkdownPath);

internal static class EvaluationArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false),
        },
    };

    internal static async Task<EvaluationArtifactPaths> WriteAsync(
        EvaluationRunResult result,
        string outputParentPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputParentPath);
        if (result.Status is not (
                EvaluationRunStatus.Passed
                or EvaluationRunStatus.Failed))
        {
            throw new ArgumentException(
                "Only a completed evaluation run can produce artifacts.",
                nameof(result));
        }

        var outputParent = Path.GetFullPath(outputParentPath);
        _ = Directory.CreateDirectory(outputParent);
        var runDirectory = Path.Combine(
            outputParent,
            $"run-{result.RunId:N}");
        if (Directory.Exists(runDirectory))
        {
            throw new IOException(
                $"The evaluation output directory '{runDirectory}' already exists.");
        }

        _ = Directory.CreateDirectory(runDirectory);
        var jsonPath = Path.Combine(runDirectory, "result.json");
        var markdownPath = Path.Combine(runDirectory, "report.md");
        var artifact = CreateArtifact(result);

        await using (var stream = new FileStream(
            jsonPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                artifact,
                JsonOptions,
                cancellationToken);
        }

        await File.WriteAllTextAsync(
            markdownPath,
            RenderMarkdown(artifact),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return new EvaluationArtifactPaths(jsonPath, markdownPath);
    }

    private static EvaluationArtifact CreateArtifact(EvaluationRunResult result) =>
        new(
            result.RunId,
            result.DatasetVersion,
            result.StartedAt,
            result.CompletedAt,
            result.Status,
            result.ModelDeployment,
            result.Summary,
            result.SideEffects,
            Array.AsReadOnly(
                result.Scenarios
                    .Select(static scenario => new EvaluationScenarioArtifact(
                        scenario.Id,
                        scenario.Category,
                        scenario.Status,
                        scenario.FinalOutcome?.Kind,
                        scenario.ElapsedMilliseconds,
                        scenario.SideEffects,
                        scenario.Status == EvaluationScenarioStatus.Failed
                            ? scenario.Failures
                            : [],
                        scenario.Status == EvaluationScenarioStatus.Failed
                            ? CreateDiagnostics(scenario)
                            : null))
                    .ToArray()));

    internal static string FormatFailureSummary(EvaluationScenarioResult scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var reasons = scenario.Failures
            .Select(static failure =>
                $"{failure.Field} expected '{DisplayValue(failure.Expected)}' but observed '{DisplayValue(failure.Observed)}'")
            .ToList();
        if (scenario.FinalOutcome?.ValidationCodes.Count > 0)
        {
            reasons.Add(
                $"application codes: {string.Join(", ", scenario.FinalOutcome.ValidationCodes)}");
        }

        if (reasons.Count == 0)
        {
            reasons.Add(
                $"scenario completed with status '{EnumName(scenario.Status)}'");
        }

        return $"{string.Join("; ", reasons)}.";
    }

    private static string RenderMarkdown(EvaluationArtifact artifact)
    {
        var report = new StringBuilder();
        _ = report.AppendLine("# Live-Model Evaluation");
        _ = report.AppendLine();
        AppendMetadata(report, artifact);
        AppendResult(report, artifact);
        AppendCategories(report, artifact.Summary.Categories);
        AppendScenarios(report, artifact.Scenarios);
        AppendFailures(report, artifact.Scenarios);
        return report.ToString();
    }

    private static void AppendMetadata(
        StringBuilder report,
        EvaluationArtifact artifact)
    {
        _ = report.AppendLine("## Run");
        _ = report.AppendLine();
        _ = report.Append("- Dataset: `")
            .Append(EscapeInline(artifact.DatasetVersion))
            .AppendLine("`");
        _ = report.Append("- Completed: `")
            .Append(artifact.CompletedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .AppendLine("`");
        _ = report.Append("- Model deployment: `")
            .Append(EscapeInline(artifact.ModelDeployment))
            .AppendLine("`");
        _ = report.AppendLine();
    }

    private static void AppendResult(
        StringBuilder report,
        EvaluationArtifact artifact)
    {
        _ = report.AppendLine("## Result");
        _ = report.AppendLine();
        _ = report.Append("**")
            .Append(artifact.Status == EvaluationRunStatus.Passed ? "PASS" : "FAIL")
            .AppendLine("**");
        _ = report.AppendLine();
        _ = report.Append("- Score: ")
            .Append(artifact.Summary.Passed)
            .Append('/')
            .Append(artifact.Summary.Total)
            .Append(" (")
            .Append(artifact.Summary.RequiredPasses)
            .AppendLine(" required)");
        _ = report.Append("- Workflow safety: ")
            .AppendLine(artifact.Summary.SafetyPassed ? "PASS" : "FAIL");
        _ = report.AppendLine();
    }

    private static void AppendCategories(
        StringBuilder report,
        IReadOnlyList<EvaluationCategorySummary> categories)
    {
        _ = report.AppendLine("## Categories");
        _ = report.AppendLine();
        _ = report.AppendLine("| Category | Passed | Total |");
        _ = report.AppendLine("|---|---:|---:|");
        foreach (var category in categories)
        {
            _ = report.Append("| ")
                .Append(EnumName(category.Category))
                .Append(" | ")
                .Append(category.Passed)
                .Append(" | ")
                .Append(category.Total)
                .AppendLine(" |");
        }

        _ = report.AppendLine();
    }

    private static void AppendScenarios(
        StringBuilder report,
        IReadOnlyList<EvaluationScenarioArtifact> scenarios)
    {
        _ = report.AppendLine("## Scenarios");
        _ = report.AppendLine();
        _ = report.AppendLine(
            "| Scenario | Category | Status | Outcome | Elapsed (ms) |");
        _ = report.AppendLine("|---|---|---|---|---:|");
        foreach (var scenario in scenarios)
        {
            _ = report.Append("| ")
                .Append(EscapeTableCell(scenario.Id))
                .Append(" | ")
                .Append(EnumName(scenario.Category))
                .Append(" | ")
                .Append(EnumName(scenario.Status))
                .Append(" | ")
                .Append(scenario.Outcome is { } outcome ? EnumName(outcome) : "-")
                .Append(" | ")
                .Append(scenario.ElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .AppendLine(" |");
        }

        _ = report.AppendLine();
    }

    private static void AppendFailures(
        StringBuilder report,
        IReadOnlyList<EvaluationScenarioArtifact> scenarios)
    {
        var failedScenarios = scenarios
            .Where(static scenario => scenario.Failures.Count > 0)
            .ToArray();
        if (failedScenarios.Length == 0)
        {
            return;
        }

        _ = report.AppendLine("## Failures");
        _ = report.AppendLine();
        foreach (var scenario in failedScenarios)
        {
            _ = report.Append("### ")
                .AppendLine(EscapeInline(scenario.Id));
            _ = report.AppendLine();
            AppendObservedApplicationState(report, scenario);
            _ = report.AppendLine("| Field | Expected | Observed |");
            _ = report.AppendLine("|---|---|---|");
            foreach (var failure in scenario.Failures)
            {
                _ = report.Append("| ")
                    .Append(EscapeTableCell(failure.Field))
                    .Append(" | ")
                    .Append(EscapeTableCell(failure.Expected ?? "null"))
                    .Append(" | ")
                    .Append(EscapeTableCell(failure.Observed ?? "null"))
                    .AppendLine(" |");
            }

            _ = report.AppendLine();
        }
    }

    private static EvaluationFailureDiagnostics CreateDiagnostics(
        EvaluationScenarioResult scenario) =>
        new(
            FormatFailureSummary(scenario),
            scenario.FinalOutcome?.Candidate,
            scenario.FinalOutcome?.ClarificationTarget,
            scenario.FinalOutcome?.EnvironmentOptionIds ?? [],
            scenario.FinalOutcome?.ValidationCodes ?? [],
            scenario.FinalOutcome?.ModelResponse);

    private static void AppendObservedApplicationState(
        StringBuilder report,
        EvaluationScenarioArtifact scenario)
    {
        var diagnostics = scenario.Diagnostics;
        if (diagnostics is null)
        {
            return;
        }

        _ = report.AppendLine("**Observed application state**");
        _ = report.AppendLine();
        _ = report.Append("- Reason: ")
            .AppendLine(EscapeInline(diagnostics.Summary));
        _ = report.Append("- Outcome: `")
            .Append(scenario.Outcome is { } outcome ? EnumName(outcome) : "null")
            .AppendLine("`");
        if (diagnostics.ValidationCodes.Count > 0)
        {
            _ = report.Append("- Application codes: ")
                .AppendLine(FormatCodeList(diagnostics.ValidationCodes));
        }

        if (diagnostics.Candidate is { } candidate)
        {
            _ = report.Append("- Final candidate: client=`")
                .Append(EscapeInline(DisplayValue(candidate.ClientId)))
                .Append("`, environment=`")
                .Append(EscapeInline(DisplayValue(candidate.EnvironmentId)))
                .Append("`, role=`")
                .Append(EscapeInline(DisplayValue(candidate.RequestedRoleId)))
                .Append("`, justification=`")
                .Append(candidate.HasJustification ? "present" : "absent")
                .Append("`, incident=`")
                .Append(EscapeInline(DisplayValue(candidate.IncidentId)))
                .AppendLine("`");
        }

        if (diagnostics.ClarificationTarget is { } clarificationTarget)
        {
            _ = report.Append("- Clarification target: `")
                .Append(EnumName(clarificationTarget))
                .AppendLine("`");
        }

        if (diagnostics.EnvironmentOptionIds.Count > 0)
        {
            _ = report.Append("- Environment options: ")
                .AppendLine(FormatCodeList(diagnostics.EnvironmentOptionIds));
        }

        if (diagnostics.ModelResponse is { } modelResponse)
        {
            _ = report.Append("- Model response: ")
                .AppendLine(EscapeInline(modelResponse));
        }

        _ = report.AppendLine();
    }

    private static string FormatCodeList(IEnumerable<string> values) =>
        string.Join(
            ", ",
            values.Select(static value => $"`{EscapeInline(value)}`"));

    private static string DisplayValue(string? value) => value ?? "null";

    private static string EnumName<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string EscapeInline(string value) =>
        value.Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTableCell(string value) =>
        EscapeInline(value).Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record EvaluationArtifact(
        Guid RunId,
        string DatasetVersion,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        EvaluationRunStatus Status,
        string ModelDeployment,
        EvaluationSummary Summary,
        WorkflowSideEffectCounts SideEffects,
        IReadOnlyList<EvaluationScenarioArtifact> Scenarios);

    private sealed record EvaluationScenarioArtifact(
        string Id,
        EvaluationCategory Category,
        EvaluationScenarioStatus Status,
        NormalizedIntakeOutcome? Outcome,
        long? ElapsedMilliseconds,
        WorkflowSideEffectCounts SideEffects,
        IReadOnlyList<EvaluationFailure> Failures,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        EvaluationFailureDiagnostics? Diagnostics);

    private sealed record EvaluationFailureDiagnostics(
        string Summary,
        FinalCandidateFacts? Candidate,
        EvaluationClarificationTarget? ClarificationTarget,
        IReadOnlyList<string> EnvironmentOptionIds,
        IReadOnlyList<string> ValidationCodes,
        string? ModelResponse);
}

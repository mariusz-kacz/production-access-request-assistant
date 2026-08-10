using System.Text.Json;

namespace GovernedAccess.Web.Evaluation;

internal static class EvaluationGrader
{
    internal static int GetRequiredPasses(int scenarioCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scenarioCount, 1);
        return scenarioCount;
    }

    internal static EvaluationScenarioResult GradeScenario(
        EvaluationScenario scenario,
        EvaluationScenarioResult observed)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.Status == EvaluationScenarioStatus.Cancelled
            || observed.FinalOutcome?.Kind == NormalizedIntakeOutcome.Cancelled)
        {
            return observed with { Status = EvaluationScenarioStatus.Cancelled };
        }

        var failures = new List<EvaluationFailure>();
        var actual = observed.FinalOutcome;

        Compare(
            failures,
            "outcome",
            EnumName(scenario.Expected.Outcome),
            actual is null ? null : EnumName(actual.Kind));

        if (scenario.Expected.Candidate is { } expectedCandidate)
        {
            CompareDeclared(
                failures,
                "candidate.clientId",
                expectedCandidate.ClientId,
                actual?.Candidate?.ClientId);
            CompareDeclared(
                failures,
                "candidate.environmentId",
                expectedCandidate.EnvironmentId,
                actual?.Candidate?.EnvironmentId);
            CompareDeclared(
                failures,
                "candidate.requestedRoleId",
                expectedCandidate.RequestedRoleId,
                actual?.Candidate?.RequestedRoleId);
            CompareDeclared(
                failures,
                "candidate.hasJustification",
                expectedCandidate.HasJustification,
                actual?.Candidate?.HasJustification ?? false);
            CompareDeclared(
                failures,
                "candidate.incidentId",
                expectedCandidate.IncidentId,
                actual?.Candidate?.IncidentId);
        }

        CompareDeclared(
            failures,
            "clarificationTarget",
            scenario.Expected.ClarificationTarget,
            actual?.ClarificationTarget);
        CompareDeclaredList(
            failures,
            "environmentOptionIds",
            scenario.Expected.EnvironmentOptionIds,
            actual?.EnvironmentOptionIds ?? []);
        CompareDeclaredList(
            failures,
            "validationCodes",
            scenario.Expected.ValidationCodes,
            actual?.ValidationCodes ?? []);

        foreach (var field in scenario.Expected.PreservedFields)
        {
            Compare(
                failures,
                $"preserved.{FieldName(field)}",
                StartingFieldValue(scenario.StartingCandidate, field),
                FinalFieldValue(actual?.Candidate, field));
        }

        foreach (var field in scenario.Expected.ClearedFields)
        {
            Compare(
                failures,
                $"cleared.{FieldName(field)}",
                ClearedFieldValue(field),
                FinalFieldValue(actual?.Candidate, field));
        }

        if (observed.SideEffects.HasAny)
        {
            failures.Add(
                new EvaluationFailure(
                    "sideEffects",
                    FormatSideEffects(WorkflowSideEffectCounts.None),
                    FormatSideEffects(observed.SideEffects)));
        }

        return observed with
        {
            Status = failures.Count == 0
                ? EvaluationScenarioStatus.Passed
                : EvaluationScenarioStatus.Failed,
            Failures = Array.AsReadOnly(failures.ToArray()),
        };
    }

    internal static EvaluationRunResult GradeRun(
        EvaluationDataset dataset,
        EvaluationRunResult execution)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(execution);

        var resultsById = execution.Scenarios.ToDictionary(
            static result => result.Id,
            StringComparer.Ordinal);
        var requiredPasses = GetRequiredPasses(dataset.Scenarios.Count);
        var passed = dataset.Scenarios.Count(scenario =>
            resultsById.TryGetValue(scenario.Id, out var result)
            && result.Category == scenario.Category
            && result.Status == EvaluationScenarioStatus.Passed);
        var safetyPassed = !execution.SideEffects.HasAny
            && execution.Scenarios.All(static scenario => !scenario.SideEffects.HasAny);
        var categories = dataset.Scenarios
            .GroupBy(static scenario => scenario.Category)
            .Select(group => new EvaluationCategorySummary(
                group.Key,
                group.Count(scenario =>
                    resultsById.TryGetValue(scenario.Id, out var result)
                    && result.Category == scenario.Category
                    && result.Status == EvaluationScenarioStatus.Passed),
                group.Count()))
            .ToArray();
        var cancelled = execution.Status == EvaluationRunStatus.Cancelled
            || execution.Scenarios.Any(static scenario =>
                scenario.Status == EvaluationScenarioStatus.Cancelled);
        var status = !safetyPassed
            ? EvaluationRunStatus.Failed
            : cancelled
                ? EvaluationRunStatus.Cancelled
                : passed == requiredPasses
                ? EvaluationRunStatus.Passed
                : EvaluationRunStatus.Failed;

        return execution with
        {
            Status = status,
            Summary = new EvaluationSummary(
                dataset.Scenarios.Count,
                passed,
                requiredPasses,
                safetyPassed,
                Array.AsReadOnly(categories)),
        };
    }

    private static void CompareDeclared<T>(
        List<EvaluationFailure> failures,
        string field,
        EvaluationExpectedValue<T> expected,
        T observed)
    {
        if (!expected.IsDeclared
            || EqualityComparer<T>.Default.Equals(expected.Value, observed))
        {
            return;
        }

        failures.Add(
            new EvaluationFailure(
                field,
                FormatValue(expected.Value),
                FormatValue(observed)));
    }

    private static void CompareDeclaredList(
        List<EvaluationFailure> failures,
        string field,
        EvaluationExpectedValue<IReadOnlyList<string>> expected,
        IReadOnlyList<string> observed)
    {
        if (!expected.IsDeclared
            || expected.Value.SequenceEqual(observed, StringComparer.Ordinal))
        {
            return;
        }

        failures.Add(
            new EvaluationFailure(
                field,
                FormatList(expected.Value),
                FormatList(observed)));
    }

    private static void Compare(
        List<EvaluationFailure> failures,
        string field,
        string? expected,
        string? observed)
    {
        if (string.Equals(expected, observed, StringComparison.Ordinal))
        {
            return;
        }

        failures.Add(new EvaluationFailure(field, expected, observed));
    }

    private static string? StartingFieldValue(
        EvaluationCandidateSetup? candidate,
        EvaluationCandidateField field) => field switch
        {
            EvaluationCandidateField.ClientId => candidate?.ClientId,
            EvaluationCandidateField.EnvironmentId => candidate?.EnvironmentId,
            EvaluationCandidateField.RequestedRoleId => candidate?.RequestedRoleId,
            EvaluationCandidateField.Justification => FormatBoolean(
                candidate?.Justification is not null),
            EvaluationCandidateField.IncidentId => candidate?.IncidentId,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static string? FinalFieldValue(
        FinalCandidateFacts? candidate,
        EvaluationCandidateField field) => field switch
        {
            EvaluationCandidateField.ClientId => candidate?.ClientId,
            EvaluationCandidateField.EnvironmentId => candidate?.EnvironmentId,
            EvaluationCandidateField.RequestedRoleId => candidate?.RequestedRoleId,
            EvaluationCandidateField.Justification => FormatBoolean(
                candidate?.HasJustification ?? false),
            EvaluationCandidateField.IncidentId => candidate?.IncidentId,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static string? ClearedFieldValue(EvaluationCandidateField field) =>
        field == EvaluationCandidateField.Justification
            ? FormatBoolean(false)
            : null;

    private static string FieldName(EvaluationCandidateField field) =>
        JsonNamingPolicy.CamelCase.ConvertName(field.ToString());

    private static string EnumName<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string? FormatValue<T>(T value) => value switch
    {
        null => null,
        bool boolean => FormatBoolean(boolean),
        Enum enumeration => JsonNamingPolicy.CamelCase.ConvertName(enumeration.ToString()),
        _ => value.ToString(),
    };

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string FormatList(IEnumerable<string> values) =>
        $"[{string.Join(", ", values)}]";

    private static string FormatSideEffects(WorkflowSideEffectCounts sideEffects) =>
        $"requests={sideEffects.Requests}, decisions={sideEffects.ApprovalDecisions}, operations={sideEffects.ProvisioningOperations}, grants={sideEffects.AccessGrants}";
}

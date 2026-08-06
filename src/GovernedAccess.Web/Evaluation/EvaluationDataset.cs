namespace GovernedAccess.Web.Evaluation;

internal enum EvaluationCategory
{
    SuccessfulResolution,
    ClarificationOrNoMatch,
    IdentifierHandling,
    MultiTurn,
    ValidationConflict,
    SafetyBoundary,
}

internal enum NormalizedIntakeOutcome
{
    Ready,
    Incomplete,
    Clarification,
    Rejected,
    ProviderFailure,
    Cancelled,
}

internal enum EvaluationCandidateField
{
    ClientId,
    EnvironmentId,
    RequestedRoleId,
    Justification,
    IncidentId,
}

internal enum EvaluationClarificationTarget
{
    EnvironmentId,
    RequestedRoleId,
    Justification,
    IncidentId,
}

// Dataset expectations must distinguish an omitted property from an explicit null.
internal readonly record struct EvaluationExpectedValue<T>(
    bool IsDeclared,
    T Value)
{
    internal static EvaluationExpectedValue<T> Declared(T value) => new(true, value);
}

internal sealed record EvaluationCandidateSetup(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRoleId,
    string? Justification,
    string? IncidentId);

internal sealed record EvaluationCandidateExpectation(
    EvaluationExpectedValue<string?> ClientId,
    EvaluationExpectedValue<string?> EnvironmentId,
    EvaluationExpectedValue<string?> RequestedRoleId,
    EvaluationExpectedValue<bool> HasJustification,
    EvaluationExpectedValue<string?> IncidentId);

internal sealed record FinalExpectation(
    NormalizedIntakeOutcome Outcome,
    EvaluationCandidateExpectation? Candidate,
    EvaluationExpectedValue<EvaluationClarificationTarget?> ClarificationTarget,
    EvaluationExpectedValue<IReadOnlyList<string>> EnvironmentOptionIds,
    EvaluationExpectedValue<IReadOnlyList<string>> ValidationCodes,
    IReadOnlyList<EvaluationCandidateField> PreservedFields,
    IReadOnlyList<EvaluationCandidateField> ClearedFields);

internal sealed record EvaluationTurn(
    string Id,
    string RequesterMessage);

internal sealed record EvaluationScenario(
    string Id,
    EvaluationCategory Category,
    EvaluationCandidateSetup? StartingCandidate,
    IReadOnlyList<EvaluationTurn> Turns,
    FinalExpectation Expected);

internal sealed record EvaluationDataset(
    int SchemaVersion,
    string DatasetVersion,
    IReadOnlyList<EvaluationScenario> Scenarios);

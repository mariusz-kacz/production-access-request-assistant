namespace GovernedAccess.Web.Evaluation;

internal enum EvaluationScenarioStatus
{
    Passed,
    Failed,
    Cancelled,
    NotRun,
}

internal enum EvaluationRunStatus
{
    Passed,
    Failed,
    Cancelled,
    PrerequisiteFailed,
}

internal sealed record FinalCandidateFacts(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRoleId,
    bool HasJustification,
    string? IncidentId);

internal sealed record FinalApplicationOutcome(
    NormalizedIntakeOutcome Kind,
    FinalCandidateFacts? Candidate,
    EvaluationClarificationTarget? ClarificationTarget,
    IReadOnlyList<string> EnvironmentOptionIds,
    IReadOnlyList<string> ValidationCodes);

internal sealed record WorkflowSideEffectCounts(
    int Requests,
    int ApprovalDecisions,
    int ProvisioningOperations,
    int AccessGrants)
{
    internal static WorkflowSideEffectCounts None { get; } = new(0, 0, 0, 0);

    internal bool HasAny =>
        Requests != 0
        || ApprovalDecisions != 0
        || ProvisioningOperations != 0
        || AccessGrants != 0;
}

internal sealed record EvaluationFailure(
    string Field,
    string? Expected,
    string? Observed);

internal sealed record EvaluationScenarioResult(
    string Id,
    EvaluationCategory Category,
    EvaluationScenarioStatus Status,
    FinalApplicationOutcome? FinalOutcome,
    long? ElapsedMilliseconds,
    WorkflowSideEffectCounts SideEffects,
    IReadOnlyList<EvaluationFailure> Failures);

internal sealed record EvaluationCategorySummary(
    EvaluationCategory Category,
    int Passed,
    int Total);

internal sealed record EvaluationSummary(
    int Total,
    int Passed,
    int RequiredPasses,
    bool SafetyPassed,
    IReadOnlyList<EvaluationCategorySummary> Categories);

internal sealed record EvaluationRunResult(
    Guid RunId,
    string DatasetVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    EvaluationRunStatus Status,
    string ModelDeployment,
    EvaluationSummary Summary,
    WorkflowSideEffectCounts SideEffects,
    IReadOnlyList<EvaluationScenarioResult> Scenarios);

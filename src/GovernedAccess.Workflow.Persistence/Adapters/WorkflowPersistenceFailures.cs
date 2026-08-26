using GovernedAccess.Core.Application;

namespace GovernedAccess.Workflow.Persistence;

internal static class WorkflowPersistenceFailures
{
    internal static ApplicationFailure NotFound() =>
        new(
            ApplicationFailureKind.NotFound,
            "request_preparation_not_found",
            "The request preparation was not found.");

    internal static ApplicationFailure Conflict() =>
        new(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_preparation_concurrency_conflict",
            "The request preparation changed while it was being saved.");

    internal static ApplicationFailure ActiveRace() =>
        new(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_preparation_active_race",
            "Another active request preparation won the conversation race.");

    internal static ApplicationFailure Unavailable() =>
        new(
            ApplicationFailureKind.DependencyUnavailable,
            "workflow_persistence_unavailable",
            "Workflow persistence is currently unavailable.");

    internal static ApplicationFailure MalformedState() =>
        new(
            ApplicationFailureKind.DependencyFailure,
            "request_preparation_malformed_state",
            "Stored request-preparation state is invalid.");

    internal static ApplicationFailure SaveFailed() =>
        new(
            ApplicationFailureKind.DependencyFailure,
            "request_preparation_persistence_failed",
            "The request preparation could not be saved.");
}

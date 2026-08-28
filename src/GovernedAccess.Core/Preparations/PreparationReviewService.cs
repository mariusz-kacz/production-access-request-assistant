using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;

namespace GovernedAccess.Core.Preparations;

public sealed record PreparationReview(
    Guid PreparationId,
    string RequesterDisplayName,
    string RequesterId,
    string ClientDisplayName,
    string ClientId,
    string EnvironmentDisplayName,
    string EnvironmentId,
    string RoleDisplayName,
    string RoleId,
    string? IncidentDisplayName,
    string? IncidentId,
    string Justification,
    DateTimeOffset ReadyDeadline);

public interface IPreparationReviewService
{
    Task<ApplicationResult<PreparationReview>> LoadAsync(
        PreparationSnapshot preparation,
        CancellationToken cancellationToken);
}

public sealed class PreparationReviewService(
    IAuthenticatedPrincipalReader principalReader,
    IProductionEnvironmentAuthority environmentAuthority,
    IEnvironmentRoleAuthority roleAuthority,
    IIncidentAuthority incidentAuthority) : IPreparationReviewService
{
    public async Task<ApplicationResult<PreparationReview>> LoadAsync(
        PreparationSnapshot preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var candidate = preparation.Candidate;
        if (preparation.Lifecycle != PreparationLifecycle.Ready
            || !candidate.IsComplete
            || preparation.ReadyDeadline is null)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                "prepared_request_not_ready",
                "Only an immutable ready preparation can be rendered for confirmation.");
        }

        var requesterResult = await principalReader.GetPrincipalAsync(
            preparation.Binding.RequesterId,
            cancellationToken);
        if (requesterResult.IsFailure)
        {
            return ApplicationResult.Failed<PreparationReview>(
                requesterResult.Failure!);
        }

        var requester = requesterResult.Value;
        if (!Matches(preparation.Binding.RequesterId, requester.Id)
            || requester.Kind != PrincipalKind.Requester)
        {
            return ContextMismatch();
        }

        var environmentResult = await environmentAuthority.GetAsync(
            candidate.EnvironmentId!,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return ApplicationResult.Failed<PreparationReview>(
                environmentResult.Failure!);
        }

        var environment = environmentResult.Value;
        if (!Matches(candidate.EnvironmentId!, environment.EnvironmentId)
            || !Matches(candidate.ClientId!, environment.ClientId)
            || !environment.CanBecomeCanonical)
        {
            return ContextMismatch();
        }

        var roleResult = await roleAuthority.GetAsync(
            candidate.EnvironmentId!,
            candidate.RoleId!,
            cancellationToken);
        if (roleResult.IsFailure)
        {
            return ApplicationResult.Failed<PreparationReview>(
                roleResult.Failure!);
        }

        var role = roleResult.Value;
        if (!Matches(candidate.EnvironmentId!, role.EnvironmentId)
            || !Matches(candidate.RoleId!, role.RoleId)
            || !role.IsCurrentlyAssignable)
        {
            return ContextMismatch();
        }

        IncidentAuthorityProjection? incident = null;
        if (candidate.IncidentId is not null)
        {
            var incidentResult = await incidentAuthority.GetAsync(
                candidate.IncidentId,
                cancellationToken);
            if (incidentResult.IsFailure)
            {
                return ApplicationResult.Failed<PreparationReview>(
                    incidentResult.Failure!);
            }

            incident = incidentResult.Value;
            if (!Matches(candidate.IncidentId, incident.IncidentId)
                || !incident.IsActive
                || !Matches(candidate.EnvironmentId!, incident.EnvironmentId))
            {
                return ContextMismatch();
            }
        }

        return ApplicationResult.Succeeded(
            new PreparationReview(
                preparation.PreparationId,
                requester.DisplayName,
                requester.Id,
                environment.ClientDisplayName,
                environment.ClientId,
                environment.DisplayName,
                environment.EnvironmentId,
                role.DisplayName,
                role.RoleId,
                incident?.Title,
                incident?.IncidentId,
                candidate.Justification!,
                preparation.ReadyDeadline.Value));
    }

    private static bool Matches(string expected, string? actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal);

    private static ApplicationResult<PreparationReview> ContextMismatch() =>
        Failed(
            ApplicationFailureKind.DependencyFailure,
            "prepared_card_context_mismatch",
            "The ready preparation could not be rendered from current authoritative context.");

    private static ApplicationResult<PreparationReview> Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        ApplicationResult.Failed<PreparationReview>(
            new ApplicationFailure(kind, code, message));
}

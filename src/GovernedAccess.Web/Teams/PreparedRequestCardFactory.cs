using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Assembles final card presentation facts from the delivered authoritative graph.
/// The pure card layout and action contract are shared with the target graph.
/// </summary>
public sealed class PreparedRequestCardFactory(IRequestContextReader requestContext)
{
    public const string AdaptiveCardContentType =
        TeamsAdaptiveCardRenderer.AdaptiveCardContentType;

    public const string ConfirmationVerb =
        TeamsAdaptiveCardRenderer.ConfirmationVerb;

    public const int ContractSchemaVersion =
        TeamsAdaptiveCardRenderer.ContractSchemaVersion;

    public async Task<ApplicationResult<Attachment>> CreateAsync(
        RequestIntakeSession session,
        string locale,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var details = session.PreparedDetails;
        if (details is null
            || session.ReservedRequestId is null
            || session.ExpiresAt is null)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                "prepared_request_not_ready",
                "Only a ready prepared request can be rendered for confirmation.");
        }

        var requesterResult = await requestContext.GetPrincipalAsync(
            session.RequesterId,
            cancellationToken);
        if (requesterResult.IsFailure)
        {
            return ApplicationResult.Failed<Attachment>(requesterResult.Failure!);
        }

        var requester = requesterResult.Value;
        if (!Matches(session.RequesterId, requester.Id)
            || requester.Kind != PrincipalKind.Requester)
        {
            return ContextMismatch();
        }

        var environmentResult =
            await requestContext.GetProductionEnvironmentContextAsync(
                details.EnvironmentId,
                cancellationToken);
        if (environmentResult.IsFailure)
        {
            return ApplicationResult.Failed<Attachment>(
                environmentResult.Failure!);
        }

        var environmentContext = environmentResult.Value;
        var client = environmentContext.Client;
        var environment = environmentContext.Environment;
        var role = environmentContext.AssignedRoles.SingleOrDefault(
            candidate => Matches(details.RoleId, candidate.RoleId));
        if (!Matches(details.ClientId, client.Id) || role is null)
        {
            return ContextMismatch();
        }

        Incident? incident = null;
        if (details.IncidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(
                details.IncidentId,
                cancellationToken);
            if (incidentResult.IsFailure)
            {
                return ApplicationResult.Failed<Attachment>(
                    incidentResult.Failure!);
            }

            incident = incidentResult.Value;
            if (!Matches(details.IncidentId, incident.Id)
                || !Matches(details.EnvironmentId, incident.EnvironmentId))
            {
                return ContextMismatch();
            }
        }

        return ApplicationResult.Succeeded(
            TeamsAdaptiveCardRenderer.CreateReadyCard(
                new TeamsReadyCardPresentation(
                    requester.DisplayName,
                    requester.Id,
                    client.DisplayName,
                    details.ClientId,
                    environment.DisplayName,
                    details.EnvironmentId,
                    GetRoleDisplayName(details.RoleId),
                    details.RoleId,
                    incident?.Title,
                    incident?.Id,
                    details.Justification,
                    session.ExpiresAt.Value,
                    locale,
                    session.Id)));
    }

    private static string GetRoleDisplayName(string roleId) =>
        roleId switch
        {
            ProductionRoleIds.ReadOnly => "Production read-only",
            ProductionRoleIds.Support => "Production support",
            ProductionRoleIds.Deployment => "Production deployment",
            _ => throw new InvalidOperationException(
                "The persisted prepared role is unsupported."),
        };

    private static bool Matches(string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.Ordinal);

    private static ApplicationResult<Attachment> ContextMismatch() =>
        Failed(
            ApplicationFailureKind.DependencyFailure,
            "prepared_card_context_mismatch",
            "The prepared request could not be rendered from authoritative context.");

    private static ApplicationResult<Attachment> Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        ApplicationResult.Failed<Attachment>(
            new ApplicationFailure(kind, code, message));
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Renders an immutable prepared snapshot as the application-owned Adaptive Card
/// contract. Authoritative lookups supply display text only; identifiers, scope,
/// expiry, and action data always come from the persisted snapshot.
/// </summary>
public sealed class PreparedRequestCardFactory(IRequestContextReader requestContext)
{
    public const string AdaptiveCardContentType =
        "application/vnd.microsoft.card.adaptive";

    public const string ConfirmationVerb = "confirmAndSubmit";

    public const int ContractSchemaVersion = 1;

    public async Task<ApplicationResult<Attachment>> CreateAsync(
        RequestIntakeSession session,
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

        var facts = new JsonArray
        {
            CreateFact(
                "Client",
                FormatDisplayValue(
                    client.DisplayName,
                    details.ClientId)),
            CreateFact(
                "Environment",
                FormatDisplayValue(
                    environment.DisplayName,
                    details.EnvironmentId)),
            CreateFact(
                "Requested role",
                FormatDisplayValue(
                    GetRoleDisplayName(details.RoleId),
                    details.RoleId)),
        };

        if (incident is not null)
        {
            facts.Add(
                CreateFact(
                    "Incident",
                    FormatDisplayValue(
                        incident.Title,
                        details.IncidentId!)));
        }

        facts.Add(CreateFact("Access lifetime", "8 hours after provisioning"));
        facts.Add(
            CreateFact(
                "Confirm by",
                session.ExpiresAt.Value.UtcDateTime.ToString(
                    "O",
                    CultureInfo.InvariantCulture)));

        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["size"] = "Large",
                    ["weight"] = "Bolder",
                    ["text"] = "Review request draft",
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] =
                        "Review the draft below. To change any details, send another message. Confirming submits it for business approval; it does not approve or grant production access.",
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "FactSet",
                    ["facts"] = facts,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["weight"] = "Bolder",
                    ["text"] = "Justification",
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = details.Justification,
                    ["wrap"] = true,
                },
            },
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Action.Execute",
                    ["title"] = "Confirm and submit",
                    ["verb"] = ConfirmationVerb,
                    ["associatedInputs"] = "none",
                    ["data"] = new JsonObject
                    {
                        ["schemaVersion"] = ContractSchemaVersion,
                        ["preparedRequestId"] =
                            session.Id.ToString("D"),
                    },
                },
            },
        };

        return ApplicationResult.Succeeded(
            new Attachment
            {
                ContentType = AdaptiveCardContentType,
                Content = JsonSerializer.SerializeToElement(card),
            });
    }

    private static JsonObject CreateFact(string title, string value) =>
        new()
        {
            ["title"] = title,
            ["value"] = value,
        };

    private static string FormatDisplayValue(
        string displayName,
        string identifier) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{displayName} ({identifier})");

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

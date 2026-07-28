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
        PreparedAccessRequest preparedRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparedRequest);

        if (preparedRequest.Status != PreparedAccessRequestStatus.Ready)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                "prepared_request_not_ready",
                "Only a ready prepared request can be rendered for confirmation.");
        }

        var clientResult = await requestContext.GetClientAsync(
            preparedRequest.ClientId,
            cancellationToken);
        if (clientResult.IsFailure)
        {
            return ApplicationResult.Failed<Attachment>(clientResult.Failure!);
        }

        var environmentResult =
            await requestContext.GetProductionEnvironmentAsync(
                preparedRequest.EnvironmentId,
                cancellationToken);
        if (environmentResult.IsFailure)
        {
            return ApplicationResult.Failed<Attachment>(
                environmentResult.Failure!);
        }

        var roleResult = await requestContext.GetEnvironmentRoleAsync(
            preparedRequest.EnvironmentId,
            preparedRequest.RequestedRoleId,
            cancellationToken);
        if (roleResult.IsFailure)
        {
            return ApplicationResult.Failed<Attachment>(roleResult.Failure!);
        }

        var client = clientResult.Value;
        var environment = environmentResult.Value;
        var role = roleResult.Value;

        if (!Matches(preparedRequest.ClientId, client.Id)
            || !Matches(preparedRequest.EnvironmentId, environment.Id)
            || !Matches(preparedRequest.ClientId, environment.ClientId)
            || !Matches(preparedRequest.EnvironmentId, role.EnvironmentId)
            || !Matches(preparedRequest.RequestedRoleId, role.RoleId))
        {
            return ContextMismatch();
        }

        Incident? incident = null;
        if (preparedRequest.IncidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(
                preparedRequest.IncidentId,
                cancellationToken);
            if (incidentResult.IsFailure)
            {
                return ApplicationResult.Failed<Attachment>(
                    incidentResult.Failure!);
            }

            incident = incidentResult.Value;
            if (!Matches(preparedRequest.IncidentId, incident.Id)
                || !Matches(preparedRequest.ClientId, incident.ClientId)
                || (incident.EnvironmentId is not null
                    && !Matches(
                        preparedRequest.EnvironmentId,
                        incident.EnvironmentId)))
            {
                return ContextMismatch();
            }
        }

        var facts = new JsonArray
        {
            CreateFact(
                "Request ID",
                preparedRequest.ReservedRequestId.ToString("D")),
            CreateFact(
                "Client",
                FormatDisplayValue(
                    client.DisplayName,
                    preparedRequest.ClientId)),
            CreateFact(
                "Environment",
                FormatDisplayValue(
                    environment.DisplayName,
                    preparedRequest.EnvironmentId)),
            CreateFact(
                "Requested role",
                FormatDisplayValue(
                    GetRoleDisplayName(preparedRequest.RequestedRoleId),
                    preparedRequest.RequestedRoleId)),
        };

        if (incident is not null)
        {
            facts.Add(
                CreateFact(
                    "Incident",
                    FormatDisplayValue(
                        incident.Title,
                        preparedRequest.IncidentId!)));
        }

        facts.Add(CreateFact("Access lifetime", "8 hours after provisioning"));
        facts.Add(
            CreateFact(
                "Confirm by",
                preparedRequest.ExpiresAt.UtcDateTime.ToString(
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
                    ["text"] = "Confirm production access request",
                    ["wrap"] = true,
                },
                new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] =
                        "Review the immutable request below. Confirming submits it for business approval; it does not approve or grant production access.",
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
                    ["text"] = preparedRequest.Justification,
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
                            preparedRequest.PreparationId.ToString("D"),
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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal static class TeamsAdaptiveCardRenderer
{
    internal const string AdaptiveCardContentType =
        "application/vnd.microsoft.card.adaptive";

    internal const string ConfirmationVerb = "confirmAndSubmit";

    internal const int ContractSchemaVersion = 1;

    internal static Attachment CreateReadyCard(
        TeamsReadyCardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var facts = new JsonArray
        {
            CreateFact(
                "Requester",
                FormatDisplayValue(
                    presentation.RequesterDisplayName,
                    presentation.RequesterId)),
            CreateFact(
                "Client",
                FormatDisplayValue(
                    presentation.ClientDisplayName,
                    presentation.ClientId)),
            CreateFact(
                "Environment",
                FormatDisplayValue(
                    presentation.EnvironmentDisplayName,
                    presentation.EnvironmentId)),
            CreateFact(
                "Requested role",
                FormatDisplayValue(
                    presentation.RoleDisplayName,
                    presentation.RoleId)),
            CreateFact(
                "Incident",
                presentation.IncidentId is null
                    ? "No incident"
                    : FormatDisplayValue(
                        presentation.IncidentDisplayName!,
                        presentation.IncidentId)),
            CreateFact("Requested access duration", "8 hours"),
            CreateFact(
                "Confirm before",
                FormatDeadline(
                    presentation.ReadyDeadline,
                    presentation.Locale)),
        };

        var card = CreateCard(
            new JsonArray
            {
                CreateTitle("Review request draft", "Large"),
                CreateText(
                    "Review the draft below. To change any details, send another message. Confirming submits it for business approval; it does not approve or grant production access."),
                new JsonObject
                {
                    ["type"] = "FactSet",
                    ["facts"] = facts,
                },
                CreateTitle("Justification", size: null),
                CreateText(presentation.Justification),
            });
        card["actions"] = new JsonArray
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
                    ["preparationId"] = presentation.PreparationId.ToString("D"),
                },
            },
        };

        return CreateAttachment(card);
    }

    internal static Attachment CreateStatusCard(
        TeamsStatusCardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return CreateAttachment(
            CreateCard(
                new JsonArray
                {
                    CreateTitle(presentation.Title, "Medium"),
                    CreateText(presentation.Message),
                }));
    }

    private static string FormatDeadline(
        DateTimeOffset deadline,
        string locale)
    {
        var culture = CultureInfo.GetCultureInfo(TeamsLocale.Resolve(locale));
        var localDate = deadline.ToString(
            "dddd, MMMM d, yyyy h:mm tt",
            culture);
        var offset = deadline.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{localDate} (UTC{sign}{offset.Hours:00}:{offset.Minutes:00})");
    }

    private static string FormatDisplayValue(
        string displayName,
        string identifier) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{displayName} ({identifier})");

    private static JsonObject CreateCard(JsonArray body) =>
        new()
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
        };

    private static JsonObject CreateFact(string title, string value) =>
        new()
        {
            ["title"] = title,
            ["value"] = value,
        };

    private static JsonObject CreateTitle(string text, string? size)
    {
        var title = CreateText(text);
        title["weight"] = "Bolder";
        if (size is not null)
        {
            title["size"] = size;
        }

        return title;
    }

    private static JsonObject CreateText(string text) =>
        new()
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["wrap"] = true,
        };

    private static Attachment CreateAttachment(JsonObject card) =>
        new()
        {
            ContentType = AdaptiveCardContentType,
            Content = JsonSerializer.SerializeToElement(card),
        };
}

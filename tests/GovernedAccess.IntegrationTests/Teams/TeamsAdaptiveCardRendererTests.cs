using System.Text.Json;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsAdaptiveCardRendererTests
{
    [Fact]
    public void ReadyCardContainsCanonicalFactsAndOnlySafeConfirmationData()
    {
        var preparationId = Guid.NewGuid();
        var review = new PreparationReview(
            preparationId,
            "Demo Requester",
            "requester",
            "Client <Alpha>",
            "client-alpha",
            "Primary \"production\"",
            "PROD-ALPHA-EU",
            "Production read-only",
            "ProductionReadOnly",
            IncidentDisplayName: null,
            IncidentId: null,
            "Investigate </TextBlock><script>alert('x')</script>",
            new DateTimeOffset(2026, 8, 26, 12, 30, 0, TimeSpan.Zero));

        var attachment = TeamsAdaptiveCardRenderer.CreateReadyCard(review, "en-US");

        Assert.Equal(
            TeamsAdaptiveCardRenderer.AdaptiveCardContentType,
            attachment.ContentType);
        var card = Assert.IsType<JsonElement>(attachment.Content);
        var factValues = card.GetProperty("body")
            .EnumerateArray()
            .Where(item => item.TryGetProperty("facts", out _))
            .SelectMany(item => item.GetProperty("facts").EnumerateArray())
            .Select(fact => fact.GetProperty("value").GetString())
            .ToArray();
        Assert.Contains("Demo Requester (requester)", factValues);
        Assert.Contains("Client <Alpha> (client-alpha)", factValues);
        Assert.Contains("Primary \"production\" (PROD-ALPHA-EU)", factValues);
        Assert.Contains("Production read-only (ProductionReadOnly)", factValues);
        Assert.Contains("8 hours", factValues);
        Assert.Contains(
            card.GetProperty("body").EnumerateArray(),
            item => item.TryGetProperty("text", out var text)
                && text.GetString()
                    == "Investigate </TextBlock><script>alert('x')</script>");

        var action = Assert.Single(card.GetProperty("actions").EnumerateArray());
        Assert.Equal(
            TeamsAdaptiveCardRenderer.ConfirmationVerb,
            action.GetProperty("verb").GetString());
        var data = action.GetProperty("data");
        Assert.Equal(2, data.EnumerateObject().Count());
        Assert.Equal(
            TeamsAdaptiveCardRenderer.ContractSchemaVersion,
            data.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            preparationId.ToString("D"),
            data.GetProperty("preparationId").GetString());
        Assert.False(data.TryGetProperty("preparedRequestId", out _));
    }

    [Fact]
    public void StatusCardHasNoActionSurface()
    {
        var attachment = TeamsAdaptiveCardRenderer.CreateStatusCard(
            "Draft replaced",
            "Use the latest request draft card.");

        var card = Assert.IsType<JsonElement>(attachment.Content);
        Assert.False(card.TryGetProperty("actions", out _));
        Assert.Contains("Draft replaced", card.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadyCardRendersAnApplicationOwnedNoticeInsideTheCard()
    {
        var attachment = TeamsAdaptiveCardRenderer.CreateReadyCard(
            new PreparationReview(
                Guid.NewGuid(),
                "Demo Requester",
                "requester",
                "Client Alpha",
                "client-alpha",
                "Primary production",
                "PROD-ALPHA-EU",
                "Production read-only",
                "ProductionReadOnly",
                IncidentDisplayName: null,
                IncidentId: null,
                "Investigate elevated customer errors.",
                new DateTimeOffset(2026, 8, 26, 12, 30, 0, TimeSpan.Zero)),
            "en-US",
            "Authoritative production context changed. Review this replacement.");

        var card = Assert.IsType<JsonElement>(attachment.Content);
        Assert.Contains(
            "Authoritative production context changed. Review this replacement.",
            card.GetRawText(),
            StringComparison.Ordinal);
        Assert.Single(card.GetProperty("actions").EnumerateArray());
    }
}

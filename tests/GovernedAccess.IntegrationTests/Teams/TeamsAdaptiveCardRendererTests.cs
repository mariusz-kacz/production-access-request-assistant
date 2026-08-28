using System.Text.Json;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsAdaptiveCardRendererTests
{
    [Fact]
    public void ReadyCardUsesTheFinalDeterministicIntakeContract()
    {
        var preparationId = Guid.NewGuid();
        var presentation = new TeamsReadyCardPresentation(
            "Demo Requester",
            "requester",
            "Client <Alpha>",
            "client-alpha",
            "Primary \"production\"",
            "PROD-ALPHA-EU",
            "Production read-only",
            "ProductionReadOnly",
            incidentDisplayName: null,
            incidentId: null,
            "Investigate </TextBlock><script>alert('x')</script>",
            new DateTimeOffset(2026, 8, 26, 12, 30, 0, TimeSpan.Zero),
            "en-US",
            preparationId);

        var attachment = TeamsAdaptiveCardRenderer.CreateReadyCard(presentation);

        Assert.Equal(
            TeamsAdaptiveCardRenderer.AdaptiveCardContentType,
            attachment.ContentType);
        var card = Assert.IsType<JsonElement>(attachment.Content);
        var facts = card.GetProperty("body")[2].GetProperty("facts");
        Assert.Equal(7, facts.GetArrayLength());
        AssertFact(facts[0], "Requester", "Demo Requester (requester)");
        AssertFact(facts[1], "Client", "Client <Alpha> (client-alpha)");
        AssertFact(
            facts[2],
            "Environment",
            "Primary \"production\" (PROD-ALPHA-EU)");
        AssertFact(
            facts[3],
            "Requested role",
            "Production read-only (ProductionReadOnly)");
        AssertFact(facts[4], "Incident", "No incident");
        AssertFact(facts[5], "Requested access duration", "8 hours");
        AssertFact(
            facts[6],
            "Confirm before",
            "Wednesday, August 26, 2026 12:30 PM (UTC+00:00)");
        Assert.Equal(
            "Investigate </TextBlock><script>alert('x')</script>",
            card.GetProperty("body")[4].GetProperty("text").GetString());

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
        Assert.Equal(
            "Draft replaced",
            card.GetProperty("body")[0].GetProperty("text").GetString());
    }

    private static void AssertFact(
        JsonElement fact,
        string expectedTitle,
        string expectedValue)
    {
        Assert.Equal(expectedTitle, fact.GetProperty("title").GetString());
        Assert.Equal(expectedValue, fact.GetProperty("value").GetString());
    }
}

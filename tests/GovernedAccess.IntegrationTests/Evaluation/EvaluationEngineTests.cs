using System.Text;
using System.Text.Json;
using GovernedAccess.Web.Evaluation;

namespace GovernedAccess.IntegrationTests.Evaluation;

public sealed class EvaluationEngineTests
{
    private static readonly string[] ScenarioIds =
    [
        "RES-01", "RES-02", "RES-03", "RES-04", "RES-05",
        "CLR-01", "CLR-02", "CLR-03", "CLR-04",
        "IDF-01", "IDF-02", "IDF-03",
        "MTN-01", "MTN-02", "MTN-03",
        "VAL-01", "VAL-02",
        "SAFE-01",
    ];

    [Fact]
    public async Task SchemaValidDatasetLoadsAndPreservesDeclaredNull()
    {
        await using var stream = CreateDatasetStream(schemaVersion: 1);

        var dataset = await EvaluationDatasetLoader.LoadAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(18, dataset.Scenarios.Count);
        var candidate = Assert.IsType<EvaluationCandidateExpectation>(
            dataset.Scenarios[0].Expected.Candidate);
        Assert.True(candidate.ClientId.IsDeclared);
        Assert.Null(candidate.ClientId.Value);
        Assert.False(candidate.EnvironmentId.IsDeclared);
    }

    [Fact]
    public async Task UnsupportedSchemaVersionFailsBeforeDeserialization()
    {
        await using var stream = CreateDatasetStream(schemaVersion: 2);

        var exception = await Assert.ThrowsAsync<EvaluationDatasetException>(
            () => EvaluationDatasetLoader.LoadAsync(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Contains("version 1 schema", exception.Message, StringComparison.Ordinal);
    }

    private static MemoryStream CreateDatasetStream(int schemaVersion)
    {
        var scenarios = ScenarioIds.Select(
            (id, index) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = CategoryFor(id),
                ["turns"] = new[]
                {
                    new
                    {
                        id = "turn-1",
                        requesterMessage = $"Synthetic requester message {index + 1}.",
                    },
                },
                ["expected"] = index == 0
                    ? new
                    {
                        outcome = "incomplete",
                        candidate = new Dictionary<string, object?>
                        {
                            ["clientId"] = null,
                        },
                    }
                    : new { outcome = "incomplete" },
            });

        var json = JsonSerializer.Serialize(
            new
            {
                schemaVersion,
                datasetVersion = "1.0.0",
                scenarios,
            });
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static string CategoryFor(string scenarioId) => scenarioId[..3] switch
    {
        "RES" => "successfulResolution",
        "CLR" => "clarificationOrNoMatch",
        "IDF" => "identifierHandling",
        "MTN" => "multiTurn",
        "VAL" => "validationConflict",
        _ => "safetyBoundary",
    };
}

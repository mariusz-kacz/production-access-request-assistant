using GovernedAccess.Web.Ai;
using Microsoft.Extensions.Configuration;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class AgentExecutionLimitsTests
{
    private static readonly IReadOnlyDictionary<string, string?> ValidConfiguration =
        new Dictionary<string, string?>
        {
            ["RequestPreparationAgent:Limits:MaximumMessageCharacters"] = "4000",
            ["RequestPreparationAgent:Limits:MaximumCallsPerTool"] = "1",
            ["RequestPreparationAgent:Limits:MaximumToolCalls"] = "4",
            ["RequestPreparationAgent:Limits:MaximumProviderIterations"] = "6",
            ["RequestPreparationAgent:Limits:CumulativeTimeout"] = "00:00:30",
        };

    [Fact]
    public void ExactDocumentedLimitsLoadSuccessfully()
    {
        var configuration = Configuration(ValidConfiguration);

        var limits = AgentExecutionLimits.Load(configuration);

        Assert.Equal(4000, limits.MaximumMessageCharacters);
        Assert.Equal(1, limits.MaximumCallsPerTool);
        Assert.Equal(4, limits.MaximumToolCalls);
        Assert.Equal(6, limits.MaximumProviderIterations);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.CumulativeTimeout);
    }

    [Fact]
    public void MissingLimitsFailClosed()
    {
        string[] requiredNames =
        [
            "MaximumMessageCharacters",
            "MaximumCallsPerTool",
            "MaximumToolCalls",
            "MaximumProviderIterations",
            "CumulativeTimeout",
        ];

        foreach (var missingName in requiredNames)
        {
            var values = ValidConfiguration
                .Where(pair => !pair.Key.EndsWith(
                    missingName,
                    StringComparison.Ordinal))
                .ToDictionary();

            Assert.Throws<InvalidOperationException>(
                () => AgentExecutionLimits.Load(Configuration(values)));
        }
    }

    [Fact]
    public void NonpositiveOrAboveHardMaximumLimitsFailClosed()
    {
        (string Name, string Value)[] invalidLimits =
        [
            ("MaximumMessageCharacters", "4001"),
            ("MaximumCallsPerTool", "2"),
            ("MaximumToolCalls", "5"),
            ("MaximumProviderIterations", "7"),
            ("CumulativeTimeout", "00:00:31"),
            ("MaximumMessageCharacters", "0"),
        ];

        foreach (var (name, value) in invalidLimits)
        {
            var values = ValidConfiguration.ToDictionary();
            values[$"RequestPreparationAgent:Limits:{name}"] = value;

            Assert.Throws<InvalidOperationException>(
                () => AgentExecutionLimits.Load(Configuration(values)));
        }
    }

    [Fact]
    public void OneProviderIterationIsValidWithoutARepairPolicy()
    {
        var values = ValidConfiguration.ToDictionary();
        values["RequestPreparationAgent:Limits:MaximumProviderIterations"] = "1";

        var limits = AgentExecutionLimits.Load(Configuration(values));

        Assert.Equal(1, limits.MaximumProviderIterations);
    }

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

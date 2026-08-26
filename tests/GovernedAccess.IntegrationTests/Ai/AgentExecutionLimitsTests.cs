using GovernedAccess.Web.Ai;
using Microsoft.Extensions.Configuration;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class AgentExecutionLimitsTests
{
    private static readonly IReadOnlyDictionary<string, string?> ValidConfiguration =
        new Dictionary<string, string?>
        {
            ["TargetRequestPreparationAgent:Limits:MaximumMessageCharacters"] = "4000",
            ["TargetRequestPreparationAgent:Limits:MaximumInterpretedTurns"] = "50",
            ["TargetRequestPreparationAgent:Limits:MaximumCallsPerTool"] = "1",
            ["TargetRequestPreparationAgent:Limits:MaximumToolCalls"] = "4",
            ["TargetRequestPreparationAgent:Limits:MaximumProviderIterations"] = "6",
            ["TargetRequestPreparationAgent:Limits:CumulativeTimeout"] = "00:00:30",
        };

    [Fact]
    public void ExactDocumentedLimitsLoadSuccessfully()
    {
        var configuration = Configuration(ValidConfiguration);

        var limits = AgentExecutionLimits.Load(configuration);

        Assert.Equal(4000, limits.MaximumMessageCharacters);
        Assert.Equal(50, limits.MaximumInterpretedTurns);
        Assert.Equal(1, limits.MaximumCallsPerTool);
        Assert.Equal(4, limits.MaximumToolCalls);
        Assert.Equal(6, limits.MaximumProviderIterations);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.CumulativeTimeout);
    }

    [Theory]
    [InlineData("MaximumMessageCharacters")]
    [InlineData("MaximumInterpretedTurns")]
    [InlineData("MaximumCallsPerTool")]
    [InlineData("MaximumToolCalls")]
    [InlineData("MaximumProviderIterations")]
    [InlineData("CumulativeTimeout")]
    public void MissingLimitsFailClosed(string missingName)
    {
        var values = ValidConfiguration
            .Where(pair => !pair.Key.EndsWith(missingName, StringComparison.Ordinal))
            .ToDictionary();

        Assert.Throws<InvalidOperationException>(
            () => AgentExecutionLimits.Load(Configuration(values)));
    }

    [Theory]
    [InlineData("MaximumMessageCharacters", "4001")]
    [InlineData("MaximumInterpretedTurns", "51")]
    [InlineData("MaximumCallsPerTool", "2")]
    [InlineData("MaximumToolCalls", "5")]
    [InlineData("MaximumProviderIterations", "7")]
    [InlineData("CumulativeTimeout", "00:00:31")]
    [InlineData("MaximumMessageCharacters", "0")]
    public void NonpositiveOrAboveHardMaximumLimitsFailClosed(
        string name,
        string value)
    {
        var values = ValidConfiguration.ToDictionary();
        values[$"TargetRequestPreparationAgent:Limits:{name}"] = value;

        Assert.Throws<InvalidOperationException>(
            () => AgentExecutionLimits.Load(Configuration(values)));
    }

    [Fact]
    public void OneProviderIterationIsValidWithoutARepairPolicy()
    {
        var values = ValidConfiguration.ToDictionary();
        values["TargetRequestPreparationAgent:Limits:MaximumProviderIterations"] = "1";

        var limits = AgentExecutionLimits.Load(Configuration(values));

        Assert.Equal(1, limits.MaximumProviderIterations);
    }

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

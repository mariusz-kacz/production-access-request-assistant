using System.Globalization;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class RoutedAssistantOptionsTests
{
    public static TheoryData<string, string, string?> InvalidLimits
    {
        get
        {
            TheoryData<string, string, string?> cases = [];
            (string Component, string Field, int Maximum)[] integerLimits =
            [
                ("Router", "MaximumOutputTokens", 100),
                ("PolicyAdvisor", "MaximumOutputTokens", 800),
                ("Retrieval", "MaximumChunks", 3),
                ("Retrieval", "MaximumApproximateTokens", 1500),
                ("Embedding", "Dimensions", 3072),
            ];
            foreach (var (component, setting, maximum) in integerLimits)
            {
                foreach (var value in new[] { null, "0", "-1", "invalid",
                    (maximum + 1).ToString(CultureInfo.InvariantCulture) })
                {
                    cases.Add(component, setting, value);
                }
            }

            (string Component, int Seconds)[] timeouts =
                [("Router", 8), ("PolicyAdvisor", 30), ("Retrieval", 30), ("Embedding", 30), ("Turn", 70)];
            foreach (var (component, seconds) in timeouts)
            {
                foreach (var value in new[] { null, "00:00:00", "invalid",
                    TimeSpan.FromSeconds(seconds + 1).ToString("c", CultureInfo.InvariantCulture) })
                {
                    cases.Add(component, "Timeout", value);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidLimits))]
    public void MissingMalformedOrExcessiveLimitsFailClosed(
        string component, string field, string? value)
    {
        var key = $"RoutedAssistant:{component}:{field}";
        var configuration = Configuration(new Dictionary<string, string?> { [key] = value });

        var exception = Assert.Throws<InvalidOperationException>(() => Load(component, configuration));

        Assert.Contains(key, exception.Message);
    }

    [Theory]
    [InlineData("Router", "Tools")]
    [InlineData("PolicyAdvisor", "ExecutionProfile")]
    [InlineData("Retrieval", "Filter")]
    [InlineData("Embedding", "FallbackDeployment")]
    [InlineData("Turn", "RetryCount")]
    public void UnknownSettingsCannotExpandTheClosedConfiguration(string component, string field)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [$"RoutedAssistant:{component}:{field}"] = "untrusted-extra-setting",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => Load(component, configuration));

        Assert.DoesNotContain("untrusted-extra-setting", exception.Message);
    }

    [Theory]
    [InlineData("Router", "Endpoint", "https://example.com/openai/v1")]
    [InlineData("PolicyAdvisor", "Endpoint", "http://synthetic.services.ai.azure.com/openai/v1")]
    [InlineData("Embedding", "Endpoint", "https://synthetic.services.ai.azure.com/openai/v1?secret=value")]
    [InlineData("Retrieval", "Endpoint", "https://synthetic.search.windows.net.evil.example/")]
    [InlineData("Retrieval", "Endpoint", "https://synthetic.search.windows.net/indexes/other")]
    [InlineData("Retrieval", "IndexName", "Invalid Index")]
    [InlineData("Retrieval", "IndexName", "policy--index")]
    [InlineData("Retrieval", "IndexName", "policy__index")]
    [InlineData("Router", "DeploymentName", "")]
    [InlineData("PolicyAdvisor", "DeploymentName", null)]
    [InlineData("Embedding", "DeploymentName", "invalid\ndeployment")]
    public void InvalidProviderCoordinatesFailWithSafeDiagnostics(
        string component, string field, string? value)
    {
        var key = $"RoutedAssistant:{component}:{field}";
        var configuration = Configuration(new Dictionary<string, string?> { [key] = value });

        var exception = Assert.Throws<InvalidOperationException>(() => Load(component, configuration));

        Assert.Contains(key, exception.Message);
        if (!string.IsNullOrEmpty(value))
        {
            Assert.DoesNotContain(value, exception.Message);
        }
    }

    [Fact]
    public void ComponentsResolveIndependentlyWithoutChangingTheAccessClient()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["RoutedAssistant:Router:DeploymentName"] = null,
            ["RoutedAssistant:PolicyAdvisor:Timeout"] = "00:00:12",
            ["RoutedAssistant:PolicyAdvisor:MaximumOutputTokens"] = "400",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequestPreparationChat(configuration);
        services.AddRoutedAssistantOptions(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<RouterModelOptions>());
        var policy = provider.GetRequiredService<PolicyAdvisorModelOptions>();
        Assert.Equal("synthetic-policy", policy.DeploymentName);
        Assert.Equal(TimeSpan.FromSeconds(12), policy.Timeout);
        Assert.Equal(400, policy.MaximumOutputTokens);
        Assert.Equal("synthetic-embedding", provider.GetRequiredService<PolicyEmbeddingOptions>().DeploymentName);
        Assert.Equal("synthetic_policy_index_", provider.GetRequiredService<PolicyRetrievalOptions>().IndexName);
        Assert.Equal(TimeSpan.FromSeconds(70), provider.GetRequiredService<RoutedTurnOptions>().Timeout);
        Assert.NotNull(provider.GetRequiredService<IChatClient>().GetService<DeterministicChatClient>());
    }

    [Fact]
    public void DocumentedCapsLoadAndMissingOperatorCoordinatesRemainUnavailable()
    {
        var configured = Configuration();
        Assert.Equal(100, RoutedAssistantOptions.LoadRouter(configured).MaximumOutputTokens);
        Assert.Equal(800, RoutedAssistantOptions.LoadPolicyAdvisor(configured).MaximumOutputTokens);
        Assert.Equal(3, RoutedAssistantOptions.LoadRetrieval(configured).MaximumChunks);
        Assert.Equal(1500, RoutedAssistantOptions.LoadRetrieval(configured).MaximumApproximateTokens);
        Assert.Equal(3072, RoutedAssistantOptions.LoadEmbedding(configured).Dimensions);

        var defaults = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();
        foreach (var component in new[] { "Router", "PolicyAdvisor", "Retrieval", "Embedding" })
        {
            Assert.Throws<InvalidOperationException>(() => Load(component, defaults));
        }
    }

    private static object Load(string component, IConfiguration configuration) => component switch
    {
        "Router" => RoutedAssistantOptions.LoadRouter(configuration),
        "PolicyAdvisor" => RoutedAssistantOptions.LoadPolicyAdvisor(configuration),
        "Retrieval" => RoutedAssistantOptions.LoadRetrieval(configuration),
        "Embedding" => RoutedAssistantOptions.LoadEmbedding(configuration),
        "Turn" => RoutedAssistantOptions.LoadTurn(configuration),
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static IConfiguration Configuration(Dictionary<string, string?>? overrides = null) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoutedAssistant:Router:Endpoint"] = "https://synthetic-router.services.ai.azure.com/openai/v1",
                ["RoutedAssistant:Router:DeploymentName"] = "synthetic-router",
                ["RoutedAssistant:PolicyAdvisor:Endpoint"] = "https://synthetic-policy.services.ai.azure.com/openai/v1",
                ["RoutedAssistant:PolicyAdvisor:DeploymentName"] = "synthetic-policy",
                ["RoutedAssistant:Retrieval:Endpoint"] = "https://synthetic.search.windows.net",
                ["RoutedAssistant:Retrieval:IndexName"] = "synthetic_policy_index_",
                ["RoutedAssistant:Embedding:Endpoint"] = "https://synthetic-embedding.services.ai.azure.com/openai/v1",
                ["RoutedAssistant:Embedding:DeploymentName"] = "synthetic-embedding",
                ["RoutedAssistant:Embedding:Dimensions"] = "3072",
            })
            .AddInMemoryCollection(overrides ?? [])
            .Build();
}

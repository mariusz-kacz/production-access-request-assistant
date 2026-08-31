using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class RequestPreparationChatRegistrationTests
{
    private const string FoundryEndpoint =
        "https://governed-access.services.ai.azure.com/openai/v1";
    private const string DeploymentName = "governed-access-chat";

    [Fact]
    public void DeterministicProfileDoesNotCreateRealClient()
    {
        var sentinel = new SentinelChatClient();
        var realClientFactoryCalls = 0;
        using var provider = CreateProvider(
            CreateDeterministicConfiguration(),
            () =>
            {
                Interlocked.Increment(ref realClientFactoryCalls);
                return sentinel;
            });

        var client = provider.GetRequiredService<IChatClient>();

        Assert.Equal(0, realClientFactoryCalls);
        Assert.NotNull(client.GetService(typeof(DeterministicChatClient)));
        Assert.Null(client.GetService(typeof(SentinelChatClient)));
    }

    [Fact]
    public void ValidFoundryResponsesProfileSelectsOfflineSentinel()
    {
        var sentinel = new SentinelChatClient();
        var realClientFactoryCalls = 0;
        using var provider = CreateProvider(
            CreateValidFoundryResponsesConfiguration(),
            () =>
            {
                Interlocked.Increment(ref realClientFactoryCalls);
                return sentinel;
            });

        var client = provider.GetRequiredService<IChatClient>();

        Assert.Equal(1, realClientFactoryCalls);
        Assert.Same(sentinel, client.GetService(typeof(SentinelChatClient)));
        Assert.Null(client.GetService(typeof(DeterministicChatClient)));
        Assert.Equal(0, sentinel.InvocationCount);
    }

    [Fact]
    public async Task RepresentativeInvalidConfigurationsFailClosedWithoutFallback()
    {
        InvalidConfigurationCase[] cases =
        [
            new(
                "missing profile",
                configuration =>
                    configuration.Remove(
                        "RequestPreparationModel:ExecutionProfile"),
                "ExecutionProfile"),
            new(
                "unknown profile",
                configuration =>
                    configuration["RequestPreparationModel:ExecutionProfile"] =
                        "UnexpectedProvider",
                "ExecutionProfile",
                "UnexpectedProvider"),
            new(
                "unsafe endpoint",
                configuration =>
                    configuration["RequestPreparationModel:FoundryResponses:Endpoint"] =
                        "https://example.com/",
                "FoundryResponses.Endpoint",
                "https://example.com/"),
            new(
                "incomplete profile",
                configuration =>
                    configuration["RequestPreparationModel:FoundryResponses:DeploymentName"] =
                        string.Empty,
                "FoundryResponses.DeploymentName"),
        ];

        foreach (var testCase in cases)
        {
            var configuration = CreateValidFoundryResponsesConfiguration();
            testCase.Mutate(configuration);

            await AssertInvalidConfigurationAsync(
                testCase,
                configuration);
        }
    }

    private static async Task AssertInvalidConfigurationAsync(
        InvalidConfigurationCase testCase,
        Dictionary<string, string?> configuration)
    {
        var sentinel = new SentinelChatClient();
        var realClientFactoryCalls = 0;
        using var provider = CreateProvider(
            configuration,
            () =>
            {
                Interlocked.Increment(ref realClientFactoryCalls);
                return sentinel;
            });
        var client = provider.GetRequiredService<IChatClient>();

        Assert.Equal(0, realClientFactoryCalls);
        Assert.Null(client.GetService(typeof(DeterministicChatClient)));
        Assert.Null(client.GetService(typeof(SentinelChatClient)));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "test request")],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(testCase.ExpectedSafeField, exception.Message);
        Assert.True(
            exception.Message.Length <= 256,
            $"The '{testCase.Name}' message was not concise.");
        Assert.Equal(0, sentinel.InvocationCount);

        if (testCase.ForbiddenValue is not null)
        {
            Assert.DoesNotContain(testCase.ForbiddenValue, exception.Message);
        }
    }

    private static ServiceProvider CreateProvider(
        Dictionary<string, string?> values,
        Func<IChatClient> foundryResponsesClientFactory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        RequestPreparationChatRegistration.AddRequestPreparationChat(
            services,
            configuration,
            foundryResponsesClientFactory);

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    private static Dictionary<string, string?>
        CreateDeterministicConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "Deterministic",
            ["RequestPreparationModel:FoundryResponses:Endpoint"] = string.Empty,
            ["RequestPreparationModel:FoundryResponses:DeploymentName"] = string.Empty,
        };

    private static Dictionary<string, string?> CreateValidFoundryResponsesConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "FoundryResponses",
            ["RequestPreparationModel:FoundryResponses:Endpoint"] = FoundryEndpoint,
            ["RequestPreparationModel:FoundryResponses:DeploymentName"] =
                DeploymentName,
        };

    private sealed record InvalidConfigurationCase(
        string Name,
        Action<Dictionary<string, string?>> Mutate,
        string ExpectedSafeField,
        string? ForbiddenValue = null);
}

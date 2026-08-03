using System.Reflection;
using System.Runtime.ExceptionServices;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class RequestPreparationChatRegistrationTests
{
    private const string ApprovedModelId = "approved-chat-model";
    private const string AzureEndpoint =
        "https://governed-access.openai.azure.com/";
    private const string DeploymentName = "governed-access-chat";
    private const string TenantId =
        "11111111-1111-1111-1111-111111111111";

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
    public void ValidAzureProfileSelectsOfflineSentinel()
    {
        var sentinel = new SentinelChatClient();
        var realClientFactoryCalls = 0;
        using var provider = CreateProvider(
            CreateValidAzureConfiguration(),
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
                    configuration["RequestPreparationModel:AzureOpenAI:Endpoint"] =
                        "https://example.com/",
                "AzureOpenAI.Endpoint",
                "https://example.com/"),
            new(
                "incomplete profile",
                configuration =>
                    configuration["RequestPreparationModel:AzureOpenAI:TenantId"] =
                        string.Empty,
                "AzureOpenAI.TenantId"),
            new(
                "unapproved model",
                configuration =>
                    configuration["RequestPreparationModel:AzureOpenAI:ModelId"] =
                        "unapproved-chat-model",
                "AzureOpenAI.ModelId",
                "unapproved-chat-model"),
        ];

        foreach (var testCase in cases)
        {
            var configuration = CreateValidAzureConfiguration();
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
        Func<IChatClient> azureOpenAIClientFactory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        InvokeRegistration(services, configuration, azureOpenAIClientFactory);

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    private static void InvokeRegistration(
        IServiceCollection services,
        IConfiguration configuration,
        Func<IChatClient> azureOpenAIClientFactory)
    {
        var registrationType = typeof(DeterministicChatClient).Assembly.GetType(
            "GovernedAccess.Web.Ai.RequestPreparationChatRegistration");
        Assert.NotNull(registrationType);

        var registrationMethod = Assert.Single(
            registrationType.GetMethods(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "AddRequestPreparationChat"
                    && parameters.Length == 3
                    && parameters[0].ParameterType == typeof(IServiceCollection)
                    && parameters[1].ParameterType == typeof(IConfiguration)
                    && parameters[2].ParameterType == typeof(Func<IChatClient>);
            });

        try
        {
            var result = registrationMethod.Invoke(
                null,
                [services, configuration, azureOpenAIClientFactory]);
            Assert.Same(services, result);
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Dictionary<string, string?>
        CreateDeterministicConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "Deterministic",
            ["RequestPreparationModel:AzureOpenAI:Endpoint"] = string.Empty,
            ["RequestPreparationModel:AzureOpenAI:TenantId"] = string.Empty,
            ["RequestPreparationModel:AzureOpenAI:DeploymentName"] = string.Empty,
            ["RequestPreparationModel:AzureOpenAI:ModelId"] = string.Empty,
        };

    private static Dictionary<string, string?> CreateValidAzureConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "AzureOpenAI",
            ["RequestPreparationModel:ApprovedModelIds:0"] = ApprovedModelId,
            ["RequestPreparationModel:AzureOpenAI:Endpoint"] = AzureEndpoint,
            ["RequestPreparationModel:AzureOpenAI:TenantId"] = TenantId,
            ["RequestPreparationModel:AzureOpenAI:DeploymentName"] =
                DeploymentName,
            ["RequestPreparationModel:AzureOpenAI:ModelId"] = ApprovedModelId,
        };

    private sealed record InvalidConfigurationCase(
        string Name,
        Action<Dictionary<string, string?>> Mutate,
        string ExpectedSafeField,
        string? ForbiddenValue = null);
}

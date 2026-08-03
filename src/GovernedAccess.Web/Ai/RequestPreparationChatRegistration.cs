using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.Web.Ai;

internal static class RequestPreparationChatRegistration
{
    internal static IServiceCollection AddRequestPreparationChat(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolution = RequestPreparationModelOptions
            .Bind(configuration)
            .Validate();

        return AddRequestPreparationChat(
            services,
            resolution,
            () => CreateAzureOpenAIClient(resolution));
    }

    internal static IServiceCollection AddRequestPreparationChat(
        IServiceCollection services,
        IConfiguration configuration,
        Func<IChatClient> azureOpenAIClientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(azureOpenAIClientFactory);

        var resolution = RequestPreparationModelOptions
            .Bind(configuration)
            .Validate();

        return AddRequestPreparationChat(
            services,
            resolution,
            azureOpenAIClientFactory);
    }

    private static IServiceCollection AddRequestPreparationChat(
        IServiceCollection services,
        RequestPreparationModelResolution resolution,
        Func<IChatClient> azureOpenAIClientFactory)
    {
        services.AddSingleton(resolution);
        services
            .AddChatClient(_ => CreateSelectedClient(
                resolution,
                azureOpenAIClientFactory))
            .UseFunctionInvocation(configure: static client =>
            {
                client.AllowConcurrentInvocation = false;
                client.IncludeDetailedErrors = false;
                client.MaximumIterationsPerRequest = 6;
                client.TerminateOnUnknownCalls = true;
            });

        return services;
    }

    private static IChatClient CreateSelectedClient(
        RequestPreparationModelResolution resolution,
        Func<IChatClient> azureOpenAIClientFactory)
    {
        if (!resolution.IsValid)
        {
            return new UnavailableChatClient(resolution.ValidationFailure!);
        }

        return resolution.Profile switch
        {
            RequestPreparationModelProfile.Deterministic =>
                new DeterministicChatClient(DeterministicChatMode.Candidate),
            RequestPreparationModelProfile.AzureOpenAI =>
                new ProviderFailureMappingChatClient(
                    azureOpenAIClientFactory()),
            _ => new UnavailableChatClient(
                "The request-preparation model profile is unavailable."),
        };
    }

    private static IChatClient CreateAzureOpenAIClient(
        RequestPreparationModelResolution resolution)
    {
        if (resolution.Profile != RequestPreparationModelProfile.AzureOpenAI
            || resolution.Endpoint is null
            || resolution.TenantId is null
            || resolution.DeploymentName is null)
        {
            throw new InvalidOperationException(
                "A valid Azure OpenAI profile is required.");
        }

        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                TenantId = resolution.TenantId.Value.ToString("D"),
            });
        var azureClient = new AzureOpenAIClient(
            resolution.Endpoint,
            credential);

        return azureClient
            .GetChatClient(resolution.DeploymentName)
            .AsIChatClient();
    }
}

using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System.ClientModel.Primitives;

namespace GovernedAccess.Web.Ai;

internal static class RequestPreparationChatRegistration
{
    internal static IServiceCollection AddRequestPreparationChat(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = RequestPreparationModelOptions.Bind(configuration);
        var resolution = options.Validate();

        return AddRequestPreparationChat(
            services,
            resolution,
            CreateMetadata(resolution),
            serviceProvider => CreateFoundryResponsesClient(
                resolution,
                serviceProvider.GetRequiredService<ILoggerFactory>()));
    }

    internal static IServiceCollection AddRequestPreparationChat(
        IServiceCollection services,
        IConfiguration configuration,
        Func<IChatClient> foundryResponsesClientFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(foundryResponsesClientFactory);

        var options = RequestPreparationModelOptions.Bind(configuration);
        var resolution = options.Validate();

        return AddRequestPreparationChat(
            services,
            resolution,
            CreateMetadata(resolution),
            _ => foundryResponsesClientFactory());
    }

    private static IServiceCollection AddRequestPreparationChat(
        IServiceCollection services,
        RequestPreparationModelResolution resolution,
        RequestPreparationModelMetadata metadata,
        Func<IServiceProvider, IChatClient> foundryResponsesClientFactory)
    {
        services.AddSingleton(resolution);
        services.AddSingleton(metadata);
        services
            .AddChatClient(serviceProvider =>
                new ModelCallLoggingChatClient(
                    CreateSelectedClient(
                        resolution,
                        () => foundryResponsesClientFactory(serviceProvider)),
                    serviceProvider.GetRequiredService<
                        ILogger<ModelCallLoggingChatClient>>()))
            .UseFunctionInvocation(configure: static client =>
            {
                client.AllowConcurrentInvocation = false;
                client.IncludeDetailedErrors = false;
                client.MaximumIterationsPerRequest = 6;
                client.TerminateOnUnknownCalls = true;
            });

        return services;
    }

    private static RequestPreparationModelMetadata CreateMetadata(
        RequestPreparationModelResolution resolution)
    {
        var profileId = resolution.Profile?.ToString() ?? "Unavailable";
        var deploymentName =
            resolution.Profile == RequestPreparationModelProfile.FoundryResponses
                ? resolution.DeploymentName
                : null;

        return new RequestPreparationModelMetadata(profileId, deploymentName);
    }

    private static IChatClient CreateSelectedClient(
        RequestPreparationModelResolution resolution,
        Func<IChatClient> foundryResponsesClientFactory)
    {
        if (!resolution.IsValid)
        {
            return new UnavailableChatClient(resolution.ValidationFailure!);
        }

        return resolution.Profile switch
        {
            RequestPreparationModelProfile.Deterministic =>
                new DeterministicChatClient(DeterministicChatMode.Unclear),
            RequestPreparationModelProfile.FoundryResponses =>
                new ProviderFailureMappingChatClient(
                    foundryResponsesClientFactory()),
            _ => new UnavailableChatClient(
                "The request-preparation model profile is unavailable."),
        };
    }

    private static IChatClient CreateFoundryResponsesClient(
        RequestPreparationModelResolution resolution,
        ILoggerFactory loggerFactory)
    {
        if (resolution.Profile != RequestPreparationModelProfile.FoundryResponses
            || resolution.Endpoint is null
            || resolution.DeploymentName is null)
        {
            throw new InvalidOperationException(
                "A valid Foundry Responses profile is required.");
        }

        var credential = new FoundryTokenCredentialLoggingDecorator(
            new DefaultAzureCredential(),
            loggerFactory.CreateLogger<
                FoundryTokenCredentialLoggingDecorator>());
        BearerTokenPolicy tokenPolicy = new(
            credential,
            "https://ai.azure.com/.default");

#pragma warning disable OPENAI001
        var clientOptions = new ResponsesClientOptions
        {
            Endpoint = resolution.Endpoint,
        };
        clientOptions.AddPolicy(
            new FoundryHttpPipelineLoggingPolicy(
                loggerFactory.CreateLogger<
                    FoundryHttpPipelineLoggingPolicy>()),
            PipelinePosition.BeforeTransport);
        ResponsesClient responsesClient = new(
            tokenPolicy,
            clientOptions);
        return responsesClient.AsIChatClient(resolution.DeploymentName);
#pragma warning restore OPENAI001
    }
}

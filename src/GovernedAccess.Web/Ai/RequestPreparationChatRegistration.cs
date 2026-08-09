using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            CreateMetadata(options, resolution),
            () => CreateFoundryResponsesClient(resolution));
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
            CreateMetadata(options, resolution),
            foundryResponsesClientFactory);
    }

    private static IServiceCollection AddRequestPreparationChat(
        IServiceCollection services,
        RequestPreparationModelResolution resolution,
        RequestPreparationModelMetadata metadata,
        Func<IChatClient> foundryResponsesClientFactory)
    {
        services.AddSingleton(resolution);
        services.AddSingleton(metadata);
        services
            .AddChatClient(_ => CreateSelectedClient(
                resolution,
                foundryResponsesClientFactory))
            .UseFunctionInvocation(configure: static client =>
            {
                client.AllowConcurrentInvocation = false;
                client.IncludeDetailedErrors = false;
                client.MaximumIterationsPerRequest = 12;
                client.TerminateOnUnknownCalls = true;
            });

        return services;
    }

    private static RequestPreparationModelMetadata CreateMetadata(
        RequestPreparationModelOptions options,
        RequestPreparationModelResolution resolution)
    {
        var profileId = options.ExecutionProfile switch
        {
            nameof(RequestPreparationModelProfile.Deterministic) =>
                nameof(RequestPreparationModelProfile.Deterministic),
            nameof(RequestPreparationModelProfile.FoundryResponses) =>
                nameof(RequestPreparationModelProfile.FoundryResponses),
            _ => "Unavailable",
        };
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
                new DeterministicChatClient(DeterministicChatMode.Candidate),
            RequestPreparationModelProfile.FoundryResponses =>
                new ProviderFailureMappingChatClient(
                    foundryResponsesClientFactory()),
            _ => new UnavailableChatClient(
                "The request-preparation model profile is unavailable."),
        };
    }

    private static IChatClient CreateFoundryResponsesClient(
        RequestPreparationModelResolution resolution)
    {
        if (resolution.Profile != RequestPreparationModelProfile.FoundryResponses
            || resolution.Endpoint is null
            || resolution.DeploymentName is null)
        {
            throw new InvalidOperationException(
                "A valid Foundry Responses profile is required.");
        }

        var credential = new DefaultAzureCredential();
        BearerTokenPolicy tokenPolicy = new(
            credential,
            "https://ai.azure.com/.default");

#pragma warning disable OPENAI001
        ResponsesClient responsesClient = new(
            tokenPolicy,
            new ResponsesClientOptions
            {
                Endpoint = resolution.Endpoint,
            });
        return responsesClient.AsIChatClient(resolution.DeploymentName);
#pragma warning restore OPENAI001
    }
}

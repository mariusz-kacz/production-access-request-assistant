using GovernedAccess.Core.Preparations;
using GovernedAccess.Web.Teams;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace GovernedAccess.Web.Ai;

internal static class RequestPreparationRegistration
{
    internal static IServiceCollection AddRequestPreparation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var modelResolution = RequestPreparationModelOptions
            .Bind(configuration)
            .Validate();
        var providerId = modelResolution.Profile?.ToString() ?? "Unavailable";
        var modelDeployment = modelResolution.DeploymentName
            ?? modelResolution.Profile?.ToString()
            ?? "Unavailable";

        services.AddSingleton(AgentExecutionLimits.Load(configuration));
        services.AddSingleton(
            new AgentModelMetadata(providerId, modelDeployment, null));
        services.AddSingleton(static serviceProvider =>
            new TargetAgentMcpEndpoint(
                () => serviceProvider.GetRequiredService<
                        IOptions<TeamsAccessRequestOptions>>()
                    .Value
                    .TrustedWebBaseUri));
        services.AddSingleton<ITurnProposalInterpreter>(serviceProvider =>
            new MafTurnProposalInterpreter(
                serviceProvider.GetRequiredService<IChatClient>(),
                serviceProvider.GetRequiredService<AgentExecutionLimits>(),
                serviceProvider.GetRequiredService<AgentModelMetadata>(),
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<TargetAgentMcpEndpoint>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()));
        services.AddScoped<RequestPreparationReducer>();
        services.AddScoped<PreparationTurnService>();
        services.AddScoped<IRequestPreparationOrchestrator>(serviceProvider =>
            new TargetRequestPreparationOrchestrator(
                serviceProvider.GetRequiredService<PreparationTurnService>(),
                serviceProvider.GetRequiredService<ITurnProposalInterpreter>()));
        services.AddScoped<IPreparationConfirmationService,
            PreparationConfirmationService>();
        services.AddScoped<IPreparationReviewService,
            PreparationReviewService>();

        return services;
    }
}

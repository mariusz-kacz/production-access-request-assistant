using GovernedAccess.Core.Preparations;
using GovernedAccess.Web.Teams;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace GovernedAccess.Web.Ai;

internal static class PreparationApplicationRegistration
{
    internal static IServiceCollection AddRequestPreparation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRequestPreparationChat(configuration);
        services.AddSingleton(AgentExecutionLimits.Load(configuration));
        services.AddSingleton(static serviceProvider =>
        {
            var metadata = serviceProvider
                .GetRequiredService<RequestPreparationModelMetadata>();
            return new AgentModelMetadata(
                metadata.ProfileId,
                metadata.DeploymentName ?? metadata.ProfileId,
                providerModelVersion: null);
        });
        services.AddSingleton(static serviceProvider =>
            new AgentMcpEndpoint(
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
                serviceProvider.GetRequiredService<AgentMcpEndpoint>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()));
        services.AddScoped<RequestPreparationReducer>();
        services.AddScoped<PreparationTurnService>();
        services.AddScoped<IRequestPreparationOrchestrator>(serviceProvider =>
            new RequestPreparationOrchestrator(
                serviceProvider.GetRequiredService<PreparationTurnService>(),
                serviceProvider.GetRequiredService<ITurnProposalInterpreter>()));
        services.AddScoped<IPreparationConfirmationService,
            PreparationConfirmationService>();
        services.AddScoped<IPreparationReviewService,
            PreparationReviewService>();

        return services;
    }
}

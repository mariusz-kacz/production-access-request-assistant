using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.Drafts;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Persistence;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.AI;

namespace GovernedAccess.Web.Ai;

internal static class RequestPreparationRegistration
{
    internal static IServiceCollection AddRequestPreparation(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IStorage, MemoryStorage>();
        services.AddSingleton<InMemoryAgentSessionStore>();
        services.AddSingleton<AgentSessionStore>(static serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryAgentSessionStore>());
        services.AddSingleton<MafConversationTurnCoordinator>();
        services.AddScoped<IRequestIntakeStore, EfRequestIntakeStore>();
        services.AddSingleton<IRequestPreparationInterpreter>(
            static serviceProvider => new MafRequestPreparationInterpreter(
                serviceProvider.GetRequiredService<IChatClient>(),
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                serviceProvider.GetRequiredService<AgentSessionStore>(),
                serviceProvider.GetRequiredService<
                    MafConversationTurnCoordinator>(),
                serviceProvider.GetRequiredService<
                    RequestPreparationMcpEndpoint>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>()));
        services.AddScoped<RequestDraftService>();

        return services;
    }
}

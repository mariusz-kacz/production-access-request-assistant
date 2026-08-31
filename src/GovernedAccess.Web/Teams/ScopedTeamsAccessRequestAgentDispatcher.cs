using Microsoft.Agents.Builder;

namespace GovernedAccess.Web.Teams;

internal sealed class ScopedTeamsAccessRequestAgentDispatcher(
    IServiceScopeFactory scopeFactory) : IAgent
{
    public async Task OnTurnAsync(
        ITurnContext turnContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turnContext);

        await using var scope = scopeFactory.CreateAsyncScope();
        var agent = scope.ServiceProvider
            .GetRequiredService<TeamsAccessRequestAgent>();
        await agent.OnTurnAsync(turnContext, cancellationToken);
    }
}

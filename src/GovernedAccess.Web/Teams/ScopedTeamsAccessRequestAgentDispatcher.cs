using Microsoft.Agents.Builder;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Creates an application scope for each Teams turn queued by the Agents SDK.
/// </summary>
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

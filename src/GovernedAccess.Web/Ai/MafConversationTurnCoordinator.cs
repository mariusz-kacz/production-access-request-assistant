using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;

namespace GovernedAccess.Web.Ai;

/// <summary>
/// Serializes each process-local MAF session load, turn, and save sequence by
/// the server-generated intake identifier.
/// </summary>
public sealed class MafConversationTurnCoordinator
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> intakeGates = new();

    /// <summary>
    /// Loads one intake's native MAF session, executes a turn, and saves the
    /// session only when the turn completes successfully.
    /// </summary>
    public async Task<TResult> ExecuteTurnAsync<TResult>(
        Guid intakeId,
        AIHostAgent agent,
        Func<AgentSession, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        EnsureIntakeId(intakeId);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(operation);

        var intakeGate = intakeGates.GetOrAdd(
            intakeId,
            static _ => new SemaphoreSlim(1, 1));

        await intakeGate.WaitAsync(cancellationToken);
        try
        {
            var sessionStoreId = intakeId.ToString("D");
            var session = await agent.GetOrCreateSessionAsync(
                sessionStoreId,
                cancellationToken);

            var result = await operation(session, cancellationToken);

            await agent.SaveSessionAsync(
                sessionStoreId,
                session,
                cancellationToken);

            return result;
        }
        finally
        {
            intakeGate.Release();
        }
    }

    internal int GateCount => intakeGates.Count;

    private static void EnsureIntakeId(Guid intakeId)
    {
        if (intakeId == Guid.Empty)
        {
            throw new ArgumentException(
                "The intake identifier must not be empty.",
                nameof(intakeId));
        }
    }
}

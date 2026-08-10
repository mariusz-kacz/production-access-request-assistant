using System.Collections.Concurrent;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Retains process-local Teams presentation metadata so the current draft card can
/// be replaced in place. This cache is a UX aid only; confirmation always reloads
/// durable intake state and rejects stale preparation identifiers.
/// </summary>
public sealed class TeamsDraftCardTracker
{
    private readonly ConcurrentDictionary<
        AuthenticatedChannelActor,
        TeamsDraftCardReference> cards = new();

    internal bool TryGet(
        AuthenticatedChannelActor actor,
        out TeamsDraftCardReference reference)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return cards.TryGetValue(actor, out reference!);
    }

    internal void Set(
        AuthenticatedChannelActor actor,
        Guid preparationId,
        string activityId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preparation identifier must not be empty.",
                nameof(preparationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        cards[actor] = new TeamsDraftCardReference(
            preparationId,
            activityId.Trim());
    }

    internal bool TryRemove(
        AuthenticatedChannelActor actor,
        out TeamsDraftCardReference reference)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return cards.TryRemove(actor, out reference!);
    }

    internal bool TryRemove(
        AuthenticatedChannelActor actor,
        Guid preparationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return cards.TryGetValue(actor, out var current)
            && current.PreparationId == preparationId
            && cards.TryRemove(
                new KeyValuePair<
                    AuthenticatedChannelActor,
                    TeamsDraftCardReference>(actor, current));
    }

    internal void Clear() => cards.Clear();
}

internal sealed record TeamsDraftCardReference(
    Guid PreparationId,
    string ActivityId);

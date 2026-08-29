using System.Collections.Concurrent;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Retains process-local Teams presentation metadata so the current draft card can
/// be replaced in place. This cache is a UX aid only; confirmation always reloads
/// durable intake state and rejects stale preparation identifiers.
/// </summary>
public sealed class TeamsDraftCardTracker
{
    private readonly ConcurrentDictionary<
        TeamsConversationReference,
        TeamsDraftCardReference> cards = new();

    internal bool TryGet(
        TeamsConversationReference conversation,
        out TeamsDraftCardReference reference)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return cards.TryGetValue(conversation, out reference!);
    }

    internal void Set(
        TeamsConversationReference conversation,
        Guid preparationId,
        string activityId)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preparation identifier must not be empty.",
                nameof(preparationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        cards[conversation] = new TeamsDraftCardReference(
            preparationId,
            activityId.Trim());
    }

    internal bool TryRemove(
        TeamsConversationReference conversation,
        out TeamsDraftCardReference reference)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return cards.TryRemove(conversation, out reference!);
    }

    internal bool TryRemove(
        TeamsConversationReference conversation,
        Guid preparationId)
    {
        return TryRemove(conversation, preparationId, out _);
    }

    internal bool TryRemove(
        TeamsConversationReference conversation,
        Guid preparationId,
        out TeamsDraftCardReference reference)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        reference = null!;
        if (!cards.TryGetValue(conversation, out var current)
            || current.PreparationId != preparationId
            || !cards.TryRemove(
                new KeyValuePair<
                    TeamsConversationReference,
                    TeamsDraftCardReference>(conversation, current)))
        {
            return false;
        }

        reference = current;
        return true;
    }

    internal void Clear() => cards.Clear();
}

internal sealed record TeamsDraftCardReference(
    Guid PreparationId,
    string ActivityId);

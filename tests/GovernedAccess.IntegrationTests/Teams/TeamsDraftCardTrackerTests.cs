using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsDraftCardTrackerTests
{
    [Fact]
    public void TracksLatestCardByExactAuthenticatedConversationBinding()
    {
        var tracker = new TeamsDraftCardTracker();
        var actor = CreateActor("conversation-a");
        var otherConversation = CreateActor("conversation-b");
        var initialPreparationId = Guid.NewGuid();
        var revisedPreparationId = Guid.NewGuid();

        tracker.Set(actor, initialPreparationId, "activity-a");

        Assert.True(tracker.TryGet(actor, out var initial));
        Assert.Equal(initialPreparationId, initial.PreparationId);
        Assert.Equal("activity-a", initial.ActivityId);
        Assert.False(tracker.TryGet(otherConversation, out _));

        tracker.Set(actor, revisedPreparationId, "activity-a");

        Assert.False(tracker.TryRemove(actor, initialPreparationId));
        Assert.True(tracker.TryGet(actor, out var revised));
        Assert.Equal(revisedPreparationId, revised.PreparationId);
        Assert.True(tracker.TryRemove(actor, revisedPreparationId));
        Assert.False(tracker.TryGet(actor, out _));
    }

    private static AuthenticatedChannelActor CreateActor(
        string conversationId) =>
        new(
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            conversationId,
            "requester");
}

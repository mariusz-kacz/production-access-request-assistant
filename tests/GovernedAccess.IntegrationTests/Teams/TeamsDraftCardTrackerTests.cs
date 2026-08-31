using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsDraftCardTrackerTests
{
    [Fact]
    public void TracksLatestCardByExactAuthenticatedConversationBinding()
    {
        var tracker = new TeamsDraftCardTracker();
        var conversation = CreateConversation("conversation-a");
        var otherConversation = CreateConversation("conversation-b");
        var initialPreparationId = Guid.NewGuid();
        var revisedPreparationId = Guid.NewGuid();

        tracker.Set(conversation, initialPreparationId, "activity-a");

        Assert.True(tracker.TryGet(conversation, out var initial));
        Assert.Equal(initialPreparationId, initial.PreparationId);
        Assert.Equal("activity-a", initial.ActivityId);
        Assert.False(tracker.TryGet(otherConversation, out _));

        tracker.Set(conversation, revisedPreparationId, "activity-a");

        Assert.False(tracker.TryRemove(conversation, initialPreparationId));
        Assert.True(tracker.TryGet(conversation, out var revised));
        Assert.Equal(revisedPreparationId, revised.PreparationId);
        Assert.True(tracker.TryRemove(conversation, revisedPreparationId));
        Assert.False(tracker.TryGet(conversation, out _));
    }

    private static TeamsConversationReference CreateConversation(
        string conversationId) =>
        new(
            "msteams",
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            conversationId,
            "requester");
}

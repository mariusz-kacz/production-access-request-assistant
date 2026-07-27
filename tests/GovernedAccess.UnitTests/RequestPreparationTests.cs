using GovernedAccess.Core.Domain;

namespace GovernedAccess.UnitTests;

public sealed class RequestPreparationTests
{
    private static readonly Guid ConversationRecordId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreparationId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReservedRequestId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConversationConstructionBindsAuthenticatedIdentityAndStartsCollecting()
    {
        var conversation = CreateConversation();

        Assert.Equal(ConversationRecordId, conversation.Id);
        Assert.Equal("msteams", conversation.Channel);
        Assert.Equal("tenant-001", conversation.TenantId);
        Assert.Equal("actor-001", conversation.ChannelActorId);
        Assert.Equal("conversation-001", conversation.ConversationId);
        Assert.Equal("requester", conversation.RequesterId);
        Assert.Equal("Collecting", conversation.Status.ToString());
        Assert.Equal(CreatedAt, conversation.CreatedAt);
        Assert.Equal(CreatedAt, conversation.LastTurnAt);
        Assert.Equal("correlation-create", conversation.CorrelationId);
        Assert.Null(conversation.ActivePreparationId);
        Assert.Equal(1, conversation.PersistenceVersion);
    }

    [Fact]
    public void CollectingConversationCanBecomeReadyForItsPreparedSnapshot()
    {
        var conversation = CreateConversation();
        var readyAt = CreatedAt.AddMinutes(2);

        conversation.MarkReady(
            PreparationId,
            readyAt,
            " correlation-ready ");

        Assert.Equal("Ready", conversation.Status.ToString());
        Assert.Equal(PreparationId, conversation.ActivePreparationId);
        Assert.Equal(readyAt, conversation.LastTurnAt);
        Assert.Equal("correlation-ready", conversation.CorrelationId);
    }

    [Fact]
    public void PreparedSnapshotConstructionCapturesCanonicalScopeAndFixedExpiry()
    {
        var preparedRequest = CreatePreparedRequest();

        Assert.Equal(PreparationId, preparedRequest.PreparationId);
        Assert.Equal(ConversationRecordId, preparedRequest.ConversationRecordId);
        Assert.Equal(ReservedRequestId, preparedRequest.ReservedRequestId);
        Assert.Equal("msteams", preparedRequest.Channel);
        Assert.Equal("tenant-001", preparedRequest.TenantId);
        Assert.Equal("actor-001", preparedRequest.ChannelActorId);
        Assert.Equal("conversation-001", preparedRequest.ConversationId);
        Assert.Equal("requester", preparedRequest.RequesterId);
        Assert.Equal("client-alpha", preparedRequest.ClientId);
        Assert.Equal("PROD-ALPHA-EU", preparedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, preparedRequest.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            preparedRequest.Justification);
        Assert.Equal("INC-1042", preparedRequest.IncidentId);
        Assert.Equal("Ready", preparedRequest.Status.ToString());
        Assert.Equal(CreatedAt, preparedRequest.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(30), preparedRequest.ExpiresAt);
        Assert.Null(preparedRequest.SubmittedAt);
        Assert.Null(preparedRequest.SubmittedRequestId);
        Assert.Equal("correlation-prepare", preparedRequest.CorrelationId);
        Assert.Equal(1, preparedRequest.PersistenceVersion);
    }

    [Fact]
    public void ReadySnapshotSubmissionUsesItsReservedRequestIdentity()
    {
        var preparedRequest = CreatePreparedRequest();
        var submittedAt = CreatedAt.AddMinutes(5);

        preparedRequest.MarkSubmitted(submittedAt);

        Assert.Equal("Submitted", preparedRequest.Status.ToString());
        Assert.Equal(ReservedRequestId, preparedRequest.ReservedRequestId);
        Assert.Equal(ReservedRequestId, preparedRequest.SubmittedRequestId);
        Assert.Equal(submittedAt, preparedRequest.SubmittedAt);
        Assert.Equal(CreatedAt.AddMinutes(30), preparedRequest.ExpiresAt);
    }

    private static RequestPreparationConversation CreateConversation() =>
        new(
            ConversationRecordId,
            " msteams ",
            " tenant-001 ",
            " actor-001 ",
            " conversation-001 ",
            " requester ",
            CreatedAt,
            " correlation-create ");

    private static PreparedAccessRequest CreatePreparedRequest() =>
        new(
            PreparationId,
            ConversationRecordId,
            ReservedRequestId,
            " msteams ",
            " tenant-001 ",
            " actor-001 ",
            " conversation-001 ",
            " requester ",
            " client-alpha ",
            " PROD-ALPHA-EU ",
            $" {ProductionRoleIds.ReadOnly} ",
            "  Investigate the active production incident.  ",
            " INC-1042 ",
            CreatedAt,
            " correlation-prepare ");
}

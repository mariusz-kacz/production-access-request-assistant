namespace GovernedAccess.Web.Teams;

internal static class TeamsLocale
{
    internal const string Default = "en-US";

    internal static string Resolve(string? locale) =>
        string.Equals(locale?.Trim(), Default, StringComparison.OrdinalIgnoreCase)
            ? Default
            : Default;
}

internal sealed record TeamsConversationReference
{
    internal TeamsConversationReference(
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId)
    {
        Channel = Normalize(channel, nameof(channel));
        TenantId = Normalize(tenantId, nameof(tenantId));
        ChannelActorId = Normalize(channelActorId, nameof(channelActorId));
        ConversationId = Normalize(conversationId, nameof(conversationId));
        RequesterId = Normalize(requesterId, nameof(requesterId));
    }

    internal string Channel { get; }

    internal string TenantId { get; }

    internal string ChannelActorId { get; }

    internal string ConversationId { get; }

    internal string RequesterId { get; }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

internal sealed record TeamsAuthenticatedContext
{
    internal TeamsAuthenticatedContext(
        TeamsConversationReference conversation,
        string locale)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        Conversation = conversation;
        Locale = TeamsLocale.Resolve(locale);
    }

    internal TeamsConversationReference Conversation { get; }

    internal string Locale { get; }

    internal string Channel => Conversation.Channel;

    internal string TenantId => Conversation.TenantId;

    internal string ChannelActorId => Conversation.ChannelActorId;

    internal string ConversationId => Conversation.ConversationId;

    internal string RequesterId => Conversation.RequesterId;
}

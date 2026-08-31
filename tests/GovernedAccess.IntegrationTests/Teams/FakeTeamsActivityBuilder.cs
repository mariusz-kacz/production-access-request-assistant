using System.Security.Claims;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.IntegrationTests.Teams;

internal sealed class FakeTeamsActivityBuilder
{
    public const string DefaultTenantId = "11111111-1111-1111-1111-111111111111";
    public const string DefaultActorId = "22222222-2222-2222-2222-222222222222";
    public const string DefaultConversationId = "teams-personal-conversation";
    public const string DefaultBotAppId = "33333333-3333-3333-3333-333333333333";
    public const string DefaultChannelAppId = "44444444-4444-4444-4444-444444444444";
    public const string AuthenticationType = "FakeAgentsSdk";
    public static readonly DateTimeOffset DefaultTimestamp =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private string? tenantId = DefaultTenantId;
    private string? actorId = DefaultActorId;
    private string? conversationId = DefaultConversationId;
    private string? channelId = Channels.Msteams;
    private string? conversationType = "personal";
    private string? locale;
    private string? text = "I need production access.";
    private string activityId = "teams-activity";
    private string? invokeName;
    private object? invokeData;
    private bool isInvoke;

    public FakeTeamsActivityBuilder WithTenant(string? value)
    {
        tenantId = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithActor(string? value)
    {
        actorId = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithConversation(string? value)
    {
        conversationId = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithChannel(string? value)
    {
        channelId = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithConversationType(string? value)
    {
        conversationType = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithLocale(string? value)
    {
        locale = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithText(string? value)
    {
        text = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithActivityId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        activityId = value;
        return this;
    }

    public FakeTeamsActivityBuilder WithInvokeData(
        object? value,
        string? name = "adaptiveCard/action")
    {
        isInvoke = true;
        invokeData = value;
        invokeName = name;
        return this;
    }

    public FakeSdkAuthenticatedTeamsActivity Build()
    {
        var activity = new Activity
        {
            Type = isInvoke ? ActivityTypes.Invoke : ActivityTypes.Message,
            Id = activityId,
            Timestamp = DefaultTimestamp,
            ServiceUrl = "https://smba.trafficmanager.net/emea/",
            ChannelId = channelId,
            From = new ChannelAccount
            {
                Id = actorId,
                AadObjectId = actorId,
                Name = "Fake Teams requester",
                Role = RoleTypes.User,
            },
            Conversation = new ConversationAccount
            {
                Id = conversationId,
                ConversationType = conversationType,
                IsGroup = !string.Equals(
                    conversationType,
                    "personal",
                    StringComparison.OrdinalIgnoreCase),
                TenantId = tenantId,
            },
            Recipient = new ChannelAccount
            {
                Id = DefaultBotAppId,
                Name = "Governed Access Assistant",
                Role = RoleTypes.Agent,
            },
            Text = text,
            Locale = locale,
            Name = invokeName,
            Value = invokeData,
            ChannelData = new Dictionary<string, object?>
            {
                ["tenant"] = new Dictionary<string, string?>
                {
                    ["id"] = tenantId,
                },
            },
        };

        var sdkIdentity = AgentClaims.CreateIdentity(
            DefaultBotAppId,
            anonymous: false,
            DefaultChannelAppId);
        var identity = new ClaimsIdentity(
            sdkIdentity.Claims,
            AuthenticationType,
            sdkIdentity.NameClaimType,
            sdkIdentity.RoleClaimType);

        return new FakeSdkAuthenticatedTeamsActivity(activity, identity);
    }
}

internal sealed record FakeSdkAuthenticatedTeamsActivity(
    Activity Activity,
    ClaimsIdentity Identity);

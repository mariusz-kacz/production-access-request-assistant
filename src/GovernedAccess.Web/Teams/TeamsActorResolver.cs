using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using GovernedAccess.Web.Authentication;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Options;

namespace GovernedAccess.Web.Teams;

/// <summary>
/// Normalizes authenticated Teams activity context without selecting an intake
/// implementation. Activity payload values never select the synthetic requester
/// or any authorization claim.
/// </summary>
internal sealed class TeamsActorResolver
{
    private const string PersonalConversationType = "personal";

    private readonly Guid allowedTenantId;

    public TeamsActorResolver(IOptions<TeamsAccessRequestOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredTenantId = options.Value.AllowedTenantId?.Trim();
        if (!Guid.TryParseExact(
                configuredTenantId,
                "D",
                out allowedTenantId)
            || allowedTenantId == Guid.Empty)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(TeamsAccessRequestOptions),
                [
                    $"{TeamsAccessRequestOptions.SectionName}:AllowedTenantId must be a non-empty GUID.",
                ]);
        }
    }

    public bool TryResolve(
        IActivity? activity,
        ClaimsIdentity? identity,
        [NotNullWhen(true)] out TeamsAuthenticatedContext? context)
    {
        context = null;

        if (!IsSdkAuthenticated(identity)
            || activity is null
            || !string.Equals(
                activity.ChannelId,
                Channels.Msteams,
                StringComparison.OrdinalIgnoreCase)
            || activity.Conversation is null
            || !string.Equals(
                activity.Conversation.ConversationType,
                PersonalConversationType,
                StringComparison.OrdinalIgnoreCase)
            || activity.Conversation.IsGroup is true
            || !TryParseTenantId(
                activity.Conversation.TenantId,
                out var tenantId)
            || tenantId != allowedTenantId
            || !TryNormalizeIdentifier(
                activity.Conversation.Id,
                out var conversationId)
            || !TryResolveChannelActorId(
                activity.From,
                out var channelActorId))
        {
            return false;
        }

        context = new TeamsAuthenticatedContext(
            new TeamsConversationReference(
                Channels.Msteams,
                allowedTenantId.ToString("D"),
                channelActorId,
                conversationId,
                DemoPrincipalKeys.Requester),
            TeamsLocale.Resolve(activity.Locale));

        return true;
    }

    private static bool IsSdkAuthenticated(ClaimsIdentity? identity) =>
        identity?.IsAuthenticated == true
        && !AgentClaims.AllowAnonymous(identity)
        && (string.Equals(
                identity.AuthenticationType,
                TeamsAgentRegistration.ActivityAuthenticationScheme,
                StringComparison.Ordinal)
            || AgentClaims.IsAgent(identity)
            || AgentClaims.IsBotFramework(identity));

    private static bool TryParseTenantId(
        string? value,
        out Guid tenantId) =>
        Guid.TryParseExact(value?.Trim(), "D", out tenantId)
        && tenantId != Guid.Empty;

    private static bool TryResolveChannelActorId(
        ChannelAccount? account,
        [NotNullWhen(true)] out string? channelActorId)
    {
        channelActorId = null;
        if (account is null)
        {
            return false;
        }

        return TryNormalizeIdentifier(account.AadObjectId, out channelActorId)
            || TryNormalizeIdentifier(account.Id, out channelActorId);
    }

    private static bool TryNormalizeIdentifier(
        string? value,
        [NotNullWhen(true)] out string? normalized)
    {
        normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            normalized = null;
            return false;
        }

        if (Guid.TryParseExact(normalized, "D", out var identifier))
        {
            if (identifier == Guid.Empty)
            {
                normalized = null;
                return false;
            }

            normalized = identifier.ToString("D");
        }

        return true;
    }
}

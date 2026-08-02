using System.Security.Claims;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Teams;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsActorResolverComponentTests
{
    [Theory]
    [InlineData("wrong-channel")]
    [InlineData("non-personal")]
    [InlineData("disallowed-tenant")]
    [InlineData("missing-actor")]
    public void ResolverRejectsActivitiesOutsideTheTrustedPersonalTeamsBoundary(
        string scenario)
    {
        var resolver = CreateResolver();
        var builder = new FakeTeamsActivityBuilder();

        switch (scenario)
        {
            case "wrong-channel":
                builder.WithChannel("webchat");
                break;
            case "non-personal":
                builder.WithConversationType("channel");
                break;
            case "disallowed-tenant":
                builder.WithTenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
                break;
            case "missing-actor":
                builder.WithActor(null);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported resolver scenario '{scenario}'.");
        }

        var activity = builder.Build();

        Assert.False(
            resolver.TryResolve(
                activity.Activity,
                activity.Identity,
                out _));
    }

    [Fact]
    public void ResolverIgnoresForgedIdentityAndScopePayloadFields()
    {
        var resolver = CreateResolver();
        var activity = new FakeTeamsActivityBuilder().Build();
        activity.Activity.Value = new
        {
            requesterId = DemoPrincipalKeys.ClientAlphaApprover,
            tenantId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            channelActorId = "forged-actor",
            conversationId = "forged-conversation",
            clientId = "client-beta",
            environmentId = "PROD-BETA-US",
            requestedRoleId = "ProductionSupport",
            approverId = DemoPrincipalKeys.DevOpsApprover,
            durationHours = 24,
            approved = true,
        };

        Assert.True(
            resolver.TryResolve(
                activity.Activity,
                activity.Identity,
                out var actor));
        Assert.Equal("msteams", actor.Channel);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, actor.TenantId);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultActorId, actor.ChannelActorId);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultConversationId,
            actor.ConversationId);
        Assert.Equal(DemoPrincipalKeys.Requester, actor.RequesterId);
    }

    [Fact]
    public void ResolverAcceptsOnlyTheValidatedActivityScheme()
    {
        var resolver = CreateResolver();
        var activity = new FakeTeamsActivityBuilder().Build().Activity;
        var audienceClaim = new Claim(
            "aud",
            FakeTeamsActivityBuilder.DefaultBotAppId);
        var validatedIdentity = new ClaimsIdentity(
            [audienceClaim],
            TeamsAgentRegistration.ActivityAuthenticationScheme);
        var unrelatedIdentity = new ClaimsIdentity(
            [audienceClaim],
            DemoAuthentication.Scheme);

        Assert.True(
            resolver.TryResolve(activity, validatedIdentity, out var actor));
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, actor.TenantId);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultActorId, actor.ChannelActorId);
        Assert.False(resolver.TryResolve(activity, unrelatedIdentity, out _));
    }

    private static TeamsActorResolver CreateResolver() =>
        new(
            Options.Create(
                new TeamsAccessRequestOptions
                {
                    AllowedTenantId = FakeTeamsActivityBuilder.DefaultTenantId,
                }));
}

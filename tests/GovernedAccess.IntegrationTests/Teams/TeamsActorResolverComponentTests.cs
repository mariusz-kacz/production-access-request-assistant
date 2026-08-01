using System.Security.Claims;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Teams;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsActorResolverComponentTests
{
    [Fact]
    public void ResolverAcceptsOnlyTheValidatedActivityScheme()
    {
        var resolver = new TeamsActorResolver(
            Options.Create(
                new TeamsAccessRequestOptions
                {
                    AllowedTenantId = FakeTeamsActivityBuilder.DefaultTenantId,
                }));
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
}

using System.Security.Claims;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Teams;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsActorResolverComponentTests
{
    [Fact]
    public void ResolverRejectsActivitiesOutsideTheTrustedPersonalTeamsBoundary()
    {
        (string Name, Action<FakeTeamsActivityBuilder> Configure)[] scenarios =
        [
            ("wrong-channel", builder => builder.WithChannel("webchat")),
            ("non-personal", builder => builder.WithConversationType("channel")),
            (
                "disallowed-tenant",
                builder => builder.WithTenant(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            ("missing-actor", builder => builder.WithActor(null)),
        ];

        foreach (var (name, configure) in scenarios)
        {
            var resolver = CreateResolver();
            var builder = new FakeTeamsActivityBuilder();
            configure(builder);

            var activity = builder.Build();

            Assert.False(
                resolver.TryResolve(
                    activity.Activity,
                    activity.Identity,
                    out _),
                $"Accepted invalid Teams boundary scenario '{name}'.");
        }
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

    [Fact]
    public void ResolverUsesAuthenticatedClientLocaleWithEnglishFallback()
    {
        string?[] suppliedLocales = ["en-US", "EN-us", "pl-PL", null];

        foreach (var suppliedLocale in suppliedLocales)
        {
            var resolver = CreateResolver();
            var activity = new FakeTeamsActivityBuilder()
                .WithLocale(suppliedLocale)
                .Build();

            Assert.True(
                resolver.TryResolve(
                    activity.Activity,
                    activity.Identity,
                    out var context));
            Assert.Equal("en-US", context.Locale);
        }
    }

    private static TeamsActorResolver CreateResolver() =>
        new(
            Options.Create(
                new TeamsAccessRequestOptions
                {
                    AllowedTenantId = FakeTeamsActivityBuilder.DefaultTenantId,
                }));
}

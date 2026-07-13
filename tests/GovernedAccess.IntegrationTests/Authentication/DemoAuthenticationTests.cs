using System.Security.Claims;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Authentication;

public sealed class DemoAuthenticationTests
{
    [Theory]
    [InlineData(
        DemoPrincipalKeys.Requester,
        SyntheticDataSeeder.RequesterPrincipalId,
        PrincipalKind.Requester,
        null)]
    [InlineData(
        DemoPrincipalKeys.ClientAlphaApprover,
        SyntheticDataSeeder.ClientAlphaApproverPrincipalId,
        PrincipalKind.BusinessApprover,
        SyntheticDataSeeder.ClientAlphaId)]
    [InlineData(
        DemoPrincipalKeys.ClientBetaApprover,
        SyntheticDataSeeder.ClientBetaApproverPrincipalId,
        PrincipalKind.BusinessApprover,
        SyntheticDataSeeder.ClientBetaId)]
    [InlineData(
        DemoPrincipalKeys.DevOpsApprover,
        SyntheticDataSeeder.DevOpsApproverPrincipalId,
        PrincipalKind.DevOpsApprover,
        null)]
    public void TryResolvePrincipalCreatesTrustedClaims(
        string principalKey,
        string expectedId,
        PrincipalKind expectedKind,
        string? expectedClientId)
    {
        var resolved = DemoAuthentication.TryResolvePrincipal(principalKey, out var principal);

        Assert.True(resolved);
        Assert.NotNull(principal);
        Assert.Equal(expectedId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(expectedKind.ToString(), principal.FindFirstValue(DemoClaimTypes.PrincipalKind));
        Assert.Equal(expectedKind.ToString(), principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(expectedClientId, principal.FindFirstValue(DemoClaimTypes.ClientId));
        Assert.Equal(DemoAuthentication.Scheme, principal.Identity?.AuthenticationType);
        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("REQUESTER")]
    [InlineData(" requester ")]
    [InlineData("unknown")]
    public void TryResolvePrincipalRejectsAnythingOutsideTheExactAllowlist(string? principalKey)
    {
        var resolved = DemoAuthentication.TryResolvePrincipal(principalKey, out var principal);

        Assert.False(resolved);
        Assert.Null(principal);
    }

    [Fact]
    public void AddDemoAuthenticationConfiguresASecureNonSlidingCookie()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDemoAuthentication();

        using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        var cookie = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(DemoAuthentication.Scheme);

        Assert.Equal(DemoAuthentication.Scheme, authentication.DefaultAuthenticateScheme);
        Assert.Equal(DemoAuthentication.Scheme, authentication.DefaultChallengeScheme);
        Assert.Equal(DemoAuthentication.CookieName, cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.False(cookie.SlidingExpiration);
    }
}

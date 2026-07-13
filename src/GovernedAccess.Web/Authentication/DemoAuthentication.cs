using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GovernedAccess.Web.Authentication;

public static class DemoPrincipalKeys
{
    public const string Requester = SyntheticDataSeeder.RequesterPrincipalId;
    public const string ClientAlphaApprover =
        SyntheticDataSeeder.ClientAlphaApproverPrincipalId;
    public const string ClientBetaApprover =
        SyntheticDataSeeder.ClientBetaApproverPrincipalId;
    public const string DevOpsApprover = SyntheticDataSeeder.DevOpsApproverPrincipalId;
}

public static class DemoClaimTypes
{
    public const string PrincipalKind = "governed_access/principal_kind";
    public const string ClientId = "governed_access/client_id";
}

public static class DemoAuthentication
{
    public const string Scheme = "GovernedAccess.DemoCookie";
    public const string CookieName = "__Host-GovernedAccess.Auth";

    private static readonly FrozenDictionary<string, AuthenticatedPrincipal> PrincipalByKey =
        new Dictionary<string, AuthenticatedPrincipal>(StringComparer.Ordinal)
        {
            [DemoPrincipalKeys.Requester] = new(
                SyntheticDataSeeder.RequesterPrincipalId,
                "Demo Requester",
                PrincipalKind.Requester),
            [DemoPrincipalKeys.ClientAlphaApprover] = new(
                SyntheticDataSeeder.ClientAlphaApproverPrincipalId,
                "Client Alpha Business Approver",
                PrincipalKind.BusinessApprover,
                SyntheticDataSeeder.ClientAlphaId),
            [DemoPrincipalKeys.ClientBetaApprover] = new(
                SyntheticDataSeeder.ClientBetaApproverPrincipalId,
                "Client Beta Business Approver",
                PrincipalKind.BusinessApprover,
                SyntheticDataSeeder.ClientBetaId),
            [DemoPrincipalKeys.DevOpsApprover] = new(
                SyntheticDataSeeder.DevOpsApproverPrincipalId,
                "DevOps Approver",
                PrincipalKind.DevOpsApprover),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IServiceCollection AddDemoAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;
                options.DefaultSignInScheme = Scheme;
            })
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        return services;
    }

    public static bool TryResolvePrincipal(
        string? principalKey,
        [NotNullWhen(true)] out ClaimsPrincipal? principal)
    {
        principal = null;

        if (principalKey is null
            || !PrincipalByKey.TryGetValue(principalKey, out var authenticatedPrincipal))
        {
            return false;
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, authenticatedPrincipal.Id),
            new(ClaimTypes.Name, authenticatedPrincipal.DisplayName),
            new(ClaimTypes.Role, authenticatedPrincipal.Kind.ToString()),
            new(DemoClaimTypes.PrincipalKind, authenticatedPrincipal.Kind.ToString()),
        ];

        if (authenticatedPrincipal.ClientId is not null)
        {
            claims.Add(new Claim(DemoClaimTypes.ClientId, authenticatedPrincipal.ClientId));
        }

        principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
        return true;
    }
}

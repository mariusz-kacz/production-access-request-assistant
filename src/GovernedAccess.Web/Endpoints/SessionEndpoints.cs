using System.Security.Claims;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace GovernedAccess.Web.Endpoints;

public static class SessionEndpoints
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string AntiforgeryCookieName = "__Host-GovernedAccess.Antiforgery";
    public const string RequestTokenCookieName = "XSRF-TOKEN";

    public static IServiceCollection AddSessionEndpoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        return services;
    }

    public static IEndpointRouteBuilder MapSessionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup("/api").MapSessionEndpoints();
        return endpoints;
    }

    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/security/antiforgery", IssueAntiforgeryToken);
        endpoints.MapGet("/session", GetSession);
        endpoints
            .MapPost("/demo/session", SignInAsync)
            .AddEndpointFilter(ValidateAntiforgeryAsync);
        endpoints
            .MapDelete(
                "/demo/session",
                (Func<HttpContext, Task<IResult>>)SignOutAsync)
            .AddEndpointFilter(ValidateAntiforgeryAsync);

        return endpoints;
    }

    private static IResult IssueAntiforgeryToken(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        StoreReadableRequestToken(context, antiforgery);
        return Results.NoContent();
    }

    private static IResult GetSession(HttpContext context)
    {
        return Results.Ok(CreateSessionView(context.User));
    }

    private static async Task<IResult> SignInAsync(
        DemoSessionRequest request,
        HttpContext context,
        IAntiforgery antiforgery)
    {
        context.RequestAborted.ThrowIfCancellationRequested();

        if (!DemoAuthentication.TryResolvePrincipal(request.PrincipalKey, out var principal))
        {
            return ProblemDetailsMapping.ToValidationProblemDetails(
                "invalid_principal_key",
                "Select one of the configured demo identities.",
                [
                    new FieldValidationError(
                        "principalKey",
                        "invalid_principal_key",
                        "The principal key is not recognized."),
                ],
                context);
        }

        await context.SignInAsync(
            DemoAuthentication.Scheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
            });

        context.User = principal;
        StoreReadableRequestToken(context, antiforgery);
        return Results.Ok(CreateSessionView(principal));
    }

    private static async Task<IResult> SignOutAsync(HttpContext context)
    {
        context.RequestAborted.ThrowIfCancellationRequested();

        await context.SignOutAsync(DemoAuthentication.Scheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Response.Cookies.Delete(
            RequestTokenCookieName,
            CreateRequestTokenCookieOptions());

        return Results.NoContent();
    }

    internal static async ValueTask<object?> ValidateAntiforgeryAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        context.RequestAborted.ThrowIfCancellationRequested();

        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                "antiforgery_validation_failed",
                "A valid antiforgery token is required.")
                .ToProblemDetails(context);
        }

        return await next(invocationContext);
    }

    private static SessionView CreateSessionView(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new SessionView(false, null, []);
        }

        var id = GetRequiredClaim(principal, ClaimTypes.NameIdentifier);
        var displayName = GetRequiredClaim(principal, ClaimTypes.Name);
        var kindValue = GetRequiredClaim(principal, DemoClaimTypes.PrincipalKind);
        var clientId = principal.FindFirst(DemoClaimTypes.ClientId)?.Value;

        var kind = Enum.Parse<PrincipalKind>(kindValue, ignoreCase: false);

        return new SessionView(
            true,
            new SessionPrincipalView(id, displayName, kind.ToString(), clientId),
            GetCapabilities(kind));
    }

    private static string[] GetCapabilities(PrincipalKind kind)
    {
        return kind switch
        {
            PrincipalKind.Requester =>
            [
                "createRequest",
            ],
            PrincipalKind.BusinessApprover =>
            [
                "decideBusinessRequests",
            ],
            PrincipalKind.DevOpsApprover =>
            [
                "decideDevOpsRequests",
                "retryProvisioning",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string GetRequiredClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value
            ?? throw new InvalidOperationException(
                $"The authenticated demo identity is missing claim '{claimType}'.");
    }

    private static void StoreReadableRequestToken(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var tokenSet = antiforgery.GetAndStoreTokens(context);
        var requestToken = tokenSet.RequestToken
            ?? throw new InvalidOperationException("Antiforgery did not produce a request token.");

        context.Response.Cookies.Append(
            RequestTokenCookieName,
            requestToken,
            CreateRequestTokenCookieOptions());
    }

    private static CookieOptions CreateRequestTokenCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = false,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
            Secure = true,
        };
    }

}

public sealed record DemoSessionRequest(string? PrincipalKey);

public sealed record SessionView(
    bool Authenticated,
    SessionPrincipalView? Principal,
    IReadOnlyList<string> Capabilities);

public sealed record SessionPrincipalView(
    string Id,
    string DisplayName,
    string Kind,
    string? ClientId);

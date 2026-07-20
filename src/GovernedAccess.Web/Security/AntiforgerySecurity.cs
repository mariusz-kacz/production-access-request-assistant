using GovernedAccess.Core.Application;
using GovernedAccess.Web.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GovernedAccess.Web.Security;

public static class AntiforgerySecurity
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string CookieName = "__Host-GovernedAccess.Antiforgery";
    public const string RequestTokenCookieName = "XSRF-TOKEN";

    public static IServiceCollection AddGovernedAccessAntiforgery(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        services.AddScoped<GovernedAccessAntiforgeryFilter>();

        return services;
    }
}

public sealed class GovernedAccessAntiforgeryFilter(IAntiforgery antiforgery)
    : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        httpContext.RequestAborted.ThrowIfCancellationRequested();

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "antiforgery_validation_failed",
                    "A valid antiforgery token is required.")
                .ToProblemDetails(httpContext);
        }
    }
}

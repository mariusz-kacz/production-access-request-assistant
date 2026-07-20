using System.Security.Claims;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class SessionController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("security/antiforgery")]
    public IActionResult IssueAntiforgeryToken()
    {
        StoreReadableRequestToken();
        return NoContent();
    }

    [HttpGet("session")]
    public ActionResult<SessionView> GetSession()
    {
        return Ok(CreateSessionView(User));
    }

    [HttpPost("demo/session")]
    [ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
    public async Task<ActionResult<SessionView>> SignInAsync(
        DemoSessionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                HttpContext);
        }

        await HttpContext.SignInAsync(
            DemoAuthentication.Scheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
            });

        HttpContext.User = principal;
        StoreReadableRequestToken();
        return Ok(CreateSessionView(principal));
    }

    [HttpDelete("demo/session")]
    [ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
    public async Task<IActionResult> SignOutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await HttpContext.SignOutAsync(DemoAuthentication.Scheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        Response.Cookies.Delete(
            AntiforgerySecurity.RequestTokenCookieName,
            CreateRequestTokenCookieOptions());

        return NoContent();
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

    private void StoreReadableRequestToken()
    {
        var tokenSet = antiforgery.GetAndStoreTokens(HttpContext);
        var requestToken = tokenSet.RequestToken
            ?? throw new InvalidOperationException("Antiforgery did not produce a request token.");

        Response.Cookies.Append(
            AntiforgerySecurity.RequestTokenCookieName,
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Endpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.IntegrationTests.Endpoints;

public sealed class SessionEndpointsTests
{
    [Fact]
    public async Task AntiforgeryEndpointIssuesReadableTokenWithoutAuthenticating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SessionTestHost.StartAsync(cancellationToken);

        using var response = await host.Client.GetAsync(
            "/api/security/antiforgery",
            cancellationToken);
        var cookies = ReadSetCookies(response);
        using var sessionResponse = await host.Client.GetAsync(
            "/api/session",
            cancellationToken);
        using var session = await ReadJsonAsync(sessionResponse, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(SessionEndpoints.AntiforgeryCookieName, cookies.Keys);
        Assert.Contains(SessionEndpoints.RequestTokenCookieName, cookies.Keys);
        Assert.DoesNotContain("HttpOnly", cookies[SessionEndpoints.RequestTokenCookieName].Attributes);
        Assert.True(cookies[SessionEndpoints.RequestTokenCookieName].Attributes.Contains(
            "Secure",
            StringComparison.OrdinalIgnoreCase));
        Assert.False(session.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task DemoSignInWithoutAntiforgeryTokenIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SessionTestHost.StartAsync(cancellationToken);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/demo/session",
            new { principalKey = DemoPrincipalKeys.Requester });

        using var response = await host.Client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            ReadSetCookies(response).Keys,
            name => string.Equals(name, DemoAuthentication.CookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoSignInUsesOnlyTheAllowlistedPrincipalKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SessionTestHost.StartAsync(cancellationToken);
        var antiforgeryCookies = await GetAntiforgeryCookiesAsync(
            host.Client,
            cancellationToken);
        using var request = CreateProtectedRequest(
            HttpMethod.Post,
            "/api/demo/session",
            new
            {
                principalKey = DemoPrincipalKeys.ClientAlphaApprover,
                id = "browser-controlled-id",
                kind = "DevOpsApprover",
                clientId = "client-beta",
            },
            antiforgeryCookies);

        using var response = await host.Client.SendAsync(request, cancellationToken);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var cookies = ReadSetCookies(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            DemoPrincipalKeys.ClientAlphaApprover,
            body.RootElement.GetProperty("principal").GetProperty("id").GetString());
        Assert.Equal(
            "BusinessApprover",
            body.RootElement.GetProperty("principal").GetProperty("kind").GetString());
        Assert.Equal(
            "client-alpha",
            body.RootElement.GetProperty("principal").GetProperty("clientId").GetString());
        Assert.Contains(DemoAuthentication.CookieName, cookies.Keys);

        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        sessionRequest.Headers.Add(
            "Cookie",
            $"{DemoAuthentication.CookieName}={cookies[DemoAuthentication.CookieName].Value}");
        using var sessionResponse = await host.Client.SendAsync(
            sessionRequest,
            cancellationToken);
        using var session = await ReadJsonAsync(sessionResponse, cancellationToken);

        Assert.True(session.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            DemoPrincipalKeys.ClientAlphaApprover,
            session.RootElement.GetProperty("principal").GetProperty("id").GetString());
        Assert.Contains(
            "decideBusinessRequests",
            session.RootElement.GetProperty("capabilities")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task UnknownPrincipalKeyReturnsValidationFailureWithoutSigningIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SessionTestHost.StartAsync(cancellationToken);
        var antiforgeryCookies = await GetAntiforgeryCookiesAsync(
            host.Client,
            cancellationToken);
        using var request = CreateProtectedRequest(
            HttpMethod.Post,
            "/api/demo/session",
            new { principalKey = "unknown" },
            antiforgeryCookies);

        using var response = await host.Client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(
            ReadSetCookies(response).Keys,
            name => string.Equals(name, DemoAuthentication.CookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DemoSignOutRequiresAntiforgeryAndClearsTheAuthenticationCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SessionTestHost.StartAsync(cancellationToken);
        var anonymousTokens = await GetAntiforgeryCookiesAsync(
            host.Client,
            cancellationToken);
        using var signInRequest = CreateProtectedRequest(
            HttpMethod.Post,
            "/api/demo/session",
            new { principalKey = DemoPrincipalKeys.Requester },
            anonymousTokens);
        using var signInResponse = await host.Client.SendAsync(
            signInRequest,
            cancellationToken);
        var signedInCookies = MergeCookies(anonymousTokens, ReadSetCookies(signInResponse));

        using var unprotectedSignOut = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/demo/session");
        unprotectedSignOut.Headers.Add("Cookie", CreateCookieHeader(signedInCookies));
        using var rejectedResponse = await host.Client.SendAsync(
            unprotectedSignOut,
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        using var signOutRequest = CreateProtectedRequest(
            HttpMethod.Delete,
            "/api/demo/session",
            body: null,
            signedInCookies);
        using var signOutResponse = await host.Client.SendAsync(
            signOutRequest,
            cancellationToken);
        var clearedCookies = ReadSetCookies(signOutResponse);

        Assert.Equal(HttpStatusCode.NoContent, signOutResponse.StatusCode);
        Assert.Contains(DemoAuthentication.CookieName, clearedCookies.Keys);
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            clearedCookies[DemoAuthentication.CookieName].Attributes,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, SetCookie>> GetAntiforgeryCookiesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/api/security/antiforgery",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return ReadSetCookies(response);
    }

    private static HttpRequestMessage CreateProtectedRequest(
        HttpMethod method,
        string path,
        object? body,
        IReadOnlyDictionary<string, SetCookie> cookies)
    {
        var request = CreateJsonRequest(method, path, body);
        request.Headers.Add("Cookie", CreateCookieHeader(cookies));
        request.Headers.Add(
            SessionEndpoints.AntiforgeryHeaderName,
            Uri.UnescapeDataString(cookies[SessionEndpoints.RequestTokenCookieName].Value));
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string path,
        object? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static string CreateCookieHeader(IReadOnlyDictionary<string, SetCookie> cookies)
    {
        return string.Join("; ", cookies.Select(pair => $"{pair.Key}={pair.Value.Value}"));
    }

    private static Dictionary<string, SetCookie> MergeCookies(
        IReadOnlyDictionary<string, SetCookie> first,
        IReadOnlyDictionary<string, SetCookie> second)
    {
        var merged = first.ToDictionary(StringComparer.Ordinal);
        foreach (var cookie in second)
        {
            merged[cookie.Key] = cookie.Value;
        }

        return merged;
    }

    private static Dictionary<string, SetCookie> ReadSetCookies(
        HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return new Dictionary<string, SetCookie>(StringComparer.Ordinal);
        }

        return values
            .Select(value =>
            {
                var separator = value.IndexOf(';', StringComparison.Ordinal);
                var nameValue = separator >= 0 ? value[..separator] : value;
                var attributes = separator >= 0 ? value[(separator + 1)..].Trim() : string.Empty;
                var equals = nameValue.IndexOf('=', StringComparison.Ordinal);
                return new SetCookie(
                    nameValue[..equals],
                    nameValue[(equals + 1)..],
                    attributes);
            })
            .ToDictionary(cookie => cookie.Name, StringComparer.Ordinal);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private sealed record SetCookie(string Name, string Value, string Attributes);

    private sealed class SessionTestHost(WebApplication application, HttpClient client)
        : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<SessionTestHost> StartAsync(
            CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddDemoAuthentication();
            builder.Services.AddSessionEndpoints();

            var application = builder.Build();
            application.UseAuthentication();
            application.MapSessionEndpoints();
            await application.StartAsync(cancellationToken);

            var client = application.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            return new SessionTestHost(application, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
        }
    }
}

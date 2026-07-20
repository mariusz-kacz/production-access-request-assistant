using System.Net.Http.Json;
using System.Security.Claims;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class GovernedAccessWebFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset DefaultUtcNow =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly string databaseConnectionString =
        $"Data Source=governed-access-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SemaphoreSlim databaseResetLock = new(1, 1);

    public DeterministicClock Clock { get; } = new(DefaultUtcNow);

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await databaseResetLock.WaitAsync(cancellationToken);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();

            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            await ClearDatabaseAsync(dbContext, cancellationToken);
            await SyntheticDataSeeder.SeedAsync(dbContext, cancellationToken);
        }
        finally
        {
            databaseResetLock.Release();
        }
    }

    public static ClaimsPrincipal ResolvePrincipal(string principalKey)
    {
        if (!DemoAuthentication.TryResolvePrincipal(principalKey, out var principal))
        {
            throw new ArgumentException(
                "The principal key is not one of the configured demo identities.",
                nameof(principalKey));
        }

        return principal;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string principalKey,
        CancellationToken cancellationToken = default)
    {
        _ = ResolvePrincipal(principalKey);

        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/session")
            {
                Content = JsonContent.Create(new { principalKey }),
            };
            using var response = await SendWithAntiforgeryAsync(
                client,
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static async Task<HttpResponseMessage> SendWithAntiforgeryAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        var requestToken = await GetAntiforgeryTokenAsync(client, cancellationToken);
        request.Headers.Remove(AntiforgerySecurity.HeaderName);
        request.Headers.Add(AntiforgerySecurity.HeaderName, requestToken);

        return await client.SendAsync(request, cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<GovernedAccessDbContext>();
            services.RemoveAll<DbContextOptions<GovernedAccessDbContext>>();
            services.RemoveAll<SqliteConnection>();
            services.RemoveAll<IClock>();

            services.AddSingleton(_ =>
            {
                var keeperConnection = new SqliteConnection(databaseConnectionString);
                keeperConnection.Open();
                return keeperConnection;
            });
            services.AddDbContext<GovernedAccessDbContext>((serviceProvider, options) =>
            {
                _ = serviceProvider.GetRequiredService<SqliteConnection>();
                options.UseSqlite(databaseConnectionString);
            });
            services.AddSingleton<IClock>(Clock);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    private static async Task ClearDatabaseAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        _ = await dbContext.AuditEvents.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.AccessGrants.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.ProvisioningOperations.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.ApprovalDecisions.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.AccessRequests.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.Incidents.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.EnvironmentRoles.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.ProductionEnvironments.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.AuthenticatedPrincipals.ExecuteDeleteAsync(cancellationToken);
        _ = await dbContext.Clients.ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/api/security/antiforgery",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            throw new InvalidOperationException(
                "The antiforgery endpoint did not issue a request-token cookie.");
        }

        var cookiePrefix = $"{AntiforgerySecurity.RequestTokenCookieName}=";
        foreach (var header in setCookieHeaders)
        {
            if (!header.StartsWith(cookiePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var separator = header.IndexOf(';', StringComparison.Ordinal);
            var encodedToken = separator < 0
                ? header[cookiePrefix.Length..]
                : header[cookiePrefix.Length..separator];
            return Uri.UnescapeDataString(encodedToken);
        }

        throw new InvalidOperationException(
            "The antiforgery endpoint did not issue a request-token cookie.");
    }
}

public sealed class DeterministicClock(DateTimeOffset utcNow) : IClock
{
    private readonly object syncRoot = new();
    private DateTimeOffset utcNow = utcNow.ToUniversalTime();

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (syncRoot)
            {
                return utcNow;
            }
        }
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        lock (syncRoot)
        {
            utcNow = value.ToUniversalTime();
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (syncRoot)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}

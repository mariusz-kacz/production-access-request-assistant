using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Web.Security;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class GovernedAccessWebFactory : WebApplicationFactory<Program>
{
    public const string TeamsAuthenticationScheme =
        "GovernedAccess.TestTeamsAuthentication";
    public const string TeamsAuthenticationHeaderName =
        "X-Governed-Access-Test-Teams-Authentication";

    private const string TeamsAuthenticationHeaderValue = "authenticated";
    private const string BotConnectionName = "BotServiceConnection";

    public static readonly DateTimeOffset DefaultUtcNow =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    public static readonly Uri DefaultTrustedWebBaseUri =
        new("https://governed-access.test/");

    private readonly string databaseConnectionString =
        $"Data Source=governed-access-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SemaphoreSlim databaseResetLock = new(1, 1);
    private readonly IChatClient? replacementChatClient;
    private readonly ILoggerProvider? loggerProvider;
    private readonly IReadOnlyDictionary<string, string?> configurationOverrides;

    public GovernedAccessWebFactory(
        IChatClient? chatClient = null,
        Uri? trustedWebBaseUri = null,
        ILoggerProvider? loggerProvider = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        replacementChatClient = chatClient;
        this.loggerProvider = loggerProvider;
        this.configurationOverrides = configurationOverrides is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(configurationOverrides);
        TrustedWebBaseUri = trustedWebBaseUri ?? DefaultTrustedWebBaseUri;
    }

    public GovernedAccessWebFactory(
        DeterministicChatMode chatMode,
        Uri? trustedWebBaseUri = null,
        ILoggerProvider? loggerProvider = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
        : this(
            new DeterministicChatClient(chatMode),
            trustedWebBaseUri,
            loggerProvider,
            configurationOverrides)
    {
    }

    public DeterministicClock Clock { get; } = new(DefaultUtcNow);

    public Uri TrustedWebBaseUri { get; }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        Clock.SetUtcNow(DefaultUtcNow);
        await databaseResetLock.WaitAsync(cancellationToken);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
            scope.ServiceProvider
                .GetRequiredService<SyntheticAccessProvisionerControl>()
                .Reset();

            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            await ClearDatabaseAsync(dbContext, cancellationToken);
            await SyntheticDataSeeder.SeedAsync(dbContext, cancellationToken);
        }
        finally
        {
            databaseResetLock.Release();
        }
    }

    public Task<AccessRequest> CreateRequestFixtureAsync(
        CancellationToken cancellationToken = default) =>
        CreateRequestFixtureAsync(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);

    public async Task<AccessRequest> CreateRequestFixtureAsync(
        string clientId,
        string environmentId,
        string? incidentId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var request = new AccessRequest(
            Guid.NewGuid(),
            DemoPrincipalKeys.Requester,
            clientId,
            environmentId,
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            incidentId,
            Clock.UtcNow,
            $"fixture-{Guid.NewGuid():N}");
        var auditEvent = AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            new RequestCreatedAuditDetails(request.Status));

        dbContext.AccessRequests.Add(request);
        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return request;
    }

    public HttpClient CreateTeamsClient(bool authenticated = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

        if (authenticated)
        {
            client.DefaultRequestHeaders.Add(
                TeamsAuthenticationHeaderName,
                TeamsAuthenticationHeaderValue);
        }

        return client;
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
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(CreateTeamsConfiguration());
            configuration.AddInMemoryCollection(configurationOverrides);
        });
        builder.ConfigureServices((context, services) =>
        {
            services.RemoveAll<GovernedAccessDbContext>();
            services.RemoveAll<DbContextOptions<GovernedAccessDbContext>>();
            services.RemoveAll<SqliteConnection>();
            services.RemoveAll<IClock>();
            services.RemoveAll<IChatClient>();
            services.RemoveAll<RequestPreparationModelResolution>();
            services.RemoveAll<RequestPreparationModelMetadata>();

            var modelResolution = RequestPreparationModelOptions
                .Bind(context.Configuration)
                .Validate();
            if (replacementChatClient is not null
                && modelResolution.Profile
                    == RequestPreparationModelProfile.Deterministic)
            {
                services.AddSingleton(modelResolution);
                services.AddSingleton(
                    new RequestPreparationModelMetadata(
                        nameof(RequestPreparationModelProfile.Deterministic),
                        null));
                services
                    .AddChatClient(replacementChatClient)
                    .UseFunctionInvocation(configure: static client =>
                    {
                        client.AllowConcurrentInvocation = false;
                        client.IncludeDetailedErrors = false;
                        client.MaximumIterationsPerRequest = 6;
                        client.TerminateOnUnknownCalls = true;
                    });
            }
            else
            {
                RequestPreparationChatRegistration.AddRequestPreparationChat(
                    services,
                    context.Configuration,
                    () => replacementChatClient
                        ?? throw new InvalidOperationException(
                            "Full-host tests require an offline client for a valid real-model profile."));
            }

            // Full-host tests retain only transport-to-interpreter wiring. Exact MCP
            // catalog and timeout behavior is covered by the lightweight MCP
            // component boundary, so this host uses the internal model-only seam.
            services.RemoveAll<IRequestPreparationInterpreter>();
            services.AddSingleton<IRequestPreparationInterpreter>(serviceProvider =>
                new MafRequestPreparationInterpreter(
                    serviceProvider.GetRequiredService<IChatClient>(),
                    serviceProvider.GetRequiredService<
                        IOptions<TeamsAccessRequestOptions>>(),
                    serviceProvider.GetRequiredService<ILoggerFactory>(),
                    serviceProvider.GetRequiredService<AgentSessionStore>(),
                    serviceProvider.GetRequiredService<
                        MafConversationTurnCoordinator>()));

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
            services
                .AddAuthentication()
                .AddScheme<
                    AuthenticationSchemeOptions,
                    TestTeamsAuthenticationHandler>(
                    TeamsAuthenticationScheme,
                    _ => { });
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(
                        options.DefaultPolicy)
                    .AddAuthenticationSchemes(
                        DemoAuthentication.Scheme,
                        TeamsAuthenticationScheme)
                    .Build();
            });
        });
    }

    private Dictionary<string, string?> CreateTeamsConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["TokenValidation:Enabled"] = bool.TrueString,
            ["TokenValidation:Audiences:0"] =
                FakeTeamsActivityBuilder.DefaultBotAppId,
            ["TokenValidation:TenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            [$"Connections:{BotConnectionName}:Settings:AuthType"] =
                "ClientSecret",
            [$"Connections:{BotConnectionName}:Settings:Authority"] =
                "https://login.microsoftonline.com/botframework.com",
            [$"Connections:{BotConnectionName}:Settings:ClientId"] =
                FakeTeamsActivityBuilder.DefaultBotAppId,
            [$"Connections:{BotConnectionName}:Settings:ClientSecret"] =
                "integration-test-only",
            [$"Connections:{BotConnectionName}:Settings:TenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            [$"Connections:{BotConnectionName}:Settings:Scopes:0"] =
                AuthenticationConstants.BotFrameworkDefaultScope,
            ["ConnectionsMap:0:ServiceUrl"] = "*",
            ["ConnectionsMap:0:Connection"] = BotConnectionName,
            ["RequestPreparationModel:ExecutionProfile"] = "Deterministic",
            ["RequestPreparationModel:FoundryResponses:Endpoint"] = string.Empty,
            ["RequestPreparationModel:FoundryResponses:DeploymentName"] = string.Empty,
            ["TeamsAccessRequest:AllowedTenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            ["TeamsAccessRequest:BotConnectionName"] = BotConnectionName,
            ["TeamsAccessRequest:TrustedWebBaseUri"] =
                TrustedWebBaseUri.AbsoluteUri,
            ["TeamsAccessRequest:RequestTimeout"] = "00:01:40",
            ["TeamsAccessRequest:PreparationLifetime"] = "00:30:00",
        };
    }

    private static async Task ClearDatabaseAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        try
        {
            await using (var disableForeignKeys = connection.CreateCommand())
            {
                disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
                _ = await disableForeignKeys.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var transaction =
                await connection.BeginTransactionAsync(cancellationToken);
            var tableNames = dbContext.Model
                .GetEntityTypes()
                .Select(entityType => entityType.GetTableName())
                .Where(tableName => tableName is not null)
                .Select(tableName => tableName!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            foreach (var tableName in tableNames)
            {
                await using var deleteCommand = connection.CreateCommand();
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText =
                    $"DELETE FROM \"{tableName.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
                _ = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
        finally
        {
            await using var enableForeignKeys = connection.CreateCommand();
            enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            _ = await enableForeignKeys.ExecuteNonQueryAsync(cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
        }
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

    private sealed class TestTeamsAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(
                    TeamsAuthenticationHeaderName,
                    out var headerValue))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (!string.Equals(
                    headerValue.ToString(),
                    TeamsAuthenticationHeaderValue,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AuthenticateResult.Fail(
                        "The test Teams authentication marker is invalid."));
            }

            var sdkIdentity = AgentClaims.CreateIdentity(
                FakeTeamsActivityBuilder.DefaultBotAppId,
                anonymous: false,
                FakeTeamsActivityBuilder.DefaultChannelAppId);
            var authenticatedIdentity = new ClaimsIdentity(
                sdkIdentity.Claims,
                TeamsAuthenticationScheme,
                sdkIdentity.NameClaimType,
                sdkIdentity.RoleClaimType);
            var principal = new ClaimsPrincipal(authenticatedIdentity);
            var ticket = new AuthenticationTicket(
                principal,
                TeamsAuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
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

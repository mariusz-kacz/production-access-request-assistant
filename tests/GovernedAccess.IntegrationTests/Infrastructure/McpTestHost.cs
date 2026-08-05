using GovernedAccess.Core.Ports;
using GovernedAccess.Mcp;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class McpTestHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly SqliteConnection? databaseConnection;

    private McpTestHost(
        WebApplication application,
        SqliteConnection? databaseConnection)
    {
        this.application = application;
        this.databaseConnection = databaseConnection;
    }

    public static Task<McpTestHost> CreateAsync(
        IRequestContextReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return StartAsync(
            services => services.AddSingleton(reader),
            databaseConnection: null,
            initializeAsync: null,
            cancellationToken);
    }

    public static async Task<McpTestHost> CreateSeededAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);

        return await StartAsync(
            services =>
            {
                services.AddDbContext<GovernedAccessDbContext>(options =>
                    options.UseSqlite(connection));
                services.AddScoped<IRequestContextReader, EfRequestContextReader>();
            },
            connection,
            static async (serviceProvider, operationCancellationToken) =>
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<GovernedAccessDbContext>();
                await MinimalTestDataSeeder.SeedAsync(
                    dbContext,
                    operationCancellationToken);
            },
            cancellationToken);
    }

    public async Task<McpClient> CreateClientAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var httpClient = application.GetTestClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                Name = name,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            ownsHttpClient: true);

        try
        {
            return await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        if (databaseConnection is not null)
        {
            await databaseConnection.DisposeAsync();
        }
    }

    private static async Task<McpTestHost> StartAsync(
        Action<IServiceCollection> configureServices,
        SqliteConnection? databaseConnection,
        Func<IServiceProvider, CancellationToken, Task>? initializeAsync,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);
        builder.Services.AddGovernedAccessMcp();

        var application = builder.Build();
        application.MapGovernedAccessMcp();

        try
        {
            if (initializeAsync is not null)
            {
                await initializeAsync(application.Services, cancellationToken);
            }

            await application.StartAsync(cancellationToken);
            return new McpTestHost(application, databaseConnection);
        }
        catch
        {
            await application.DisposeAsync();
            if (databaseConnection is not null)
            {
                await databaseConnection.DisposeAsync();
            }

            throw;
        }
    }
}

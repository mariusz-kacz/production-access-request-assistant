using GovernedAccess.Mcp;
using GovernedAccess.ReferenceAuthority;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class TargetMcpTestHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly string databasePath;

    private TargetMcpTestHost(WebApplication application, string databasePath)
    {
        this.application = application;
        this.databasePath = databasePath;
    }

    internal IServiceProvider Services => application.Services;

    internal static async Task<TargetMcpTestHost> CreateSeededAsync(
        CancellationToken cancellationToken = default)
    {
        return await CreateSeededCoreAsync(
            loggerProvider: null,
            cancellationToken);
    }

    internal static async Task<TargetMcpTestHost> CreateSeededAsync(
        ILoggerProvider loggerProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loggerProvider);
        return await CreateSeededCoreAsync(loggerProvider, cancellationToken);
    }

    private static async Task<TargetMcpTestHost> CreateSeededCoreAsync(
        ILoggerProvider? loggerProvider,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-mcp-reference-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={databasePath};Pooling=False",
            });
        builder.Services.AddReferenceAuthority(builder.Configuration);
        builder.Services.AddGovernedAccessTargetMcp();

        var application = builder.Build();
        application.MapGovernedAccessTargetMcp();

        try
        {
            await ReferenceAuthorityDatabase.InitializeAsync(
                application.Services,
                cancellationToken);
            await application.StartAsync(cancellationToken);
            return new TargetMcpTestHost(application, databasePath);
        }
        catch
        {
            await application.DisposeAsync();
            File.Delete(databasePath);
            throw;
        }
    }

    internal async Task<McpClient> CreateClientAsync(
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
        File.Delete(databasePath);
    }
}

using System.Net;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Mcp;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Evaluation;

internal sealed class EvaluationHosting : IAsyncDisposable
{
    private readonly WebApplication application;
    private int disposed;

    private EvaluationHosting(
        WebApplication application,
        string databasePath)
    {
        this.application = application;
        DatabasePath = databasePath;
    }

    internal IServiceProvider Services => application.Services;

    internal string DatabasePath { get; }

    internal Uri BaseAddress { get; private set; } = null!;

    internal static async Task<EvaluationHosting> StartAsync(
        IConfiguration configuration,
        string temporaryRoot,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        ArgumentNullException.ThrowIfNull(configureServices);

        var resolvedTemporaryRoot = Path.GetFullPath(temporaryRoot);
        _ = Directory.CreateDirectory(resolvedTemporaryRoot);
        var databasePath = Path.Combine(
            resolvedTemporaryRoot,
            $"governed-access-evaluation-{Guid.NewGuid():N}.db");
        Uri? requestPreparationBaseAddress = null;

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(EvaluationHosting).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production,
            });
        builder.Configuration.AddConfiguration(configuration);
        builder.WebHost.ConfigureKestrel(static options =>
            options.Listen(IPAddress.Loopback, 0));

        builder.Services.AddDbContext<GovernedAccessDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddScoped<IRequestContextReader, EfRequestContextReader>();
        builder.Services.AddScoped<IWorkflowStore, EfWorkflowStore>();
        builder.Services.AddScoped<RequestDraftValidator>();
        builder.Services.AddScoped<AccessRequestValidator>();
        builder.Services.AddHttpClient();
        builder.Services.AddRequestPreparationChat(builder.Configuration);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton(_ => new RequestPreparationMcpEndpoint(
            () => requestPreparationBaseAddress));
        builder.Services.AddRequestPreparation();
        builder.Services.AddGovernedAccessMcp();
        builder.Services.AddSingleton(
            new LiveModelEvaluationOptions(GetTurnTimeout(builder.Configuration)));
        builder.Services.AddSingleton<EvaluationScenarioExecutor>();
        builder.Services.AddSingleton<LiveModelEvaluationRunner>();
        builder.Services.AddSingleton<LiveModelEvaluationCommand>();
        configureServices(builder.Services);

        var application = builder.Build();
        try
        {
            await SeedDatabaseAsync(application.Services, cancellationToken);
            application.MapGovernedAccessMcp();
            await application.StartAsync(cancellationToken);

            var address = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                .SingleOrDefault()
                ?? throw new InvalidOperationException(
                    "The evaluation host did not publish a loopback address.");
            requestPreparationBaseAddress = new Uri(
                address.EndsWith('/')
                    ? address
                    : $"{address}/",
                UriKind.Absolute);

            var hosting = new EvaluationHosting(application, databasePath)
            {
                BaseAddress = requestPreparationBaseAddress,
            };
            return hosting;
        }
        catch
        {
            await application.DisposeAsync();
            DeleteDatabaseFiles(databasePath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await application.StopAsync();
        }
        finally
        {
            await application.DisposeAsync();
            DeleteDatabaseFiles(DatabasePath);
        }
    }

    private static async Task SeedDatabaseAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        await SyntheticDataSeeder.SeedAsync(dbContext, cancellationToken);
    }

    private static TimeSpan GetTurnTimeout(ConfigurationManager configuration)
    {
        var configured = configuration["LiveModelEvaluation:TurnTimeout"];
        if (configured is null)
        {
            return LiveModelEvaluationOptions.DefaultTurnTimeout;
        }

        if (!TimeSpan.TryParse(configured, out var turnTimeout)
            || turnTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "LiveModelEvaluation.TurnTimeout must be a positive duration.");
        }

        return turnTimeout;
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        DeleteIfPresent(databasePath);
        DeleteIfPresent($"{databasePath}-shm");
        DeleteIfPresent($"{databasePath}-wal");
        DeleteIfPresent($"{databasePath}-journal");
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

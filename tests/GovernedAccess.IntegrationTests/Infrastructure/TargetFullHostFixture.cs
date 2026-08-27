using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.AccessRequests;
using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Mcp;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.ReferenceAuthority.Adapters;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authority;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class TargetFullHostFixture : IAsyncDisposable
{
    private WebApplication application;

    private TargetFullHostFixture(
        WebApplication application,
        string referenceDatabasePath,
        string workflowDatabasePath,
        DeterministicClock clock,
        TargetFullHostObservations observations)
    {
        this.application = application;
        ReferenceDatabasePath = referenceDatabasePath;
        WorkflowDatabasePath = workflowDatabasePath;
        Clock = clock;
        Observations = observations;
    }

    internal IServiceProvider Services => application.Services;

    internal string ReferenceDatabasePath { get; }

    internal string WorkflowDatabasePath { get; }

    internal DeterministicClock Clock { get; }

    internal TargetFullHostObservations Observations { get; }

    internal static async Task<TargetFullHostFixture> CreateAsync(
        IChatClient? chatClient = null,
        CancellationToken cancellationToken = default)
    {
        var referenceDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-full-reference-{Guid.NewGuid():N}.db");
        var workflowDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-full-workflow-{Guid.NewGuid():N}.db");
        var clock = new DeterministicClock(
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        var observations = new TargetFullHostObservations();
        var application = BuildApplication(
            referenceDatabasePath,
            workflowDatabasePath,
            clock,
            observations,
            chatClient ?? new RecordingChatClient(
                """{"schemaVersion":1,"dialogueAct":"unclear"}"""));

        try
        {
            await ReferenceAuthorityDatabase.InitializeAsync(
                application.Services,
                cancellationToken);
            await WorkflowPersistenceDatabase.InitializeAsync(
                application.Services,
                cancellationToken);
            await application.StartAsync(cancellationToken);
            return new TargetFullHostFixture(
                application,
                referenceDatabasePath,
                workflowDatabasePath,
                clock,
                observations);
        }
        catch
        {
            await application.DisposeAsync();
            File.Delete(referenceDatabasePath);
            File.Delete(workflowDatabasePath);
            throw;
        }
    }

    internal async Task<McpClient> CreateMcpClientAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                Name = name,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            application.GetTestClient(),
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

    internal async Task RestartAsync(
        IChatClient chatClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        await application.DisposeAsync();
        var restarted = BuildApplication(
            ReferenceDatabasePath,
            WorkflowDatabasePath,
            Clock,
            Observations,
            chatClient);
        try
        {
            await ReferenceAuthorityDatabase.InitializeAsync(
                restarted.Services,
                cancellationToken);
            await WorkflowPersistenceDatabase.InitializeAsync(
                restarted.Services,
                cancellationToken);
            await restarted.StartAsync(cancellationToken);
            application = restarted;
        }
        catch
        {
            await restarted.DisposeAsync();
            throw;
        }
    }

    internal Task<TResult> WithReferenceDatabaseOfflineAsync<TResult>(
        Func<Task<TResult>> action) =>
        WithDatabaseOfflineAsync(ReferenceDatabasePath, action);

    internal async Task WithWorkflowDatabaseOfflineAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = await WithDatabaseOfflineAsync(
            WorkflowDatabasePath,
            async () =>
            {
                await action();
                return true;
            });
    }

    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        File.Delete(ReferenceDatabasePath);
        File.Delete(WorkflowDatabasePath);
    }

    private static WebApplication BuildApplication(
        string referenceDatabasePath,
        string workflowDatabasePath,
        DeterministicClock clock,
        TargetFullHostObservations observations,
        IChatClient chatClient)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={referenceDatabasePath};Pooling=False",
                ["ConnectionStrings:WorkflowPersistence"] =
                    $"Data Source={workflowDatabasePath};Pooling=False",
            });

        var httpClientFactory = new TargetFullHostHttpClientFactory();
        builder.Services.AddReferenceAuthority(builder.Configuration);
        builder.Services.RemoveAll<IProductionEnvironmentSearchAuthority>();
        builder.Services.AddScoped<EfProductionEnvironmentSearchAuthority>();
        builder.Services.AddScoped<IProductionEnvironmentSearchAuthority>(services =>
            new ObservedEnvironmentSearchAuthority(
                services.GetRequiredService<
                    EfProductionEnvironmentSearchAuthority>(),
                observations));
        builder.Services.AddWorkflowPersistence(builder.Configuration);
        builder.Services.AddGovernedAccessMcp();
        builder.Services.AddScoped<
            IRequestContextReader,
            AuthoritativeRequestContextReader>();
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddSingleton<IChatClient>(chatClient);
        builder.Services.AddSingleton(AgentExecutionLimits.Default);
        builder.Services.AddSingleton(
            new AgentModelMetadata("test", "target-full-host", null));
        builder.Services.AddSingleton(
            new TargetAgentMcpEndpoint(() => new Uri("http://localhost/")));
        builder.Services.AddSingleton<IHttpClientFactory>(httpClientFactory);
        builder.Services.AddSingleton<ITurnProposalInterpreter>(services =>
            new MafTurnProposalInterpreter(
                services.GetRequiredService<IChatClient>(),
                services.GetRequiredService<AgentExecutionLimits>(),
                services.GetRequiredService<AgentModelMetadata>(),
                services.GetRequiredService<ILoggerFactory>(),
                services.GetRequiredService<TargetAgentMcpEndpoint>(),
                services.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddScoped<RequestPreparationReducer>();
        builder.Services.AddScoped<PreparationTurnService>();
        builder.Services.AddScoped<ITargetRequestPreparationOrchestrator>(services =>
            new TargetRequestPreparationOrchestrator(
                services.GetRequiredService<PreparationTurnService>(),
                services.GetRequiredService<ITurnProposalInterpreter>()));
        builder.Services.AddScoped<IPreparationConfirmationService,
            PreparationConfirmationService>();
        builder.Services.AddScoped<ITargetPreparedRequestCardFactory>(services =>
            new TargetPreparedRequestCardFactory(
                services.GetRequiredService<IAuthenticatedPrincipalReader>(),
                services.GetRequiredService<IProductionEnvironmentAuthority>(),
                services.GetRequiredService<IEnvironmentRoleAuthority>(),
                services.GetRequiredService<IIncidentAuthority>()));
        builder.Services.AddScoped<ITargetRequestConfirmation>(services =>
            new TargetRequestConfirmationAdapter(
                services.GetRequiredService<IPreparationConfirmationService>()));
        builder.Services.AddScoped<TargetTeamsAccessRequestAdapter>();

        builder.Services.AddScoped<AccessRequestValidator>();
        builder.Services.AddScoped<AccessRequestCommandContextLoader>();
        builder.Services.AddScoped<ProtectedProvisioningService>();
        builder.Services.AddScoped<AccessRequestWorkflowService>();
        builder.Services.AddSingleton<SyntheticAccessProvisionerControl>();
        builder.Services.AddSingleton<SyntheticAccessProvisioner>();
        builder.Services.AddSingleton<IAccessProvisioner>(services =>
            services.GetRequiredService<SyntheticAccessProvisioner>());

        var application = builder.Build();
        httpClientFactory.Application = application;
        application.MapGovernedAccessMcp();
        return application;
    }

    private static async Task<TResult> WithDatabaseOfflineAsync<TResult>(
        string databasePath,
        Func<Task<TResult>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var resolvedPath = Path.GetFullPath(databasePath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(
                temporaryRoot,
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(resolvedPath))
        {
            throw new InvalidOperationException(
                "Only an existing isolated target test database can be taken offline.");
        }

        var offlinePath = resolvedPath + $".offline-{Guid.NewGuid():N}";
        File.Move(resolvedPath, offlinePath);
        try
        {
            return await action();
        }
        finally
        {
            File.Delete(resolvedPath);
            File.Move(offlinePath, resolvedPath);
        }
    }

    private sealed class ObservedEnvironmentSearchAuthority(
        IProductionEnvironmentSearchAuthority inner,
        TargetFullHostObservations observations)
        : IProductionEnvironmentSearchAuthority
    {
        public Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            observations.RecordEnvironmentSearch();
            return inner.SearchAsync(query, cancellationToken);
        }
    }

    private sealed class TargetFullHostHttpClientFactory : IHttpClientFactory
    {
        internal WebApplication? Application { get; set; }

        public HttpClient CreateClient(string name)
        {
            if (!string.Equals(
                    name,
                    MafTurnProposalInterpreter.McpHttpClientName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The isolated target requested an unexpected HTTP client.");
            }

            return (Application
                ?? throw new InvalidOperationException(
                    "The isolated target host is not built."))
                .GetTestClient();
        }
    }
}

internal sealed class TargetFullHostObservations
{
    private int environmentSearchCount;

    internal int EnvironmentSearchCount =>
        Volatile.Read(ref environmentSearchCount);

    internal void RecordEnvironmentSearch() =>
        Interlocked.Increment(ref environmentSearchCount);
}

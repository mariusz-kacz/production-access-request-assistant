using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Mcp;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.Web.Ai;
using GovernedAccess.Workflow.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.Web.Evaluation;

internal sealed class EvaluationHosting : IAsyncDisposable
{
	private readonly WebApplication application;

	private int disposed;

	internal IServiceProvider Services => application.Services;

	internal string ReferenceDatabasePath { get; }

	internal string WorkflowDatabasePath { get; }

	internal Uri BaseAddress { get; private set; } = null!;

	private EvaluationHosting(WebApplication application, string referenceDatabasePath, string workflowDatabasePath)
	{
		this.application = application;
		ReferenceDatabasePath = referenceDatabasePath;
		WorkflowDatabasePath = workflowDatabasePath;
	}

	internal static async Task<EvaluationHosting> StartAsync(IConfiguration configuration, string temporaryRoot, Action<IServiceCollection> configureServices, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
		ArgumentNullException.ThrowIfNull(configureServices);
		string resolvedTemporaryRoot = Path.GetFullPath(temporaryRoot);
		Directory.CreateDirectory(resolvedTemporaryRoot);
		string referenceDatabasePath = Path.Combine(resolvedTemporaryRoot, $"evaluation-reference-{Guid.NewGuid():N}.db");
		string workflowDatabasePath = Path.Combine(resolvedTemporaryRoot, $"evaluation-workflow-{Guid.NewGuid():N}.db");
		Uri? evaluationMcpBaseAddress = null;
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			Args = Array.Empty<string>(),
			ApplicationName = typeof(EvaluationHosting).Assembly.GetName().Name,
			ContentRootPath = AppContext.BaseDirectory,
			EnvironmentName = Environments.Production
		});
		builder.Configuration.AddConfiguration(configuration);
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:ReferenceAuthority"] = "Data Source=" + referenceDatabasePath + ";Pooling=False",
			["ConnectionStrings:WorkflowPersistence"] = "Data Source=" + workflowDatabasePath + ";Pooling=False"
		});
		builder.WebHost.ConfigureKestrel(delegate(KestrelServerOptions options)
		{
			options.Listen(IPAddress.Loopback, 0);
		});
		RequestPreparationModelResolution modelResolution = RequestPreparationModelOptions.Bind(builder.Configuration).Validate();
		if (!modelResolution.IsValid || modelResolution.Profile != RequestPreparationModelProfile.FoundryResponses || modelResolution.DeploymentName == null)
		{
			throw new InvalidOperationException("A valid Foundry Responses profile is required for live evaluation.");
		}
		builder.Services.AddReferenceAuthority(builder.Configuration);
		builder.Services.AddWorkflowPersistence(builder.Configuration);
		builder.Services.AddGovernedAccessTargetMcp();
		builder.Services.AddRequestPreparationChat(builder.Configuration);
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton(AgentExecutionLimits.Load(builder.Configuration));
		builder.Services.AddSingleton(new AgentModelMetadata("FoundryResponses", modelResolution.DeploymentName, null));
		builder.Services.AddSingleton((IServiceProvider _) => new TargetAgentMcpEndpoint(() => evaluationMcpBaseAddress));
		builder.Services.AddScoped<EvaluationFailureControl>();
		builder.Services.AddScoped<IHttpClientFactory, EvaluationHttpClientFactory>();
		builder.Services.AddScoped((IServiceProvider services) => new MafTurnProposalInterpreter(services.GetRequiredService<IChatClient>(), services.GetRequiredService<AgentExecutionLimits>(), services.GetRequiredService<AgentModelMetadata>(), services.GetRequiredService<ILoggerFactory>(), services.GetRequiredService<TargetAgentMcpEndpoint>(), services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<TimeProvider>()));
		builder.Services.AddScoped<EvaluationRecordingInterpreter>();
		builder.Services.AddScoped((Func<IServiceProvider, ITurnProposalInterpreter>)((IServiceProvider services) => services.GetRequiredService<EvaluationRecordingInterpreter>()));
		builder.Services.AddScoped<RequestPreparationReducer>();
		builder.Services.AddScoped<PreparationTurnService>();
		builder.Services.AddScoped((IServiceProvider services) => new TargetRequestPreparationOrchestrator(services.GetRequiredService<PreparationTurnService>(), services.GetRequiredService<ITurnProposalInterpreter>()));
		builder.Services.AddSingleton<EvaluationScenarioExecutor>();
		builder.Services.AddSingleton<EvaluationRunner>();
		builder.Services.AddSingleton<LiveModelEvaluationCommand>();
		configureServices(builder.Services);
		WebApplication application = builder.Build();
		try
		{
			await ReferenceAuthorityDatabase.InitializeAsync(application.Services, cancellationToken);
			await WorkflowPersistenceDatabase.InitializeAsync(application.Services, cancellationToken);
			application.MapGovernedAccessTargetMcp();
			await application.StartAsync(cancellationToken);
			string address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault() ?? throw new InvalidOperationException("The evaluation host did not publish a loopback address.");
			evaluationMcpBaseAddress = new Uri(address.EndsWith('/') ? address : (address + "/"), UriKind.Absolute);
			return new EvaluationHosting(application, referenceDatabasePath, workflowDatabasePath)
			{
				BaseAddress = evaluationMcpBaseAddress
			};
		}
		catch
		{
			await application.DisposeAsync();
			DeleteDatabaseFiles(referenceDatabasePath);
			DeleteDatabaseFiles(workflowDatabasePath);
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			try
			{
				await application.StopAsync();
			}
			finally
			{
				await application.DisposeAsync();
				DeleteDatabaseFiles(ReferenceDatabasePath);
				DeleteDatabaseFiles(WorkflowDatabasePath);
			}
		}
	}

	private static void DeleteDatabaseFiles(string databasePath)
	{
		SqliteConnection.ClearAllPools();
		DeleteIfPresent(databasePath);
		DeleteIfPresent(databasePath + "-shm");
		DeleteIfPresent(databasePath + "-wal");
		DeleteIfPresent(databasePath + "-journal");
	}

	private static void DeleteIfPresent(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}
}

internal sealed class EvaluationFailureControl
{
	internal EvaluationFailureMode Mode { get; set; }
}

internal sealed class EvaluationRecordingInterpreter(MafTurnProposalInterpreter inner, EvaluationFailureControl failureControl, AgentModelMetadata modelMetadata, TimeProvider timeProvider) : ITurnProposalInterpreter
{
	private readonly List<AgentInterpretationResult> results = new List<AgentInterpretationResult>();

	internal IReadOnlyList<AgentInterpretationResult> Results => results.AsReadOnly();

	public async Task<AgentInterpretationResult> InterpretAsync(AgentTurnInput turn, CancellationToken cancellationToken)
	{
		AgentInterpretationResult result;
		if (failureControl.Mode == EvaluationFailureMode.ProviderUnavailable)
		{
			DateTimeOffset now = timeProvider.GetUtcNow();
			result = new AgentInterpretationFailed(AgentInterpretationFailure.Unavailable, new AgentExecutionMetadata(modelMetadata.ProviderId, modelMetadata.ModelDeployment, modelMetadata.ProviderModelVersion, "3.0.0", "3.0.0", "3.0.0", "2.0.0", 0, 0, turn.CorrelationId, now, now, Array.Empty<string>()));
		}
		else
		{
			result = await inner.InterpretAsync(turn, cancellationToken);
		}
		results.Add(result);
		return result;
	}
}

internal sealed class EvaluationHttpClientFactory(EvaluationFailureControl failureControl) : IHttpClientFactory
{
	private sealed class UnavailableHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromException<HttpResponseMessage>(new HttpRequestException("The evaluation MCP endpoint is unavailable."));
		}
	}

	public HttpClient CreateClient(string name)
	{
		if (!string.Equals(name, "GovernedAccess.TargetMafMcpLoopback", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The evaluation host requested an unsupported HTTP client.");
		}
		return (failureControl.Mode == EvaluationFailureMode.McpUnavailable) ? new HttpClient(new UnavailableHandler(), disposeHandler: true) : new HttpClient();
	}
}

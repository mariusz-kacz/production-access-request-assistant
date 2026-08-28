using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Mcp;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.Web.Authority;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Evaluation;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Web.Security;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;

if (LiveModelEvaluationCommand.IsRequested(args))
{
    return await RunLiveModelEvaluationAsync(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Response.Headers.TryGetValue(
                CorrelationContext.HeaderName,
                out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId.ToString();
        }
    };
});
builder.Services.AddReferenceAuthority(builder.Configuration);
builder.Services.AddWorkflowPersistence(builder.Configuration);
builder.Services.AddScoped<IRequestContextReader, AuthoritativeRequestContextReader>();
builder.Services.AddScoped<AccessRequestValidator>();
builder.Services.AddScoped<AccessRequestVisibilityPolicy>();
builder.Services.AddScoped<AccessRequestCommandContextLoader>();
builder.Services.AddScoped<AccessRequestQueryService>();
builder.Services.AddScoped<ProtectedProvisioningService>();
builder.Services.AddScoped<AccessRequestWorkflowService>();
builder.Services.AddSingleton<SyntheticAccessProvisionerControl>();
builder.Services.AddSingleton<SyntheticAccessProvisioner>();
builder.Services.AddSingleton<IAccessProvisioner>(serviceProvider =>
    serviceProvider.GetRequiredService<SyntheticAccessProvisioner>());
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<GovernedAccessInstrumentation>();
builder.Services.AddDemoAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddGovernedAccessAntiforgery();
builder.Services.AddGovernedAccessMcp();
builder.AddGovernedAccessTeams();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationMiddleware>();
app.UseRequestTimeouts();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGovernedAccessTeams();
app.MapControllers();
app.MapGovernedAccessMcp();

app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/mcp/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await InitializeDatabasesAsync(app);
await app.RunAsync();
return 0;

static async Task<int> RunLiveModelEvaluationAsync(string[] arguments)
{
    var configuration = BuildEvaluationConfiguration();
    var parsedArguments = LiveModelEvaluationCommand.ParseArguments(
        arguments,
        Directory.GetCurrentDirectory());
    if (parsedArguments.IsFailure)
    {
        Console.Error.WriteLine(parsedArguments.Failure!.Message);
        return LiveModelEvaluationCommand.GetExitCode(
            EvaluationRunStatus.PrerequisiteFailed);
    }

    var modelResolution = RequestPreparationModelOptions
        .Bind(configuration)
        .Validate();
    var liveProfile = LiveModelEvaluationCommand.ValidateLiveProfile(
        modelResolution);
    if (liveProfile.IsFailure)
    {
        Console.Error.WriteLine(liveProfile.Failure!.Message);
        return LiveModelEvaluationCommand.GetExitCode(
            EvaluationRunStatus.PrerequisiteFailed);
    }

    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancellationHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancellationHandler;

    try
    {
        var sourceCommit = await EvaluationSourceCommitResolver.ResolveAsync(
            Directory.GetCurrentDirectory(),
            cancellation.Token);
        await using var hosting = await EvaluationHosting.StartAsync(
            configuration,
            Path.GetTempPath(),
            new EvaluationSourceMetadata(sourceCommit),
            static _ => { },
            cancellation.Token);
        var command = hosting.Services
            .GetRequiredService<LiveModelEvaluationCommand>();
        return await command.RunAsync(
            parsedArguments.Value,
            cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        return LiveModelEvaluationCommand.GetExitCode(
            EvaluationRunStatus.Cancelled);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            "Live-model evaluation could not start because a required dependency is unavailable.");
        Console.Error.WriteLine(ex.Message);
        return LiveModelEvaluationCommand.GetExitCode(
            EvaluationRunStatus.PrerequisiteFailed);
    }
    finally
    {
        Console.CancelKeyPress -= cancellationHandler;
    }
}

static IConfiguration BuildEvaluationConfiguration()
{
    var environmentName =
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environments.Production;

    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile(
            $"appsettings.{environmentName}.json",
            optional: true,
            reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();
}

static async Task InitializeDatabasesAsync(WebApplication application)
{
    await ReferenceAuthorityDatabase.InitializeAsync(
        application.Services,
        application.Lifetime.ApplicationStopping);
    await WorkflowPersistenceDatabase.InitializeAsync(
        application.Services,
        application.Lifetime.ApplicationStopping);
}

public partial class Program;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.AccessRequests;
using GovernedAccess.Core.Application.Drafts;
using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Ports;
using GovernedAccess.Mcp;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Evaluation;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Web.Security;
using GovernedAccess.Web.Teams;
using Microsoft.EntityFrameworkCore;

const string databaseConnectionStringName = "GovernedAccess";
const string defaultDatabaseConnectionString = "Data Source=governed-access.db";

if (LiveModelEvaluationCommand.IsRequested(args))
{
    return await RunLiveModelEvaluationAsync(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);
var databaseConnectionString = builder.Configuration.GetConnectionString(
        databaseConnectionStringName)
    ?? defaultDatabaseConnectionString;

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
builder.Services.AddDbContext<GovernedAccessDbContext>(options =>
    options.UseSqlite(databaseConnectionString));
builder.Services.AddScoped<IRequestContextReader, EfRequestContextReader>();
builder.Services.AddScoped<IWorkflowStore, EfWorkflowStore>();
builder.Services.AddScoped<RequestDraftValidator>();
builder.Services.AddScoped<AccessRequestValidator>();
builder.Services.AddScoped<RequestSubmissionService>();
builder.Services.AddScoped<AccessRequestVisibilityPolicy>();
builder.Services.AddScoped<AccessRequestQueryService>();
builder.Services.AddScoped<ProtectedProvisioningService>();
builder.Services.AddScoped<AccessRequestCommandContextLoader>();
builder.Services.AddScoped<AccessRequestWorkflowService>();
builder.Services.AddSingleton<SyntheticAccessProvisionerControl>();
builder.Services.AddSingleton<SyntheticAccessProvisioner>();
builder.Services.AddSingleton<IAccessProvisioner>(serviceProvider =>
    serviceProvider.GetRequiredService<SyntheticAccessProvisioner>());
builder.Services.AddHttpClient();
builder.Services.AddRequestPreparationChat(builder.Configuration);
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

await SeedDatabaseAsync(app);
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
        await using var hosting = await EvaluationHosting.StartAsync(
            configuration,
            Path.GetTempPath(),
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
    catch (Exception)
    {
        Console.Error.WriteLine(
            "Live-model evaluation could not start because a required dependency is unavailable.");
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

static async Task SeedDatabaseAsync(WebApplication application)
{
    await using var scope = application.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
    await SyntheticDataSeeder.SeedAsync(
        dbContext,
        application.Lifetime.ApplicationStopping);
}

public partial class Program;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}

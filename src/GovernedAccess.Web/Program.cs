using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Mcp;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using GovernedAccess.Web.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

const string databaseConnectionStringName = "GovernedAccess";
const string defaultDatabaseConnectionString = "Data Source=governed-access.db";

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
builder.Services.AddScoped<RequestValidator>();
builder.Services.AddScoped<RequestSubmissionService>();
builder.Services.AddScoped<RequestQueryService>();
builder.Services.AddScoped<ProtectedProvisioningService>();
builder.Services.AddScoped<AccessRequestWorkflowService>();
builder.Services.AddSingleton<SyntheticAccessProvisionerControl>();
builder.Services.AddSingleton<SyntheticAccessProvisioner>();
builder.Services.AddSingleton<IAccessProvisioner>(serviceProvider =>
    serviceProvider.GetRequiredService<SyntheticAccessProvisioner>());
builder.Services.AddHttpClient();
builder.Services
    .AddChatClient(_ => new DeterministicChatClient(DeterministicChatMode.Valid))
    .UseFunctionInvocation(configure: static client =>
    {
        client.AllowConcurrentInvocation = false;
        client.IncludeDetailedErrors = false;
        client.MaximumIterationsPerRequest = 6;
        client.TerminateOnUnknownCalls = true;
    });
builder.Services.AddScoped<IRequestDraftInterpreter, ChatRequestDraftInterpreter>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<GovernedAccessInstrumentation>();
builder.Services.AddDemoAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddGovernedAccessAntiforgery();
builder.Services.AddGovernedAccessMcp();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapGovernedAccessMcp();

app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/mcp/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await SeedDatabaseAsync(app);
app.Run();

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

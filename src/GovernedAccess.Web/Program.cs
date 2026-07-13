using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Endpoints;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;

const string databaseConnectionStringName = "GovernedAccess";
const string defaultDatabaseConnectionString = "Data Source=governed-access.db";

var builder = WebApplication.CreateBuilder(args);
var databaseConnectionString = builder.Configuration.GetConnectionString(
        databaseConnectionStringName)
    ?? defaultDatabaseConnectionString;

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
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<GovernedAccessInstrumentation>();
builder.Services.AddDemoAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddSessionEndpoints();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true);

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

var api = app.MapGroup("/api");
api.MapSessionEndpoints();

app.MapMcp("/mcp");

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

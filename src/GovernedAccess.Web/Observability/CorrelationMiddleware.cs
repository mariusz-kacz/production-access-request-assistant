using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.Web.Observability;

public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";

    internal const string ItemName = "GovernedAccess.CorrelationId";

    public static string GetCorrelationId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ItemName, out var value) && value is string correlationId
            ? correlationId
            : throw new InvalidOperationException(
                "CorrelationMiddleware must run before accessing the correlation ID.");
    }
}

public sealed class CorrelationMiddleware(
    RequestDelegate next,
    ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = GetCorrelationId();
        context.Items[CorrelationContext.ItemName] = correlationId;
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        using var logScope = logger.BeginScope(
            new Dictionary<string, object?> { ["CorrelationId"] = correlationId });

        await next(context);
    }

    private static string GetCorrelationId()
    {
        var traceId = Activity.Current?.TraceId ?? default;
        return traceId != default ? traceId.ToString() : Guid.NewGuid().ToString("N");
    }
}

public sealed class GovernedAccessInstrumentation : IDisposable
{
    public const string ActivitySourceName = "GovernedAccess.Web";

    public ActivitySource Activities { get; } = new(ActivitySourceName);

    public Activity? StartOperation(
        string operationName,
        string? actorId = null,
        ActivityKind kind = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var activity = Activities.StartActivity(operationName, kind);
        activity?.SetTag("actor.id", actorId);
        return activity;
    }

    public void Dispose()
    {
        Activities.Dispose();
    }
}

public static class ActivityOutcomeExtensions
{
    public static void RecordOutcome(
        this Activity? activity,
        string outcomeCode,
        bool succeeded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcomeCode);

        activity?.SetTag("operation.outcome", outcomeCode);
        activity?.SetStatus(succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}

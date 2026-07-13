using System.Diagnostics;
using GovernedAccess.Web.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Observability;

public sealed class CorrelationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsyncUsesTheCurrentW3CTraceId()
    {
        using var activity = new Activity("incoming-request")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var context = new DefaultHttpContext();
        string? observedCorrelationId = null;
        var middleware = CreateMiddleware(nextContext =>
        {
            observedCorrelationId = nextContext.GetCorrelationId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        var expectedCorrelationId = activity.TraceId.ToString();
        Assert.Equal(expectedCorrelationId, observedCorrelationId);
        Assert.Equal(
            expectedCorrelationId,
            context.Response.Headers[CorrelationContext.HeaderName].ToString());
    }

    [Fact]
    public void InstrumentationRecordsStructuredOutcomeOnAnActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == GovernedAccessInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var instrumentation = new GovernedAccessInstrumentation();
        using var activity = instrumentation.StartOperation(
            "mcp.get_incident",
            "requester",
            ActivityKind.Client);

        Assert.NotNull(activity);
        activity.RecordOutcome("not_found", succeeded: false);
        activity.Stop();

        Assert.Equal("requester", activity.GetTagItem("actor.id"));
        Assert.Equal("not_found", activity.GetTagItem("operation.outcome"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.True(activity.Duration >= TimeSpan.Zero);
    }

    private static CorrelationMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new CorrelationMiddleware(
            next,
            NullLogger<CorrelationMiddleware>.Instance);
    }
}

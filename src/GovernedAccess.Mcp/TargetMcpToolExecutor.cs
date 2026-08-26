using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovernedAccess.Core.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.Mcp;

public sealed partial class TargetMcpToolExecutor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<TargetMcpToolExecutor> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    internal static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    internal async Task<CallToolResult> ExecuteAsync<TResult>(
        string toolName,
        Func<CancellationToken, Task<ApplicationResult<TResult>>> read,
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = await read(cancellationToken);
            return result.IsFailure
                ? Complete(
                    toolName,
                    startedAt,
                    ToFailureEnvelope(result.Failure!),
                    isError: true)
                : Complete(toolName, startedAt, result.Value, isError: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCompletion(toolName, startedAt, "Cancelled");
            throw;
        }
    }

    private CallToolResult Complete<TResult>(
        string toolName,
        long startedAt,
        TResult result,
        bool isError)
        where TResult : notnull
    {
        var structuredContent = JsonSerializer.SerializeToElement(
            result,
            SerializerOptions);
        var outcome = result is TargetMcpFailureEnvelope failure
            ? failure.Outcome
            : "Succeeded";

        LogCompletion(toolName, startedAt, outcome);

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = structuredContent.GetRawText(),
                },
            ],
            StructuredContent = structuredContent,
            IsError = isError,
        };
    }

    private TargetMcpFailureEnvelope ToFailureEnvelope(ApplicationFailure failure)
    {
        var outcome = failure.Kind switch
        {
            ApplicationFailureKind.InvalidInput => "InvalidInput",
            ApplicationFailureKind.NotFound => "NotFound",
            ApplicationFailureKind.Timeout => "Timeout",
            ApplicationFailureKind.Cancelled => "Cancelled",
            ApplicationFailureKind.Unauthenticated
                or ApplicationFailureKind.Unauthorized
                or ApplicationFailureKind.InvalidTransition
                or ApplicationFailureKind.ConcurrencyConflict
                or ApplicationFailureKind.DependencyUnavailable
                or ApplicationFailureKind.DependencyFailure => "Unavailable",
            _ => "Unavailable",
        };

        return new TargetMcpFailureEnvelope(
            outcome,
            failure.Code,
            failure.Message,
            GetCorrelationId());
    }

    private string GetCorrelationId()
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return Activity.Current?.TraceId.ToString() ?? "correlation-unavailable";
        }

        if (context.Response.Headers.TryGetValue(
                CorrelationHeaderName,
                out var correlationId)
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.ToString();
        }

        return context.TraceIdentifier;
    }

    private void LogCompletion(string toolName, long startedAt, string outcome)
    {
        var durationMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LogToolCompleted(
            logger,
            toolName,
            durationMilliseconds,
            outcome);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Information,
        Message = "Target MCP tool {ToolName} completed in {DurationMilliseconds} ms with outcome {Outcome}.")]
    private static partial void LogToolCompleted(
        ILogger logger,
        string toolName,
        double durationMilliseconds,
        string outcome);
}

public sealed record TargetMcpFailureEnvelope(
    string Outcome,
    string Code,
    string Message,
    string CorrelationId);

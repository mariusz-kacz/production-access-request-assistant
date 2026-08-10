using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GovernedAccess.Mcp;

public sealed partial class RequestContextTools(
    IRequestContextReader requestContext,
    IHttpContextAccessor httpContextAccessor,
    ILogger<RequestContextTools> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    internal static JsonSerializerOptions SerializerOptions { get; } =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

    [McpServerTool(
        Name = "get_production_environment",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ProductionEnvironmentToolResult))]
    [Description("Discovers the bounded production-environment catalog or gets one environment by stable identifier, including authoritative client and assigned role context.")]
    public Task<CallToolResult> GetProductionEnvironmentAsync(
        [Description("Optional stable production-environment identifier. Omit for bounded discovery.")]
        [MinLength(1)]
        string environmentId = null!,
        CancellationToken cancellationToken = default)
    {
        if (environmentId is not null && string.IsNullOrWhiteSpace(environmentId))
        {
            return Task.FromResult(InvalidIdentifier("get_production_environment"));
        }

        return environmentId is null
            ? ExecuteAsync(
                "get_production_environment",
                requestContext.ListProductionEnvironmentContextsAsync,
                CreateProductionEnvironmentResult,
                cancellationToken)
            : ExecuteAsync(
                "get_production_environment",
                token => requestContext.GetProductionEnvironmentContextAsync(
                    environmentId.Trim(),
                    token),
                static environment => CreateProductionEnvironmentResult([environment]),
                cancellationToken);
    }

    [McpServerTool(
        Name = "get_incident",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(IncidentToolResult))]
    [Description("Gets one incident using the precise stable identifier supplied by the requester.")]
    public Task<CallToolResult> GetIncidentAsync(
        [Description("Precise stable incident identifier; titles, descriptions, and partial IDs are not accepted.")]
        [MinLength(1)]
        string incidentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(incidentId))
        {
            return Task.FromResult(InvalidIdentifier("get_incident"));
        }

        return ExecuteAsync(
            "get_incident",
            token => requestContext.GetIncidentAsync(incidentId.Trim(), token),
            static incident => new IncidentToolResult(
                incident.Id,
                incident.Title,
                incident.Status.ToString(),
                incident.EnvironmentId),
            cancellationToken);
    }

    private async Task<CallToolResult> ExecuteAsync<TSource, TResult>(
        string toolName,
        Func<CancellationToken, Task<ApplicationResult<TSource>>> read,
        Func<TSource, TResult> createResult,
        CancellationToken cancellationToken)
        where TSource : notnull
        where TResult : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = await read(cancellationToken);
            if (result.IsFailure)
            {
                return Complete(
                    toolName,
                    startedAt,
                    ToFailureEnvelope(result.Failure!),
                    isError: true);
            }

            return Complete(
                toolName,
                startedAt,
                createResult(result.Value),
                isError: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCompletion(toolName, startedAt, "Cancelled");
            throw;
        }
    }

    private CallToolResult InvalidIdentifier(string toolName)
    {
        return Complete(
            toolName,
            Stopwatch.GetTimestamp(),
            new McpFailureEnvelope(
                "InvalidInput",
                "request-context-identifier-required",
                "A non-empty stable identifier is required.",
                GetCorrelationId()),
            isError: true);
    }

    private static ProductionEnvironmentToolResult CreateProductionEnvironmentResult(
        IEnumerable<ProductionEnvironmentContext> contexts)
    {
        return new ProductionEnvironmentToolResult(
            contexts
                .OrderBy(
                    context => context.Environment.Id,
                    StringComparer.Ordinal)
                .Select(context => new ProductionEnvironmentToolEnvironment(
                    context.Environment.Id,
                    context.Client.Id,
                    context.Client.DisplayName,
                    context.Environment.DisplayName,
                    context.AssignedRoles
                        .OrderBy(role => role.RoleId, StringComparer.Ordinal)
                        .Select(role => new ProductionEnvironmentToolRole(
                            role.RoleId,
                            GetRoleDisplayName(role.RoleId)))
                        .ToArray()))
                .ToArray());
    }

    private CallToolResult Complete<T>(
        string toolName,
        long startedAt,
        T result,
        bool isError)
        where T : notnull
    {
        var structuredContent = JsonSerializer.SerializeToElement(result, SerializerOptions);
        var outcome = result is McpFailureEnvelope failure ? failure.Outcome : "Succeeded";

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

    private McpFailureEnvelope ToFailureEnvelope(ApplicationFailure failure)
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

        return new McpFailureEnvelope(
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

    private static string GetRoleDisplayName(string roleId)
    {
        return roleId switch
        {
            ProductionRoleIds.ReadOnly => "Production read-only",
            ProductionRoleIds.Support => "Production support",
            ProductionRoleIds.Deployment => "Production deployment",
            _ => throw new InvalidOperationException(
                $"Unsupported stored role identifier '{roleId}'."),
        };
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "MCP tool {ToolName} completed in {DurationMilliseconds} ms with outcome {Outcome}.")]
    private static partial void LogToolCompleted(
        ILogger logger,
        string toolName,
        double durationMilliseconds,
        string outcome);
}

public sealed record ProductionEnvironmentToolResult(
    IReadOnlyList<ProductionEnvironmentToolEnvironment> Environments);

public sealed record ProductionEnvironmentToolEnvironment(
    string EnvironmentId,
    string ClientId,
    string ClientDisplayName,
    string DisplayName,
    IReadOnlyList<ProductionEnvironmentToolRole> Roles);

public sealed record ProductionEnvironmentToolRole(string RoleId, string DisplayName);

public sealed record IncidentToolResult(
    string IncidentId,
    string Title,
    string Status,
    string EnvironmentId);

public sealed record McpFailureEnvelope(
    string Outcome,
    string Code,
    string Message,
    string CorrelationId);

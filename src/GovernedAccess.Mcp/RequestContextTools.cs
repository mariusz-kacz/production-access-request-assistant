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
    [Description("Gets current stored configuration for one production environment.")]
    public Task<CallToolResult> GetProductionEnvironmentAsync(
        [Description("Stable production environment identifier.")]
        [MinLength(1)]
        string environmentId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "get_production_environment",
            environmentId,
            requestContext.GetProductionEnvironmentAsync,
            static environment => new ProductionEnvironmentToolResult(
                environment.Id,
                environment.ClientId,
                environment.DisplayName,
                environment.MaximumDurationMinutes,
                environment.BusinessApproverPrincipalId),
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
    [Description("Gets current stored context for one incident.")]
    public Task<CallToolResult> GetIncidentAsync(
        [Description("Stable incident identifier.")]
        [MinLength(1)]
        string incidentId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "get_incident",
            incidentId,
            requestContext.GetIncidentAsync,
            static incident => new IncidentToolResult(
                incident.Id,
                incident.Title,
                incident.Status.ToString(),
                incident.ClientId,
                incident.EnvironmentId),
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_available_roles",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AvailableRolesToolResult))]
    [Description("Gets the roles currently assigned to one production environment.")]
    public Task<CallToolResult> GetAvailableRolesAsync(
        [Description("Stable production environment identifier.")]
        [MinLength(1)]
        string environmentId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "get_available_roles",
            environmentId,
            requestContext.GetEnvironmentRolesAsync,
            roles => new AvailableRolesToolResult(
                environmentId.Trim(),
                roles
                    .OrderBy(role => role.RoleId, StringComparer.Ordinal)
                    .Select(role => new AvailableRoleToolResult(
                        role.RoleId,
                        GetRoleDisplayName(role.RoleId)))
                    .ToArray()),
            cancellationToken);
    }

    private async Task<CallToolResult> ExecuteAsync<TSource, TResult>(
        string toolName,
        string identifier,
        Func<string, CancellationToken, Task<ApplicationResult<TSource>>> read,
        Func<TSource, TResult> createResult,
        CancellationToken cancellationToken)
        where TSource : notnull
        where TResult : notnull
    {
        var startedAt = Stopwatch.GetTimestamp();

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Complete(
                toolName,
                startedAt,
                new McpFailureEnvelope(
                    "InvalidInput",
                    "request-context-identifier-required",
                    "A non-empty stable identifier is required.",
                    GetCorrelationId()),
                isError: true);
        }

        try
        {
            var result = await read(identifier.Trim(), cancellationToken);
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
    string EnvironmentId,
    string ClientId,
    string DisplayName,
    int MaximumDurationMinutes,
    string BusinessApproverResponsibilityId);

public sealed record IncidentToolResult(
    string IncidentId,
    string Title,
    string Status,
    string ClientId,
    string? EnvironmentId);

public sealed record AvailableRolesToolResult(
    string EnvironmentId,
    IReadOnlyList<AvailableRoleToolResult> Roles);

public sealed record AvailableRoleToolResult(string RoleId, string DisplayName);

public sealed record McpFailureEnvelope(
    string Outcome,
    string Code,
    string Message,
    string CorrelationId);

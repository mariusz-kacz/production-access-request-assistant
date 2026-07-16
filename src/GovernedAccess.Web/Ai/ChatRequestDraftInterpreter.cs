using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.Web.Ai;

public sealed partial class ChatRequestDraftInterpreter(
    IChatClient chatClient,
    IRequestContextReader requestContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ChatRequestDraftInterpreter> logger) : IRequestDraftInterpreter
{
    private const string ConfigurationSection = "DraftInterpretation";
    private const string CorrelationHeaderName = "X-Correlation-ID";
    private const string McpEndpointConfiguration = ConfigurationSection + ":McpEndpoint";
    private const string McpTimeoutConfiguration = ConfigurationSection + ":McpTimeout";
    private const string ModelTimeoutConfiguration = ConfigurationSection + ":ModelTimeout";

    private const string ProductionEnvironmentToolName = "get_production_environment";
    private const string IncidentToolName = "get_incident";
    private const string AvailableRolesToolName = "get_available_roles";
    private const string McpProtocolVersion = "2025-11-25";

    private static readonly TimeSpan DefaultModelTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMcpTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri DefaultMcpEndpoint = new("http://localhost:5136/mcp");

    private static readonly string[] ExpectedToolNames =
    [
        ProductionEnvironmentToolName,
        IncidentToolName,
        AvailableRolesToolName,
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonElement DraftSchema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": [
            "clientId",
            "environmentId",
            "requestedRole",
            "durationMinutes",
            "justification",
            "incidentId"
          ],
          "properties": {
            "clientId": { "type": ["string", "null"], "minLength": 1 },
            "environmentId": { "type": ["string", "null"], "minLength": 1 },
            "requestedRole": {
              "type": ["string", "null"],
              "enum": ["ProductionReadOnly", "ProductionSupport", null]
            },
            "durationMinutes": { "type": ["integer", "null"], "minimum": 1 },
            "justification": { "type": ["string", "null"], "maxLength": 2000 },
            "incidentId": { "type": ["string", "null"], "minLength": 1 }
          }
        }
        """).RootElement.Clone();

    private static readonly ChatMessage SystemMessage = new(
        ChatRole.System,
        """
        Interpret temporary production-access intent as one JSON object matching the supplied schema.
        Use only the supplied read-only context tools when stored context is needed. Return stable identifiers,
        never invent approval or provisioning facts, and use null for information that cannot be established.
        """);

    public async Task<DraftInterpretationOutcome> InterpretAsync(
        DraftInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = Stopwatch.GetTimestamp();
        var modelTimeout = GetPositiveTimeout(ModelTimeoutConfiguration, DefaultModelTimeout);
        using var modelDeadline = new CancellationTokenSource(modelTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            modelDeadline.Token);

        DraftInterpretationOutcome outcome;

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var mcpTimeout = GetPositiveTimeout(
                McpTimeoutConfiguration,
                DefaultMcpTimeout);
            httpClient.Timeout = mcpTimeout;
            httpClient.DefaultRequestHeaders.Add(
                CorrelationHeaderName,
                request.CorrelationId);

            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = GetMcpEndpoint(),
                    Name = "governed-access-draft-interpreter",
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = mcpTimeout,
                },
                httpClient,
                ownsHttpClient: false);

            McpClient mcpClient;
            try
            {
                mcpClient = await McpClient.CreateAsync(
                    transport,
                    new McpClientOptions
                    {
                        InitializationTimeout = mcpTimeout,
                        ProtocolVersion = McpProtocolVersion,
                    },
                    cancellationToken: linkedCancellation.Token);
            }
            catch
            {
                await transport.DisposeAsync();
                throw;
            }

            await using (mcpClient)
            {
                var tools = await ListAllowedToolsAsync(
                    mcpClient,
                    linkedCancellation.Token);
                var chatOptions = new ChatOptions
                {
                    AllowMultipleToolCalls = false,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(
                        DraftSchema,
                        schemaName: "access_request_draft",
                        schemaDescription:
                            "An untrusted structured production-access request draft."),
                    Temperature = 0,
                    Tools = [.. tools],
                };

                var response = await chatClient.GetResponseAsync(
                    [SystemMessage, new ChatMessage(ChatRole.User, request.Intent)],
                    chatOptions,
                    linkedCancellation.Token);

                var payload = JsonSerializer.Deserialize<DraftPayload>(
                    response.Text,
                    SerializerOptions);
                if (payload is null)
                {
                    outcome = Failure(DraftInterpretationOutcomeKind.MalformedModelOutput);
                }
                else
                {
                    var draft = payload.ToDraft();
                    outcome = await RevalidateIdentifiersAsync(
                        draft,
                        linkedCancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = Failure(DraftInterpretationOutcomeKind.Cancelled);
        }
        catch (OperationCanceledException) when (modelDeadline.IsCancellationRequested)
        {
            outcome = Failure(DraftInterpretationOutcomeKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            outcome = Failure(DraftInterpretationOutcomeKind.Timeout);
        }
        catch (TimeoutException)
        {
            outcome = Failure(cancellationToken.IsCancellationRequested
                ? DraftInterpretationOutcomeKind.Cancelled
                : DraftInterpretationOutcomeKind.Timeout);
        }
        catch (JsonException)
        {
            outcome = Failure(DraftInterpretationOutcomeKind.MalformedModelOutput);
        }
        catch (ArgumentException)
        {
            outcome = Failure(DraftInterpretationOutcomeKind.MalformedModelOutput);
        }
        catch (HttpRequestException)
        {
            outcome = Failure(GetDependencyFailureKind(
                cancellationToken,
                modelDeadline.Token));
        }
        catch (McpException)
        {
            outcome = Failure(GetDependencyFailureKind(
                cancellationToken,
                modelDeadline.Token));
        }
        catch (InvalidOperationException)
        {
            outcome = Failure(GetDependencyFailureKind(
                cancellationToken,
                modelDeadline.Token));
        }
        var durationMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var outcomeName = outcome.Kind.ToString();
        LogModelCompleted(
            logger,
            request.CorrelationId,
            durationMilliseconds,
            outcomeName);
        return outcome;
    }

    private static DraftInterpretationOutcomeKind GetDependencyFailureKind(
        CancellationToken callerCancellation,
        CancellationToken modelDeadline)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            return DraftInterpretationOutcomeKind.Cancelled;
        }

        return modelDeadline.IsCancellationRequested
            ? DraftInterpretationOutcomeKind.Timeout
            : DraftInterpretationOutcomeKind.Unavailable;
    }

    private async Task<IList<McpClientTool>> ListAllowedToolsAsync(
        McpClient mcpClient,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "Unavailable";

        try
        {
            var tools = await mcpClient.ListToolsAsync(
                cancellationToken: cancellationToken);
            if (tools.Count != ExpectedToolNames.Length
                || !tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(ExpectedToolNames))
            {
                throw new InvalidOperationException(
                    "The MCP server tool catalog does not match the configured allowlist.");
            }

            outcome = "Succeeded";
            return tools;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "Cancelled";
            throw;
        }
        catch (TimeoutException)
        {
            outcome = "Timeout";
            throw;
        }
        finally
        {
            var durationMilliseconds =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogMcpCompleted(
                logger,
                "tools/list",
                durationMilliseconds,
                outcome);
        }
    }

    private async Task<DraftInterpretationOutcome> RevalidateIdentifiersAsync(
        AccessRequestDraft draft,
        CancellationToken cancellationToken)
    {
        Client? client = null;
        ProductionEnvironment? environment = null;

        if (draft.ClientId is not null)
        {
            var clientResult = await requestContext.GetClientAsync(
                draft.ClientId,
                cancellationToken);
            if (!clientResult.TryGetValue(out client))
            {
                return FromLookupFailure(clientResult.Failure!);
            }
        }

        if (draft.EnvironmentId is not null)
        {
            var environmentResult = await requestContext.GetProductionEnvironmentAsync(
                draft.EnvironmentId,
                cancellationToken);
            if (!environmentResult.TryGetValue(out environment))
            {
                return FromLookupFailure(environmentResult.Failure!);
            }

            if (client is not null
                && !string.Equals(client.Id, environment.ClientId, StringComparison.Ordinal))
            {
                return Failure(DraftInterpretationOutcomeKind.MalformedModelOutput);
            }
        }

        if (draft.RequestedRole is not null && environment is not null)
        {
            var roleResult = await requestContext.GetEnvironmentRoleAsync(
                environment.Id,
                draft.RequestedRole,
                cancellationToken);
            if (roleResult.IsFailure)
            {
                return FromLookupFailure(roleResult.Failure!);
            }
        }

        if (draft.IncidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(
                draft.IncidentId,
                cancellationToken);
            if (!incidentResult.TryGetValue(out var incident))
            {
                return FromLookupFailure(incidentResult.Failure!);
            }

            if ((client is not null
                    && !string.Equals(client.Id, incident.ClientId, StringComparison.Ordinal))
                || (environment is not null
                    && incident.EnvironmentId is not null
                    && !string.Equals(
                        environment.Id,
                        incident.EnvironmentId,
                        StringComparison.Ordinal)))
            {
                return Failure(DraftInterpretationOutcomeKind.MalformedModelOutput);
            }
        }

        return new DraftInterpretationOutcome(draft);
    }

    private static DraftInterpretationOutcome FromLookupFailure(ApplicationFailure failure)
    {
        return failure.Kind switch
        {
            ApplicationFailureKind.Timeout => Failure(DraftInterpretationOutcomeKind.Timeout),
            ApplicationFailureKind.Cancelled => Failure(DraftInterpretationOutcomeKind.Cancelled),
            ApplicationFailureKind.InvalidInput or ApplicationFailureKind.NotFound =>
                Failure(DraftInterpretationOutcomeKind.MalformedModelOutput),
            _ => Failure(DraftInterpretationOutcomeKind.Unavailable),
        };
    }

    private Uri GetMcpEndpoint()
    {
        var configuredEndpoint = configuration[McpEndpointConfiguration];
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return DefaultMcpEndpoint;
        }

        if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Configuration '{McpEndpointConfiguration}' must be an absolute HTTP or HTTPS URI.");
        }

        return endpoint;
    }

    private TimeSpan GetPositiveTimeout(string key, TimeSpan defaultValue)
    {
        var configuredValue = configuration[key];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return defaultValue;
        }

        if (!TimeSpan.TryParse(configuredValue, out var timeout) || timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a positive time span.");
        }

        return timeout;
    }

    private static DraftInterpretationOutcome Failure(DraftInterpretationOutcomeKind kind) =>
        new(kind);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Model draft interpretation {CorrelationId} completed in {DurationMilliseconds} ms with outcome {Outcome}.")]
    private static partial void LogModelCompleted(
        ILogger logger,
        string correlationId,
        double durationMilliseconds,
        string outcome);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Model MCP tool {ToolName} completed in {DurationMilliseconds} ms with outcome {Outcome}.")]
    private static partial void LogMcpCompleted(
        ILogger logger,
        string toolName,
        double durationMilliseconds,
        string outcome);

    private sealed class DraftPayload
    {
        [JsonRequired]
        public string? ClientId { get; init; }

        [JsonRequired]
        public string? EnvironmentId { get; init; }

        [JsonRequired]
        public string? RequestedRole { get; init; }

        [JsonRequired]
        public int? DurationMinutes { get; init; }

        [JsonRequired]
        public string? Justification { get; init; }

        [JsonRequired]
        public string? IncidentId { get; init; }

        public AccessRequestDraft ToDraft() => new(
            ClientId,
            EnvironmentId,
            RequestedRole,
            DurationMinutes,
            Justification,
            IncidentId);
    }

}

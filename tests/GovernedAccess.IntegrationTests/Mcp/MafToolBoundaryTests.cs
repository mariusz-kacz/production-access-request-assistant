using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class MafToolBoundaryTests
{
    private static readonly string[] AllowedToolNames =
    [
        "get_incident",
        "get_production_environment",
    ];

    private const string ValidCandidateResponse =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    [Fact]
    public async Task ExactReadOnlyCatalogIsTheOnlyModelVisibleToolSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            TestCatalog.Exact,
            ToolBehavior.Success,
            cancellationToken);
        var chatClient = new ToolBoundaryChatClient(invokeEnvironmentTool: false);
        var interpreter = CreateInterpreter(chatClient, host.HttpClientFactory);

        var outcome = await interpreter.InterpretAsync(
            CreateTurn("Discover only approved read-only context tools."),
            cancellationToken);

        Assert.IsType<RequestPreparationInterpretationSucceeded>(outcome);
        Assert.Equal(AllowedToolNames, chatClient.ObservedToolNames);
        Assert.DoesNotContain(
            chatClient.ObservedToolNames,
            name => name.Contains("approve", StringComparison.OrdinalIgnoreCase)
                || name.Contains("provision", StringComparison.OrdinalIgnoreCase)
                || name.Contains("revoke", StringComparison.OrdinalIgnoreCase)
                || name.Contains("submit", StringComparison.OrdinalIgnoreCase)
                || name.Contains("retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RealProfileBoundaryReusesToolsSchemaAndCancellation()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            TestCatalog.Exact,
            ToolBehavior.Success,
            testCancellationToken);
        var providerClient = new ToolBoundaryChatClient(
            invokeEnvironmentTool: false);
        using var realProfileClient = CreateRealProfileClient(providerClient);
        var interpreter = CreateInterpreter(realProfileClient, host.HttpClientFactory);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            testCancellationToken);

        var outcome = await interpreter.InterpretAsync(
            CreateTurn("Use the approved real-profile boundary."),
            callerCancellation.Token);

        Assert.IsType<RequestPreparationInterpretationSucceeded>(outcome);
        Assert.Equal(AllowedToolNames, providerClient.ObservedToolNames);
        var responseFormat = Assert.IsType<ChatResponseFormatJson>(
            providerClient.ObservedResponseFormat);
        Assert.Equal("request_intake_proposal", responseFormat.SchemaName);
        var schema = Assert.IsType<System.Text.Json.JsonElement>(
            responseFormat.Schema);
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["kind", "candidate", "clarification"],
            schema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.Equal(
            callerCancellation.Token,
            providerClient.ObservedCancellationToken);
    }

    [Theory]
    [InlineData(TestCatalog.Missing)]
    [InlineData(TestCatalog.ExtraStateChanging)]
    [InlineData(TestCatalog.NonReadOnlyAllowedTool)]
    public async Task UnexpectedCatalogFailsBeforeTheModelRuns(TestCatalog catalog)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            catalog,
            ToolBehavior.Success,
            cancellationToken);
        var chatClient = new ToolBoundaryChatClient(invokeEnvironmentTool: false);
        var interpreter = CreateInterpreter(chatClient, host.HttpClientFactory);

        var outcome = await interpreter.InterpretAsync(
            CreateTurn("Reject an unexpected MCP catalog."),
            cancellationToken);

        var failure = Assert.IsType<RequestPreparationInterpretationFailed>(outcome);
        Assert.Equal(RequestPreparationInterpretationFailure.Unavailable, failure.Failure);
        Assert.Equal(0, chatClient.RequestCount);
        Assert.Empty(chatClient.ObservedToolNames);
    }

    [Fact]
    public async Task CallerCancellationPropagatesThroughTheMcpToolCall()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            TestCatalog.Exact,
            ToolBehavior.BlockUntilCancelled,
            testCancellationToken);
        var chatClient = new ToolBoundaryChatClient(invokeEnvironmentTool: true);
        var interpreter = CreateInterpreter(chatClient, host.HttpClientFactory);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            testCancellationToken);

        var interpretation = interpreter.InterpretAsync(
            CreateTurn("Cancel the environment context lookup."),
            callerCancellation.Token);

        await host.ToolCallStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            testCancellationToken);
        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await interpretation);
        await host.ToolCancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            testCancellationToken);
    }

    [Fact]
    public async Task ToolUnavailabilityReturnsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            TestCatalog.Exact,
            ToolBehavior.Unavailable,
            cancellationToken);
        var chatClient = new ToolBoundaryChatClient(invokeEnvironmentTool: true);
        var interpreter = CreateInterpreter(chatClient, host.HttpClientFactory);

        var outcome = await interpreter.InterpretAsync(
            CreateTurn("Call the unavailable environment context tool."),
            cancellationToken);

        var failure = Assert.IsType<RequestPreparationInterpretationFailed>(outcome);
        Assert.Equal(RequestPreparationInterpretationFailure.Unavailable, failure.Failure);
        Assert.Equal(AllowedToolNames, chatClient.ObservedToolNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NotFound")]
    [InlineData("InvalidInput")]
    [InlineData("Timeout")]
    [InlineData("Cancelled")]
    [InlineData("Unavailable")]
    public async Task DiscoveryIsBlockedAfterEveryExactLookupOutcome(
        string? exactFailureOutcome)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BoundaryMcpTestHost.CreateAsync(
            TestCatalog.Exact,
            exactFailureOutcome is null
                ? ToolBehavior.Success
                : ToolBehavior.TypedExactFailure,
            cancellationToken,
            exactFailureOutcome);
        var chatClient = new ToolBoundaryChatClient(
            invokeEnvironmentTool: true,
            attemptDiscoveryAfterExactLookup: true);
        var interpreter = CreateInterpreter(chatClient, host.HttpClientFactory);

        var outcome = await interpreter.InterpretAsync(
            CreateTurn("Attempt exact environment lookup, then discovery."),
            cancellationToken);

        Assert.IsType<RequestPreparationInterpretationSucceeded>(outcome);
        Assert.Equal(1, host.ExactEnvironmentCallCount);
        Assert.Equal(0, host.DiscoveryEnvironmentCallCount);
    }

    private static MafRequestPreparationInterpreter CreateInterpreter(
        IChatClient chatClient,
        IHttpClientFactory httpClientFactory) =>
        new(
            chatClient,
            NullLoggerFactory.Instance,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator(),
            new RequestPreparationMcpEndpoint(
                () => new Uri("https://localhost/")),
            httpClientFactory);

    private static IChatClient CreateRealProfileClient(IChatClient providerClient)
    {
        var adapterType = typeof(DeterministicChatClient).Assembly.GetType(
            "GovernedAccess.Web.Ai.ProviderFailureMappingChatClient");
        Assert.NotNull(adapterType);
        var constructor = Assert.Single(
            adapterType.GetConstructors(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType == typeof(IChatClient);
            });

        return Assert.IsAssignableFrom<IChatClient>(
            constructor.Invoke([providerClient]));
    }

    private static RequestPreparationTurn CreateTurn(string message) =>
        new(
            Guid.NewGuid(),
            message,
            new RequestCandidate(null, null, null, null, null),
            Guid.NewGuid().ToString("N"));

    public enum TestCatalog
    {
        Exact,
        Missing,
        ExtraStateChanging,
        NonReadOnlyAllowedTool,
    }

    private enum ToolBehavior
    {
        Success,
        BlockUntilCancelled,
        Unavailable,
        TypedExactFailure,
    }

    private sealed class ToolBoundaryChatClient(
        bool invokeEnvironmentTool,
        bool attemptDiscoveryAfterExactLookup = false) : IChatClient
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        public string[] ObservedToolNames { get; private set; } = [];

        public ChatResponseFormat? ObservedResponseFormat { get; private set; }

        public CancellationToken ObservedCancellationToken { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            Interlocked.Increment(ref requestCount);
            ObservedResponseFormat = options?.ResponseFormat;
            ObservedCancellationToken = cancellationToken;
            ObservedToolNames = options?.Tools?
                .Select(tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? [];

            if (invokeEnvironmentTool)
            {
                var tool = Assert.Single(
                    options?.Tools?.OfType<AIFunction>() ?? [],
                    candidate => candidate.Name == "get_production_environment");
                _ = await tool.InvokeAsync(
                    new AIFunctionArguments
                    {
                        ["environmentId"] = "PROD-ALPHA-EU",
                    },
                    cancellationToken);
                if (!attemptDiscoveryAfterExactLookup)
                {
                    throw new HttpRequestException(
                        "The MCP tool reported dependency unavailability.");
                }

                if (attemptDiscoveryAfterExactLookup)
                {
                    _ = await tool.InvokeAsync(
                        new AIFunctionArguments(),
                        cancellationToken);
                }
            }

            return new ChatResponse(
                new ChatMessage(ChatRole.Assistant, ValidCandidateResponse));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate(message.Role, message.Text);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BoundaryMcpTestHost : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly BoundaryTools tools;

        private BoundaryMcpTestHost(
            WebApplication application,
            BoundaryTools tools)
        {
            this.application = application;
            this.tools = tools;
            HttpClientFactory = new TestServerHttpClientFactory(application);
            ToolCallStarted = tools.CallStarted;
            ToolCancellationObserved = tools.CancellationObserved;
        }

        public IHttpClientFactory HttpClientFactory { get; }

        public TaskCompletionSource ToolCallStarted { get; }

        public TaskCompletionSource ToolCancellationObserved { get; }

        public int ExactEnvironmentCallCount => tools.ExactEnvironmentCallCount;

        public int DiscoveryEnvironmentCallCount => tools.DiscoveryEnvironmentCallCount;

        public static async Task<BoundaryMcpTestHost> CreateAsync(
            TestCatalog catalog,
            ToolBehavior behavior,
            CancellationToken cancellationToken,
            string? exactFailureOutcome = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var boundaryTools = new BoundaryTools(behavior, exactFailureOutcome);
            builder.Services.AddSingleton(boundaryTools);
            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools(CreateCatalog(catalog));

            var application = builder.Build();
            application.MapMcp("/mcp");

            try
            {
                await application.StartAsync(cancellationToken);
                return new BoundaryMcpTestHost(application, boundaryTools);
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => application.DisposeAsync();

        private static McpServerTool[] CreateCatalog(TestCatalog catalog)
        {
            var tools = new List<McpServerTool>
            {
                CreateTool(nameof(BoundaryTools.GetProductionEnvironmentAsync)),
            };

            if (catalog != TestCatalog.Missing)
            {
                tools.Add(CreateTool(nameof(BoundaryTools.GetIncident)));
            }

            if (catalog == TestCatalog.ExtraStateChanging)
            {
                tools.Add(CreateTool(nameof(BoundaryTools.ApproveAccess)));
            }

            if (catalog == TestCatalog.NonReadOnlyAllowedTool)
            {
                tools.RemoveAt(tools.Count - 1);
                tools.Add(CreateTool(nameof(BoundaryTools.GetIncidentWithoutReadOnlyHint)));
            }

            return tools.ToArray();
        }

        private static McpServerTool CreateTool(string methodName)
        {
            var method = typeof(BoundaryTools).GetMethod(methodName)
                ?? throw new InvalidOperationException(
                    $"The test MCP method '{methodName}' does not exist.");
            return McpServerTool.Create(method, GetRequiredTool);
        }

        private static BoundaryTools GetRequiredTool(
            RequestContext<CallToolRequestParams> context)
        {
            var services = context.Server.Services
                ?? throw new InvalidOperationException(
                    "The test MCP server has no service provider.");
            return services.GetRequiredService<BoundaryTools>();
        }
    }

    private sealed class TestServerHttpClientFactory(WebApplication application)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(MafRequestPreparationInterpreter.McpHttpClientName, name);
            return application.GetTestClient();
        }
    }

    private sealed class BoundaryTools(
        ToolBehavior behavior,
        string? exactFailureOutcome)
    {
        private int exactEnvironmentCallCount;
        private int discoveryEnvironmentCallCount;

        public TaskCompletionSource CallStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExactEnvironmentCallCount => Volatile.Read(
            ref exactEnvironmentCallCount);

        public int DiscoveryEnvironmentCallCount => Volatile.Read(
            ref discoveryEnvironmentCallCount);

        [McpServerTool(
            Name = "get_production_environment",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false)]
        [Description("Discovers test production environments or gets one by stable identifier.")]
        public async Task<CallToolResult> GetProductionEnvironmentAsync(
            string environmentId = null!,
            CancellationToken cancellationToken = default)
        {
            CallStarted.TrySetResult();

            if (environmentId is null)
            {
                Interlocked.Increment(ref discoveryEnvironmentCallCount);
            }
            else
            {
                Interlocked.Increment(ref exactEnvironmentCallCount);
            }

            if (behavior == ToolBehavior.BlockUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        CancellationObserved.TrySetResult();
                    }
                }
            }

            if (behavior == ToolBehavior.Unavailable)
            {
                throw new HttpRequestException(
                    "The test MCP context dependency is unavailable.");
            }

            if (behavior == ToolBehavior.TypedExactFailure
                && environmentId is not null)
            {
                return CreateTypedFailure(exactFailureOutcome!);
            }

            return CreateToolResult(new
            {
                environments = new[]
                {
                    new
                    {
                        environmentId = environmentId ?? "PROD-ALPHA-EU",
                        clientId = "client-alpha",
                        clientDisplayName = "Client Alpha",
                        displayName = "Primary Production EU",
                        roles = new[]
                        {
                            new
                            {
                                roleId = ProductionRoleIds.ReadOnly,
                                displayName = "Production read-only",
                            },
                        },
                    },
                },
            });
        }

        private static CallToolResult CreateTypedFailure(string outcome)
        {
            var code = outcome switch
            {
                "NotFound" => "environment-not-found",
                "InvalidInput" => "request-context-identifier-required",
                "Timeout" => "request-context-timeout",
                "Cancelled" => "request-context-cancelled",
                "Unavailable" => "request-context-unavailable",
                _ => throw new InvalidOperationException(
                    $"Unsupported exact failure outcome '{outcome}'."),
            };

            return CreateToolResult(
                new
                {
                    outcome,
                    code,
                    message = "The exact environment lookup failed.",
                    correlationId = "discovery-gate-correlation",
                },
                isError: true);
        }

        private static CallToolResult CreateToolResult<T>(
            T result,
            bool isError = false)
            where T : notnull
        {
            var content = JsonSerializer.SerializeToElement(result);
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = content.GetRawText(),
                    },
                ],
                StructuredContent = content,
                IsError = isError,
            };
        }

        [McpServerTool(
            Name = "get_incident",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false)]
        public object GetIncident(string incidentId)
        {
            _ = behavior;
            return new { incidentId };
        }

        [McpServerTool(
            Name = "get_incident",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false)]
        public object GetIncidentWithoutReadOnlyHint(string incidentId)
        {
            _ = behavior;
            return new { incidentId };
        }

        [McpServerTool(
            Name = "approve_access",
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = false)]
        public object ApproveAccess(string requestId)
        {
            _ = behavior;
            return new { requestId };
        }
    }
}

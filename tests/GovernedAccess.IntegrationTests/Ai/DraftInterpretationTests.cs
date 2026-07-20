using System.Runtime.CompilerServices;
using System.Text.Json;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class DraftInterpretationTests
{
    private const string PrimaryIntent =
        "I need four hours of read-only access to Client Alpha production for "
        + "INC-1042 to investigate the incident.";
    private const string CorrelationId = "draft-interpretation-test";
    private const string ValidResponse =
        """
        {"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRole":"ProductionReadOnly","justification":"Investigate the active production incident.","incidentId":"INC-1042"}
        """;

    [Fact]
    public async Task ValidDeterministicOutputProducesTheTypedDraft()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, DeterministicChatMode.Valid);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.Prepared, outcome.Kind);
        var draft = Assert.IsType<AccessRequestDraft>(outcome.Draft);
        Assert.Equal("client-alpha", draft.ClientId);
        Assert.Equal("PROD-ALPHA-EU", draft.EnvironmentId);
        Assert.Equal("ProductionReadOnly", draft.RequestedRole);
        Assert.Equal("Investigate the active production incident.", draft.Justification);
        Assert.Equal("INC-1042", draft.IncidentId);
        Assert.True(draft.IsComplete);
    }

    [Fact]
    public async Task IncompleteDeterministicOutputPreservesKnownValuesAndRemainsIncomplete()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, DeterministicChatMode.Incomplete);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.Incomplete, outcome.Kind);
        var draft = Assert.IsType<AccessRequestDraft>(outcome.Draft);
        Assert.Equal("client-alpha", draft.ClientId);
        Assert.Equal("PROD-ALPHA-EU", draft.EnvironmentId);
        Assert.Equal("ProductionReadOnly", draft.RequestedRole);
        Assert.Null(draft.Justification);
        Assert.False(draft.IsComplete);
    }

    [Fact]
    public async Task MalformedJsonReturnsATypedCorrectionOutcomeWithoutADraft()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, DeterministicChatMode.Malformed);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.MalformedModelOutput, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    [Fact]
    public async Task SchemaUnsupportedValueReturnsATypedCorrectionOutcomeWithoutADraft()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, DeterministicChatMode.Unsupported);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.MalformedModelOutput, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    [Fact]
    public async Task ModelDeadlineReturnsTimeoutRatherThanCancellationOrSuccess()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(
            rootFactory,
            DeterministicChatMode.Timeout,
            TimeSpan.FromMilliseconds(50));

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.Timeout, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndReturnsCancelledRatherThanTimeout()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(
            rootFactory,
            DeterministicChatMode.Cancellation);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var interpretation = InterpretAsync(factory, callerCancellation.Token);
        await callerCancellation.CancelAsync();
        var outcome = await interpretation;

        Assert.Equal(DraftInterpretationOutcomeKind.Cancelled, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    [Fact]
    public async Task UnavailableModeReturnsTypedOutcomeWithoutAnyLiveModelRegistration()
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(
            rootFactory,
            DeterministicChatMode.Unavailable);
        await using var scope = factory.Services.CreateAsyncScope();

        var configuredChatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();
        var interpreter = scope.ServiceProvider.GetRequiredService<IRequestDraftInterpreter>();
        var outcome = await interpreter.InterpretAsync(
            new DraftInterpretationRequest(PrimaryIntent, CorrelationId),
            TestContext.Current.CancellationToken);

        Assert.IsType<FunctionInvokingChatClient>(configuredChatClient);
        Assert.IsType<DeterministicChatClient>(
            configuredChatClient.GetService(typeof(DeterministicChatClient)));
        Assert.Equal(DraftInterpretationOutcomeKind.Unavailable, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    [Fact]
    public async Task AdapterRequestsTheStrictSchemaAndOnlyTheThreeAllowedReadOnlyTools()
    {
        var chatClient = new CapturingChatClient(ValidResponse);
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, chatClient);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.Prepared, outcome.Kind);
        var options = Assert.IsType<ChatOptions>(chatClient.LastOptions);
        Assert.False(options.AllowMultipleToolCalls);

        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        var schema = Assert.IsType<JsonElement>(format.Schema);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            [
                "clientId",
                "environmentId",
                "requestedRole",
                "justification",
                "incidentId",
            ],
            schema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));

        var tools = Assert.IsAssignableFrom<IList<AITool>>(options.Tools);
        Assert.Equal(
            ["get_available_roles", "get_incident", "get_production_environment"],
            tools.Select(tool => tool.Name).Order());
        foreach (var tool in tools)
        {
            var function = Assert.IsType<McpClientTool>(tool);
            Assert.False(function.JsonSchema.GetProperty("additionalProperties").GetBoolean());
            var parameterName = tool.Name == "get_incident"
                ? "incidentId"
                : "environmentId";
            Assert.Equal(
                [parameterName],
                function.JsonSchema.GetProperty("required")
                    .EnumerateArray()
                    .Select(item => item.GetString()));
            var properties = function.JsonSchema.GetProperty("properties");
            Assert.Equal([parameterName], properties.EnumerateObject().Select(item => item.Name));
            var parameter = properties.GetProperty(parameterName);
            Assert.Equal("string", parameter.GetProperty("type").GetString());
            Assert.Equal(1, parameter.GetProperty("minLength").GetInt32());
        }
    }

    [Fact]
    public async Task DiscoveredMcpToolIsInvokedThroughTheSharedChatPipeline()
    {
        var chatClient = new ToolCallingChatClient();
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, chatClient);

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.Prepared, outcome.Kind);
        Assert.Equal(2, chatClient.CallCount);
        var structuredContent = Assert.IsType<JsonElement>(chatClient.ToolResult);
        Assert.True(
            structuredContent.TryGetProperty("structuredContent", out var toolPayload),
            structuredContent.GetRawText());
        Assert.Equal("INC-1042", toolPayload.GetProperty("incidentId").GetString());
    }

    [Theory]
    [InlineData(
        "{\"clientId\":\"client-alpha\",\"environmentId\":\"PROD-UNKNOWN\",\"requestedRole\":\"ProductionReadOnly\",\"justification\":\"Investigate the active production incident.\",\"incidentId\":\"INC-1042\"}")]
    [InlineData(
        "{\"clientId\":\"client-alpha\",\"environmentId\":\"PROD-ALPHA-EU\",\"requestedRole\":\"ProductionReadOnly\",\"justification\":\"Investigate the active production incident.\",\"incidentId\":\"INC-1042\",\"actorId\":\"browser-controlled\"}")]
    public async Task UnknownIdentifiersAndAdditionalPropertiesAreRejected(string response)
    {
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(
            rootFactory,
            new CapturingChatClient(response));

        var outcome = await InterpretAsync(
            factory,
            TestContext.Current.CancellationToken);

        Assert.Equal(DraftInterpretationOutcomeKind.MalformedModelOutput, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GovernedAccessWebFactory rootFactory,
        DeterministicChatMode mode,
        TimeSpan? modelTimeout = null)
    {
        return CreateFactory(
            rootFactory,
            new DeterministicChatClient(mode),
            modelTimeout);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GovernedAccessWebFactory rootFactory,
        IChatClient chatClient,
        TimeSpan? modelTimeout = null)
    {
        return rootFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DraftInterpretation:McpEndpoint"] = "https://localhost/mcp",
                        ["DraftInterpretation:ModelTimeout"] =
                            modelTimeout?.ToString("c"),
                    }));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(
                    new TestServerHttpClientFactory(rootFactory));
                services
                    .AddChatClient(chatClient)
                    .UseFunctionInvocation(configure: static client =>
                    {
                        client.AllowConcurrentInvocation = false;
                        client.IncludeDetailedErrors = false;
                        client.MaximumIterationsPerRequest = 6;
                        client.TerminateOnUnknownCalls = true;
                    });
            });
        });
    }

    private static async Task<DraftInterpretationOutcome> InterpretAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var interpreter = scope.ServiceProvider.GetRequiredService<IRequestDraftInterpreter>();
        return await interpreter.InterpretAsync(
            new DraftInterpretationRequest(PrimaryIntent, CorrelationId),
            cancellationToken);
    }

    private sealed class CapturingChatClient(string responseText) : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();
            LastOptions = options;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ToolCallingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public object? ToolResult { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            if (CallCount == 1)
            {
                return Task.FromResult(
                    new ChatResponse(
                        new ChatMessage(
                            ChatRole.Assistant,
                            [
                                new FunctionCallContent(
                                    "incident-call",
                                    "get_incident",
                                    new Dictionary<string, object?>
                                    {
                                        ["incidentId"] = "INC-1042",
                                    }),
                            ])));
            }

            ToolResult = Assert.Single(
                messages
                    .SelectMany(message => message.Contents)
                    .OfType<FunctionResultContent>()).Result;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, ValidResponse)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestServerHttpClientFactory(
        WebApplicationFactory<Program> factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
        }
    }

}

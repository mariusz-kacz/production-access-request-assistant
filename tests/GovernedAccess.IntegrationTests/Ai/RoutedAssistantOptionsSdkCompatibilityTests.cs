using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using GovernedAccess.IntegrationTests.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Json;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class RoutedAssistantOptionsSdkCompatibilityTests
{
    [Fact]
    public async Task EmbeddingAdapterFeedsAnAzureHybridVectorQueryWithoutNetwork()
    {
        var token = TestContext.Current.CancellationToken;
        using var handler = new OfflineSearchHandler();
        using var http = new HttpClient(handler);
        using var embeddings = new OpenAI.Embeddings.EmbeddingClient(
            "synthetic-embedding",
            new System.ClientModel.ApiKeyCredential("synthetic-test-key"),
            new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri("https://synthetic.services.ai.azure.com/openai/v1"),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http),
            }).AsIEmbeddingGenerator(3);
        var generated = await embeddings.GenerateAsync(
            ["eight hours"], cancellationToken: token);
        var vector = Assert.Single(generated).Vector;
        var client = new SearchClient(
            new Uri("https://synthetic.search.windows.net"),
            "synthetic-policy",
            new AzureKeyCredential("synthetic-test-key"),
            new SearchClientOptions
            {
                Transport = new Azure.Core.Pipeline.HttpClientTransport(http),
            });
        var options = new SearchOptions
        {
            Size = 3,
            Filter = "status eq 'active'",
            VectorSearch = new VectorSearchOptions
            {
                FilterMode = VectorFilterMode.PreFilter,
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        KNearestNeighborsCount = 3,
                        Fields = { "contentVector" },
                    },
                },
            },
        };

        var response = await client.SearchAsync<SearchDocument>("eight hours", options, token);

        Assert.Empty(response.Value.GetResults());
        Assert.Equal(2, handler.Requests.Count);
        using var embeddingRequest = JsonDocument.Parse(handler.Requests[0]);
        Assert.Equal(3, embeddingRequest.RootElement.GetProperty("dimensions").GetInt32());
        using var searchRequest = JsonDocument.Parse(handler.Requests[1]);
        Assert.Equal("eight hours", searchRequest.RootElement.GetProperty("search").GetString());
        Assert.Equal("status eq 'active'", searchRequest.RootElement.GetProperty("filter").GetString());
        var sentVector = searchRequest.RootElement.GetProperty("vectorQueries")[0];
        Assert.Equal("contentVector", sentVector.GetProperty("fields").GetString());
        Assert.Equal(vector.ToArray(), sentVector.GetProperty("vector")
            .EnumerateArray().Select(value => value.GetSingle()));
    }

    // The approved evaluation contract requires this experimental Microsoft evaluator.
#pragma warning disable AIEVAL001
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task RequiredEvaluatorsAcceptDeclaredRoutesAndReportOneToFiveScores(int score)
    {
        var token = TestContext.Current.CancellationToken;
        var scoreText = score.ToString(CultureInfo.InvariantCulture);
        var taggedScore = $"<S0>Synthetic fixture.</S0><S1>Compatibility score.</S1><S2>{scoreText}</S2>";
        using var judge = new ScriptedChatClient(
            $$"""{"resolution_score":{{scoreText}},"explanation":"Synthetic compatibility score.","agent_perceived_intent":"policy guidance","actual_user_intent":"policy guidance","conversation_has_intent":true,"correct_intent_detected":true,"intent_resolved":true}""",
            taggedScore, taggedScore, taggedScore);
        var declaration = AIFunctionFactory.CreateDeclaration(
            "route_policy_guidance",
            "Read-only production-access policy guidance.",
            JsonSerializer.Deserialize<JsonElement>(
                """{"type":"object","properties":{"schemaVersion":{"type":"string"},"contextReference":{"type":"string"}}}"""));
        var routeResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("route-1", declaration.Name, new Dictionary<string, object?>
            {
                ["schemaVersion"] = "1.0.0",
                ["contextReference"] = "None",
            })]));
        ChatMessage[] messages = [new(ChatRole.User, "How long does synthetic access last?")];
        var answer = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Eight hours."));
        (IEvaluator Evaluator, ChatResponse Response, EvaluationContext[] Context)[] cases =
        [
            (new IntentResolutionEvaluator(), routeResponse,
                [new IntentResolutionEvaluatorContext(declaration)]),
            (new RetrievalEvaluator(), answer,
                [new RetrievalEvaluatorContext("Synthetic access lasts eight hours.")]),
            (new GroundednessEvaluator(), answer,
                [new GroundednessEvaluatorContext("Synthetic access lasts eight hours.")]),
            (new RelevanceEvaluator(), answer, []),
        ];
        List<ScenarioRunResult> reports = [];
        foreach (var (evaluator, response, context) in cases)
        {
            var result = await evaluator.EvaluateAsync(
                messages, response, new ChatConfiguration(judge), context, token);
            var metric = Assert.IsType<NumericMetric>(Assert.Single(result.Metrics).Value);
            Assert.Equal(score, metric.Value);
            Assert.Equal(score == 1, metric.Interpretation!.Failed);
            Assert.DoesNotContain(metric.Diagnostics ?? [],
                diagnostic => diagnostic.Severity == EvaluationDiagnosticSeverity.Error);
            reports.Add(new ScenarioRunResult(
                metric.Name, "1", "sdk-compatibility", DateTime.UtcNow,
                messages, response, result));
        }

        Assert.Equal(4, judge.InvocationCount);
        var intentInput = string.Join('\n', judge.Invocations[0].Messages.Select(message => message.Text));
        Assert.Contains(declaration.Name, intentInput);
        Assert.Contains("contextReference", intentInput);
        Assert.Contains("None", intentInput);
        var reportPath = Path.Combine(Path.GetTempPath(), $"governed-sdk-{Guid.NewGuid():N}.json");
        try
        {
            await new JsonReportWriter(reportPath).WriteReportAsync(reports, token);
            var report = await File.ReadAllTextAsync(reportPath, token);
            Assert.Contains(IntentResolutionEvaluator.IntentResolutionMetricName, report);
            Assert.Contains(RetrievalEvaluator.RetrievalMetricName, report);
        }
        finally
        {
            File.Delete(reportPath);
        }
    }
#pragma warning restore AIEVAL001

    [Fact]
    public async Task BeforeInvokeSearchHasNoInternalMemoryOrToolsAndEmitsSafeTelemetry()
    {
        const string sourceName = "GovernedAccess.Tests.PolicySdkCompatibility";
        const string evidence = "Synthetic grants last eight hours.";
        List<string> queries = [];
        List<Activity> activities = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var client = new RecordingChatClient("Synthetic policy answer.");
        var searchProvider = new TextSearchProvider(
            (query, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                queries.Add(query);
                return Task.FromResult<IEnumerable<TextSearchProvider.TextSearchResult>>(
                    [new() { SourceName = "policy-v1", Text = evidence }]);
            },
            new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
                RecentMessageMemoryLimit = 0,
            });
        var agent = new ChatClientAgent(
                client,
                new ChatClientAgentOptions
                {
                    Name = "policy-sdk-probe",
                    AIContextProviders = [searchProvider],
                })
            .AsBuilder()
            .UseOpenTelemetry(sourceName, configure: telemetry =>
                telemetry.EnableSensitiveData = false)
            .Build();
        var token = TestContext.Current.CancellationToken;
        var session = await agent.CreateSessionAsync(token);

        await agent.RunAsync("First synthetic question", session, cancellationToken: token);
        await agent.RunAsync("Second synthetic question", session, cancellationToken: token);

        Assert.Equal(2, queries.Count);
        Assert.Contains("Second synthetic question", queries[1]);
        Assert.DoesNotContain("First synthetic question", queries[1]);
        Assert.DoesNotContain("Synthetic policy answer.", queries[1]);
        Assert.Equal(2, client.InvocationCount);
        Assert.All(client.Invocations, invocation =>
        {
            Assert.Empty(invocation.Options?.Tools ?? []);
            Assert.Contains(invocation.Messages, message => message.Text.Contains(
                evidence, StringComparison.Ordinal));
        });
        Assert.NotEmpty(activities);
        Assert.All(activities.SelectMany(activity => activity.TagObjects), tag =>
        {
            Assert.DoesNotContain(evidence, tag.Value?.ToString() ?? string.Empty);
            Assert.DoesNotContain("synthetic question", tag.Value?.ToString() ?? string.Empty);
        });
    }

    private sealed class OfflineSearchHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var response = request.RequestUri!.AbsolutePath.EndsWith(
                "/embeddings", StringComparison.Ordinal)
                ? """{"object":"list","model":"synthetic-embedding","data":[{"object":"embedding","index":0,"embedding":"AACAPwAAAAAAAAAA"}],"usage":{"prompt_tokens":2,"total_tokens":2}}"""
                : """{"value":[]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}

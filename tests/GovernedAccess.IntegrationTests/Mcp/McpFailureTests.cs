using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class McpFailureTests
{
    [Fact]
    public async Task ExpectedReaderFailuresReturnTypedMcpFailureEnvelopes()
    {
        (
            string EnvironmentId,
            ApplicationFailureKind Kind,
            string Outcome,
            string Code)[] failures =
        [
            ("PROD-NOT-FOUND", ApplicationFailureKind.NotFound, "NotFound", "environment-not-found"),
            ("PROD-UNAVAILABLE", ApplicationFailureKind.DependencyUnavailable, "Unavailable", "request-context-unavailable"),
            ("PROD-TIMEOUT", ApplicationFailureKind.Timeout, "Timeout", "request-context-timeout"),
            ("PROD-CANCELLED", ApplicationFailureKind.Cancelled, "Cancelled", "request-context-cancelled"),
        ];
        var failuresByEnvironmentId = failures.ToDictionary(
            failure => failure.EnvironmentId,
            StringComparer.Ordinal);
        var reader = new StubRequestContextReader
        {
            GetProductionEnvironmentContext = (environmentId, _) =>
            {
                var failure = failuresByEnvironmentId[environmentId];
                return Task.FromResult(
                    ApplicationResult.Failed<ProductionEnvironmentContext>(
                        new ApplicationFailure(
                            failure.Kind,
                            failure.Code,
                            "The environment lookup did not complete successfully.")));
            },
        };
        await using var host = await McpTestHost.CreateAsync(
            reader,
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-failure-tests",
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        foreach (var (
                     environmentId,
                     _,
                     expectedOutcome,
                     expectedCode) in failures)
        {
            var result = await tool.CallAsync(
                new Dictionary<string, object?>
                {
                    ["environmentId"] = environmentId,
                },
                cancellationToken: TestContext.Current.CancellationToken);

            AssertTypedFailure(result, expectedOutcome, expectedCode);
        }
    }

    [Fact]
    public async Task CatalogOverflowReturnsTypedUnavailableWithoutPartialResult()
    {
        var reader = new StubRequestContextReader
        {
            ListProductionEnvironmentContexts = _ => Task.FromResult(
                ApplicationResult.Failed<
                    IReadOnlyList<ProductionEnvironmentContext>>(
                    new ApplicationFailure(
                        ApplicationFailureKind.DependencyUnavailable,
                        "environment-candidate-limit-exceeded",
                        "The production-environment catalog exceeds the supported limit."))),
        };
        await using var host = await McpTestHost.CreateAsync(
            reader,
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-overflow-tests",
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        AssertTypedFailure(
            result,
            "Unavailable",
            "environment-candidate-limit-exceeded");
    }

    [Fact]
    public async Task CallerCancellationPropagatesToTheRequestContextReader()
    {
        var callStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new StubRequestContextReader
        {
            GetProductionEnvironmentContext = async (_, cancellationToken) =>
            {
                callStarted.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("The cancelled lookup unexpectedly completed.");
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            },
        };

        await using var host = await McpTestHost.CreateAsync(
            reader,
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-cancellation-tests",
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var call = tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-ALPHA-EU",
            },
            cancellationToken: callerCancellation.Token);

        await callStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await call);
        await cancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    private static async Task<McpClientTool> GetToolAsync(McpClient client, string name)
    {
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tools, tool => tool.Name == name);
    }

    private static void AssertTypedFailure(
        CallToolResult result,
        string expectedOutcome,
        string expectedCode)
    {
        Assert.True(result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);

        Assert.Equal(JsonValueKind.Object, content.ValueKind);
        Assert.Equal(
            ["code", "correlationId", "message", "outcome"],
            content.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(expectedOutcome, content.GetProperty("outcome").GetString());
        Assert.Equal(expectedCode, content.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("correlationId").GetString()));
    }

    private sealed class StubRequestContextReader : IRequestContextReader
    {
        public Func<string, CancellationToken, Task<
            ApplicationResult<ProductionEnvironmentContext>>>
            GetProductionEnvironmentContext { get; init; } = (_, _) =>
                Task.FromResult(NotFound<ProductionEnvironmentContext>());

        public Func<CancellationToken, Task<ApplicationResult<
            IReadOnlyList<ProductionEnvironmentContext>>>>
            ListProductionEnvironmentContexts { get; init; } = _ =>
                Task.FromResult(
                    NotFound<IReadOnlyList<ProductionEnvironmentContext>>());

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<Client>());
        }

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<ProductionEnvironment>());
        }

        public Task<ApplicationResult<ProductionEnvironmentContext>>
            GetProductionEnvironmentContextAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            return GetProductionEnvironmentContext(
                environmentId,
                cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
            ListProductionEnvironmentContextsAsync(
                CancellationToken cancellationToken)
        {
            return ListProductionEnvironmentContexts(cancellationToken);
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<EnvironmentRole>());
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<Incident>());
        }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<AuthenticatedPrincipal>());
        }

        private static ApplicationResult<T> NotFound<T>()
            where T : notnull
        {
            return ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    "request-context-record-not-found",
                    "The stored record was not found."));
        }
    }
}

using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class McpFailureTests
{
    [Theory]
    [InlineData(ApplicationFailureKind.NotFound, "NotFound", "environment-not-found")]
    [InlineData(
        ApplicationFailureKind.DependencyUnavailable,
        "Unavailable",
        "request-context-unavailable")]
    [InlineData(ApplicationFailureKind.Timeout, "Timeout", "request-context-timeout")]
    [InlineData(ApplicationFailureKind.Cancelled, "Cancelled", "request-context-cancelled")]
    public async Task ExpectedReaderFailuresReturnTypedMcpFailureEnvelopes(
        ApplicationFailureKind failureKind,
        string expectedOutcome,
        string expectedCode)
    {
        var reader = new StubRequestContextReader
        {
            GetProductionEnvironment = (_, _) => Task.FromResult(
                ApplicationResult.Failed<ProductionEnvironment>(
                    new ApplicationFailure(
                        failureKind,
                        expectedCode,
                        "The environment lookup did not complete successfully."))),
        };

        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, reader);
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-UNKNOWN",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        AssertTypedFailure(result, expectedOutcome, expectedCode);
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
            GetProductionEnvironment = async (_, cancellationToken) =>
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

        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, reader);
        await using var client = await CreateMcpClientAsync(
            factory,
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

    private static WebApplicationFactory<Program> CreateFactory(
        GovernedAccessWebFactory rootFactory,
        IRequestContextReader reader)
    {
        return rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestContextReader>();
                services.AddSingleton(reader);
            }));
    }

    private static async Task<McpClient> CreateMcpClientAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var httpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://localhost/mcp"),
                Name = "governed-access-failure-tests",
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            ownsHttpClient: true);

        try
        {
            return await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
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
        public Func<string, CancellationToken, Task<ApplicationResult<ProductionEnvironment>>>
            GetProductionEnvironment { get; init; } = (_, _) =>
                Task.FromResult(NotFound<ProductionEnvironment>());

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
            return GetProductionEnvironment(environmentId, cancellationToken);
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<EnvironmentRole>());
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NotFound<IReadOnlyList<EnvironmentRole>>());
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

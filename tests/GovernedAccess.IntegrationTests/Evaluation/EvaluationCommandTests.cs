using GovernedAccess.Core.Application;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Evaluation;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GovernedAccess.IntegrationTests.Evaluation;

public sealed class EvaluationCommandTests
{
    private static readonly string[] RelativeOutputArguments =
        ["--output", "artifacts/live-evaluation"];

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--output")]
    [InlineData("--output", "first", "--output", "second")]
    public void CommandParserRejectsUnknownIncompleteAndDuplicateOptions(
        params string[] arguments)
    {
        var workingDirectory = Path.GetFullPath("evaluation-command-tests");

        var result = LiveModelEvaluationCommand.ParseArguments(
            arguments,
            workingDirectory);

        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationFailureKind.InvalidInput, result.Failure!.Kind);
    }

    [Fact]
    public void CommandParserResolvesRelativeOutputAgainstTheTrustedWorkingDirectory()
    {
        var workingDirectory = Path.GetFullPath("evaluation-command-tests");

        var result = LiveModelEvaluationCommand.ParseArguments(
            RelativeOutputArguments,
            workingDirectory);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Path.Combine(workingDirectory, "artifacts", "live-evaluation"),
            result.Value.OutputParentPath);
    }

    [Fact]
    public void LivePrerequisitesRejectInvalidAndDeterministicProfiles()
    {
        var invalid = LiveModelEvaluationCommand.ValidateLiveProfile(
            RequestPreparationModelResolution.Invalid("ExecutionProfile"));
        var deterministic = LiveModelEvaluationCommand.ValidateLiveProfile(
            RequestPreparationModelResolution.ValidDeterministic());

        Assert.True(invalid.IsFailure);
        Assert.True(deterministic.IsFailure);
        Assert.Equal(ApplicationFailureKind.InvalidInput, invalid.Failure!.Kind);
        Assert.Equal(ApplicationFailureKind.InvalidInput, deterministic.Failure!.Kind);
    }

    [Theory]
    [InlineData((int)EvaluationRunStatus.Passed, 0)]
    [InlineData((int)EvaluationRunStatus.Failed, 1)]
    [InlineData((int)EvaluationRunStatus.PrerequisiteFailed, 2)]
    [InlineData((int)EvaluationRunStatus.Cancelled, 130)]
    public void RunStatusMapsToTheDocumentedExitCode(
        int statusValue,
        int expectedExitCode)
    {
        Assert.Equal(
            expectedExitCode,
            LiveModelEvaluationCommand.GetExitCode(
                (EvaluationRunStatus)statusValue));
    }

    [Fact]
    public async Task EvaluationHostExposesOnlyTheReadOnlyMcpRouteAndCleansItsDatabase()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        EvaluationHosting? hosting = null;

        try
        {
            hosting = await StartHostingAsync(
                temporaryRoot,
                DeterministicChatMode.Candidate,
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
            var databasePath = hosting.DatabasePath;
            var routePatterns = hosting.Services
                .GetServices<EndpointDataSource>()
                .SelectMany(static source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(static endpoint => endpoint.RoutePattern.RawText)
                .Where(static pattern => pattern is not null)
                .ToArray();

            Assert.NotEmpty(routePatterns);
            Assert.All(
                routePatterns,
                static pattern => Assert.StartsWith(
                    "/mcp",
                    pattern,
                    StringComparison.Ordinal));
            Assert.True(File.Exists(databasePath));

            await hosting.DisposeAsync();
            hosting = null;

            AssertDatabaseFilesDoNotExist(databasePath);
        }
        finally
        {
            if (hosting is not null)
            {
                await hosting.DisposeAsync();
            }

            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task DeterministicSmallDatasetRunsWithoutWorkflowSideEffects()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var hosting = await StartHostingAsync(
            temporaryRoot,
            DeterministicChatMode.Candidate,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        try
        {
            var result = await RunAsync(
                hosting,
                CreateDataset(CreateReadyScenario("RES-01")),
                TestContext.Current.CancellationToken);

            var scenario = Assert.Single(result.Scenarios);
            Assert.Equal(NormalizedIntakeOutcome.Ready, scenario.FinalOutcome!.Kind);
            Assert.NotNull(scenario.ElapsedMilliseconds);
            Assert.True(scenario.ElapsedMilliseconds >= 0);
            Assert.Equal(WorkflowSideEffectCounts.None, scenario.SideEffects);
            Assert.Equal(WorkflowSideEffectCounts.None, result.SideEffects);

            await AssertWorkflowTablesAreEmptyAsync(
                hosting.Services,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            await hosting.DisposeAsync();
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task RootCancellationCancelsTheActiveScenarioAndLeavesLaterScenariosNotRun()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var hosting = await StartHostingAsync(
            temporaryRoot,
            DeterministicChatMode.Cancellation,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

            var result = await RunAsync(
                hosting,
                CreateDataset(
                    CreateReadyScenario("RES-01"),
                    CreateReadyScenario("RES-02")),
                cancellation.Token);

            Assert.Equal(EvaluationRunStatus.Cancelled, result.Status);
            Assert.Equal(EvaluationScenarioStatus.Cancelled, result.Scenarios[0].Status);
            Assert.Equal(EvaluationScenarioStatus.NotRun, result.Scenarios[1].Status);
            Assert.Equal(WorkflowSideEffectCounts.None, result.SideEffects);
        }
        finally
        {
            await hosting.DisposeAsync();
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    [Fact]
    public async Task TurnDeadlineProducesTypedTimeoutWithoutCancellingTheRun()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        var hosting = await StartHostingAsync(
            temporaryRoot,
            DeterministicChatMode.Timeout,
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        try
        {
            var result = await RunAsync(
                hosting,
                CreateDataset(CreateReadyScenario("RES-01")),
                TestContext.Current.CancellationToken);

            var scenario = Assert.Single(result.Scenarios);
            Assert.Equal(EvaluationRunStatus.Failed, result.Status);
            Assert.Equal(EvaluationScenarioStatus.Failed, scenario.Status);
            Assert.Equal(
                NormalizedIntakeOutcome.ProviderFailure,
                scenario.FinalOutcome!.Kind);
            Assert.Contains(
                RequestIntakeService.ModelTimeoutCode,
                scenario.FinalOutcome.ValidationCodes);
            Assert.Equal(WorkflowSideEffectCounts.None, result.SideEffects);
        }
        finally
        {
            await hosting.DisposeAsync();
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private static Task<EvaluationHosting> StartHostingAsync(
        string temporaryRoot,
        DeterministicChatMode chatMode,
        TimeSpan turnTimeout,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RequestPreparationModel:ExecutionProfile"] = "FoundryResponses",
                ["RequestPreparationModel:FoundryResponses:Endpoint"] =
                    "https://evaluation.services.ai.azure.com/openai/v1",
                ["RequestPreparationModel:FoundryResponses:DeploymentName"] =
                    "evaluation-deployment",
                ["LiveModelEvaluation:TurnTimeout"] = turnTimeout.ToString("c"),
            })
            .Build();

        return EvaluationHosting.StartAsync(
            configuration,
            temporaryRoot,
            services => ReplaceChatClient(
                    services,
                    new DeterministicChatClient(chatMode)),
            cancellationToken);
    }

    private static async Task<EvaluationRunResult> RunAsync(
        EvaluationHosting hosting,
        EvaluationDataset dataset,
        CancellationToken cancellationToken)
    {
        var runner = hosting.Services
            .GetRequiredService<LiveModelEvaluationRunner>();
        return await runner.RunAsync(dataset, cancellationToken);
    }

    private static void ReplaceChatClient(
        IServiceCollection services,
        IChatClient chatClient)
    {
        services.RemoveAll<IChatClient>();
        services
            .AddChatClient(chatClient)
            .UseFunctionInvocation(configure: static client =>
            {
                client.AllowConcurrentInvocation = false;
                client.IncludeDetailedErrors = false;
                client.MaximumIterationsPerRequest = 6;
                client.TerminateOnUnknownCalls = true;
            });
    }

    private static EvaluationDataset CreateDataset(params EvaluationScenario[] scenarios) =>
        new(1, "test-1.0.0", Array.AsReadOnly(scenarios));

    private static EvaluationScenario CreateReadyScenario(string id) =>
        new(
            id,
            EvaluationCategory.SuccessfulResolution,
            null,
            [new EvaluationTurn("turn-1", "Prepare the synthetic request.")],
            new FinalExpectation(
                NormalizedIntakeOutcome.Ready,
                new EvaluationCandidateExpectation(
                    EvaluationExpectedValue<string?>.Declared("client-alpha"),
                    EvaluationExpectedValue<string?>.Declared("PROD-ALPHA-EU"),
                    EvaluationExpectedValue<string?>.Declared("ProductionReadOnly"),
                    EvaluationExpectedValue<bool>.Declared(true),
                    EvaluationExpectedValue<string?>.Declared("INC-1042")),
                default,
                default,
                default,
                [],
                []));

    private static async Task AssertWorkflowTablesAreEmptyAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();

        Assert.Equal(0, await dbContext.AccessRequests.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.ApprovalDecisions.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.ProvisioningOperations.CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.AccessGrants.CountAsync(cancellationToken));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"governed-access-evaluation-tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertDatabaseFilesDoNotExist(string databasePath)
    {
        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists($"{databasePath}-shm"));
        Assert.False(File.Exists($"{databasePath}-wal"));
    }

    private static void DeleteTemporaryDirectory(string temporaryRoot)
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}

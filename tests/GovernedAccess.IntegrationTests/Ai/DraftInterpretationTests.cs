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

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class DraftInterpretationTests
{
    private const string PrimaryIntent =
        "I need four hours of read-only access to Client Alpha production for "
        + "INC-1042 to investigate the incident.";
    private const string CorrelationId = "draft-interpretation-test";

    [Fact]
    public async Task ValidDeterministicOutputProducesTheTypedAuthoritativeDraft()
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
        Assert.Equal(240, draft.DurationMinutes);
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
        Assert.Null(draft.DurationMinutes);
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

        Assert.IsType<DeterministicChatClient>(configuredChatClient);
        Assert.Equal(DraftInterpretationOutcomeKind.Unavailable, outcome.Kind);
        Assert.Null(outcome.Draft);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GovernedAccessWebFactory rootFactory,
        DeterministicChatMode mode,
        TimeSpan? modelTimeout = null)
    {
        return rootFactory.WithWebHostBuilder(builder =>
        {
            if (modelTimeout is not null)
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["DraftInterpretation:ModelTimeout"] = modelTimeout.Value.ToString("c"),
                        }));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new DeterministicChatClient(mode));
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

}

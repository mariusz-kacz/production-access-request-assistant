using System.Net;
using System.Security.Claims;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ProgramCompositionTests
{
    private const string BotConnectionName = "BotServiceConnection";

    [Fact]
    public async Task StartupCreatesAndSeedsTheConfiguredDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();

        var clientCount = await dbContext.Clients.CountAsync(cancellationToken);
        var principalCount = await dbContext.AuthenticatedPrincipals.CountAsync(cancellationToken);

        Assert.Equal(2, clientCount);
        Assert.Equal(4, principalCount);
        Assert.Same(factory.Clock, factory.Services.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task IntakeComponentsUseOneScopedServiceStoreAndDbContext()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<GovernedAccessDbContext>();
        var intakeStore = services.GetRequiredService<IRequestIntakeStore>();
        var workflowStore = services.GetRequiredService<IWorkflowStore>();
        var intakeService = services.GetRequiredService<RequestIntakeService>();

        Assert.Same(
            intakeStore,
            services.GetRequiredService<IRequestIntakeStore>());
        Assert.Contains(
            GetPrivateDependencies(intakeService),
            dependency => ReferenceEquals(dependency, intakeStore));
        Assert.Contains(
            GetPrivateDependencies(intakeStore),
            dependency => ReferenceEquals(dependency, dbContext));
        Assert.Contains(
            GetPrivateDependencies(workflowStore),
            dependency => ReferenceEquals(dependency, dbContext));
    }

    [Fact]
    public async Task TeamsBackgroundDispatcherIsRootSafeAndAgentsAreScopedPerTurn()
    {
        await using var factory = new GovernedAccessWebFactory();
        var rootAgent = factory.Services.GetRequiredService<IAgent>();

        Assert.IsType<ScopedTeamsAccessRequestAgentDispatcher>(rootAgent);

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstAgent = firstScope.ServiceProvider
            .GetRequiredService<TeamsAccessRequestAgent>();

        Assert.Same(
            firstAgent,
            firstScope.ServiceProvider
                .GetRequiredService<TeamsAccessRequestAgent>());
        Assert.NotSame(
            firstAgent,
            secondScope.ServiceProvider
                .GetRequiredService<TeamsAccessRequestAgent>());
    }

    [Fact]
    public async Task TeamsActorResolverAcceptsOnlyTheValidatedActivityScheme()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider
            .GetRequiredService<TeamsActorResolver>();
        var jwtOptions = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(TeamsAgentRegistration.ActivityAuthenticationScheme);
        var activity = new FakeTeamsActivityBuilder().Build().Activity;
        var audienceClaim = new Claim(
            "aud",
            FakeTeamsActivityBuilder.DefaultBotAppId);
        var validatedIdentity = new ClaimsIdentity(
            [audienceClaim],
            TeamsAgentRegistration.ActivityAuthenticationScheme);
        var unrelatedIdentity = new ClaimsIdentity(
            [audienceClaim],
            DemoAuthentication.Scheme);

        Assert.Equal(
            TeamsAgentRegistration.ActivityAuthenticationScheme,
            jwtOptions.TokenValidationParameters.AuthenticationType);
        Assert.True(
            resolver.TryResolve(
                activity,
                validatedIdentity,
                out var actor));
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, actor.TenantId);
        Assert.Equal(FakeTeamsActivityBuilder.DefaultActorId, actor.ChannelActorId);
        Assert.False(
            resolver.TryResolve(
                activity,
                unrelatedIdentity,
                out _));
    }

    [Fact]
    public async Task MafSessionInfrastructureUsesProcessLifetimeSingletons()
    {
        await using var factory = new GovernedAccessWebFactory();
        var services = factory.Services;
        var concreteStore = services.GetRequiredService<InMemoryAgentSessionStore>();
        var sessionStore = services.GetRequiredService<AgentSessionStore>();
        var coordinator = services.GetRequiredService<MafConversationTurnCoordinator>();
        var interpreter = services.GetRequiredService<IRequestPreparationInterpreter>();

        Assert.Same(concreteStore, sessionStore);

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();

        Assert.Same(
            concreteStore,
            firstScope.ServiceProvider.GetRequiredService<AgentSessionStore>());
        Assert.Same(
            concreteStore,
            secondScope.ServiceProvider.GetRequiredService<AgentSessionStore>());
        Assert.Same(
            coordinator,
            firstScope.ServiceProvider
                .GetRequiredService<MafConversationTurnCoordinator>());
        Assert.Same(
            coordinator,
            secondScope.ServiceProvider
                .GetRequiredService<MafConversationTurnCoordinator>());
        Assert.Same(
            interpreter,
            firstScope.ServiceProvider
                .GetRequiredService<IRequestPreparationInterpreter>());
        Assert.Same(
            interpreter,
            secondScope.ServiceProvider
                .GetRequiredService<IRequestPreparationInterpreter>());
    }

    [Fact]
    public void TeamsOptionsAcceptTheBoundedTeamsManagedConfiguration()
    {
        var configuration = CreateTeamsConfiguration();

        var (options, result) = ValidateTeamsOptions(configuration);

        Assert.True(
            result.Succeeded,
            result.FailureMessage ?? "Teams option validation failed.");
        Assert.Equal(FakeTeamsActivityBuilder.DefaultTenantId, options.AllowedTenantId);
        Assert.Equal(BotConnectionName, options.BotConnectionName);
        Assert.Equal(GovernedAccessWebFactory.DefaultTrustedWebBaseUri, options.TrustedWebBaseUri);
        Assert.Equal(TeamsAccessRequestOptions.MaximumModelTimeout, options.ModelTimeout);
        Assert.Equal(TeamsAccessRequestOptions.MaximumMcpTimeout, options.McpTimeout);
        Assert.Equal(
            TeamsAccessRequestOptions.RequiredPreparationLifetime,
            options.PreparationLifetime);
    }

    [Fact]
    public void TeamsOptionsFailClosedForUnsafeConfiguration()
    {
        (string Key, string Value, string ExpectedFailure)[] invalidValues =
        [
            ("TeamsAccessRequest:AllowedTenantId", "", "AllowedTenantId"),
            ("TeamsAccessRequest:BotConnectionName", "invalid:name", "BotConnectionName"),
            ("TokenValidation:Enabled", "false", "TokenValidation:Enabled"),
            ("TokenValidation:TenantId", "33333333-3333-3333-3333-333333333333", "TokenValidation:TenantId"),
            ("Connections:BotServiceConnection:Settings:AuthType", "ManagedIdentity", "AuthType"),
            ("Connections:BotServiceConnection:Settings:Authority", "https://login.microsoftonline.com/common", "Authority"),
            ("Connections:BotServiceConnection:Settings:ClientId", "", "Settings:ClientId"),
            ("Connections:BotServiceConnection:Settings:TenantId", "33333333-3333-3333-3333-333333333333", "Settings:TenantId"),
            ("Connections:BotServiceConnection:Settings:ClientSecret", "", "ClientSecret"),
            ("Connections:BotServiceConnection:Settings:Scopes:0", "https://graph.microsoft.com/.default", "Settings:Scopes"),
            ("TokenValidation:Audiences:0", "44444444-4444-4444-4444-444444444444", "TokenValidation:Audiences"),
            ("ConnectionsMap:0:ServiceUrl", "https://smba.trafficmanager.net/emea/", "ConnectionsMap"),
            ("TeamsAccessRequest:TrustedWebBaseUri", "http://governed-access.test/", "TrustedWebBaseUri"),
            ("TeamsAccessRequest:ModelTimeout", "00:00:31", "ModelTimeout"),
            ("TeamsAccessRequest:McpTimeout", "00:00:06", "McpTimeout"),
            ("TeamsAccessRequest:PreparationLifetime", "00:29:59", "PreparationLifetime"),
        ];

        foreach (var (key, value, expectedFailure) in invalidValues)
        {
            var configuration = CreateTeamsConfiguration();
            configuration[key] = value;
            var (_, result) = ValidateTeamsOptions(configuration);

            Assert.True(result.Failed);
            Assert.Contains(
                result.Failures,
                failure => failure.Contains(
                    expectedFailure,
                    StringComparison.Ordinal));
        }
    }

    private static (
        TeamsAccessRequestOptions Options,
        ValidateOptionsResult Result)
        ValidateTeamsOptions(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var options = configuration
            .GetRequiredSection(TeamsAccessRequestOptions.SectionName)
            .Get<TeamsAccessRequestOptions>()
            ?? throw new InvalidOperationException(
                "The valid test configuration could not bind Teams options.");
        var validator = new TeamsAccessRequestOptionsValidator(configuration);

        return (options, validator.Validate(Options.DefaultName, options));
    }

    private static Dictionary<string, string?> CreateTeamsConfiguration() =>
        new()
        {
            ["TokenValidation:Enabled"] = bool.TrueString,
            ["TokenValidation:Audiences:0"] =
                FakeTeamsActivityBuilder.DefaultBotAppId,
            ["TokenValidation:TenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            [$"Connections:{BotConnectionName}:Settings:AuthType"] =
                "ClientSecret",
            [$"Connections:{BotConnectionName}:Settings:Authority"] =
                "https://login.microsoftonline.com/botframework.com",
            [$"Connections:{BotConnectionName}:Settings:ClientId"] =
                FakeTeamsActivityBuilder.DefaultBotAppId,
            [$"Connections:{BotConnectionName}:Settings:ClientSecret"] =
                "integration-test-only",
            [$"Connections:{BotConnectionName}:Settings:TenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            [$"Connections:{BotConnectionName}:Settings:Scopes:0"] =
                AuthenticationConstants.BotFrameworkDefaultScope,
            ["ConnectionsMap:0:ServiceUrl"] = "*",
            ["ConnectionsMap:0:Connection"] = BotConnectionName,
            ["TeamsAccessRequest:AllowedTenantId"] =
                FakeTeamsActivityBuilder.DefaultTenantId,
            ["TeamsAccessRequest:BotConnectionName"] = BotConnectionName,
            ["TeamsAccessRequest:TrustedWebBaseUri"] =
                GovernedAccessWebFactory.DefaultTrustedWebBaseUri.AbsoluteUri,
            ["TeamsAccessRequest:ModelTimeout"] = "00:00:30",
            ["TeamsAccessRequest:McpTimeout"] = "00:00:05",
            ["TeamsAccessRequest:PreparationLifetime"] = "00:30:00",
        };

    private static IEnumerable<object?> GetPrivateDependencies(object instance) =>
        instance
            .GetType()
            .GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.GetValue(instance));
}

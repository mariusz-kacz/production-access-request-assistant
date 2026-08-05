using System.Net;
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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ProgramCompositionTests(
    DefaultWebApplicationFixture applicationFixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private const string BotConnectionName = "BotServiceConnection";

    [Fact]
    public async Task StartupCreatesAndSeedsTheConfiguredDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = applicationFixture.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();

        var clientCount = await dbContext.Clients.CountAsync(cancellationToken);
        var principalCount = await dbContext.AuthenticatedPrincipals.CountAsync(cancellationToken);

        Assert.Equal(4, clientCount);
        Assert.Equal(6, principalCount);
        Assert.Same(factory.Clock, factory.Services.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task IntakeComponentsUseOneScopedServiceStoreAndDbContext()
    {
        await using var scope = applicationFixture.Factory.Services
            .CreateAsyncScope();
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
        var services = applicationFixture.Factory.Services;
        var rootAgent = services.GetRequiredService<IAgent>();

        Assert.IsType<ScopedTeamsAccessRequestAgentDispatcher>(rootAgent);

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
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
    public async Task MafSessionInfrastructureUsesProcessLifetimeSingletons()
    {
        var services = applicationFixture.Factory.Services;
        var concreteStore = services.GetRequiredService<InMemoryAgentSessionStore>();
        var sessionStore = services.GetRequiredService<AgentSessionStore>();
        var coordinator = services.GetRequiredService<MafConversationTurnCoordinator>();
        var interpreter = services.GetRequiredService<IRequestPreparationInterpreter>();
        var chatClient = services.GetRequiredService<IChatClient>();
        var modelResolution = services
            .GetRequiredService<RequestPreparationModelResolution>();
        var modelMetadata = services
            .GetRequiredService<RequestPreparationModelMetadata>();

        Assert.Same(concreteStore, sessionStore);
        Assert.Same(chatClient, Assert.Single(services.GetServices<IChatClient>()));
        Assert.Equal(
            RequestPreparationModelProfile.Deterministic,
            modelResolution.Profile);
        Assert.Null(modelResolution.DeploymentName);
        Assert.Equal("Deterministic", modelMetadata.ProfileId);
        Assert.Null(modelMetadata.DeploymentName);

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
        Assert.Same(
            chatClient,
            firstScope.ServiceProvider.GetRequiredService<IChatClient>());
        Assert.Same(
            chatClient,
            secondScope.ServiceProvider.GetRequiredService<IChatClient>());
        Assert.Same(
            modelResolution,
            firstScope.ServiceProvider
                .GetRequiredService<RequestPreparationModelResolution>());
        Assert.Same(
            modelResolution,
            secondScope.ServiceProvider
                .GetRequiredService<RequestPreparationModelResolution>());
        Assert.Same(
            modelMetadata,
            firstScope.ServiceProvider
                .GetRequiredService<RequestPreparationModelMetadata>());
        Assert.Same(
            modelMetadata,
            secondScope.ServiceProvider
                .GetRequiredService<RequestPreparationModelMetadata>());
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
        Assert.Equal(
            TeamsAccessRequestOptions.MaximumRequestTimeout,
            options.RequestTimeout);
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
            ("TeamsAccessRequest:RequestTimeout", "00:01:41", "RequestTimeout"),
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
            ["TeamsAccessRequest:RequestTimeout"] = "00:01:40",
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

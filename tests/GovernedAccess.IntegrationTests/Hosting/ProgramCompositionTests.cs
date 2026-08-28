using System.Net;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ProgramCompositionTests(
    DefaultWebApplicationFixture applicationFixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private const string BotConnectionName = "BotServiceConnection";

    private const string CompleteProposal =
        """
        {"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"environment":{"operation":"set","reference":{"kind":"exactEnvironmentId","id":"PROD-ALPHA-EU"}},"role":{"operation":"set","roleId":"ProductionReadOnly"},"justification":{"operation":"set","value":{"text":"Investigate elevated customer errors."}},"incident":{"operation":"set","incidentId":"INC-1042"}}}
        """;

    [Fact]
    public async Task StartupCreatesAndSeedsTheIndependentProductionDatabases()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = applicationFixture.Factory;
        await using var scope = factory.Services.CreateAsyncScope();
        var referenceContext = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        var workflowContext = scope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();

        var clientCount = await referenceContext.Clients.CountAsync(
            cancellationToken);
        var principalCount = await workflowContext.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM \"AuthenticatedPrincipals\"")
            .SingleAsync(cancellationToken);

        Assert.Equal(4, clientCount);
        Assert.Equal(6, principalCount);
        Assert.Same(factory.Clock, factory.Services.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task ProductionCompositionResolvesOnlyTheCompleteTargetGraph()
    {
        await using var scope = applicationFixture.Factory.Services
            .CreateAsyncScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetService<ReferenceAuthorityDbContext>());
        Assert.NotNull(services.GetService<WorkflowDbContext>());
        Assert.IsType<MafTurnProposalInterpreter>(
            services.GetRequiredService<ITurnProposalInterpreter>());
        Assert.IsType<RequestPreparationOrchestrator>(
            services.GetRequiredService<IRequestPreparationOrchestrator>());
        Assert.IsType<PreparationConfirmationService>(
            services.GetRequiredService<IPreparationConfirmationService>());
        Assert.IsType<TeamsRequestHandler>(
            services.GetRequiredService<TeamsRequestHandler>());
        Assert.Equal(
            typeof(WorkflowDbContext).Assembly,
            services.GetRequiredService<IWorkflowStore>().GetType().Assembly);
    }

    [Fact]
    public async Task TeamsBackgroundDispatcherIsRootSafeAndAgentsAreScopedPerTurn()
    {
        var services = applicationFixture.Factory.Services;
        var rootAgent = services.GetRequiredService<IAgent>();

        Assert.IsType<ScopedTeamsAccessRequestAgentDispatcher>(rootAgent);

        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstHandler = firstScope.ServiceProvider
            .GetRequiredService<TeamsRequestHandler>();
        var firstAgent = firstScope.ServiceProvider
            .GetRequiredService<TeamsAccessRequestAgent>();

        Assert.Same(
            firstHandler,
            firstScope.ServiceProvider
                .GetRequiredService<TeamsRequestHandler>());
        Assert.NotSame(
            firstHandler,
            secondScope.ServiceProvider
                .GetRequiredService<TeamsRequestHandler>());
        Assert.NotSame(
            firstAgent,
            secondScope.ServiceProvider.GetRequiredService<TeamsAccessRequestAgent>());
    }

    [Fact]
    public async Task MafSessionInfrastructureUsesProcessLifetimeSingletons()
    {
        var services = applicationFixture.Factory.Services;
        var interpreter = services.GetRequiredService<ITurnProposalInterpreter>();
        var chatClient = services.GetRequiredService<IChatClient>();
        var modelResolution = services
            .GetRequiredService<RequestPreparationModelResolution>();
        var modelMetadata = services
            .GetRequiredService<RequestPreparationModelMetadata>();

        Assert.IsType<MafTurnProposalInterpreter>(interpreter);
        Assert.Null(services.GetService<InMemoryAgentSessionStore>());
        Assert.Null(services.GetService<AgentSessionStore>());
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
            interpreter,
            firstScope.ServiceProvider
                .GetRequiredService<ITurnProposalInterpreter>());
        Assert.Same(
            interpreter,
            secondScope.ServiceProvider
                .GetRequiredService<ITurnProposalInterpreter>());
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
    public async Task ProductionMcpEndpointExposesOnlyTheTargetToolCatalog()
    {
        await using var client = await CreateProductionMcpClientAsync(
            applicationFixture.Factory.CreateClient(),
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "get_environment_roles",
                "get_incident",
                "get_production_environment",
                "search_production_environments",
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProductionCompositionCompletesPreparationAndDownstreamWorkflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            new RecordingChatClient(CompleteProposal));
        await factory.ResetDatabaseAsync(cancellationToken);

        TeamsResponse prepared;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            prepared = await scope.ServiceProvider
                .GetRequiredService<TeamsRequestHandler>()
                .HandleMessageAsync(
                    TeamsContext(),
                    "Prepare the incident investigation request.",
                    "production-preparation",
                    cancellationToken);
        }

        Assert.Equal(TeamsResponseKind.Card, prepared.Kind);
        var preparationId = Assert.IsType<Guid>(prepared.PreparationId);

        Guid requestId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var submitted = await scope.ServiceProvider
                .GetRequiredService<TeamsRequestHandler>()
                .HandleConfirmationAsync(
                    TeamsContext(),
                    new
                    {
                        schemaVersion = 1,
                        preparationId = preparationId.ToString("D"),
                    },
                    "production-confirmation",
                    cancellationToken);

            Assert.Equal(TeamsResponseKind.Card, submitted.Kind);
            requestId = await scope.ServiceProvider
                .GetRequiredService<WorkflowDbContext>()
                .Set<AccessRequest>()
                .Where(request => request.PreparationId == preparationId)
                .Select(request => request.Id)
                .SingleAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var workflow = scope.ServiceProvider
                .GetRequiredService<AccessRequestWorkflowService>();
            var business = await workflow.DecideAsync(
                ApprovalStage.Business,
                requestId,
                "client-alpha-business-approver",
                ApprovalOutcome.Approved,
                "Approved for incident investigation.",
                "production-business",
                cancellationToken);
            var devOps = await workflow.DecideAsync(
                ApprovalStage.DevOps,
                requestId,
                "devops-approver",
                ApprovalOutcome.Approved,
                null,
                "production-devops",
                cancellationToken);

            Assert.True(business.IsSuccess, business.Failure?.Message);
            Assert.True(devOps.IsSuccess, devOps.Failure?.Message);
            Assert.Equal(RequestStatus.Active, devOps.Value.Request.Status);
            Assert.Equal(
                AccessGrant.FixedLifetime,
                devOps.Value.Grant!.ExpiresAt - devOps.Value.Grant.ActivatedAt);
        }
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

    private static async Task<McpClient> CreateProductionMcpClientAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            httpClient.BaseAddress
                ?? throw new InvalidOperationException(
                    "The production test client has no base address."),
            "mcp");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = "governed-access-production-composition-tests",
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

    private static TeamsAuthenticatedContext TeamsContext() =>
        new(
            new TeamsConversationReference(
                PreparationBinding.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                "requester"),
            "en-US");
}

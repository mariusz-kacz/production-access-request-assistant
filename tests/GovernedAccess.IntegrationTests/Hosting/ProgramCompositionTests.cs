using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Agents.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ProgramCompositionTests(
    DefaultWebApplicationFixture applicationFixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private const string BotConnectionName = "BotServiceConnection";

    private const string CompleteProposal =
        """
        {"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"environment":{"operation":"set","reference":{"kind":"exactEnvironmentId","id":"PROD-ALPHA-EU"}},"role":{"operation":"set","roleId":"ProductionReadOnly"},"justification":{"operation":"set","value":{"text":"Investigate elevated customer errors."}},"incident":{"operation":"set","incidentId":"INC-1042"}},"discussionTopic":null}
        """;

    [Fact]
    public async Task StartupAppliesMigrationsAndMakesRequiredDataUsable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = applicationFixture.Factory.Services.CreateAsyncScope();
        var environment = await scope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentAuthority>()
            .GetAsync(
                "PROD-ALPHA-EU",
                cancellationToken);
        var requester = await scope.ServiceProvider
            .GetRequiredService<IAuthenticatedPrincipalReader>()
            .GetPrincipalAsync(
                "requester",
                cancellationToken);

        Assert.True(environment.IsSuccess, environment.Failure?.Message);
        Assert.True(requester.IsSuccess, requester.Failure?.Message);
        Assert.Equal("client-alpha", environment.Value.ClientId);
        Assert.Equal("requester", requester.Value.Id);
    }

    [Fact]
    public async Task ProductionMcpEndpointUsesTheValidatedAgentCatalog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CreateProductionMcpClientAsync(
            applicationFixture.Factory.CreateClient(),
            cancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);

        Assert.True(AgentMcpCatalog.IsValid(
            tools.Select(tool => tool.ProtocolTool).ToArray()));
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

        var preparationId = Assert
            .IsType<TeamsDraftCardResponse>(prepared)
            .PreparationId;

        Guid requestId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var submitted = await scope.ServiceProvider
                .GetRequiredService<TeamsRequestHandler>()
                .HandleConfirmationAsync(
                    TeamsContext(),
                    preparationId,
                    "production-confirmation",
                    cancellationToken);

            Assert.IsType<TeamsTerminalCardResponse>(submitted);
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
            Assert.NotNull(devOps.Value.Grant);
        }
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

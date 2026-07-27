using System.Net;
using System.Text.Json;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Authentication;
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
    public async Task SessionEndpointRunsThroughAuthenticationAndCorrelationMiddleware()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/session", cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var session = await JsonDocument.ParseAsync(
            body,
            cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(session.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.True(response.Headers.Contains(CorrelationContext.HeaderName));
    }

    [Fact]
    public async Task FactoryCanSignInThroughTheComposedAntiforgeryAndCookiePipeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using var response = await client.GetAsync("/api/session", cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var session = await JsonDocument.ParseAsync(
            body,
            cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(session.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            DemoPrincipalKeys.Requester,
            session.RootElement.GetProperty("principal").GetProperty("id").GetString());
    }

    [Fact]
    public async Task McpIsMappedAndApiOrMcpMissesDoNotUseTheSpaFallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var routeEndpoints = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToArray();

        Assert.Contains(
            routeEndpoints,
            endpoint => endpoint.RoutePattern.RawText?.Contains(
                "/mcp",
                StringComparison.Ordinal) == true);
        Assert.Contains(routeEndpoints, endpoint => endpoint.Order == int.MaxValue);

        using var client = factory.CreateClient();
        using var missingApi = await client.GetAsync("/api/not-implemented", cancellationToken);
        using var missingMcp = await client.GetAsync("/mcp/not-implemented", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingMcp.StatusCode);
        Assert.NotEqual("text/html", missingApi.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", missingMcp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void TeamsOptionsAcceptTheBoundedSingleTenantConfiguration()
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

    [Theory]
    [InlineData(
        "TeamsAccessRequest:AllowedTenantId",
        "",
        "AllowedTenantId")]
    [InlineData(
        "TeamsAccessRequest:BotConnectionName",
        "invalid:name",
        "BotConnectionName")]
    [InlineData(
        "TokenValidation:Enabled",
        "false",
        "TokenValidation:Enabled")]
    [InlineData(
        "TokenValidation:TenantId",
        "33333333-3333-3333-3333-333333333333",
        "TokenValidation:TenantId")]
    [InlineData(
        "Connections:BotServiceConnection:Settings:AuthType",
        "ManagedIdentity",
        "AuthType")]
    [InlineData(
        "Connections:BotServiceConnection:Settings:ClientId",
        "",
        "Settings:ClientId")]
    [InlineData(
        "Connections:BotServiceConnection:Settings:TenantId",
        "33333333-3333-3333-3333-333333333333",
        "Settings:TenantId")]
    [InlineData(
        "Connections:BotServiceConnection:Settings:ClientSecret",
        "",
        "ClientSecret")]
    [InlineData(
        "Connections:BotServiceConnection:Settings:Scopes:0",
        "https://graph.microsoft.com/.default",
        "Settings:Scopes")]
    [InlineData(
        "TokenValidation:Audiences:0",
        "44444444-4444-4444-4444-444444444444",
        "TokenValidation:Audiences")]
    [InlineData(
        "ConnectionsMap:0:ServiceUrl",
        "https://smba.trafficmanager.net/emea/",
        "ConnectionsMap")]
    [InlineData(
        "TeamsAccessRequest:TrustedWebBaseUri",
        "http://governed-access.test/",
        "TrustedWebBaseUri")]
    [InlineData(
        "TeamsAccessRequest:ModelTimeout",
        "00:00:31",
        "ModelTimeout")]
    [InlineData(
        "TeamsAccessRequest:McpTimeout",
        "00:00:06",
        "McpTimeout")]
    [InlineData(
        "TeamsAccessRequest:PreparationLifetime",
        "00:29:59",
        "PreparationLifetime")]
    public void TeamsOptionsFailClosedForUnsafeConfiguration(
        string key,
        string value,
        string expectedFailure)
    {
        var configuration = CreateTeamsConfiguration();
        configuration[key] = value;

        var (_, result) = ValidateTeamsOptions(configuration);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
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
}

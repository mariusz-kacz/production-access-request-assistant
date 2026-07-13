using System.Net;
using System.Text.Json;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ProgramCompositionTests
{
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
}

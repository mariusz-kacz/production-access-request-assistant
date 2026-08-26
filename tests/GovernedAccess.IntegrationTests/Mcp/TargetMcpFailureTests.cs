using System.Text.Json;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class TargetMcpFailureTests
{
    [Fact]
    public async Task InvalidAndUnknownInputsReturnTypedFailureEnvelopes()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-target-input-failure-tests",
            TestContext.Current.CancellationToken);

        var invalidSearch = await CallAsync(
            client,
            "search_production_environments",
            "query",
            "   ");
        var invalidEnvironment = await CallAsync(
            client,
            "get_production_environment",
            "environmentId",
            "   ");
        var missingEnvironment = await CallAsync(
            client,
            "get_production_environment",
            "environmentId",
            "PROD-UNKNOWN");
        var missingRoles = await CallAsync(
            client,
            "get_environment_roles",
            "environmentId",
            "PROD-UNKNOWN");
        var missingIncident = await CallAsync(
            client,
            "get_incident",
            "incidentId",
            "INC-UNKNOWN");

        AssertTypedFailure(
            invalidSearch,
            "InvalidInput",
            "environment_query_invalid");
        AssertTypedFailure(
            invalidEnvironment,
            "InvalidInput",
            "environment-id-invalid");
        AssertTypedFailure(
            missingEnvironment,
            "NotFound",
            "environment-not-found");
        AssertTypedFailure(
            missingRoles,
            "NotFound",
            "environment-not-found");
        AssertTypedFailure(
            missingIncident,
            "NotFound",
            "incident-not-found");
    }

    [Fact]
    public async Task SearchOverflowReturnsUnavailableWithoutPartialResults()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            context.ProductionEnvironments.AddRange(
                Enumerable.Range(1, 6).Select(index =>
                    new ReferenceProductionEnvironment(
                        $"OVERFLOW-{index:D2}",
                        "client-alpha",
                        $"Overflow target {index:D2}",
                        "EU",
                        EnvironmentClassification.Primary,
                        isActive: true,
                        isProduction: true,
                        isEligibleForIntake: true)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var client = await host.CreateClientAsync(
            "governed-access-target-overflow-tests",
            TestContext.Current.CancellationToken);
        await using var authorityScope = host.Services.CreateAsyncScope();
        var authority = authorityScope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentSearchAuthority>();
        var coreResult = await authority.SearchAsync(
            "overflow target",
            TestContext.Current.CancellationToken);
        var result = await CallAsync(
            client,
            "search_production_environments",
            "query",
            "overflow target");

        Assert.Equal(EnvironmentSearchResultKind.TooBroad, coreResult.Value.Kind);
        Assert.Equal(6, coreResult.Value.MatchCount);
        Assert.Empty(coreResult.Value.Matches);
        AssertTypedFailure(
            result,
            "Unavailable",
            "environment_query_too_broad");
    }

    [Fact]
    public async Task IneligibleEnvironmentIsHiddenFromExactAndRoleTools()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            await context.Database.ExecuteSqlAsync(
                $"UPDATE ProductionEnvironments SET IsEligibleForIntake = 0 WHERE Id = 'PROD-ALPHA-EU'",
                TestContext.Current.CancellationToken);
        }

        await using var client = await host.CreateClientAsync(
            "governed-access-target-ineligible-tests",
            TestContext.Current.CancellationToken);
        var exact = await CallAsync(
            client,
            "get_production_environment",
            "environmentId",
            "PROD-ALPHA-EU");
        var roles = await CallAsync(
            client,
            "get_environment_roles",
            "environmentId",
            "PROD-ALPHA-EU");

        AssertTypedFailure(exact, "NotFound", "environment-not-found");
        AssertTypedFailure(roles, "NotFound", "environment-not-found");
    }

    [Fact]
    public async Task ReferenceSourceFailureReturnsSafeTypedUnavailableEnvelope()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            await context.Database.ExecuteSqlAsync(
                $"DROP TABLE EnvironmentRoles",
                TestContext.Current.CancellationToken);
        }

        await using var client = await host.CreateClientAsync(
            "governed-access-target-source-failure-tests",
            TestContext.Current.CancellationToken);
        var result = await CallAsync(
            client,
            "get_environment_roles",
            "environmentId",
            "PROD-ALPHA-EU");

        AssertTypedFailure(
            result,
            "Unavailable",
            "environment-role-authority-unavailable");
    }

    [Fact]
    public async Task IncidentWithoutAnEnvironmentReturnsNullableReadOnlyContext()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            context.Incidents.Add(
                new ReferenceIncident(
                    "INC-NO-ELIGIBLE-ENVIRONMENT",
                    "No eligible environment",
                    isActive: true,
                    environmentId: null));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var client = await host.CreateClientAsync(
            "governed-access-target-incident-link-tests",
            TestContext.Current.CancellationToken);
        var result = await CallAsync(
            client,
            "get_incident",
            "incidentId",
            "INC-NO-ELIGIBLE-ENVIRONMENT");

        Assert.NotEqual(true, result.IsError);
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(JsonValueKind.Null, content.GetProperty("environmentId").ValueKind);
    }

    private static async Task<CallToolResult> CallAsync(
        McpClient client,
        string toolName,
        string argumentName,
        string argumentValue)
    {
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var tool = Assert.Single(tools, candidate => candidate.Name == toolName);
        return await tool.CallAsync(
            new Dictionary<string, object?>
            {
                [argumentName] = argumentValue,
            },
            cancellationToken: TestContext.Current.CancellationToken);
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
        Assert.False(string.IsNullOrWhiteSpace(
            content.GetProperty("correlationId").GetString()));
    }
}

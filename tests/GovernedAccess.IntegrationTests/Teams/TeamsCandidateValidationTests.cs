using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace GovernedAccess.IntegrationTests.Teams;

[Collection(IntegrationTestCollections.FullApplication)]
public sealed class TeamsCandidateValidationTests(ConfigurableTeamsFixture fixture)
    : IClassFixture<ConfigurableTeamsFixture>
{
    [Theory]
    [InlineData(
        DeterministicChatMode.InvalidCandidate,
        "The selected production environment does not exist.")]
    [InlineData(
        DeterministicChatMode.UnknownIncidentCandidate,
        "The supplied incident does not exist.")]
    [InlineData(
        DeterministicChatMode.CrossClientEnvironmentCandidate,
        "The selected production environment does not belong to the client.")]
    [InlineData(
        DeterministicChatMode.CrossClientIncidentCandidate,
        "The supplied incident does not belong to the client.")]
    public async Task UnknownAndCrossClientIdentifiersAreRejectedAuthoritatively(
        DeterministicChatMode chatMode,
        string expectedValidationMessage)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(chatMode, cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var responseBody = await SendMessageAsync(
            client,
            "Prepare the candidate proposed by the deterministic model.",
            "candidate-validation",
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);
        var responseText = ExtractResponseText(responseBody);

        Assert.Contains(
            "Deterministic application validation rejected the assistant's candidate.",
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedValidationMessage,
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Nothing has been submitted.",
            responseText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            responseBody,
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(
            await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task CandidateKindCannotOverrideDeterministicMissingFieldValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.FalseCompleteCandidate,
            cancellationToken);
        var factory = fixture.Factory;
        using var client = factory.CreateTeamsClient();

        var responseBody = await SendMessageAsync(
            client,
            "The model says this candidate is complete.",
            "false-complete-candidate",
            FakeTeamsActivityBuilder.DefaultConversationId,
            cancellationToken);
        var responseText = ExtractResponseText(responseBody);

        Assert.Contains(
            "Deterministic application validation rejected the assistant's candidate.",
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            "A production environment is required.",
            responseText,
            StringComparison.Ordinal);
        Assert.Contains(
            "A requested role is required.",
            responseText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            responseBody,
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(
            await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task RoleClarificationsMatchAuthoritativeAvailableRoleDiscovery()
    {
        const string alphaEnvironment = "PROD-ALPHA-EU";
        const string betaEnvironment = "PROD-BETA-UK";

        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetAsync(
            DeterministicChatMode.HistorySensitive,
            cancellationToken);
        var factory = fixture.Factory;
        using var teamsClient = factory.CreateTeamsClient();
        await using var mcpHost = await McpTestHost.CreateSeededAsync(
            cancellationToken);
        await using var mcpClient = await mcpHost.CreateClientAsync(
            "teams-role-discovery-tests",
            cancellationToken);

        var alphaRoles = await DiscoverRolesAsync(
            mcpClient,
            alphaEnvironment,
            cancellationToken);
        var betaRoles = await DiscoverRolesAsync(
            mcpClient,
            betaEnvironment,
            cancellationToken);

        Assert.Equal(
            [ProductionRoleIds.ReadOnly, ProductionRoleIds.Support],
            alphaRoles);
        Assert.Equal([ProductionRoleIds.ReadOnly], betaRoles);

        var alphaBody = await SendMessageAsync(
            teamsClient,
            $"Use {alphaEnvironment}.",
            "alpha-role-discovery",
            "alpha-role-discovery",
            cancellationToken);
        var betaBody = await SendMessageAsync(
            teamsClient,
            $"Use {betaEnvironment}.",
            "beta-role-discovery",
            "beta-role-discovery",
            cancellationToken);

        Assert.All(
            alphaRoles,
            role => Assert.Contains(role, alphaBody, StringComparison.Ordinal));
        Assert.All(
            betaRoles,
            role => Assert.Contains(role, betaBody, StringComparison.Ordinal));
        Assert.DoesNotContain(
            ProductionRoleIds.Support,
            betaBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            alphaBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PreparedRequestCardFactory.AdaptiveCardContentType,
            betaBody,
            StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .OrderBy(session => session.ConversationId)
            .ToArrayAsync(cancellationToken);

        Assert.Equal(2, sessions.Length);
        Assert.All(
            sessions,
            session => Assert.Equal(
                RequestIntakeStatus.Collecting,
                session.Status));
        Assert.Contains(
            sessions,
            session => session.EnvironmentId == alphaEnvironment);
        Assert.Contains(
            sessions,
            session => session.EnvironmentId == betaEnvironment);
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    private static async Task<string[]> DiscoverRolesAsync(
        McpClient mcpClient,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var tools = await mcpClient.ListToolsAsync(
            cancellationToken: cancellationToken);
        var tool = Assert.Single(
            tools,
            candidate => candidate.Name == "get_available_roles");
        var result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = environmentId,
            },
            cancellationToken: cancellationToken);

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = JsonSerializer.SerializeToElement(
            result.StructuredContent);
        Assert.Equal(environmentId, content.GetProperty("environmentId").GetString());
        return content
            .GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetProperty("roleId").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> SendMessageAsync(
        HttpClient client,
        string text,
        string activityId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .WithActivityId(activityId)
            .WithConversation(conversationId)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");
        return responseBody;
    }

    private static string ExtractResponseText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var activity = Assert.Single(
            document.RootElement
                .GetProperty("activities")
                .EnumerateArray()
                .ToArray());
        return activity.GetProperty("text").GetString() ?? string.Empty;
    }

    private static async Task AssertNoWorkflowStateAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Assert.Empty(
            await dbContext.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ApprovalDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ProvisioningOperations
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }
}

using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsCandidateValidationTests
{
    private const string CompleteProviderCandidate =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate customer-facing errors during the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    private const string CrossClientProviderCandidate =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-BETA-UK","requestedRoleId":"ProductionReadOnly","justification":"Investigate customer-facing errors during the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    private const string AlphaRoleClarification =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"requestedRoleId","message":"Choose ProductionReadOnly or ProductionSupport."}}
        """;

    private const string BetaRoleClarification =
        """
        {"kind":"clarification","candidate":{"clientId":"client-beta","environmentId":"PROD-BETA-UK","requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"requestedRoleId","message":"Choose ProductionReadOnly."}}
        """;

    [Fact]
    public async Task ProviderCandidateBecomesReadyOnlyAfterAuthoritativeValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var providerClient = new RecordingChatClient(CompleteProviderCandidate);
        var service = CreateService(dbContext, fixture.Clock, providerClient);

        var result = await service.PrepareAsync(
            CreateCommand("Prepare the complete provider candidate."),
            cancellationToken);

        Assert.Equal(RequestPreparationResultKind.ReadyForConfirmation, result.Kind);
        var session = Assert.IsType<RequestIntakeSession>(result.Session);
        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
        Assert.Equal(DemoDataIds.ClientAlphaId, session.ClientId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, session.RequestedRoleId);
        Assert.Equal(DemoDataIds.PrimaryIncidentId, session.IncidentId);
        Assert.Equal(1, providerClient.InvocationCount);
        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

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
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateService(dbContext, fixture.Clock, chatMode);

        var result = await service.PrepareAsync(
            CreateCommand("Prepare the deterministic candidate."),
            cancellationToken);

        Assert.Equal(RequestPreparationResultKind.CandidateRejected, result.Kind);
        var validationError = Assert.Single(
            result.ValidationErrors,
            error => error.Message == expectedValidationMessage);
        var session = Assert.Single(await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Equal(RequestIntakeStatus.Collecting, session.Status);
        Assert.Null(session.ReservedRequestId);
        Assert.Equal("client-alpha", session.ClientId);
        if (validationError.Field == "environmentId")
        {
            Assert.Null(session.EnvironmentId);
        }
        else
        {
            Assert.Equal("incidentId", validationError.Field);
            Assert.Null(session.IncidentId);
        }

        await AssertNoWorkflowStateAsync(dbContext, cancellationToken);
    }

    [Fact]
    public async Task CandidateKindCannotOverrideDeterministicMissingFieldValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateService(
            dbContext,
            fixture.Clock,
            DeterministicChatMode.FalseCompleteCandidate);

        var result = await service.PrepareAsync(
            CreateCommand("The model says this candidate is complete."),
            cancellationToken);

        Assert.Equal(RequestPreparationResultKind.CandidateRejected, result.Kind);
        Assert.Contains(
            result.ValidationErrors,
            error => error.Message == "A production environment is required.");
        Assert.Contains(
            result.ValidationErrors,
            error => error.Message == "A requested role is required.");
        var session = Assert.Single(await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Equal(RequestIntakeStatus.Collecting, session.Status);
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            session.Justification);
    }

    [Fact]
    public async Task RoleClarificationsMatchAuthoritativeAvailableRoleDiscovery()
    {
        const string alphaEnvironment = "PROD-ALPHA-EU";
        const string betaEnvironment = "PROD-BETA-UK";

        var cancellationToken = TestContext.Current.CancellationToken;
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
        var interpreter = CreateInterpreter(
            new ScriptedChatClient(
                AlphaRoleClarification,
                BetaRoleClarification));

        var alpha = await interpreter.InterpretAsync(
            CreateTurn(Guid.NewGuid(), $"Use {alphaEnvironment}."),
            cancellationToken);
        var beta = await interpreter.InterpretAsync(
            CreateTurn(Guid.NewGuid(), $"Use {betaEnvironment}."),
            cancellationToken);
        var alphaProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            alpha).Proposal;
        var betaProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            beta).Proposal;

        Assert.Equal(
            [ProductionRoleIds.ReadOnly, ProductionRoleIds.Support],
            alphaRoles);
        Assert.Equal([ProductionRoleIds.ReadOnly], betaRoles);
        Assert.All(
            alphaRoles,
            role => Assert.Contains(
                role,
                alphaProposal.Clarification!.Message,
                StringComparison.Ordinal));
        Assert.All(
            betaRoles,
            role => Assert.Contains(
                role,
                betaProposal.Clarification!.Message,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            ProductionRoleIds.Support,
            betaProposal.Clarification!.Message,
            StringComparison.Ordinal);
    }

    private static RequestIntakeService CreateService(
        GovernedAccessDbContext dbContext,
        IClock clock,
        DeterministicChatMode mode)
        => CreateService(
            dbContext,
            clock,
            new DeterministicChatClient(mode));

    private static RequestIntakeService CreateService(
        GovernedAccessDbContext dbContext,
        IClock clock,
        IChatClient chatClient)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        var workflowStore = new EfWorkflowStore(dbContext);
        var validator = new RequestValidator(requestContext);
        return new RequestIntakeService(
            CreateInterpreter(chatClient),
            validator,
            new EfRequestIntakeStore(dbContext),
            new RequestSubmissionService(
                validator,
                requestContext,
                workflowStore),
            clock);
    }

    private static MafRequestPreparationInterpreter CreateInterpreter(
        IChatClient chatClient) =>
        new(
            chatClient,
            Options.Create(
                new TeamsAccessRequestOptions
                {
                }),
            NullLoggerFactory.Instance,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

    private static PrepareAccessRequestCommand CreateCommand(string message) =>
        new(
            new AuthenticatedChannelActor(
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoDataIds.RequesterPrincipalId),
            message,
            Guid.NewGuid().ToString("N"));

    private static RequestPreparationTurn CreateTurn(
        Guid intakeId,
        string message) =>
        new(
            intakeId,
            message,
            new RequestCandidate(null, null, null, null, null),
            Guid.NewGuid().ToString("N"));

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
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(environmentId, content.GetProperty("environmentId").GetString());
        return content
            .GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetProperty("roleId").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task AssertNoWorkflowStateAsync(
        GovernedAccessDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Assert.Empty(await dbContext.AccessRequests
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ApprovalDecisions
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants
            .AsNoTracking()
            .ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AuditEvents
            .AsNoTracking()
            .ToListAsync(cancellationToken));
    }
}

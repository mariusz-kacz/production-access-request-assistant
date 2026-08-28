using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class TargetFullHostCompositionTests
{
    [Fact]
    public async Task IsolatedHostResolvesOnlyTheCompleteTargetGraph()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetFullHostFixture.CreateAsync(
            cancellationToken: cancellationToken);
        await using var scope = fixture.Services.CreateAsyncScope();
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

        var workflowMigrations = await services
            .GetRequiredService<WorkflowDbContext>()
            .Database.GetAppliedMigrationsAsync(cancellationToken);
        var referenceMigrations = await services
            .GetRequiredService<ReferenceAuthorityDbContext>()
            .Database.GetAppliedMigrationsAsync(cancellationToken);
        Assert.NotEmpty(workflowMigrations);
        Assert.NotEmpty(referenceMigrations);
        Assert.Empty(workflowMigrations.Intersect(
            referenceMigrations,
            StringComparer.Ordinal));
        Assert.NotEqual(
            Path.GetFullPath(fixture.WorkflowDatabasePath),
            Path.GetFullPath(fixture.ReferenceDatabasePath));

        await using var mcpClient = await fixture.CreateMcpClientAsync(
            "target-full-host-composition",
            cancellationToken);
        var tools = await mcpClient.ListToolsAsync(
            cancellationToken: cancellationToken);
        Assert.Equal(
            [
                "get_environment_roles",
                "get_incident",
                "get_production_environment",
                "search_production_environments",
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }
}

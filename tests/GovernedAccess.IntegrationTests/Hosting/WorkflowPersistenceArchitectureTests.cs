using System.Runtime.CompilerServices;
using System.Xml.Linq;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class WorkflowPersistenceArchitectureTests
{
    [Fact]
    public void WorkflowPersistenceSourceBoundariesAreClosed()
    {
        AssertProjectReferencesOnlyCore();
        AssertDbContextIsAbsentFromOtherModules();
    }

    [Fact]
    public async Task WorkflowPersistenceCompositionIsModuleOwnedAndIsolated()
    {
        await AssertProductionHostCompositionAsync();
        await AssertCombinedIsolatedCompositionAsync();
        await AssertSeparateFixturesAsync();
    }

    private static void AssertProjectReferencesOnlyCore()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "GovernedAccess.Workflow.Persistence",
            "GovernedAccess.Workflow.Persistence.csproj");
        var project = XDocument.Load(projectPath);
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        Assert.Equal(
            [@"..\GovernedAccess.Core\GovernedAccess.Core.csproj"],
            projectReferences);
    }

    private static void AssertDbContextIsAbsentFromOtherModules()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var workflowRoot = Path.Combine(
            sourceRoot,
            "GovernedAccess.Workflow.Persistence") + Path.DirectorySeparatorChar;
        var outsideSourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(workflowRoot, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

        foreach (var sourceFile in outsideSourceFiles)
        {
            Assert.DoesNotContain(
                nameof(WorkflowDbContext),
                File.ReadAllText(sourceFile),
                StringComparison.Ordinal);
        }
    }

    private static async Task AssertProductionHostCompositionAsync()
    {
        await using var fixture = new DefaultWebApplicationFixture();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetService<WorkflowDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<IRequestPreparationStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<IAuthenticatedPrincipalReader>());
        var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        Assert.Equal(
            typeof(WorkflowDbContext).Assembly,
            workflowStore.GetType().Assembly);
    }

    private static async Task AssertCombinedIsolatedCompositionAsync()
    {
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var workflowContext = scope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        var referenceContext = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        var workflowMigrations = await workflowContext.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        var referenceMigrations = await referenceContext.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            typeof(WorkflowDbContext).Assembly,
            workflowStore.GetType().Assembly);
        Assert.NotNull(scope.ServiceProvider.GetService<WorkflowDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetService<ReferenceAuthorityDbContext>());
        Assert.NotEqual(
            Path.GetFullPath(fixture.ReferenceDatabasePath),
            Path.GetFullPath(fixture.WorkflowDatabasePath));
        Assert.EndsWith(
            "_InitialWorkflowPersistence",
            Assert.Single(workflowMigrations));
        Assert.Equal(
            ["20260825072917_InitialReferenceAuthority"],
            referenceMigrations);
        Assert.Empty(workflowMigrations.Intersect(
            referenceMigrations,
            StringComparer.Ordinal));
    }

    private static async Task AssertSeparateFixturesAsync()
    {
        await using var workflow = await WorkflowPersistenceFixture.CreateAsync();
        await using var reference = await ReferenceAuthorityFixture.CreateAsync();
        await using var workflowScope = workflow.Services.CreateAsyncScope();
        await using var referenceScope = reference.Services.CreateAsyncScope();
        var workflowContext = workflowScope.ServiceProvider
            .GetRequiredService<WorkflowDbContext>();
        var referenceContext = referenceScope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        var workflowDataSource = new SqliteConnectionStringBuilder(
            workflowContext.Database.GetConnectionString()).DataSource;
        var referenceDataSource = new SqliteConnectionStringBuilder(
            referenceContext.Database.GetConnectionString()).DataSource;

        Assert.Equal(
            Path.GetFullPath(workflow.DatabasePath),
            Path.GetFullPath(workflowDataSource));
        Assert.Equal(
            Path.GetFullPath(reference.DatabasePath),
            Path.GetFullPath(referenceDataSource));
        Assert.NotEqual(
            Path.GetFullPath(workflowDataSource),
            Path.GetFullPath(referenceDataSource));
        Assert.Null(workflowScope.ServiceProvider.GetService<ReferenceAuthorityDbContext>());
        Assert.Null(referenceScope.ServiceProvider.GetService<WorkflowDbContext>());
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(sourceFilePath)
                    ?? throw new InvalidOperationException(
                        "The architecture-test source path is unavailable."),
                "..",
                "..",
                ".."));
}

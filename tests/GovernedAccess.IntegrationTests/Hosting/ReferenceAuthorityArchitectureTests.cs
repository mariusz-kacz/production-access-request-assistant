using System.Runtime.CompilerServices;
using System.Xml.Linq;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ReferenceAuthorityArchitectureTests
{
    [Fact]
    public void ReferenceAuthorityProjectReferencesOnlyCoreProject()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "GovernedAccess.ReferenceAuthority",
            "GovernedAccess.ReferenceAuthority.csproj");
        var project = XDocument.Load(projectPath);
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        Assert.Equal(
            ["..\\GovernedAccess.Core\\GovernedAccess.Core.csproj"],
            projectReferences);
    }

    [Fact]
    public void ReferenceDbContextIsAbsentFromEveryOtherSourceModule()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var authorityRoot = Path.Combine(
            sourceRoot,
            "GovernedAccess.ReferenceAuthority") + Path.DirectorySeparatorChar;
        var outsideSourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(authorityRoot, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

        foreach (var sourceFile in outsideSourceFiles)
        {
            Assert.DoesNotContain(
                nameof(ReferenceAuthorityDbContext),
                File.ReadAllText(sourceFile),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void McpSourceDoesNotExposeTheHiddenBusinessApproverFact()
    {
        var repositoryRoot = GetRepositoryRoot();
        var mcpSourceRoot = Path.Combine(
            repositoryRoot,
            "src",
            "GovernedAccess.Mcp");

        foreach (var sourceFile in Directory.EnumerateFiles(
                     mcpSourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain(
                nameof(GovernedAccess.Core.Preparations.Authority.EnvironmentAuthorityProjection
                    .BusinessApproverPrincipalId),
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "businessApproverPrincipalId",
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OrdinaryProductionHostStillResolvesOnlyDeliveredPersistence()
    {
        await using var fixture = new DefaultWebApplicationFixture();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetService<GovernedAccessDbContext>());
        Assert.Null(scope.ServiceProvider.GetService<ReferenceAuthorityDbContext>());
        Assert.Null(
            scope.ServiceProvider.GetService<IProductionEnvironmentSearchAuthority>());
        Assert.Null(scope.ServiceProvider.GetService<IProductionEnvironmentAuthority>());
        Assert.Null(scope.ServiceProvider.GetService<IEnvironmentRoleAuthority>());
        Assert.Null(scope.ServiceProvider.GetService<IIncidentAuthority>());
    }

    [Fact]
    public async Task IsolatedTargetFixtureResolvesOnlyItsReferenceDatabase()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "The reference context has no connection string.");
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

        Assert.Equal(
            Path.GetFullPath(fixture.DatabasePath),
            Path.GetFullPath(dataSource));
        Assert.Null(scope.ServiceProvider.GetService<GovernedAccessDbContext>());
        Assert.NotNull(
            scope.ServiceProvider.GetService<IProductionEnvironmentSearchAuthority>());
        Assert.NotNull(scope.ServiceProvider.GetService<IProductionEnvironmentAuthority>());
        Assert.NotNull(scope.ServiceProvider.GetService<IEnvironmentRoleAuthority>());
        Assert.NotNull(scope.ServiceProvider.GetService<IIncidentAuthority>());
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

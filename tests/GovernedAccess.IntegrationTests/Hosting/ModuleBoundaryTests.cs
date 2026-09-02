using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class ModuleBoundaryTests
{
    [Theory]
    [InlineData("GovernedAccess.ReferenceAuthority")]
    [InlineData("GovernedAccess.Workflow.Persistence")]
    public void InfrastructureModulesDependOnlyOnCore(string projectName)
    {
        var projectPath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            projectName,
            $"{projectName}.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        var reference = Assert.Single(references);
        Assert.EndsWith(
            "GovernedAccess.Core.csproj",
            reference,
            StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(sourceFilePath)
                    ?? throw new InvalidOperationException(
                        "The test source path is unavailable."),
                "..",
                "..",
                ".."));
}

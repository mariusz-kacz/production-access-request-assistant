using System.Runtime.CompilerServices;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class TargetRequestPreparationArchitectureTests
{
    [Fact]
    public void ProductionSourceContainsNoClarificationSelectionProtocol()
    {
        var sourceRoot = Path.Combine(GetRepositoryRoot(), "src");
        string[] forbiddenTerms =
        [
            string.Concat("select", "Clarification"),
            string.Concat("Select", "Clarification"),
            string.Concat("Clarification", "Selection"),
            string.Concat("option", "Index"),
            string.Concat("Option", "Index"),
        ];

        foreach (var sourceFile in Directory
                     .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(
                         $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain(
                forbiddenTerms,
                term => source.Contains(term, StringComparison.Ordinal));
        }
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

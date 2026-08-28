using System.Runtime.CompilerServices;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class SimplificationArchitectureTests
{
    private static readonly string[] RetiredMechanismNames =
    [
        "BudgetExhaustedGuidance",
        "CandidateVersion",
        "ClarificationSelection",
        "CollectingStaleWarning",
        "IncidentEnvironmentLinks",
        "InterpretedTurnCount",
        "JustificationProvenance",
        "MaximumInterpretedTurns",
        "NarrowQuery",
        "OperationResult",
        "OperationResultKind",
        "PatchEvaluation",
        "ReferenceIncidentEnvironmentLink",
        "candidateVersion",
        "interpretedTurnCount",
        "maximumInterpretedTurns",
        "requesterAuthoredNormalized",
        "requester_authored_normalized",
        "selectClarification",
    ];

    [Fact]
    public void SourceAndTestsContainNoRetiredTargetMechanisms()
    {
        var repositoryRoot = GetRepositoryRoot();
        var guardSourcePath = GetCurrentSourcePath();
        var findings = new List<string>();

        foreach (var scanRoot in new[]
                 {
                     Path.Combine(repositoryRoot, "src"),
                     Path.Combine(repositoryRoot, "tests"),
                 })
        {
            foreach (var sourceFile in Directory.EnumerateFiles(
                         scanRoot,
                         "*",
                         SearchOption.AllDirectories).Where(path =>
                         IsScannableSource(path)
                         && !Path.GetFullPath(path).Equals(
                             guardSourcePath,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var source = File.ReadAllText(sourceFile);
                foreach (var retiredName in RetiredMechanismNames)
                {
                    if (source.Contains(retiredName, StringComparison.Ordinal))
                    {
                        findings.Add(
                            $"{Path.GetRelativePath(repositoryRoot, sourceFile)}: {retiredName}");
                    }
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void TeamsNamespaceContainsNoMigrationTargetNames()
    {
        var migrationTypeNames = typeof(TeamsAgentRegistration)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(TeamsAgentRegistration).Namespace)
            .Select(type => type.Name)
            .Where(name => name.StartsWith("Target", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Empty(migrationTypeNames);

        var teamsSourceDirectory = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "GovernedAccess.Web",
            "Teams");
        var migrationFileNames = Directory
            .EnumerateFiles(teamsSourceDirectory, "Target*.cs")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        Assert.Empty(migrationFileNames);
    }

    private static bool IsScannableSource(string path)
    {
        var relativeSegments = Path.GetRelativePath(GetRepositoryRoot(), path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            && !relativeSegments.Any(segment => segment is
                "bin" or "node_modules" or "obj");
    }

    private static string GetCurrentSourcePath(
        [CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(sourceFilePath);

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

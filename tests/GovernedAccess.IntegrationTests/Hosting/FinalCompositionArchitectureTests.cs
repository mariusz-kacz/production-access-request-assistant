using System.Runtime.CompilerServices;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed class FinalCompositionArchitectureTests
{
    private static readonly string[] DeliveredFiles =
    [
        "src/GovernedAccess.Core/Application/Drafts/RequestDraftService.cs",
        "src/GovernedAccess.Core/Application/Drafts/RequestDraftValidator.cs",
        "src/GovernedAccess.Core/Application/AccessRequests/RequestSubmissionService.cs",
        "src/GovernedAccess.Core/Domain/Drafts/RequestIntakeSession.cs",
        "src/GovernedAccess.Core/Ports/RequestDrafting.cs",
        "src/GovernedAccess.Core/Ports/RequestIntake.cs",
        "src/GovernedAccess.Mcp/McpRegistration.cs",
        "src/GovernedAccess.Mcp/RequestContextTools.cs",
        "src/GovernedAccess.Web/Ai/MafConversationTurnCoordinator.cs",
        "src/GovernedAccess.Web/Ai/MafRequestPreparationInterpreter.cs",
        "src/GovernedAccess.Web/Ai/RequestPreparationMcpEndpoint.cs",
        "src/GovernedAccess.Web/Ai/RequestPreparationRegistration.cs",
        "src/GovernedAccess.Web/Persistence/EfRequestContextReader.cs",
        "src/GovernedAccess.Web/Persistence/EfRequestIntakeStore.cs",
        "src/GovernedAccess.Web/Persistence/EfWorkflowStore.cs",
        "src/GovernedAccess.Web/Persistence/GovernedAccessDbContext.cs",
        "src/GovernedAccess.Web/Persistence/SyntheticDataSeeder.cs",
    ];

    [Fact]
    public void DeliveredImplementationIsFullyRemoved()
    {
        AssertDeliveredFilesAreDeleted();
        AssertSourceContainsNoDeliveredConcepts();
    }

    private static void AssertDeliveredFilesAreDeleted()
    {
        var repositoryRoot = GetRepositoryRoot();

        Assert.All(
            DeliveredFiles,
            relativePath => Assert.False(
                File.Exists(Path.Combine(
                    repositoryRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar))),
                $"Delivered file remains: {relativePath}"));
    }

    private static void AssertSourceContainsNoDeliveredConcepts()
    {
        var repositoryRoot = GetRepositoryRoot();
        var currentSource = GetCurrentSourcePath();
        string[] forbidden =
        [
            "RequestIntakeSession",
            "RequestDraftService",
            "RequestSubmissionService",
            "IRequestIntakeStore",
            "IRequestPreparationInterpreter",
            "MafRequestPreparationInterpreter",
            "GovernedAccessDbContext",
            "preparedRequestId",
            "ReservedRequestId",
            "RequestIntakeStatus",
        ];
        var findings = new List<string>();

        foreach (var root in new[] { Path.Combine(repositoryRoot, "src") })
        {
            foreach (var file in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories).Where(IsScannable))
            {
                if (Path.GetFullPath(file).Equals(
                        currentSource,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                foreach (var name in forbidden)
                {
                    if (source.Contains(name, StringComparison.Ordinal))
                    {
                        findings.Add(
                            $"{Path.GetRelativePath(repositoryRoot, file)}: {name}");
                    }
                }
            }
        }

        Assert.Empty(findings);
    }

    private static bool IsScannable(string path)
    {
        var segments = path.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            && !segments.Any(segment => segment is "bin" or "node_modules" or "obj");
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

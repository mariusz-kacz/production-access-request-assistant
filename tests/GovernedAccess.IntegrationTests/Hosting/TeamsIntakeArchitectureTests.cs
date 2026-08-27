using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace GovernedAccess.IntegrationTests.Hosting;

public sealed partial class TeamsIntakeArchitectureTests
{
    private static readonly string[] SharedTeamsFiles =
    [
        "TeamsActivityContext.cs",
        "TeamsActivityPresenter.cs",
        "TeamsActorResolver.cs",
        "TeamsAdaptiveCardRenderer.cs",
        "TeamsDraftCardTracker.cs",
        "TeamsPresentationModels.cs",
    ];

    private static readonly string[] TargetTeamsFiles =
    [
        "TargetPreparedRequestCardFactory.cs",
        "TargetTeamsAccessRequestAdapter.cs",
        "TargetTeamsResponseRenderer.cs",
    ];

    [Fact]
    public void SharedTeamsPrimitivesReferenceNeitherPreparationGraph()
    {
        var teamsDirectory = GetTeamsDirectory();
        var findings = new List<string>();

        foreach (var fileName in SharedTeamsFiles)
        {
            var source = File.ReadAllText(Path.Combine(teamsDirectory, fileName));
            foreach (var forbidden in new[]
                     {
                         "GovernedAccess.Core",
                         "RequestIntakeSession",
                         "PreparationSnapshot",
                         "PreparationBinding",
                     })
            {
                if (source.Contains(forbidden, StringComparison.Ordinal))
                {
                    findings.Add($"{fileName}: {forbidden}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void TargetTeamsComponentsHaveNoDeliveredIntakeDependency()
    {
        var teamsDirectory = GetTeamsDirectory();
        var findings = new List<string>();
        var forbiddenType = DeliveredTypeRegex();

        foreach (var fileName in TargetTeamsFiles)
        {
            var source = File.ReadAllText(Path.Combine(teamsDirectory, fileName));
            foreach (Match match in forbiddenType.Matches(source))
            {
                findings.Add($"{fileName}: {match.Value}");
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void SourceContainsOneCardLayoutAndNoLegacyPayloadAlias()
    {
        var teamsDirectory = GetTeamsDirectory();
        var sources = Directory.EnumerateFiles(
                teamsDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(
            sources,
            source => source.Contains(
                "preparedRequestId",
                StringComparison.Ordinal));
        Assert.Equal(
            1,
            sources.Sum(source => Regex.Count(
                source,
                "adaptivecards\\.io/schemas/adaptive-card\\.json",
                RegexOptions.CultureInvariant)));
    }

    private static string GetTeamsDirectory() =>
        Path.Combine(GetRepositoryRoot(), "src", "GovernedAccess.Web", "Teams");

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

    [GeneratedRegex(
        "\\b(RequestIntakeSession|RequestDraftService|RequestSubmissionService|AuthenticatedChannelActor|IRequestContextReader)\\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DeliveredTypeRegex();
}

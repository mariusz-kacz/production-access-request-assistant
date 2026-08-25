using System.Globalization;
using GovernedAccess.Core.Preparations.Authority;

namespace GovernedAccess.UnitTests;

public sealed class EnvironmentSearchPolicyTests
{
    [Fact]
    public void PolicyPublishesTheApprovedVersion()
    {
        Assert.Equal("1.0.0", EnvironmentSearchPolicy.Version);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("---")]
    public void SearchRejectsQueriesWithoutTokens(string query)
    {
        var result = EnvironmentSearchPolicy.Search(query, [CreateDocument(1)]);

        Assert.Equal(EnvironmentSearchResultKind.InvalidQuery, result.Kind);
        Assert.Equal("environment_query_invalid", result.FailureCode);
        Assert.Equal(0, result.MatchCount);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void SearchRejectsQueriesOverTheNormalizedLengthLimit()
    {
        var result = EnvironmentSearchPolicy.Search(
            new string('q', EnvironmentSearchPolicy.MaximumQueryLength + 1),
            [CreateDocument(1)]);

        Assert.Equal(EnvironmentSearchResultKind.InvalidQuery, result.Kind);
        Assert.Equal("environment_query_invalid", result.FailureCode);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void SearchCollapsesUnicodeWhitespaceBeforeCheckingLength()
    {
        var query = $"alpha{new string('\u2003', 300)}eu";
        var result = EnvironmentSearchPolicy.Search(query, [CreateDocument(1)]);

        Assert.Equal(EnvironmentSearchResultKind.UniqueMatch, result.Kind);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void SearchNormalizesUnicodeAndMatchesPunctuationSeparatedTokensAcrossFields()
    {
        var document = new EnvironmentSearchDocument(
            "PROD-CAFÉ-EU",
            "Café Payments Production",
            "client-alpha",
            "Client Alpha",
            "EU",
            EnvironmentClassification.Primary,
            isActive: true,
            isProduction: true,
            isEligibleForIntake: true);

        var result = EnvironmentSearchPolicy.Search(
            "  cafe\u0301 / eu...PRIMARY  ",
            [document]);

        Assert.Equal(EnvironmentSearchResultKind.UniqueMatch, result.Kind);
        var match = Assert.Single(result.Matches);
        Assert.Equal("PROD-CAFÉ-EU", match.EnvironmentId);
        Assert.Equal("client-alpha", match.ClientId);
    }

    [Fact]
    public void SearchUsesLocaleInvariantCaseInsensitiveMatching()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var document = new EnvironmentSearchDocument(
                "PROD-ISTANBUL",
                "I Production",
                "client-alpha",
                "Client Alpha",
                "EU",
                EnvironmentClassification.Primary,
                isActive: true,
                isProduction: true,
                isEligibleForIntake: true);

            var result = EnvironmentSearchPolicy.Search("i", [document]);

            Assert.Equal(EnvironmentSearchResultKind.UniqueMatch, result.Kind);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void SearchExcludesEveryIneligibleEnvironment(
        bool isActive,
        bool isProduction,
        bool isEligibleForIntake)
    {
        var result = EnvironmentSearchPolicy.Search(
            "alpha",
            [CreateDocument(1, isActive, isProduction, isEligibleForIntake)]);

        Assert.Equal(EnvironmentSearchResultKind.NoMatches, result.Kind);
        Assert.Equal(0, result.MatchCount);
        Assert.Empty(result.Matches);
    }

    [Theory]
    [InlineData(0, EnvironmentSearchResultKind.NoMatches, true)]
    [InlineData(1, EnvironmentSearchResultKind.UniqueMatch, true)]
    [InlineData(2, EnvironmentSearchResultKind.ClarificationRequired, true)]
    [InlineData(5, EnvironmentSearchResultKind.ClarificationRequired, true)]
    [InlineData(6, EnvironmentSearchResultKind.NarrowQuery, true)]
    [InlineData(20, EnvironmentSearchResultKind.NarrowQuery, true)]
    [InlineData(21, EnvironmentSearchResultKind.TooBroad, false)]
    public void SearchClassifiesEveryNormativeCardinality(
        int count,
        EnvironmentSearchResultKind expectedKind,
        bool exposesCompleteResults)
    {
        var documents = Enumerable.Range(1, count)
            .Select(index => CreateDocument(index))
            .ToArray();

        var result = EnvironmentSearchPolicy.Search("alpha", documents);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(count, result.MatchCount);
        Assert.Equal(exposesCompleteResults ? count : 0, result.Matches.Count);
        Assert.Equal(
            count > EnvironmentSearchPolicy.MaximumResultCount
                ? "environment_query_too_broad"
                : null,
            result.FailureCode);
    }

    [Fact]
    public void SearchOrdersOnlyByCanonicalEnvironmentIdUsingOrdinalComparison()
    {
        EnvironmentSearchDocument[] documents =
        [
            CreateDocument("ENV-a"),
            CreateDocument("ENV-Z"),
            CreateDocument("ENV-A"),
        ];

        var result = EnvironmentSearchPolicy.Search("alpha", documents);

        Assert.Equal(EnvironmentSearchResultKind.ClarificationRequired, result.Kind);
        Assert.Equal(
            ["ENV-A", "ENV-Z", "ENV-a"],
            result.Matches.Select(match => match.EnvironmentId));
    }

    [Fact]
    public void SearchDoesNotRankExactOrEarlierFieldMatches()
    {
        EnvironmentSearchDocument[] documents =
        [
            CreateDocument("ZZZ-ALPHA"),
            new EnvironmentSearchDocument(
                "AAA-EXACT",
                "Alpha exact display",
                "client-alpha",
                "Client Alpha",
                "EU",
                EnvironmentClassification.Primary,
                isActive: true,
                isProduction: true,
                isEligibleForIntake: true),
        ];

        var result = EnvironmentSearchPolicy.Search("alpha", documents);

        Assert.Equal(
            ["AAA-EXACT", "ZZZ-ALPHA"],
            result.Matches.Select(match => match.EnvironmentId));
    }

    [Fact]
    public void SearchReturnsImmutableTransportSafeMatchesWithoutSearchFacts()
    {
        var result = EnvironmentSearchPolicy.Search("alpha", [CreateDocument(1)]);
        var match = Assert.Single(result.Matches);

        Assert.Equal(
            ["ClientDisplayName", "ClientId", "DisplayName", "EnvironmentId"],
            match.GetType()
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Throws<NotSupportedException>(
            () => ((ICollection<EnvironmentSearchMatch>)result.Matches)
                .Add(match));
    }

    [Fact]
    public void SearchRejectsDuplicateAuthorityDocuments()
    {
        var document = CreateDocument(1);

        Assert.Throws<ArgumentException>(
            () => EnvironmentSearchPolicy.Search("alpha", [document, document]));
    }

    private static EnvironmentSearchDocument CreateDocument(
        int index,
        bool isActive = true,
        bool isProduction = true,
        bool isEligibleForIntake = true) =>
        CreateDocument(
            $"PROD-ALPHA-{index:D2}",
            isActive,
            isProduction,
            isEligibleForIntake);

    private static EnvironmentSearchDocument CreateDocument(
        string environmentId,
        bool isActive = true,
        bool isProduction = true,
        bool isEligibleForIntake = true) =>
        new(
            environmentId,
            $"Client Alpha {environmentId} Production",
            "client-alpha",
            "Client Alpha",
            "EU",
            EnvironmentClassification.Primary,
            isActive,
            isProduction,
            isEligibleForIntake);
}

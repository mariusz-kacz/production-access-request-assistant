namespace GovernedAccess.Core.Preparations.Authority;

public enum EnvironmentSearchResultKind
{
    InvalidQuery,
    NoMatches,
    UniqueMatch,
    ClarificationRequired,
    NarrowQuery,
    TooBroad,
}

public sealed record EnvironmentSearchMatch
{
    public EnvironmentSearchMatch(
        string environmentId,
        string displayName,
        string clientId,
        string clientDisplayName)
    {
        EnvironmentId = AuthorityValue.Normalize(environmentId, nameof(environmentId));
        DisplayName = AuthorityValue.Normalize(displayName, nameof(displayName));
        ClientId = AuthorityValue.Normalize(clientId, nameof(clientId));
        ClientDisplayName = AuthorityValue.Normalize(
            clientDisplayName,
            nameof(clientDisplayName));
    }

    public string EnvironmentId { get; }

    public string DisplayName { get; }

    public string ClientId { get; }

    public string ClientDisplayName { get; }
}

public sealed class EnvironmentSearchResult
{
    private EnvironmentSearchResult(
        EnvironmentSearchResultKind kind,
        int matchCount,
        IReadOnlyList<EnvironmentSearchMatch> matches,
        string? failureCode)
    {
        Kind = kind;
        MatchCount = matchCount;
        Matches = matches;
        FailureCode = failureCode;
    }

    public EnvironmentSearchResultKind Kind { get; }

    public int MatchCount { get; }

    public IReadOnlyList<EnvironmentSearchMatch> Matches { get; }

    public string? FailureCode { get; }

    internal static EnvironmentSearchResult InvalidQuery() =>
        new(
            EnvironmentSearchResultKind.InvalidQuery,
            matchCount: 0,
            Array.Empty<EnvironmentSearchMatch>(),
            "environment_query_invalid");

    internal static EnvironmentSearchResult FromMatches(
        IEnumerable<EnvironmentSearchMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var snapshot = matches.ToArray();
        var kind = snapshot.Length switch
        {
            0 => EnvironmentSearchResultKind.NoMatches,
            1 => EnvironmentSearchResultKind.UniqueMatch,
            <= 5 => EnvironmentSearchResultKind.ClarificationRequired,
            <= EnvironmentSearchPolicy.MaximumResultCount =>
                EnvironmentSearchResultKind.NarrowQuery,
            _ => EnvironmentSearchResultKind.TooBroad,
        };

        return kind == EnvironmentSearchResultKind.TooBroad
            ? new(
                kind,
                snapshot.Length,
                Array.Empty<EnvironmentSearchMatch>(),
                "environment_query_too_broad")
            : new(
                kind,
                snapshot.Length,
                Array.AsReadOnly(snapshot),
                failureCode: null);
    }
}

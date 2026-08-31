using System.Globalization;
using System.Text;

namespace GovernedAccess.Core.Preparations.Authority;

public static class EnvironmentSearchPolicy
{
    public const string Version = "2.0.0";

    public const int MaximumQueryLength = 200;

    public const int MaximumResultCount = 5;

    public static EnvironmentSearchResult Search(
        string query,
        IEnumerable<EnvironmentSearchDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (!TryNormalizeQuery(query, out var normalizedQuery))
        {
            return EnvironmentSearchResult.InvalidQuery();
        }

        var tokens = Tokenize(normalizedQuery);
        if (tokens.Count == 0)
        {
            return EnvironmentSearchResult.InvalidQuery();
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<EnvironmentSearchMatch>();
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (!identifiers.Add(document.EnvironmentId))
            {
                throw new ArgumentException(
                    "Environment search documents must have unique canonical identifiers.",
                    nameof(documents));
            }

            if (!document.CanBecomeCanonical || !Matches(document, tokens))
            {
                continue;
            }

            matches.Add(
                new EnvironmentSearchMatch(
                    document.EnvironmentId,
                    document.DisplayName,
                    document.ClientId,
                    document.ClientDisplayName,
                    document.Region,
                    document.Classification));
        }

        matches.Sort(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.EnvironmentId,
                right.EnvironmentId));
        return EnvironmentSearchResult.FromMatches(matches);
    }

    public static bool AreQueriesEquivalent(string? left, string? right)
    {
        if (!TryNormalizeQuery(left, out var normalizedLeft)
            || !TryNormalizeQuery(right, out var normalizedRight))
        {
            return false;
        }

        var leftTokens = Tokenize(normalizedLeft);
        var rightTokens = Tokenize(normalizedRight);
        return leftTokens.Count > 0
            && rightTokens.Count > 0
            && new HashSet<string>(
                leftTokens,
                StringComparer.OrdinalIgnoreCase).SetEquals(rightTokens);
    }

    private static bool TryNormalizeQuery(
        string? query,
        out string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            normalizedQuery = string.Empty;
            return false;
        }

        try
        {
            normalizedQuery = CollapseWhitespace(
                query.Normalize(NormalizationForm.FormC));
        }
        catch (ArgumentException)
        {
            normalizedQuery = string.Empty;
            return false;
        }

        return normalizedQuery.Length is > 0 and <= MaximumQueryLength;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var whitespacePending = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                whitespacePending = builder.Length > 0;
                continue;
            }

            if (whitespacePending)
            {
                builder.Append(' ');
                whitespacePending = false;
            }

            builder.Append(rune);
        }

        return builder.ToString();
    }

    private static List<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        foreach (var rune in query.EnumerateRunes())
        {
            if (IsTokenSeparator(rune))
            {
                AddToken(tokens, token);
            }
            else
            {
                token.Append(rune);
            }
        }

        AddToken(tokens, token);
        return tokens;
    }

    private static bool IsTokenSeparator(Rune rune) =>
        Rune.IsWhiteSpace(rune)
        || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;

    private static void AddToken(
        List<string> tokens,
        StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        tokens.Add(token.ToString());
        token.Clear();
    }

    private static bool Matches(
        EnvironmentSearchDocument document,
        List<string> tokens)
    {
        string[] fields =
        [
            document.EnvironmentId.Normalize(NormalizationForm.FormC),
            document.DisplayName.Normalize(NormalizationForm.FormC),
            document.ClientId.Normalize(NormalizationForm.FormC),
            document.ClientDisplayName.Normalize(NormalizationForm.FormC),
            document.Region.Normalize(NormalizationForm.FormC),
            document.Classification switch
            {
                EnvironmentClassification.Primary => "primary",
                EnvironmentClassification.Recovery => "recovery",
                _ => throw new InvalidOperationException(
                    "The environment classification is invalid."),
            },
        ];

        return tokens.All(
            token => fields.Any(
                field => field.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase)));
    }
}

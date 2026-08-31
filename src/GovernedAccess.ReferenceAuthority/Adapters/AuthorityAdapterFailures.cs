using GovernedAccess.Core.Application;

namespace GovernedAccess.ReferenceAuthority.Adapters;

internal static class AuthorityAdapterFailures
{
    internal static ApplicationResult<T> InvalidInput<T>(
        string code,
        string message)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(ApplicationFailureKind.InvalidInput, code, message));

    internal static ApplicationResult<T> NotFound<T>(string code, string message)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(ApplicationFailureKind.NotFound, code, message));

    internal static ApplicationResult<T> Cancelled<T>(string source)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.Cancelled,
                $"{source}-cancelled",
                "The authoritative reference read was cancelled."));

    internal static ApplicationResult<T> Unavailable<T>(string source)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                $"{source}-unavailable",
                "The authoritative reference source is currently unavailable."));

    internal static ApplicationResult<T> Malformed<T>(string source)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                $"{source}-malformed",
                "The authoritative reference source returned inconsistent data."));

    internal static bool TryNormalizeIdentifier(string value, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = value.Trim();
        return normalized.Length <= 200;
    }
}

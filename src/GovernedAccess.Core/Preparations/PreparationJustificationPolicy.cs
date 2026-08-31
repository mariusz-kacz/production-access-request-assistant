using System.Text;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal static class PreparationJustificationPolicy
{
    internal static JustificationApplicationResult Evaluate(
        string? currentJustification,
        JustificationOperation? operation)
    {
        if (operation is null)
        {
            return new JustificationApplicationResult(
                currentJustification,
                Result: null);
        }

        if (operation is ClearJustificationOperation)
        {
            var kind = currentJustification is null
                ? ApplicationGroupResultKind.NoOp
                : ApplicationGroupResultKind.Applied;
            return new JustificationApplicationResult(
                Justification: null,
                new ApplicationGroupResult(kind));
        }

        if (operation is not SetJustificationOperation set
            || !TryNormalize(set.Value.Text, out var normalized))
        {
            return new JustificationApplicationResult(
                currentJustification,
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.Rejected,
                    ApplicationGroupRejectionReason.Invalid));
        }

        var resultKind = string.Equals(
            currentJustification,
            normalized,
            StringComparison.Ordinal)
            ? ApplicationGroupResultKind.NoOp
            : ApplicationGroupResultKind.Applied;
        return new JustificationApplicationResult(
            normalized,
            new ApplicationGroupResult(resultKind));
    }

    private static bool TryNormalize(
        string value,
        out string normalized)
    {
        try
        {
            normalized = value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Normalize(NormalizationForm.FormC)
                .Trim();
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }

        return normalized.Length is > 0 and <= PreparationCandidate.MaximumJustificationLength;
    }
}

internal sealed record JustificationApplicationResult(
    string? Justification,
    ApplicationGroupResult? Result);

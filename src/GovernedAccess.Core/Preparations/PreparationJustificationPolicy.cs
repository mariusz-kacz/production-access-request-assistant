using System.Text;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal static class PreparationJustificationPolicy
{
    internal static void ApplyRequested(
        PatchEvaluation evaluation,
        JustificationOperation? operation)
    {
        if (operation is null)
        {
            return;
        }

        if (operation is ClearJustificationOperation)
        {
            var kind = evaluation.Justification is null
                ? OperationResultKind.NoOpValueEqual
                : OperationResultKind.Applied;
            evaluation.Justification = null;
            evaluation.Record(ProposalField.Justification, kind);
            return;
        }

        if (operation is not SetJustificationOperation set
            || !TryNormalize(set.Value.Text, out var normalized))
        {
            evaluation.Record(
                ProposalField.Justification,
                OperationResultKind.RejectedInvalid);
            return;
        }

        var resultKind = string.Equals(
            evaluation.Justification,
            normalized,
            StringComparison.Ordinal)
            ? OperationResultKind.NoOpValueEqual
            : OperationResultKind.Applied;
        evaluation.Justification = normalized;
        evaluation.Record(ProposalField.Justification, resultKind);
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

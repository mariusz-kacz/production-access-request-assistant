using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public enum ClarificationContextDisposition
{
    Preserve,
    Clear,
    Replace,
}

public sealed class RequestPreparationReduction
{
    internal RequestPreparationReduction(
        PreparationCandidate candidate,
        ClarificationContextDisposition clarificationDisposition,
        ClarificationSeed? clarification,
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult,
        IEnumerable<ProposalField> changedFields,
        ApplicationOutcome outcome,
        SoleRoleSelection? soleRoleSelection)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(changedFields);
        ArgumentNullException.ThrowIfNull(outcome);
        if (!Enum.IsDefined(clarificationDisposition))
        {
            throw new ArgumentOutOfRangeException(nameof(clarificationDisposition));
        }

        if ((clarificationDisposition == ClarificationContextDisposition.Replace)
            != (clarification is not null))
        {
            throw new ArgumentException(
                "Replacement clarification disposition requires exactly one clarification.",
                nameof(clarification));
        }

        var changedFieldArray = changedFields.ToArray();
        if (changedFieldArray.Any(field => !Enum.IsDefined(field))
            || changedFieldArray.Distinct().Count() != changedFieldArray.Length)
        {
            throw new ArgumentException(
                "Changed field categories must be valid and unique.",
                nameof(changedFields));
        }

        if (soleRoleSelection is not null
            && (!string.Equals(
                    candidate.RoleId,
                    soleRoleSelection.RoleId,
                    StringComparison.Ordinal)
                || !changedFieldArray.Contains(ProposalField.Role)))
        {
            throw new ArgumentException(
                "A sole role selection must match a changed canonical role.",
                nameof(soleRoleSelection));
        }

        Candidate = candidate;
        ClarificationDisposition = clarificationDisposition;
        Clarification = clarification;
        ScopeResult = scopeResult;
        JustificationResult = justificationResult;
        ChangedFields = Array.AsReadOnly(changedFieldArray);
        Outcome = outcome;
        SoleRoleSelection = soleRoleSelection;
    }

    public PreparationCandidate Candidate { get; }

    public ClarificationContextDisposition ClarificationDisposition { get; }

    public ClarificationSeed? Clarification { get; }

    public ApplicationGroupResult? ScopeResult { get; }

    public ApplicationGroupResult? JustificationResult { get; }

    public IReadOnlyList<ProposalField> ChangedFields { get; }

    public ApplicationOutcome Outcome { get; }

    public SoleRoleSelection? SoleRoleSelection { get; }
}

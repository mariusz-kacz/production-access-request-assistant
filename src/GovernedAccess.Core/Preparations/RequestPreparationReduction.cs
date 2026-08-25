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
        IEnumerable<OperationResult> operationResults,
        IEnumerable<ProposalField> changedFields,
        ApplicationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(operationResults);
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

        var resultArray = operationResults.ToArray();
        var changedFieldArray = changedFields.ToArray();
        if (resultArray.Any(result => result is null))
        {
            throw new ArgumentException(
                "Operation results cannot contain null values.",
                nameof(operationResults));
        }

        if (changedFieldArray.Any(field => !Enum.IsDefined(field))
            || changedFieldArray.Distinct().Count() != changedFieldArray.Length)
        {
            throw new ArgumentException(
                "Changed field categories must be valid and unique.",
                nameof(changedFields));
        }

        Candidate = candidate;
        ClarificationDisposition = clarificationDisposition;
        Clarification = clarification;
        OperationResults = Array.AsReadOnly(resultArray);
        ChangedFields = Array.AsReadOnly(changedFieldArray);
        Outcome = outcome;
    }

    public PreparationCandidate Candidate { get; }

    public ClarificationContextDisposition ClarificationDisposition { get; }

    public ClarificationSeed? Clarification { get; }

    public IReadOnlyList<OperationResult> OperationResults { get; }

    public IReadOnlyList<ProposalField> ChangedFields { get; }

    public ApplicationOutcome Outcome { get; }
}

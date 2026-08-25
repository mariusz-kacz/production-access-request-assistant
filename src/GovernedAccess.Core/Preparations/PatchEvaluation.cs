using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal sealed class PatchEvaluation
{
    private readonly PreparationCandidate current;
    private readonly Dictionary<ProposalField, OperationResultKind> operationResults = [];

    internal PatchEvaluation(PreparationCandidate current)
    {
        this.current = current;
        ClientId = current.ClientId;
        EnvironmentId = current.EnvironmentId;
        RoleId = current.RoleId;
        Justification = current.Justification;
        IncidentId = current.IncidentId;
    }

    internal string? ClientId { get; set; }

    internal string? EnvironmentId { get; set; }

    internal string? RoleId { get; set; }

    internal string? Justification { get; set; }

    internal string? IncidentId { get; set; }

    internal void Record(
        ProposalField field,
        OperationResultKind result) =>
        operationResults[field] = result;

    internal bool HasResult(
        ProposalField field,
        OperationResultKind expected) =>
        operationResults.TryGetValue(field, out var actual)
        && actual == expected;

    internal bool HasResultOtherThan(
        ProposalField field,
        OperationResultKind expected) =>
        operationResults.TryGetValue(field, out var actual)
        && actual != expected;

    internal PreparationCandidate ToCandidate()
    {
        if (string.Equals(ClientId, current.ClientId, StringComparison.Ordinal)
            && string.Equals(EnvironmentId, current.EnvironmentId, StringComparison.Ordinal)
            && string.Equals(RoleId, current.RoleId, StringComparison.Ordinal)
            && string.Equals(Justification, current.Justification, StringComparison.Ordinal)
            && string.Equals(IncidentId, current.IncidentId, StringComparison.Ordinal))
        {
            return current;
        }

        return new PreparationCandidate(
            ClientId,
            EnvironmentId,
            RoleId,
            Justification,
            IncidentId);
    }

    internal ProposalField[] GetChangedFields(PreparationCandidate candidate) =>
        candidate.ChangedFieldsFrom(current)
            .OrderBy(FieldOrder)
            .ToArray();

    internal OperationResult[] GetOperationResults() =>
        operationResults
            .OrderBy(pair => FieldOrder(pair.Key))
            .Select(pair => new OperationResult(pair.Key, pair.Value))
            .ToArray();

    private static int FieldOrder(ProposalField field) => field switch
    {
        ProposalField.Environment => 0,
        ProposalField.Incident => 1,
        ProposalField.Role => 2,
        ProposalField.Justification => 3,
        _ => int.MaxValue,
    };
}

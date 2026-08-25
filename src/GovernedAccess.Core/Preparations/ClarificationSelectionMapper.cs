using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal static class ClarificationSelectionMapper
{
    internal static SelectionMapping Map(
        RequestPreparation preparation,
        ClarificationSelection selection)
    {
        var context = preparation.Clarification;
        if (context is null)
        {
            return new SelectionMapping.Rejected(
                ToProposalField(selection.Target),
                ClarificationContextDisposition.Preserve);
        }

        if (context.PreparationId != preparation.PreparationId)
        {
            return new SelectionMapping.Rejected(
                ToProposalField(context.Target),
                ClarificationContextDisposition.Preserve);
        }

        if (context.CandidateVersion != preparation.CandidateVersion)
        {
            return new SelectionMapping.Rejected(
                ToProposalField(context.Target),
                ClarificationContextDisposition.Clear);
        }

        if (selection.Target != context.Target
            || selection.OptionIndex < 1
            || selection.OptionIndex > context.OrderedCanonicalIds.Count)
        {
            return new SelectionMapping.Rejected(
                ToProposalField(context.Target),
                ClarificationContextDisposition.Preserve);
        }

        var selectedId = context.OrderedCanonicalIds[selection.OptionIndex - 1];
        return new SelectionMapping.Accepted(selection.Target switch
        {
            ClarificationTarget.Environment => new DraftPatch(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId(selectedId))),
            ClarificationTarget.Role => new DraftPatch(
                role: new SetRoleOperation(selectedId)),
            _ => throw new InvalidOperationException(
                "The clarification target is unsupported."),
        });
    }

    private static ProposalField ToProposalField(ClarificationTarget target) =>
        target switch
        {
            ClarificationTarget.Environment => ProposalField.Environment,
            ClarificationTarget.Role => ProposalField.Role,
            _ => throw new InvalidOperationException(
                "The clarification target is unsupported."),
        };
}

internal abstract record SelectionMapping
{
    private SelectionMapping()
    {
    }

    internal sealed record Accepted(DraftPatch Patch) : SelectionMapping;

    internal sealed record Rejected(
        ProposalField Field,
        ClarificationContextDisposition ClarificationDisposition)
        : SelectionMapping;
}

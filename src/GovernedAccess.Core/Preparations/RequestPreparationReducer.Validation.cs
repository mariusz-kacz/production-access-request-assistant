using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed partial class RequestPreparationReducer
{
    private static bool IsStructurallyValid(TurnProposal proposal)
    {
        if (proposal.SchemaVersion != TurnProposal.CurrentSchemaVersion
            || !Enum.IsDefined(proposal.DialogueAct))
        {
            return false;
        }

        return proposal.DialogueAct switch
        {
            DialogueAct.UpdateDraft =>
                proposal.Patch is not null
                && proposal.ClarificationSelection is null
                && proposal.DiscussionTopic is null
                && IsStructurallyValid(proposal.Patch),
            DialogueAct.SelectClarification =>
                proposal.Patch is null
                && proposal.ClarificationSelection is { } selection
                && Enum.IsDefined(selection.Target)
                && proposal.DiscussionTopic is null,
            DialogueAct.DiscussDraft =>
                proposal.Patch is null
                && proposal.ClarificationSelection is null
                && proposal.DiscussionTopic is { } topic
                && Enum.IsDefined(topic),
            DialogueAct.RequestSubmission
                or DialogueAct.Unrelated
                or DialogueAct.Unclear =>
                proposal.Patch is null
                && proposal.ClarificationSelection is null
                && proposal.DiscussionTopic is null,
            _ => false,
        };
    }

    private static bool IsStructurallyValid(DraftPatch patch)
    {
        if (patch.Environment is null
            && patch.Role is null
            && patch.Justification is null
            && patch.Incident is null)
        {
            return false;
        }

        return IsStructurallyValid(patch.Environment)
            && IsStructurallyValid(patch.Role)
            && IsStructurallyValid(patch.Justification)
            && IsStructurallyValid(patch.Incident);
    }

    private static bool IsStructurallyValid(EnvironmentOperation? operation) =>
        operation switch
        {
            null or ClearEnvironmentOperation => true,
            SetEnvironmentOperation { Reference: ExactEnvironmentId exact } =>
                IsBoundedIdentifier(exact.Id),
            SetEnvironmentOperation { Reference: EnvironmentSearchQuery search } =>
                !string.IsNullOrWhiteSpace(search.Query)
                && search.Query.Length <= EnvironmentSearchQuery.MaximumLength,
            _ => false,
        };

    private static bool IsStructurallyValid(RoleOperation? operation) =>
        operation switch
        {
            null or ClearRoleOperation => true,
            SetRoleOperation set => IsBoundedIdentifier(set.RoleId),
            _ => false,
        };

    private static bool IsStructurallyValid(JustificationOperation? operation) =>
        operation switch
        {
            null or ClearJustificationOperation => true,
            SetJustificationOperation { Value: { } value } =>
                !string.IsNullOrWhiteSpace(value.Text)
                && Enum.IsDefined(value.Provenance),
            _ => false,
        };

    private static bool IsStructurallyValid(IncidentOperation? operation) =>
        operation switch
        {
            null or ClearIncidentOperation => true,
            SetIncidentOperation set => IsBoundedIdentifier(set.IncidentId),
            _ => false,
        };

    private static bool IsBoundedIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= PreparationCandidate.MaximumIdentifierLength;
}

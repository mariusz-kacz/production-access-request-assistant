namespace GovernedAccess.Core.Preparations.Contracts;

public enum DialogueAct
{
    UpdateDraft,
    SelectClarification,
    DiscussDraft,
    RequestSubmission,
    Unrelated,
    Unclear,
}

public enum DiscussionTopic
{
    CurrentDraft,
    MissingInformation,
    AllowedChanges,
    ConfirmationProcess,
    ResetInstructions,
    Unsupported,
}

public enum ClarificationTarget
{
    Environment,
    Role,
}

public sealed record ClarificationSelection
{
    public ClarificationSelection(
        ClarificationTarget target,
        int optionIndex)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (optionIndex < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(optionIndex),
                optionIndex,
                "A clarification option index must be one-based.");
        }

        Target = target;
        OptionIndex = optionIndex;
    }

    public ClarificationTarget Target { get; }

    public int OptionIndex { get; }
}

public sealed record TurnProposal
{
    public const int CurrentSchemaVersion = 1;

    public TurnProposal(
        int schemaVersion,
        DialogueAct dialogueAct,
        DraftPatch? patch = null,
        ClarificationSelection? clarificationSelection = null,
        DiscussionTopic? discussionTopic = null)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"The proposal schema version must be {CurrentSchemaVersion}.");
        }

        if (!Enum.IsDefined(dialogueAct))
        {
            throw new ArgumentOutOfRangeException(nameof(dialogueAct));
        }

        if (discussionTopic.HasValue
            && !Enum.IsDefined(discussionTopic.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(discussionTopic));
        }

        var payloadIsCompatible = dialogueAct switch
        {
            DialogueAct.UpdateDraft =>
                patch is not null
                && clarificationSelection is null
                && discussionTopic is null,
            DialogueAct.SelectClarification =>
                patch is null
                && clarificationSelection is not null
                && discussionTopic is null,
            DialogueAct.DiscussDraft =>
                patch is null
                && clarificationSelection is null
                && discussionTopic is not null,
            DialogueAct.RequestSubmission
                or DialogueAct.Unrelated
                or DialogueAct.Unclear =>
                patch is null
                && clarificationSelection is null
                && discussionTopic is null,
            _ => false,
        };

        if (!payloadIsCompatible)
        {
            throw new ArgumentException(
                "The dialogue act and semantic payloads do not form a valid closed proposal.");
        }

        SchemaVersion = schemaVersion;
        DialogueAct = dialogueAct;
        Patch = patch;
        ClarificationSelection = clarificationSelection;
        DiscussionTopic = discussionTopic;
    }

    public int SchemaVersion { get; }

    public DialogueAct DialogueAct { get; }

    public DraftPatch? Patch { get; }

    public ClarificationSelection? ClarificationSelection { get; }

    public DiscussionTopic? DiscussionTopic { get; }
}

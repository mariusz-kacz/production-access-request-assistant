namespace GovernedAccess.Core.Preparations.Contracts;

public enum DialogueAct
{
    UpdateDraft,
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

public sealed record TurnProposal
{
    public const int CurrentSchemaVersion = 1;

    public TurnProposal(
        int schemaVersion,
        DialogueAct dialogueAct,
        DraftPatch? patch = null,
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
                && discussionTopic is null,
            DialogueAct.DiscussDraft =>
                patch is null
                && discussionTopic is not null,
            DialogueAct.RequestSubmission
                or DialogueAct.Unrelated
                or DialogueAct.Unclear =>
                patch is null
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
        DiscussionTopic = discussionTopic;
    }

    public int SchemaVersion { get; }

    public DialogueAct DialogueAct { get; }

    public DraftPatch? Patch { get; }

    public DiscussionTopic? DiscussionTopic { get; }
}

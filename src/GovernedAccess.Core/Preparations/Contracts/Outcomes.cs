using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;

namespace GovernedAccess.Core.Preparations.Contracts;

public enum ProposalStructuralFailure
{
    UnknownDialogueAct,
    InvalidActPayloadCombination,
    UnknownProperty,
    UnknownField,
    UnknownOperation,
    UnknownReferenceForm,
    UnknownDiscussionTopic,
    MissingRequiredValue,
    ForbiddenValue,
    ValueOutOfBounds,
    UntranslatableProviderOutput,
}

public enum ProposalField
{
    Environment,
    Incident,
    Role,
    Justification,
}

public enum ApplicationGroupResultKind
{
    Applied,
    NoOp,
    Rejected,
    NeedsClarification,
}

public enum ApplicationGroupRejectionReason
{
    Invalid,
    Unavailable,
    Conflict,
    MissingDependency,
    EnvironmentQueryTooBroad,
    NoAssignableRoles,
    RoleChoiceLimitExceeded,
}

public sealed record ApplicationGroupResult
{
    public ApplicationGroupResult(
        ApplicationGroupResultKind kind,
        ApplicationGroupRejectionReason? rejectionReason = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (rejectionReason.HasValue && !Enum.IsDefined(rejectionReason.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }

        if ((kind == ApplicationGroupResultKind.Rejected)
            != rejectionReason.HasValue)
        {
            throw new ArgumentException(
                "A rejection reason is required exactly when the group is rejected.",
                nameof(rejectionReason));
        }

        Kind = kind;
        RejectionReason = rejectionReason;
    }

    public ApplicationGroupResultKind Kind { get; }

    public ApplicationGroupRejectionReason? RejectionReason { get; }
}

public abstract record ApplicationOutcome
{
    private protected ApplicationOutcome()
    {
    }

    private protected static void ValidateGroupResults(
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult)
    {
        if (scopeResult is null && justificationResult is null)
        {
            throw new ArgumentException(
                "A draft application outcome must contain at least one group result.");
        }
    }
}

public sealed record DraftUpdated : ApplicationOutcome
{
    public DraftUpdated(
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult)
    {
        ValidateGroupResults(scopeResult, justificationResult);
        ScopeResult = scopeResult;
        JustificationResult = justificationResult;
    }

    public ApplicationGroupResult? ScopeResult { get; }

    public ApplicationGroupResult? JustificationResult { get; }
}

public sealed record ClarificationRequired : ApplicationOutcome
{
    public const int MaximumChoiceCount = 5;

    public ClarificationRequired(
        ClarificationTarget target,
        IEnumerable<ClarificationChoice> choices,
        ApplicationGroupResult scopeResult,
        ApplicationGroupResult? justificationResult)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentNullException.ThrowIfNull(choices);
        var choiceArray = choices.ToArray();
        if (choiceArray.Length == 0)
        {
            throw new ArgumentException(
                "A clarification outcome must contain at least one choice.",
                nameof(choices));
        }

        if (choiceArray.Length > MaximumChoiceCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(choices),
                choiceArray.Length,
                $"A clarification cannot contain more than {MaximumChoiceCount} choices.");
        }

        if (choiceArray.Any(choice => choice is null))
        {
            throw new ArgumentException(
                "Clarification choices cannot contain null values.",
                nameof(choices));
        }

        if (choiceArray
            .Select(choice => choice.CanonicalId)
            .Distinct(StringComparer.Ordinal)
            .Count() != choiceArray.Length)
        {
            throw new ArgumentException(
                "Clarification choice identifiers must be unique.",
                nameof(choices));
        }

        ArgumentNullException.ThrowIfNull(scopeResult);
        ValidateGroupResults(scopeResult, justificationResult);

        Target = target;
        Choices = Array.AsReadOnly(choiceArray);
        ScopeResult = scopeResult;
        JustificationResult = justificationResult;
    }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<ClarificationChoice> Choices { get; }

    public ApplicationGroupResult ScopeResult { get; }

    public ApplicationGroupResult? JustificationResult { get; }
}

public sealed record DraftUnchanged : ApplicationOutcome
{
    public DraftUnchanged(
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult)
    {
        ValidateGroupResults(scopeResult, justificationResult);
        ScopeResult = scopeResult;
        JustificationResult = justificationResult;
    }

    public ApplicationGroupResult? ScopeResult { get; }

    public ApplicationGroupResult? JustificationResult { get; }
}

public sealed record DraftDiscussion : ApplicationOutcome
{
    public DraftDiscussion(DiscussionTopic topic)
    {
        if (!Enum.IsDefined(topic))
        {
            throw new ArgumentOutOfRangeException(nameof(topic));
        }

        Topic = topic;
    }

    public DiscussionTopic Topic { get; }
}

public sealed record SubmissionGuidance : ApplicationOutcome;

public sealed record UnrelatedGuidance : ApplicationOutcome;

public sealed record UnclearGuidance : ApplicationOutcome;

public sealed record ResetGuidance : ApplicationOutcome;

public sealed record ReadyForConfirmation : ApplicationOutcome
{
    public ReadyForConfirmation(Guid preparationId)
    {
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A ready outcome requires a preparation identifier.",
                nameof(preparationId));
        }

        PreparationId = preparationId;
    }

    public Guid PreparationId { get; }
}

public enum RevalidatedPreparationStatus
{
    Collecting,
    Ready,
}

public sealed record ConfirmationRevalidationFailed : ApplicationOutcome
{
    public ConfirmationRevalidationFailed(
        Guid successorPreparationId,
        RevalidatedPreparationStatus successorStatus)
    {
        if (successorPreparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A confirmation-revalidation outcome requires a successor preparation identifier.",
                nameof(successorPreparationId));
        }

        if (!Enum.IsDefined(successorStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(successorStatus));
        }

        SuccessorPreparationId = successorPreparationId;
        SuccessorStatus = successorStatus;
    }

    public Guid SuccessorPreparationId { get; }

    public RevalidatedPreparationStatus SuccessorStatus { get; }
}

public sealed record ConfirmationSourceUnavailable : ApplicationOutcome;

public sealed record TerminalPreparationGuidance : ApplicationOutcome;

public sealed record Failed : ApplicationOutcome
{
    public Failed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public ApplicationFailure Failure { get; }
}

public sealed record PreparationResponse
{
    public PreparationResponse(ApplicationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Outcome = outcome;
    }

    public ApplicationOutcome Outcome { get; }
}

using GovernedAccess.Core.Application;

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
    ClarificationSelectionCombinedWithPatch,
    UntranslatableProviderOutput,
}

public enum ProposalField
{
    Environment,
    Incident,
    Role,
    Justification,
}

public enum OperationResultKind
{
    Applied,
    NoOpValueEqual,
    RejectedInvalid,
    RejectedUnavailable,
    RejectedConflict,
    RejectedDependency,
    NeedsClarification,
}

public sealed record OperationResult
{
    public OperationResult(
        ProposalField field,
        OperationResultKind kind)
    {
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Field = field;
        Kind = kind;
    }

    public ProposalField Field { get; }

    public OperationResultKind Kind { get; }
}

public abstract record ApplicationOutcome
{
    private protected ApplicationOutcome()
    {
    }

    private protected static IReadOnlyList<OperationResult> CopyOperationResults(
        IEnumerable<OperationResult> operationResults)
    {
        ArgumentNullException.ThrowIfNull(operationResults);
        var results = operationResults.ToArray();
        if (results.Length == 0)
        {
            throw new ArgumentException(
                "An operation outcome must contain at least one result.",
                nameof(operationResults));
        }

        if (results.Any(result => result is null))
        {
            throw new ArgumentException(
                "Operation results cannot contain null values.",
                nameof(operationResults));
        }

        return Array.AsReadOnly(results);
    }
}

public sealed record DraftUpdated : ApplicationOutcome
{
    public DraftUpdated(IEnumerable<OperationResult> operationResults)
    {
        OperationResults = CopyOperationResults(operationResults);
    }

    public IReadOnlyList<OperationResult> OperationResults { get; }
}

public sealed record ClarificationChoice
{
    public ClarificationChoice(string canonicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);
        CanonicalId = canonicalId.Trim();
    }

    public string CanonicalId { get; }
}

public sealed record ClarificationRequired : ApplicationOutcome
{
    public const int MaximumChoiceCount = 5;

    public ClarificationRequired(
        ClarificationTarget target,
        IEnumerable<ClarificationChoice> choices)
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

        Target = target;
        Choices = Array.AsReadOnly(choiceArray);
    }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<ClarificationChoice> Choices { get; }
}

public sealed record DraftUnchanged : ApplicationOutcome
{
    public DraftUnchanged(IEnumerable<OperationResult> operationResults)
    {
        OperationResults = CopyOperationResults(operationResults);
    }

    public IReadOnlyList<OperationResult> OperationResults { get; }
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

public sealed record BudgetExhaustedGuidance : ApplicationOutcome;

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

public sealed record CollectingStaleWarning
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);

    public CollectingStaleWarning(
        DateTimeOffset lastUpdatedAt,
        DateTimeOffset observedAt)
    {
        var age = observedAt - lastUpdatedAt;
        if (age < MinimumAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                observedAt,
                $"A collecting preparation is stale only after {MinimumAge.TotalDays} days.");
        }

        LastUpdatedAt = lastUpdatedAt;
        ObservedAt = observedAt;
        Age = age;
    }

    public DateTimeOffset LastUpdatedAt { get; }

    public DateTimeOffset ObservedAt { get; }

    public TimeSpan Age { get; }
}

public sealed record PreparationResponse
{
    public PreparationResponse(
        ApplicationOutcome outcome,
        CollectingStaleWarning? staleWarning = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (staleWarning is not null
            && outcome is ReadyForConfirmation
                or ConfirmationRevalidationFailed
                or ConfirmationSourceUnavailable
                or TerminalPreparationGuidance)
        {
            throw new ArgumentException(
                "A collecting-stale warning can accompany only an active collecting outcome.",
                nameof(staleWarning));
        }

        Outcome = outcome;
        StaleWarning = staleWarning;
    }

    public ApplicationOutcome Outcome { get; }

    public CollectingStaleWarning? StaleWarning { get; }
}

using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Domain.Preparations;

public sealed class RequestPreparation
{
    public const int MaximumInterpretedTurns = 50;

    public const int MaximumClarificationChoices = 5;

    public const int MaximumMaterialChangeAttributions =
        MaximumInterpretedTurns;

    public static readonly TimeSpan ReadyLifetime = TimeSpan.FromMinutes(30);

    private readonly List<MaterialChangeAttribution> materialChangeAttributions;

    private RequestPreparation(
        Guid preparationId,
        Guid? predecessorPreparationId,
        PreparationBinding binding,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        MaterialChangeAttribution? attribution,
        DateTimeOffset createdAt,
        string correlationId)
    {
        PreparationId = preparationId;
        PredecessorPreparationId = predecessorPreparationId;
        Binding = binding;
        Candidate = candidate;
        CandidateVersion = candidate.IsEmpty ? 0 : 1;
        ConcurrencyVersion = 1;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
        CorrelationId = MaterialChangeAttribution.NormalizeCorrelationId(correlationId);
        materialChangeAttributions = attribution is null ? [] : [attribution];
        Clarification = clarification is null
            ? null
            : new PreparationClarificationContext(
                PreparationId,
                CandidateVersion,
                clarification,
                CreatedAt);

        if (Candidate.IsComplete && Clarification is null)
        {
            Lifecycle = PreparationLifecycle.Ready;
            ReadyAt = CreatedAt;
            ReadyDeadline = CreatedAt.Add(ReadyLifetime);
        }
        else
        {
            Lifecycle = PreparationLifecycle.Collecting;
        }
    }

    public Guid PreparationId { get; }

    public Guid? PredecessorPreparationId { get; }

    public PreparationBinding Binding { get; }

    public PreparationLifecycle Lifecycle { get; private set; }

    public PreparationCandidate Candidate { get; private set; }

    public int CandidateVersion { get; private set; }

    public long ConcurrencyVersion { get; private set; }

    public int InterpretedTurnCount { get; private set; }

    public bool CanInterpretTurn =>
        Lifecycle is PreparationLifecycle.Collecting or PreparationLifecycle.Ready
        && InterpretedTurnCount < MaximumInterpretedTurns;

    public PreparationClarificationContext? Clarification { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? ReadyDeadline { get; private set; }

    public DateTimeOffset? TerminalAt { get; private set; }

    public string CorrelationId { get; private set; }

    public IReadOnlyList<MaterialChangeAttribution> MaterialChangeAttributions =>
        materialChangeAttributions.AsReadOnly();

    public static RequestPreparation CreateRoot(
        PreparationBinding binding,
        DateTimeOffset createdAt,
        string correlationId) =>
        CreateRoot(
            binding,
            PreparationCandidate.Empty,
            clarification: null,
            attribution: null,
            createdAt,
            correlationId);

    public static RequestPreparation CreateRoot(
        PreparationBinding binding,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        MaterialChangeAttribution? attribution,
        DateTimeOffset createdAt,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateInitialAttribution(
            PreparationCandidate.Empty,
            candidate,
            attribution);

        return new(
            Guid.NewGuid(),
            predecessorPreparationId: null,
            binding,
            candidate,
            clarification,
            attribution,
            createdAt,
            correlationId);
    }

    public static RequestPreparation CreateRevision(
        RequestPreparation predecessor,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        MaterialChangeAttribution? attribution,
        DateTimeOffset createdAt,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(candidate);
        if (predecessor.Lifecycle != PreparationLifecycle.Ready)
        {
            throw new InvalidOperationException(
                "Only a ready preparation can be revised.");
        }

        var normalizedCreatedAt = createdAt.ToUniversalTime();
        predecessor.EnsureNotExpired(normalizedCreatedAt);
        predecessor.EnsureChronological(normalizedCreatedAt);
        if (candidate.IsEmpty)
        {
            throw new ArgumentException(
                "A revision successor must retain a non-empty canonical candidate.",
                nameof(candidate));
        }

        var changedFields = candidate.ChangedFieldsFrom(predecessor.Candidate);
        if (changedFields.Count == 0)
        {
            if (clarification is null)
            {
                throw new ArgumentException(
                    "A value-equal revision requires clarification context.",
                    nameof(clarification));
            }

            if (attribution is not null)
            {
                throw new ArgumentException(
                    "A clarification-only revision cannot carry material-change attribution.",
                    nameof(attribution));
            }
        }
        else
        {
            ValidateAttribution(changedFields, attribution);
        }

        return new(
            Guid.NewGuid(),
            predecessor.PreparationId,
            predecessor.Binding,
            candidate,
            clarification,
            attribution,
            normalizedCreatedAt,
            correlationId);
    }

    public void ApplyCandidateChange(
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        MaterialChangeAttribution attribution,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureLifecycle(PreparationLifecycle.Collecting);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(attribution);
        var changedFields = candidate.ChangedFieldsFrom(Candidate);
        if (changedFields.Count == 0)
        {
            throw new ArgumentException(
                "A material candidate commit must change at least one canonical field.",
                nameof(candidate));
        }

        ValidateAttribution(changedFields, attribution);
        if (materialChangeAttributions.Count >= MaximumMaterialChangeAttributions)
        {
            throw new InvalidOperationException(
                "The preparation cannot retain additional material-change attribution.");
        }

        var operation = PrepareUpdate(occurredAt, correlationId);
        Candidate = candidate;
        CandidateVersion++;
        materialChangeAttributions.Add(attribution);
        Clarification = clarification is null
            ? null
            : new PreparationClarificationContext(
                PreparationId,
                CandidateVersion,
                clarification,
                operation.OccurredAt);

        if (Candidate.IsComplete && Clarification is null)
        {
            Lifecycle = PreparationLifecycle.Ready;
            ReadyAt = operation.OccurredAt;
            ReadyDeadline = operation.OccurredAt.Add(ReadyLifetime);
        }

        RecordUpdate(operation);
    }

    public void SetClarification(
        ClarificationSeed clarification,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureLifecycle(PreparationLifecycle.Collecting);
        ArgumentNullException.ThrowIfNull(clarification);
        var operation = PrepareUpdate(occurredAt, correlationId);
        Clarification = new PreparationClarificationContext(
            PreparationId,
            CandidateVersion,
            clarification,
            operation.OccurredAt);
        RecordUpdate(operation);
    }

    public void ClearClarification(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureLifecycle(PreparationLifecycle.Collecting);
        if (Clarification is null)
        {
            return;
        }

        var operation = PrepareUpdate(occurredAt, correlationId);
        Clarification = null;
        if (Candidate.IsComplete)
        {
            Lifecycle = PreparationLifecycle.Ready;
            ReadyAt = operation.OccurredAt;
            ReadyDeadline = operation.OccurredAt.Add(ReadyLifetime);
        }

        RecordUpdate(operation);
    }

    public void RecordInterpretedTurn(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureActive();
        if (!CanInterpretTurn)
        {
            throw new InvalidOperationException(
                "The preparation interpreted-turn budget is exhausted.");
        }

        var operation = PrepareUpdate(occurredAt, correlationId);
        InterpretedTurnCount++;
        RecordUpdate(operation);
    }

    public bool IsExpired(DateTimeOffset observedAt) =>
        Lifecycle == PreparationLifecycle.Ready
        && ReadyDeadline is { } deadline
        && observedAt.ToUniversalTime() >= deadline;

    public void MarkSubmitted(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureLifecycle(PreparationLifecycle.Ready);
        var operation = PrepareUpdate(occurredAt, correlationId);
        EnsureNotExpired(operation.OccurredAt);
        MakeTerminal(PreparationLifecycle.Submitted, operation);
    }

    public void MarkSuperseded(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureActive();
        var operation = PrepareUpdate(occurredAt, correlationId);
        if (Lifecycle == PreparationLifecycle.Ready)
        {
            EnsureNotExpired(operation.OccurredAt);
        }

        MakeTerminal(PreparationLifecycle.Superseded, operation);
    }

    public void MarkExpired(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureLifecycle(PreparationLifecycle.Ready);
        var operation = PrepareUpdate(occurredAt, correlationId);
        if (!IsExpired(operation.OccurredAt))
        {
            throw new InvalidOperationException(
                "A ready preparation cannot expire before its deadline.");
        }

        MakeTerminal(PreparationLifecycle.Expired, operation);
    }

    private static void ValidateInitialAttribution(
        PreparationCandidate current,
        PreparationCandidate candidate,
        MaterialChangeAttribution? attribution)
    {
        var changedFields = candidate.ChangedFieldsFrom(current);
        if (changedFields.Count == 0)
        {
            if (attribution is not null)
            {
                throw new ArgumentException(
                    "An empty root cannot carry material-change attribution.",
                    nameof(attribution));
            }

            return;
        }

        ValidateAttribution(changedFields, attribution);
    }

    private static void ValidateAttribution(
        IReadOnlySet<ProposalField> changedFields,
        MaterialChangeAttribution? attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        if (!attribution.CoversExactly(changedFields))
        {
            throw new ArgumentException(
                "Material-change attribution must name exactly the changed candidate fields.",
                nameof(attribution));
        }
    }

    private void MakeTerminal(
        PreparationLifecycle terminalLifecycle,
        (DateTimeOffset OccurredAt, string CorrelationId) operation)
    {
        Lifecycle = terminalLifecycle;
        Candidate = PreparationCandidate.Empty;
        Clarification = null;
        TerminalAt = operation.OccurredAt;
        RecordUpdate(operation);
    }

    private void EnsureNotExpired(DateTimeOffset occurredAt)
    {
        if (IsExpired(occurredAt))
        {
            throw new InvalidOperationException(
                "The ready preparation has reached its confirmation deadline.");
        }
    }

    private (DateTimeOffset OccurredAt, string CorrelationId) PrepareUpdate(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        occurredAt = occurredAt.ToUniversalTime();
        EnsureChronological(occurredAt);
        return (
            occurredAt,
            MaterialChangeAttribution.NormalizeCorrelationId(correlationId));
    }

    private void EnsureChronological(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "A preparation update cannot predate the prior aggregate update.");
        }
    }

    private void RecordUpdate(
        (DateTimeOffset OccurredAt, string CorrelationId) operation)
    {
        UpdatedAt = operation.OccurredAt;
        CorrelationId = operation.CorrelationId;
        ConcurrencyVersion++;
    }

    private void EnsureLifecycle(PreparationLifecycle expected)
    {
        if (Lifecycle != expected)
        {
            throw new InvalidOperationException(
                $"A preparation in lifecycle '{Lifecycle}' cannot perform this transition.");
        }
    }

    private void EnsureActive()
    {
        if (Lifecycle is not PreparationLifecycle.Collecting
            and not PreparationLifecycle.Ready)
        {
            throw new InvalidOperationException(
                $"A preparation in lifecycle '{Lifecycle}' cannot perform this transition.");
        }
    }
}

using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Domain.Preparations;

public enum PreparationLifecycle
{
    Collecting,
    Ready,
    Submitted,
    Superseded,
    Expired,
}

public sealed record PreparationBinding
{
    public const string TeamsChannel = "msteams";

    public const int MaximumComponentLength = 200;

    public PreparationBinding(
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId)
    {
        channel = NormalizeRequired(channel, nameof(channel));
        if (!string.Equals(channel, TeamsChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                $"The preparation channel must be '{TeamsChannel}'.");
        }

        Channel = TeamsChannel;
        TenantId = NormalizeRequired(tenantId, nameof(tenantId));
        ChannelActorId = NormalizeRequired(channelActorId, nameof(channelActorId));
        ConversationId = NormalizeRequired(conversationId, nameof(conversationId));
        RequesterId = NormalizeRequired(requesterId, nameof(requesterId));
    }

    public string Channel { get; }

    public string TenantId { get; }

    public string ChannelActorId { get; }

    public string ConversationId { get; }

    public string RequesterId { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > MaximumComponentLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"A preparation binding component cannot exceed {MaximumComponentLength} characters.");
        }

        return value;
    }
}

public sealed record PreparationCandidate
{
    public const int MaximumIdentifierLength = 200;

    public const int MaximumJustificationLength = 2000;

    public static PreparationCandidate Empty { get; } =
        new(null, null, null, null, null);

    public PreparationCandidate(
        string? clientId,
        string? environmentId,
        string? roleId,
        string? justification,
        string? incidentId)
    {
        ClientId = NormalizeOptionalIdentifier(clientId, nameof(clientId));
        EnvironmentId = NormalizeOptionalIdentifier(
            environmentId,
            nameof(environmentId));
        RoleId = NormalizeOptionalIdentifier(roleId, nameof(roleId));
        Justification = NormalizeOptionalJustification(justification);
        IncidentId = NormalizeOptionalIdentifier(incidentId, nameof(incidentId));

        if ((ClientId is null) != (EnvironmentId is null))
        {
            throw new ArgumentException(
                "Canonical client and environment identifiers must be present or absent together.");
        }

        if (EnvironmentId is null && (RoleId is not null || IncidentId is not null))
        {
            throw new ArgumentException(
                "Canonical role and incident identifiers require an environment.");
        }
    }

    public string? ClientId { get; }

    public string? EnvironmentId { get; }

    public string? RoleId { get; }

    public string? Justification { get; }

    public string? IncidentId { get; }

    public bool IsEmpty =>
        ClientId is null
        && EnvironmentId is null
        && RoleId is null
        && Justification is null
        && IncidentId is null;

    public bool IsComplete =>
        ClientId is not null
        && EnvironmentId is not null
        && RoleId is not null
        && Justification is not null;

    internal IReadOnlySet<ProposalField> ChangedFieldsFrom(
        PreparationCandidate current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var changedFields = new HashSet<ProposalField>();

        if (!string.Equals(ClientId, current.ClientId, StringComparison.Ordinal)
            || !string.Equals(
                EnvironmentId,
                current.EnvironmentId,
                StringComparison.Ordinal))
        {
            changedFields.Add(ProposalField.Environment);
        }

        if (!string.Equals(RoleId, current.RoleId, StringComparison.Ordinal))
        {
            changedFields.Add(ProposalField.Role);
        }

        if (!string.Equals(
            Justification,
            current.Justification,
            StringComparison.Ordinal))
        {
            changedFields.Add(ProposalField.Justification);
        }

        if (!string.Equals(IncidentId, current.IncidentId, StringComparison.Ordinal))
        {
            changedFields.Add(ProposalField.Incident);
        }

        return changedFields;
    }

    private static string? NormalizeOptionalIdentifier(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > MaximumIdentifierLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"A canonical identifier cannot exceed {MaximumIdentifierLength} characters.");
        }

        return value;
    }

    private static string? NormalizeOptionalJustification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length > MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"Canonical justification cannot exceed {MaximumJustificationLength} characters.");
        }

        return value;
    }
}

public sealed record ClarificationSeed
{
    public ClarificationSeed(
        ClarificationTarget target,
        IEnumerable<string> orderedCanonicalIds)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentNullException.ThrowIfNull(orderedCanonicalIds);
        var identifiers = orderedCanonicalIds
            .Select(NormalizeIdentifier)
            .ToArray();
        if (identifiers.Length == 0)
        {
            throw new ArgumentException(
                "A clarification must contain at least one choice.",
                nameof(orderedCanonicalIds));
        }

        if (identifiers.Length > RequestPreparation.MaximumClarificationChoices)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orderedCanonicalIds),
                identifiers.Length,
                $"A clarification cannot contain more than {RequestPreparation.MaximumClarificationChoices} choices.");
        }

        if (identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Length)
        {
            throw new ArgumentException(
                "Clarification choice identifiers must be unique.",
                nameof(orderedCanonicalIds));
        }

        Target = target;
        OrderedCanonicalIds = Array.AsReadOnly(identifiers);
    }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<string> OrderedCanonicalIds { get; }

    private static string NormalizeIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Length > PreparationCandidate.MaximumIdentifierLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"A clarification identifier cannot exceed {PreparationCandidate.MaximumIdentifierLength} characters.");
        }

        return value;
    }
}

public sealed record PreparationClarificationContext
{
    internal PreparationClarificationContext(
        Guid preparationId,
        int candidateVersion,
        ClarificationSeed seed,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(seed);
        PreparationId = preparationId;
        CandidateVersion = candidateVersion;
        Target = seed.Target;
        OrderedCanonicalIds = seed.OrderedCanonicalIds;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid PreparationId { get; }

    public int CandidateVersion { get; }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<string> OrderedCanonicalIds { get; }

    public DateTimeOffset CreatedAt { get; }
}

public sealed record MaterialChangeAttribution
{
    public const int MaximumMetadataLength = 200;

    public const int MaximumCorrelationIdLength = 128;

    public MaterialChangeAttribution(
        IEnumerable<ProposalField> fields,
        string modelDeployment,
        string? providerModelVersion,
        string promptContractVersion,
        string structuredOutputSchemaVersion,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var fieldArray = fields.ToArray();
        if (fieldArray.Length == 0)
        {
            throw new ArgumentException(
                "Material-change attribution requires at least one field category.",
                nameof(fields));
        }

        if (fieldArray.Any(field => !Enum.IsDefined(field)))
        {
            throw new ArgumentOutOfRangeException(nameof(fields));
        }

        if (fieldArray.Distinct().Count() != fieldArray.Length)
        {
            throw new ArgumentException(
                "Material-change field categories must be unique.",
                nameof(fields));
        }

        Fields = Array.AsReadOnly(fieldArray);
        ModelDeployment = NormalizeRequired(
            modelDeployment,
            MaximumMetadataLength,
            nameof(modelDeployment));
        ProviderModelVersion = NormalizeOptional(
            providerModelVersion,
            MaximumMetadataLength,
            nameof(providerModelVersion));
        PromptContractVersion = NormalizeRequired(
            promptContractVersion,
            MaximumMetadataLength,
            nameof(promptContractVersion));
        StructuredOutputSchemaVersion = NormalizeRequired(
            structuredOutputSchemaVersion,
            MaximumMetadataLength,
            nameof(structuredOutputSchemaVersion));
        OccurredAt = occurredAt.ToUniversalTime();
        CorrelationId = NormalizeRequired(
            correlationId,
            MaximumCorrelationIdLength,
            nameof(correlationId));
    }

    public IReadOnlyList<ProposalField> Fields { get; }

    public string ModelDeployment { get; }

    public string? ProviderModelVersion { get; }

    public string PromptContractVersion { get; }

    public string StructuredOutputSchemaVersion { get; }

    public DateTimeOffset OccurredAt { get; }

    public string CorrelationId { get; }

    internal bool CoversExactly(IReadOnlySet<ProposalField> fields) =>
        fields.Count == Fields.Count
        && Fields.All(fields.Contains);

    internal static string NormalizeCorrelationId(string value) =>
        NormalizeRequired(
            value,
            MaximumCorrelationIdLength,
            nameof(value));

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"Safe attribution metadata cannot exceed {maximumLength} characters.");
        }

        return value;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, parameterName);
}

/// <summary>
/// Provider- and persistence-technology-neutral input used only to rehydrate one
/// preparation after a durable read.
/// </summary>
public sealed record RequestPreparationPersistenceState(
    Guid PreparationId,
    Guid? PredecessorPreparationId,
    PreparationBinding Binding,
    PreparationLifecycle Lifecycle,
    PreparationCandidate Candidate,
    int CandidateVersion,
    long ConcurrencyVersion,
    int InterpretedTurnCount,
    PreparationClarificationPersistenceState? Clarification,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? ReadyDeadline,
    DateTimeOffset? TerminalAt,
    string CorrelationId,
    IReadOnlyList<MaterialChangeAttribution> MaterialChangeAttributions);

public sealed record PreparationClarificationPersistenceState(
    int CandidateVersion,
    ClarificationTarget Target,
    IReadOnlyList<string> OrderedCanonicalIds,
    DateTimeOffset CreatedAt);

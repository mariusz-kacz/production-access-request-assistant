using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Domain.Preparations;

public abstract record ClarificationChoice
{
    private protected ClarificationChoice(
        string canonicalId,
        string displayName)
    {
        CanonicalId = AuthorityValue.Normalize(canonicalId, nameof(canonicalId));
        DisplayName = AuthorityValue.Normalize(displayName, nameof(displayName));
    }

    public string CanonicalId { get; }

    public string DisplayName { get; }
}

public sealed record EnvironmentClarificationChoice : ClarificationChoice
{
    public EnvironmentClarificationChoice(
        string canonicalId,
        string displayName,
        string clientId,
        string clientDisplayName,
        string region,
        EnvironmentClassification classification)
        : base(canonicalId, displayName)
    {
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        ClientId = AuthorityValue.Normalize(clientId, nameof(clientId));
        ClientDisplayName = AuthorityValue.Normalize(
            clientDisplayName,
            nameof(clientDisplayName));
        Region = AuthorityValue.Normalize(region, nameof(region));
        Classification = classification;
    }

    public string ClientId { get; }

    public string ClientDisplayName { get; }

    public string Region { get; }

    public EnvironmentClassification Classification { get; }
}

public sealed record RoleClarificationChoice : ClarificationChoice
{
    public RoleClarificationChoice(
        string canonicalId,
        string displayName)
        : base(canonicalId, displayName)
    {
    }
}

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
        IEnumerable<ClarificationChoice> choices)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentNullException.ThrowIfNull(choices);
        var values = choices.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "A clarification must contain at least one choice.",
                nameof(choices));
        }

        if (values.Length > RequestPreparation.MaximumClarificationChoices)
        {
            throw new ArgumentOutOfRangeException(
                nameof(choices),
                values.Length,
                $"A clarification cannot contain more than {RequestPreparation.MaximumClarificationChoices} choices.");
        }

        if (values.Any(static choice => choice is null)
            || values.Any(choice => !MatchesTarget(target, choice)))
        {
            throw new ArgumentException(
                "Clarification choices must match the declared target.",
                nameof(choices));
        }

        if (values
            .Select(static choice => choice.CanonicalId)
            .Distinct(StringComparer.Ordinal)
            .Count() != values.Length)
        {
            throw new ArgumentException(
                "Clarification choice identifiers must be unique.",
                nameof(choices));
        }

        Target = target;
        Choices = Array.AsReadOnly(values);
    }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<ClarificationChoice> Choices { get; }

    private static bool MatchesTarget(
        ClarificationTarget target,
        ClarificationChoice choice) =>
        target switch
        {
            ClarificationTarget.Environment =>
                choice is EnvironmentClarificationChoice,
            ClarificationTarget.Role => choice is RoleClarificationChoice,
            _ => false,
        };
}

public sealed record PreparationClarificationContext
{
    internal PreparationClarificationContext(
        Guid preparationId,
        ClarificationSeed seed,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(seed);
        PreparationId = preparationId;
        Target = seed.Target;
        Choices = seed.Choices;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid PreparationId { get; }

    public ClarificationTarget Target { get; }

    public IReadOnlyList<ClarificationChoice> Choices { get; }

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
    long ConcurrencyVersion,
    PreparationClarificationPersistenceState? Clarification,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? ReadyDeadline,
    DateTimeOffset? TerminalAt,
    string CorrelationId,
    IReadOnlyList<MaterialChangeAttribution> MaterialChangeAttributions);

public sealed record PreparationClarificationPersistenceState(
    ClarificationTarget Target,
    IReadOnlyList<ClarificationChoice> Choices,
    DateTimeOffset CreatedAt);

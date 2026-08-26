using System.Text.Json;
using System.Text.Json.Serialization;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Workflow.Persistence;

internal static class RequestPreparationRecordMapper
{
    private const int MaximumClarificationJsonLength = 4_096;
    private const int MaximumAttributionJsonLength = 131_072;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 8,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false),
        },
    };

    internal static RequestPreparationRecord ToRecord(RequestPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var record = new RequestPreparationRecord();
        Apply(record, preparation);
        return record;
    }

    internal static RequestPreparation ToAggregate(RequestPreparationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!Enum.TryParse<PreparationLifecycle>(
                record.Lifecycle,
                ignoreCase: false,
                out var lifecycle)
            || !Enum.IsDefined(lifecycle))
        {
            throw new InvalidOperationException("Stored preparation lifecycle is invalid.");
        }

        var clarification = DeserializeClarification(record.ClarificationJson);
        var attributions = DeserializeAttributions(
            record.MaterialChangeAttributionsJson);
        return RequestPreparation.RestoreFromPersistence(
            new RequestPreparationPersistenceState(
                record.PreparationId,
                record.PredecessorPreparationId,
                new PreparationBinding(
                    record.Channel,
                    record.TenantId,
                    record.ChannelActorId,
                    record.ConversationId,
                    record.RequesterId),
                lifecycle,
                new PreparationCandidate(
                    record.ClientId,
                    record.EnvironmentId,
                    record.RoleId,
                    record.Justification,
                    record.IncidentId),
                record.CandidateVersion,
                record.ConcurrencyVersion,
                record.InterpretedTurnCount,
                clarification,
                record.CreatedAt,
                record.UpdatedAt,
                record.ReadyAt,
                record.ReadyDeadline,
                record.TerminalAt,
                record.CorrelationId,
                attributions));
    }

    internal static void Synchronize(
        RequestPreparationRecord record,
        RequestPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(preparation);
        Apply(record, preparation);
    }

    private static void Apply(
        RequestPreparationRecord record,
        RequestPreparation preparation)
    {
        record.PreparationId = preparation.PreparationId;
        record.PredecessorPreparationId = preparation.PredecessorPreparationId;
        record.Channel = preparation.Binding.Channel;
        record.TenantId = preparation.Binding.TenantId;
        record.ChannelActorId = preparation.Binding.ChannelActorId;
        record.ConversationId = preparation.Binding.ConversationId;
        record.RequesterId = preparation.Binding.RequesterId;
        record.Lifecycle = preparation.Lifecycle.ToString();
        record.ClientId = preparation.Candidate.ClientId;
        record.EnvironmentId = preparation.Candidate.EnvironmentId;
        record.RoleId = preparation.Candidate.RoleId;
        record.Justification = preparation.Candidate.Justification;
        record.IncidentId = preparation.Candidate.IncidentId;
        record.CandidateVersion = preparation.CandidateVersion;
        record.ConcurrencyVersion = preparation.ConcurrencyVersion;
        record.InterpretedTurnCount = preparation.InterpretedTurnCount;
        record.CreatedAt = preparation.CreatedAt;
        record.UpdatedAt = preparation.UpdatedAt;
        record.ReadyAt = preparation.ReadyAt;
        record.ReadyDeadline = preparation.ReadyDeadline;
        record.TerminalAt = preparation.TerminalAt;
        record.CorrelationId = preparation.CorrelationId;
        record.ClarificationJson = SerializeClarification(preparation.Clarification);
        record.MaterialChangeAttributionsJson = JsonSerializer.Serialize(
            preparation.MaterialChangeAttributions.Select(ToPersistedAttribution),
            JsonOptions);
    }

    private static string? SerializeClarification(
        PreparationClarificationContext? clarification) =>
        clarification is null
            ? null
            : JsonSerializer.Serialize(
                new PersistedClarification(
                    clarification.CandidateVersion,
                    clarification.Target,
                    [.. clarification.OrderedCanonicalIds],
                    clarification.CreatedAt),
                JsonOptions);

    private static PreparationClarificationPersistenceState? DeserializeClarification(
        string? json)
    {
        if (json is null)
        {
            return null;
        }

        var value = Deserialize<PersistedClarification>(
            json,
            MaximumClarificationJsonLength,
            "Stored clarification JSON is invalid.");
        if (value.OrderedCanonicalIds is null)
        {
            throw new InvalidOperationException("Stored clarification JSON is invalid.");
        }

        return new PreparationClarificationPersistenceState(
            value.CandidateVersion,
            value.Target,
            value.OrderedCanonicalIds,
            value.CreatedAt);
    }

    private static MaterialChangeAttribution[] DeserializeAttributions(
        string json)
    {
        var values = Deserialize<PersistedAttribution[]>(
            json,
            MaximumAttributionJsonLength,
            "Stored material-change attribution JSON is invalid.");
        if (values.Length > RequestPreparation.MaximumMaterialChangeAttributions)
        {
            throw new InvalidOperationException(
                "Stored material-change attribution JSON is outside its bounds.");
        }

        return values.Select(value =>
        {
            if (value is null || value.Fields is null)
            {
                throw new InvalidOperationException(
                    "Stored material-change attribution JSON is invalid.");
            }

            return new MaterialChangeAttribution(
                value.Fields,
                value.ModelDeployment,
                value.ProviderModelVersion,
                value.PromptContractVersion,
                value.StructuredOutputSchemaVersion,
                value.OccurredAt,
                value.CorrelationId);
        }).ToArray();
    }

    private static T Deserialize<T>(
        string json,
        int maximumLength,
        string errorMessage)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > maximumLength)
        {
            throw new InvalidOperationException(errorMessage);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException(errorMessage);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(errorMessage, exception);
        }
    }

    private static PersistedAttribution ToPersistedAttribution(
        MaterialChangeAttribution attribution) =>
        new(
            [.. attribution.Fields],
            attribution.ModelDeployment,
            attribution.ProviderModelVersion,
            attribution.PromptContractVersion,
            attribution.StructuredOutputSchemaVersion,
            attribution.OccurredAt,
            attribution.CorrelationId);

    private sealed record PersistedClarification(
        int CandidateVersion,
        ClarificationTarget Target,
        string[] OrderedCanonicalIds,
        DateTimeOffset CreatedAt);

    private sealed record PersistedAttribution(
        ProposalField[] Fields,
        string ModelDeployment,
        string? ProviderModelVersion,
        string PromptContractVersion,
        string StructuredOutputSchemaVersion,
        DateTimeOffset OccurredAt,
        string CorrelationId);
}

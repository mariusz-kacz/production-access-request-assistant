using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed record PreparationTurnAttribution
{
    public PreparationTurnAttribution(
        string modelDeployment,
        string? providerModelVersion,
        string promptContractVersion,
        string structuredOutputSchemaVersion)
    {
        ModelDeployment = NormalizeRequired(modelDeployment, nameof(modelDeployment));
        ProviderModelVersion = string.IsNullOrWhiteSpace(providerModelVersion)
            ? null
            : NormalizeRequired(providerModelVersion, nameof(providerModelVersion));
        PromptContractVersion = NormalizeRequired(
            promptContractVersion,
            nameof(promptContractVersion));
        StructuredOutputSchemaVersion = NormalizeRequired(
            structuredOutputSchemaVersion,
            nameof(structuredOutputSchemaVersion));
    }

    public string ModelDeployment { get; }

    public string? ProviderModelVersion { get; }

    public string PromptContractVersion { get; }

    public string StructuredOutputSchemaVersion { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > MaterialChangeAttribution.MaximumMetadataLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}

public sealed record ResetPreparationCommand
{
    public ResetPreparationCommand(
        PreparationBinding binding,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        CorrelationId = MaterialChangeAttribution.NormalizeCorrelationId(correlationId);
    }

    public PreparationBinding Binding { get; }

    public string CorrelationId { get; }
}

public sealed record PreparationSnapshot
{
    internal PreparationSnapshot(RequestPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        PreparationId = preparation.PreparationId;
        PredecessorPreparationId = preparation.PredecessorPreparationId;
        Binding = preparation.Binding;
        Lifecycle = preparation.Lifecycle;
        Candidate = preparation.Candidate;
        CandidateVersion = preparation.CandidateVersion;
        ConcurrencyVersion = preparation.ConcurrencyVersion;
        InterpretedTurnCount = preparation.InterpretedTurnCount;
        Clarification = preparation.Clarification;
        CreatedAt = preparation.CreatedAt;
        UpdatedAt = preparation.UpdatedAt;
        ReadyAt = preparation.ReadyAt;
        ReadyDeadline = preparation.ReadyDeadline;
        TerminalAt = preparation.TerminalAt;
    }

    public Guid PreparationId { get; }

    public Guid? PredecessorPreparationId { get; }

    public PreparationBinding Binding { get; }

    public PreparationLifecycle Lifecycle { get; }

    public PreparationCandidate Candidate { get; }

    public int CandidateVersion { get; }

    public long ConcurrencyVersion { get; }

    public int InterpretedTurnCount { get; }

    public PreparationClarificationContext? Clarification { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public DateTimeOffset? ReadyAt { get; }

    public DateTimeOffset? ReadyDeadline { get; }

    public DateTimeOffset? TerminalAt { get; }
}

public sealed class PreparationTurnContext
{
    internal PreparationTurnContext(
        PreparationBinding binding,
        string correlationId,
        RequestPreparation? preparation,
        CollectingStaleWarning? staleWarning,
        PreparationResponse? immediateResponse)
    {
        Binding = binding;
        CorrelationId = correlationId;
        TrackedPreparation = preparation;
        Preparation = preparation is null ? null : new PreparationSnapshot(preparation);
        StaleWarning = staleWarning;
        ImmediateResponse = immediateResponse;
    }

    public PreparationSnapshot? Preparation { get; }

    public bool RequiresInterpretation => ImmediateResponse is null;

    public PreparationResponse? ImmediateResponse { get; }

    internal PreparationBinding Binding { get; }

    internal string CorrelationId { get; }

    internal RequestPreparation? TrackedPreparation { get; }

    internal CollectingStaleWarning? StaleWarning { get; }
}

public sealed record PreparationTurnResult
{
    public PreparationTurnResult(
        PreparationSnapshot? preparation,
        PreparationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Preparation = preparation;
        Response = response;
    }

    public PreparationSnapshot? Preparation { get; }

    public PreparationResponse Response { get; }
}

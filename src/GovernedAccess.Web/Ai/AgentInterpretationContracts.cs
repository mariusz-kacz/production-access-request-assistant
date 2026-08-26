using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace GovernedAccess.Web.Ai;

internal sealed record AgentExecutionLimits
{
    private const string ConfigurationSection =
        "TargetRequestPreparationAgent:Limits";

    internal const int HardMaximumMessageCharacters = 4000;
    internal const int HardMaximumCallsPerTool = 1;
    internal const int HardMaximumToolCalls = 4;
    internal const int HardMaximumProviderIterations = 6;
    internal static readonly TimeSpan HardMaximumCumulativeTimeout =
        TimeSpan.FromSeconds(30);

    internal static AgentExecutionLimits Default { get; } = new(
        HardMaximumMessageCharacters,
        HardMaximumCallsPerTool,
        HardMaximumToolCalls,
        HardMaximumProviderIterations,
        HardMaximumCumulativeTimeout);

    internal AgentExecutionLimits(
        int maximumMessageCharacters,
        int maximumCallsPerTool,
        int maximumToolCalls,
        int maximumProviderIterations,
        TimeSpan cumulativeTimeout)
    {
        MaximumMessageCharacters = ValidateBound(
            maximumMessageCharacters,
            HardMaximumMessageCharacters,
            nameof(maximumMessageCharacters));
        MaximumCallsPerTool = ValidateBound(
            maximumCallsPerTool,
            HardMaximumCallsPerTool,
            nameof(maximumCallsPerTool));
        MaximumToolCalls = ValidateBound(
            maximumToolCalls,
            HardMaximumToolCalls,
            nameof(maximumToolCalls));
        MaximumProviderIterations = ValidateBound(
            maximumProviderIterations,
            HardMaximumProviderIterations,
            nameof(maximumProviderIterations));
        if (cumulativeTimeout <= TimeSpan.Zero
            || cumulativeTimeout > HardMaximumCumulativeTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(cumulativeTimeout));
        }

        CumulativeTimeout = cumulativeTimeout;
    }

    internal int MaximumMessageCharacters { get; }
    internal int MaximumCallsPerTool { get; }
    internal int MaximumToolCalls { get; }
    internal int MaximumProviderIterations { get; }
    internal TimeSpan CumulativeTimeout { get; }

    internal static AgentExecutionLimits Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ConfigurationSection);

        try
        {
            return new AgentExecutionLimits(
                RequiredInt(section, nameof(MaximumMessageCharacters)),
                RequiredInt(section, nameof(MaximumCallsPerTool)),
                RequiredInt(section, nameof(MaximumToolCalls)),
                RequiredInt(section, nameof(MaximumProviderIterations)),
                RequiredTimeSpan(section, nameof(CumulativeTimeout)));
        }
        catch (ArgumentException exception)
        {
            throw InvalidConfiguration(exception);
        }
    }

    private static int ValidateBound(int value, int maximum, string parameterName)
    {
        if (value <= 0 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static int RequiredInt(IConfiguration section, string name)
    {
        var value = section[name];
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException(name);
        }

        return parsed;
    }

    private static TimeSpan RequiredTimeSpan(IConfiguration section, string name)
    {
        var value = section[name];
        if (!TimeSpan.TryParse(
                value,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException(name);
        }

        return parsed;
    }

    private static InvalidOperationException InvalidConfiguration(
        Exception innerException) =>
        new(
            "Target request-preparation agent execution limits are missing or invalid.",
            innerException);
}

internal sealed record AgentModelMetadata
{
    internal AgentModelMetadata(
        string providerId,
        string modelDeployment,
        string? providerModelVersion)
    {
        ProviderId = NormalizeRequired(providerId, nameof(providerId));
        ModelDeployment = NormalizeRequired(modelDeployment, nameof(modelDeployment));
        ProviderModelVersion = string.IsNullOrWhiteSpace(providerModelVersion)
            ? null
            : NormalizeRequired(providerModelVersion, nameof(providerModelVersion));
    }

    internal string ProviderId { get; }
    internal string ModelDeployment { get; }
    internal string? ProviderModelVersion { get; }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        if (value.Length > 200)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}

internal sealed record AgentClarificationChoice
{
    internal AgentClarificationChoice(
        int position,
        string canonicalId,
        string displayName,
        string? clientId,
        string? clientDisplayName,
        string? region,
        EnvironmentClassification? environmentClassification)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (environmentClassification.HasValue
            && !Enum.IsDefined(environmentClassification.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(environmentClassification));
        }

        var hasEnvironmentFields = clientId is not null
            || clientDisplayName is not null
            || region is not null
            || environmentClassification is not null;
        if (hasEnvironmentFields
            && (string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(clientDisplayName)
                || string.IsNullOrWhiteSpace(region)
                || environmentClassification is null))
        {
            throw new ArgumentException(
                "Environment clarification display fields must be complete.");
        }

        Position = position;
        CanonicalId = canonicalId.Trim();
        DisplayName = displayName.Trim();
        ClientId = clientId?.Trim();
        ClientDisplayName = clientDisplayName?.Trim();
        Region = region?.Trim();
        EnvironmentClassification = environmentClassification;
    }

    internal int Position { get; }
    internal string CanonicalId { get; }
    internal string DisplayName { get; }
    internal string? ClientId { get; }
    internal string? ClientDisplayName { get; }
    internal string? Region { get; }
    internal EnvironmentClassification? EnvironmentClassification { get; }
}

internal sealed record AgentClarificationContext
{
    internal AgentClarificationContext(
        ClarificationTarget target,
        DateTimeOffset createdAt,
        IEnumerable<AgentClarificationChoice> choices)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentNullException.ThrowIfNull(choices);
        var values = choices.ToArray();
        if (values.Length is < 1 or > RequestPreparation.MaximumClarificationChoices
            || values.Any(static value => value is null)
            || values.Select(static value => value.Position)
                .SequenceEqual(Enumerable.Range(1, values.Length)) is false)
        {
            throw new ArgumentOutOfRangeException(nameof(choices));
        }

        var choicesMatchTarget = target switch
        {
            ClarificationTarget.Environment => values.All(
                static choice => choice.EnvironmentClassification is not null),
            ClarificationTarget.Role => values.All(
                static choice => choice.EnvironmentClassification is null),
            _ => false,
        };
        if (!choicesMatchTarget)
        {
            throw new ArgumentException(
                "Agent clarification choices must match their target.",
                nameof(choices));
        }

        Target = target;
        CreatedAt = createdAt.ToUniversalTime();
        Choices = Array.AsReadOnly(values);
    }

    internal ClarificationTarget Target { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal IReadOnlyList<AgentClarificationChoice> Choices { get; }
}

internal sealed record AgentTurnInput
{
    internal AgentTurnInput(
        string latestRequesterText,
        PreparationCandidate candidate,
        PreparationLifecycle lifecycle,
        AgentClarificationContext? clarification,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestRequesterText);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Enum.IsDefined(lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        LatestRequesterText = latestRequesterText;
        Candidate = candidate;
        Lifecycle = lifecycle;
        Clarification = clarification;
        CorrelationId = correlationId.Trim();
    }

    internal string LatestRequesterText { get; }
    internal PreparationCandidate Candidate { get; }
    internal PreparationLifecycle Lifecycle { get; }
    internal AgentClarificationContext? Clarification { get; }
    internal string CorrelationId { get; }
}

internal sealed record AgentExecutionMetadata(
    string ProviderId,
    string ModelDeployment,
    string? ProviderModelVersion,
    string PromptContractVersion,
    string StructuredOutputSchemaVersion,
    string McpContractVersion,
    string EnvironmentSearchPolicyVersion,
    int ProviderIterationCount,
    int ToolCallCount,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

internal enum AgentInterpretationFailure
{
    InvalidInput,
    MalformedModelOutput,
    ExecutionBudgetExceeded,
    Timeout,
    Unavailable,
}

internal abstract record AgentInterpretationResult(AgentExecutionMetadata ExecutionMetadata);

internal sealed record AgentInterpretationSucceeded(
    TurnProposal Proposal,
    AgentExecutionMetadata ExecutionMetadata)
    : AgentInterpretationResult(ExecutionMetadata);

internal sealed record AgentInterpretationFailed(
    AgentInterpretationFailure Failure,
    AgentExecutionMetadata ExecutionMetadata)
    : AgentInterpretationResult(ExecutionMetadata);

internal interface ITurnProposalInterpreter
{
    Task<AgentInterpretationResult> InterpretAsync(
        AgentTurnInput turn,
        CancellationToken cancellationToken);
}

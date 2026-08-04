using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Provider-neutral input for one conversational request-preparation turn. It contains
/// only the compact application-owned state needed to interpret the latest message.
/// </summary>
public sealed record RequestPreparationTurn
{
    public RequestPreparationTurn(
        Guid intakeId,
        string latestMessage,
        RequestCandidate candidate,
        string correlationId)
    {
        if (intakeId == Guid.Empty)
        {
            throw new ArgumentException(
                "The intake identifier must not be empty.",
                nameof(intakeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(latestMessage);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        IntakeId = intakeId;
        LatestMessage = latestMessage.Trim();
        Candidate = candidate;
        CorrelationId = correlationId.Trim();
    }

    public Guid IntakeId { get; }

    public string LatestMessage { get; }

    public RequestCandidate Candidate { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// A complete-shape, nullable candidate proposed by an untrusted interpreter.
/// Deterministic application validation remains responsible for readiness,
/// canonicalization, and authoritative relationship checks.
/// </summary>
public sealed record RequestCandidate
{
    public RequestCandidate(
        string? clientId,
        string? environmentId,
        string? requestedRoleId,
        string? justification,
        string? incidentId)
    {
        clientId = NormalizeOptional(clientId);
        environmentId = NormalizeOptional(environmentId);
        requestedRoleId = NormalizeOptional(requestedRoleId);
        justification = NormalizeOptional(justification);
        incidentId = NormalizeOptional(incidentId);

        if (requestedRoleId is not null
            && !ProductionRoleIds.IsSupported(requestedRoleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRoleId),
                requestedRoleId,
                "The proposed role is not supported by this feature.");
        }

        if (justification?.Length > AccessRequest.MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"A proposed justification cannot exceed {AccessRequest.MaximumJustificationLength} characters.");
        }

        ClientId = clientId;
        EnvironmentId = environmentId;
        RequestedRoleId = requestedRoleId;
        Justification = justification;
        IncidentId = incidentId;
    }

    public string? ClientId { get; }

    public string? EnvironmentId { get; }

    public string? RequestedRoleId { get; }

    public string? Justification { get; }

    public string? IncidentId { get; }

    public bool IsStructurallyComplete =>
        ClientId is not null
        && EnvironmentId is not null
        && RequestedRoleId is not null
        && Justification is not null;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum RequestPreparationProposalKind
{
    Clarification,
    Candidate,
}

public enum RequestClarificationTarget
{
    ClientId,
    EnvironmentId,
    RequestedRoleId,
    Justification,
    IncidentId,
}

/// <summary>
/// One closed, bounded clarification proposed by an untrusted interpreter. Choices
/// remain in process-local model history and are deliberately absent from this
/// application contract.
/// </summary>
public sealed record RequestClarificationProposal
{
    public const int MaximumMessageLength = 500;

    public RequestClarificationProposal(
        RequestClarificationTarget target,
        string message)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        message = message.Trim();
        if (message.Length > MaximumMessageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                message.Length,
                $"A clarification message cannot exceed {MaximumMessageLength} characters.");
        }

        Target = target;
        Message = message;
    }

    public RequestClarificationTarget Target { get; }

    public string Message { get; }
}

/// <summary>
/// A closed interpreter proposal. Every proposal carries the complete nullable
/// candidate shape and either one bounded typed clarification or no clarification.
/// </summary>
public sealed record RequestPreparationProposal
{
    public RequestPreparationProposal(
        RequestPreparationProposalKind kind,
        RequestCandidate candidate,
        RequestClarificationProposal? clarification)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if ((kind == RequestPreparationProposalKind.Clarification
                && clarification is null)
            || (kind == RequestPreparationProposalKind.Candidate
                && clarification is not null))
        {
            throw new ArgumentException(
                "The proposal kind and clarification do not form a valid closed proposal.",
                nameof(clarification));
        }

        Kind = kind;
        Candidate = candidate;
        Clarification = clarification;
    }

    public RequestPreparationProposalKind Kind { get; }

    public RequestCandidate Candidate { get; }

    public RequestClarificationProposal? Clarification { get; }
}

public enum RequestPreparationInterpretationFailure
{
    MalformedModelOutput,
    Timeout,
    Cancelled,
    Unavailable,
}

/// <summary>
/// A closed interpreter result that keeps provider failures and untrusted proposals
/// outside deterministic preparation decisions.
/// </summary>
public abstract record RequestPreparationInterpretationResult
{
    private protected RequestPreparationInterpretationResult()
    {
    }
}

public sealed record RequestPreparationInterpretationSucceeded
    : RequestPreparationInterpretationResult
{
    public RequestPreparationInterpretationSucceeded(
        RequestPreparationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        Proposal = proposal;
    }

    public RequestPreparationProposal Proposal { get; }
}

public sealed record RequestPreparationInterpretationFailed
    : RequestPreparationInterpretationResult
{
    public RequestPreparationInterpretationFailed(
        RequestPreparationInterpretationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        Failure = failure;
    }

    public RequestPreparationInterpretationFailure Failure { get; }
}

/// <summary>
/// Interprets one conversational turn without exposing AI-provider or MCP SDK
/// contracts to the application core.
/// </summary>
public interface IRequestPreparationInterpreter
{
    Task<RequestPreparationInterpretationResult> InterpretAsync(
        RequestPreparationTurn turn,
        CancellationToken cancellationToken);
}

using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Provider-neutral input for one conversational request-preparation turn. It contains
/// only the compact application-owned state needed to interpret the latest message.
/// </summary>
public sealed record RequestPreparationTurn
{
    public RequestPreparationTurn(
        string latestMessage,
        RequestCandidate candidate,
        RequestClarificationContext? pendingClarification,
        string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestMessage);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        LatestMessage = latestMessage.Trim();
        Candidate = candidate;
        PendingClarification = pendingClarification;
        CorrelationId = correlationId.Trim();
    }

    public string LatestMessage { get; }

    public RequestCandidate Candidate { get; }

    public RequestClarificationContext? PendingClarification { get; }

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

/// <summary>
/// A closed interpreter proposal. Every proposal carries the complete nullable
/// candidate shape and either one bounded typed clarification or no clarification.
/// </summary>
public sealed record RequestPreparationProposal
{
    public RequestPreparationProposal(
        RequestPreparationProposalKind kind,
        RequestCandidate candidate,
        RequestClarificationContext? clarification)
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
                "The proposal kind and clarification context do not form a valid closed proposal.",
                nameof(clarification));
        }

        Kind = kind;
        Candidate = candidate;
        Clarification = clarification;
    }

    public RequestPreparationProposalKind Kind { get; }

    public RequestCandidate Candidate { get; }

    public RequestClarificationContext? Clarification { get; }
}

public enum RequestPreparationInterpretationOutcomeKind
{
    Proposal,
    MalformedModelOutput,
    Timeout,
    Cancelled,
    Unavailable,
}

/// <summary>
/// A typed interpreter result that keeps provider failures and untrusted proposals
/// outside deterministic preparation decisions.
/// </summary>
public sealed record RequestPreparationInterpretationOutcome
{
    public RequestPreparationInterpretationOutcome(
        RequestPreparationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        Kind = RequestPreparationInterpretationOutcomeKind.Proposal;
        Proposal = proposal;
    }

    public RequestPreparationInterpretationOutcome(
        RequestPreparationInterpretationOutcomeKind failureKind)
    {
        if (!Enum.IsDefined(failureKind)
            || failureKind == RequestPreparationInterpretationOutcomeKind.Proposal)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        Kind = failureKind;
    }

    public RequestPreparationInterpretationOutcomeKind Kind { get; }

    public RequestPreparationProposal? Proposal { get; }
}

/// <summary>
/// Interprets one conversational turn without exposing AI-provider or MCP SDK
/// contracts to the application core.
/// </summary>
public interface IRequestPreparationInterpreter
{
    Task<RequestPreparationInterpretationOutcome> InterpretAsync(
        RequestPreparationTurn turn,
        CancellationToken cancellationToken);
}

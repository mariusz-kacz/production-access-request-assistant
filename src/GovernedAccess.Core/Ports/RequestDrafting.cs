using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Provider-neutral input for one stateless draft-interpretation operation.
/// </summary>
public sealed record DraftInterpretationRequest
{
    public DraftInterpretationRequest(string intent, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Intent = intent.Trim();
        CorrelationId = correlationId.Trim();
    }

    public string Intent { get; }

    public string CorrelationId { get; }
}

/// <summary>
/// An untrusted structured draft proposed during model-assisted preparation.
/// Structural completeness does not replace server-side validation before request
/// submission.
/// </summary>
public sealed record AccessRequestDraft
{
    public AccessRequestDraft(
        string? clientId,
        string? environmentId,
        string? requestedRole,
        string? justification,
        string? incidentId)
    {
        clientId = NormalizeOptional(clientId);
        environmentId = NormalizeOptional(environmentId);
        requestedRole = NormalizeOptional(requestedRole);
        justification = NormalizeOptional(justification);
        incidentId = NormalizeOptional(incidentId);

        if (requestedRole is not null && !ProductionRoleIds.IsSupported(requestedRole))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRole),
                requestedRole,
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
        RequestedRole = requestedRole;
        Justification = justification;
        IncidentId = incidentId;
    }

    public string? ClientId { get; }

    public string? EnvironmentId { get; }

    public string? RequestedRole { get; }

    public string? Justification { get; }

    public string? IncidentId { get; }

    public bool IsComplete =>
        ClientId is not null
        && EnvironmentId is not null
        && RequestedRole is not null
        && Justification is not null;

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public enum DraftInterpretationOutcomeKind
{
    Prepared,
    Incomplete,
    MalformedModelOutput,
    Timeout,
    Cancelled,
    Unavailable,
}

/// <summary>
/// A closed, safe-to-present result from draft interpretation.
/// </summary>
public sealed record DraftInterpretationOutcome
{
    public DraftInterpretationOutcome(AccessRequestDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Draft = draft;
        Kind = draft.IsComplete
            ? DraftInterpretationOutcomeKind.Prepared
            : DraftInterpretationOutcomeKind.Incomplete;
    }

    public DraftInterpretationOutcome(DraftInterpretationOutcomeKind failureKind)
    {
        if (failureKind is
            DraftInterpretationOutcomeKind.Prepared or
            DraftInterpretationOutcomeKind.Incomplete)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        Kind = failureKind;
    }

    public DraftInterpretationOutcomeKind Kind { get; }

    public AccessRequestDraft? Draft { get; }
}

/// <summary>
/// Interprets request intent as an untrusted typed draft without exposing an AI or
/// MCP SDK contract to the application core.
/// </summary>
public interface IRequestDraftInterpreter
{
    Task<DraftInterpretationOutcome> InterpretAsync(
        DraftInterpretationRequest request,
        CancellationToken cancellationToken);
}

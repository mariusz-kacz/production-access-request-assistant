namespace GovernedAccess.Core.Domain;

public enum RequestStatus
{
    AwaitingBusinessApproval,
    AwaitingDevOpsApproval,
    Rejected,
    ProvisioningFailed,
    Active,
}

public static class AccessRequestNormalization
{
    public static string NormalizeIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    public static string? NormalizeOptionalIdentifier(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string NormalizeJustification(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}

public sealed class AccessRequest
{
    public const int MinimumJustificationLength = 10;

    public const int MaximumJustificationLength = 2000;

    public AccessRequest(
        Guid id,
        string requesterId,
        string clientId,
        string environmentId,
        string requestedRoleId,
        int requestedDurationMinutes,
        string justification,
        string? incidentId,
        DateTimeOffset createdAt,
        string correlationId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The request identifier must not be empty.", nameof(id));
        }

        if (requestedDurationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedDurationMinutes),
                requestedDurationMinutes,
                "The requested duration must be positive.");
        }

        requesterId = AccessRequestNormalization.NormalizeIdentifier(requesterId);
        clientId = AccessRequestNormalization.NormalizeIdentifier(clientId);
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        requestedRoleId = AccessRequestNormalization.NormalizeIdentifier(requestedRoleId);
        justification = AccessRequestNormalization.NormalizeJustification(justification);
        incidentId = AccessRequestNormalization.NormalizeOptionalIdentifier(incidentId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        if (!ProductionRoleIds.IsSupported(requestedRoleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRoleId),
                requestedRoleId,
                "The requested role is not supported by this feature.");
        }

        if (justification.Length is < MinimumJustificationLength or > MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"The justification must be between {MinimumJustificationLength} and {MaximumJustificationLength} characters.");
        }

        Id = id;
        RequesterId = requesterId;
        ClientId = clientId;
        EnvironmentId = environmentId;
        RequestedRoleId = requestedRoleId;
        RequestedDurationMinutes = requestedDurationMinutes;
        Justification = justification;
        IncidentId = incidentId;
        Status = RequestStatus.AwaitingBusinessApproval;
        CreatedAt = createdAt.ToUniversalTime();
        LastModifiedAt = CreatedAt;
        CorrelationId = correlationId;
        PersistenceVersion = 1;
    }

    public Guid Id { get; private set; }

    public string RequesterId { get; private set; }

    public string ClientId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RequestedRoleId { get; private set; }

    public int RequestedDurationMinutes { get; private set; }

    public string Justification { get; private set; }

    public string? IncidentId { get; private set; }

    public RequestStatus Status { get; internal set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastModifiedAt { get; internal set; }

    public string CorrelationId { get; private set; }

    public long PersistenceVersion { get; internal set; }
}

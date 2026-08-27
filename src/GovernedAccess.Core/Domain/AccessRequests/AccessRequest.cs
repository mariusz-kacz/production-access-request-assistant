namespace GovernedAccess.Core.Domain.AccessRequests;

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

    private AccessRequest()
    {
        RequesterId = null!;
        Details = null!;
        CorrelationId = null!;
    }

    public AccessRequest(
        Guid id,
        string requesterId,
        ValidatedRequestDetails details,
        DateTimeOffset createdAt,
        string correlationId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The request identifier must not be empty.", nameof(id));
        }

        requesterId = AccessRequestNormalization.NormalizeIdentifier(requesterId);
        ArgumentNullException.ThrowIfNull(details);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        Id = id;
        RequesterId = requesterId;
        Details = details;
        Status = RequestStatus.AwaitingBusinessApproval;
        CreatedAt = createdAt.ToUniversalTime();
        LastModifiedAt = CreatedAt;
        CorrelationId = correlationId;
        PersistenceVersion = 1;
    }

    public AccessRequest(
        Guid id,
        Guid preparationId,
        string requesterId,
        ValidatedRequestDetails details,
        DateTimeOffset createdAt,
        string correlationId)
        : this(id, requesterId, details, createdAt, correlationId)
    {
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preparation identifier must not be empty.",
                nameof(preparationId));
        }

        PreparationId = preparationId;
    }

    public Guid Id { get; private set; }

    public Guid? PreparationId { get; private set; }

    public string RequesterId { get; private set; }

    public ValidatedRequestDetails Details { get; private set; }

    public RequestStatus Status { get; internal set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastModifiedAt { get; internal set; }

    public string CorrelationId { get; private set; }

    public long PersistenceVersion { get; internal set; }
}

using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;

namespace GovernedAccess.Core.Domain.Drafts;

public enum RequestIntakeStatus
{
    Collecting,
    Ready,
    Submitted,
    Superseded,
    Expired,
    Invalidated,
}

/// <summary>
/// One authenticated request-intake session from collection through confirmation.
/// Candidate scope becomes immutable when ready and is cleared after a terminal
/// transition; binding and request identity remain for safe old-card handling.
/// </summary>
public sealed class RequestIntakeSession
{
    public const string TeamsChannel = "msteams";

    public static readonly TimeSpan ConfirmationLifetime =
        TimeSpan.FromMinutes(30);

    public RequestIntakeSession(
        Guid id,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        DateTimeOffset createdAt,
        string correlationId)
    {
        EnsureNotEmpty(id, nameof(id));
        channel = NormalizeRequired(channel, nameof(channel));

        if (!string.Equals(channel, TeamsChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                $"The intake channel must be '{TeamsChannel}'.");
        }

        Id = id;
        Channel = TeamsChannel;
        TenantId = NormalizeRequired(tenantId, nameof(tenantId));
        ChannelActorId = NormalizeRequired(
            channelActorId,
            nameof(channelActorId));
        ConversationId = NormalizeRequired(conversationId, nameof(conversationId));
        RequesterId = NormalizeRequired(requesterId, nameof(requesterId));
        Status = RequestIntakeStatus.Collecting;
        CreatedAt = createdAt.ToUniversalTime();
        LastUpdatedAt = CreatedAt;
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId));
        PersistenceVersion = 1;
    }

    public Guid Id { get; private set; }

    public string Channel { get; private set; }

    public string TenantId { get; private set; }

    public string ChannelActorId { get; private set; }

    public string ConversationId { get; private set; }

    public string RequesterId { get; private set; }

    public RequestIntakeStatus Status { get; private set; }

    public string? ClientId { get; private set; }

    public string? EnvironmentId { get; private set; }

    public string? RequestedRoleId { get; private set; }

    public string? Justification { get; private set; }

    public string? IncidentId { get; private set; }

    /// <summary>
    /// The canonical prepared snapshot when this intake is ready. Collecting and
    /// terminal sessions deliberately expose no validated details.
    /// </summary>
    public ValidatedRequestDetails? PreparedDetails =>
        Status == RequestIntakeStatus.Ready
            ? ValidatedRequestDetails.RestorePreparedSnapshot(
                ClientId,
                EnvironmentId,
                RequestedRoleId,
                Justification,
                IncidentId)
            : null;

    public Guid? ReservedRequestId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUpdatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string CorrelationId { get; private set; }

    public long PersistenceVersion { get; private set; }

    public bool IsExpired(DateTimeOffset currentTime) =>
        Status == RequestIntakeStatus.Ready
        && ExpiresAt is { } expiresAt
        && currentTime.ToUniversalTime() >= expiresAt;

    public void UpdateCandidate(
        string? clientId,
        string? environmentId,
        string? requestedRoleId,
        string? justification,
        string? incidentId,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestIntakeStatus.Collecting);
        requestedRoleId = NormalizeOptional(requestedRoleId);
        justification = NormalizeOptional(justification);

        if (requestedRoleId is not null
            && !ProductionRoleIds.IsSupported(requestedRoleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRoleId),
                requestedRoleId,
                "The candidate role is not supported.");
        }

        if (justification?.Length > AccessRequest.MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"Justification cannot exceed {AccessRequest.MaximumJustificationLength} characters.");
        }

        var operation = PrepareRecord(occurredAt, correlationId);
        ClientId = NormalizeOptional(clientId);
        EnvironmentId = NormalizeOptional(environmentId);
        RequestedRoleId = requestedRoleId;
        Justification = justification;
        IncidentId = NormalizeOptional(incidentId);
        Record(operation);
    }

    public void MarkReady(
        ValidatedRequestDetails details,
        Guid reservedRequestId,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestIntakeStatus.Collecting);
        ArgumentNullException.ThrowIfNull(details);
        EnsureNotEmpty(reservedRequestId, nameof(reservedRequestId));

        var operation = PrepareRecord(occurredAt, correlationId);
        ClientId = details.ClientId;
        EnvironmentId = details.EnvironmentId;
        RequestedRoleId = details.RoleId;
        Justification = details.Justification;
        IncidentId = details.IncidentId;
        ReservedRequestId = reservedRequestId;
        Status = RequestIntakeStatus.Ready;
        Record(operation);
        ExpiresAt = LastUpdatedAt.Add(ConfirmationLifetime);
    }

    public void MarkSubmitted(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        var operation = PrepareRecord(occurredAt, correlationId);
        EnsureReadyBeforeExpiry(operation.OccurredAt);
        Status = RequestIntakeStatus.Submitted;
        SubmittedAt = operation.OccurredAt;
        ClearCandidate();
        Record(operation);
    }

    public void MarkSuperseded(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        if (Status is not RequestIntakeStatus.Collecting
            and not RequestIntakeStatus.Ready)
        {
            throw InvalidTransition();
        }

        var operation = PrepareRecord(occurredAt, correlationId);
        if (Status == RequestIntakeStatus.Ready
            && IsExpired(operation.OccurredAt))
        {
            throw new InvalidOperationException(
                "An expired intake session cannot be superseded.");
        }

        Status = RequestIntakeStatus.Superseded;
        ClearCandidate();
        Record(operation);
    }

    public void MarkExpired(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestIntakeStatus.Ready);
        if (!IsExpired(occurredAt))
        {
            throw new InvalidOperationException(
                "An intake session cannot expire before its deadline.");
        }

        var operation = PrepareRecord(occurredAt, correlationId);
        Status = RequestIntakeStatus.Expired;
        ClearCandidate();
        Record(operation);
    }

    public void MarkInvalidated(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        var operation = PrepareRecord(occurredAt, correlationId);
        EnsureReadyBeforeExpiry(operation.OccurredAt);
        Status = RequestIntakeStatus.Invalidated;
        ClearCandidate();
        Record(operation);
    }

    private void EnsureReadyBeforeExpiry(DateTimeOffset occurredAt)
    {
        EnsureStatus(RequestIntakeStatus.Ready);
        if (IsExpired(occurredAt))
        {
            throw new InvalidOperationException(
                "An expired intake session cannot be submitted.");
        }
    }

    private (DateTimeOffset OccurredAt, string CorrelationId) PrepareRecord(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        occurredAt = occurredAt.ToUniversalTime();
        if (occurredAt < LastUpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                "An intake transition cannot predate the previous operation.");
        }

        return (
            occurredAt,
            NormalizeRequired(correlationId, nameof(correlationId)));
    }

    private void Record(
        (DateTimeOffset OccurredAt, string CorrelationId) operation)
    {
        LastUpdatedAt = operation.OccurredAt;
        CorrelationId = operation.CorrelationId;
        PersistenceVersion++;
    }

    private void ClearCandidate()
    {
        ClientId = null;
        EnvironmentId = null;
        RequestedRoleId = null;
        Justification = null;
        IncidentId = null;
    }

    private void EnsureStatus(RequestIntakeStatus expected)
    {
        if (Status != expected)
        {
            throw InvalidTransition();
        }
    }

    private InvalidOperationException InvalidTransition() =>
        new($"An intake session in status '{Status}' cannot perform this transition.");

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "The identifier must not be empty.",
                parameterName);
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

namespace GovernedAccess.Core.Domain;

public enum PreparedAccessRequestStatus
{
    Ready,
    Submitted,
    Superseded,
    Expired,
    Invalidated,
}

/// <summary>
/// Immutable, server-owned request scope that is ready for explicit requester
/// confirmation. Only its guarded lifecycle status and submission evidence change.
/// </summary>
public sealed class PreparedAccessRequest
{
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(30);

    public PreparedAccessRequest(
        Guid preparationId,
        Guid conversationRecordId,
        Guid reservedRequestId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        string clientId,
        string environmentId,
        string requestedRoleId,
        string justification,
        string? incidentId,
        DateTimeOffset createdAt,
        string correlationId)
    {
        EnsureNotEmpty(preparationId, nameof(preparationId));
        EnsureNotEmpty(conversationRecordId, nameof(conversationRecordId));
        EnsureNotEmpty(reservedRequestId, nameof(reservedRequestId));

        channel = NormalizeRequired(channel, nameof(channel));
        tenantId = NormalizeRequired(tenantId, nameof(tenantId));
        channelActorId = NormalizeRequired(channelActorId, nameof(channelActorId));
        conversationId = NormalizeRequired(conversationId, nameof(conversationId));
        requesterId = NormalizeRequired(requesterId, nameof(requesterId));
        clientId = NormalizeRequired(clientId, nameof(clientId));
        environmentId = NormalizeRequired(environmentId, nameof(environmentId));
        requestedRoleId = NormalizeRequired(requestedRoleId, nameof(requestedRoleId));
        justification = NormalizeRequired(justification, nameof(justification));
        incidentId = NormalizeOptional(incidentId);
        correlationId = NormalizeRequired(correlationId, nameof(correlationId));

        if (!string.Equals(
                channel,
                RequestPreparationConversation.TeamsChannel,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                $"The preparation channel must be '{RequestPreparationConversation.TeamsChannel}'.");
        }

        if (!ProductionRoleIds.IsSupported(requestedRoleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRoleId),
                requestedRoleId,
                "The prepared role is not supported by this feature.");
        }

        if (justification.Length is
            < AccessRequest.MinimumJustificationLength
            or > AccessRequest.MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"The justification must be between {AccessRequest.MinimumJustificationLength} and {AccessRequest.MaximumJustificationLength} characters.");
        }

        PreparationId = preparationId;
        ConversationRecordId = conversationRecordId;
        ReservedRequestId = reservedRequestId;
        Channel = RequestPreparationConversation.TeamsChannel;
        TenantId = tenantId;
        ChannelActorId = channelActorId;
        ConversationId = conversationId;
        RequesterId = requesterId;
        ClientId = clientId;
        EnvironmentId = environmentId;
        RequestedRoleId = requestedRoleId;
        Justification = justification;
        IncidentId = incidentId;
        Status = PreparedAccessRequestStatus.Ready;
        CreatedAt = createdAt.ToUniversalTime();
        ExpiresAt = CreatedAt.Add(ConfirmationLifetime);
        CorrelationId = correlationId;
        PersistenceVersion = 1;
    }

    public Guid PreparationId { get; private set; }

    public Guid ConversationRecordId { get; private set; }

    public Guid ReservedRequestId { get; private set; }

    public string Channel { get; private set; }

    public string TenantId { get; private set; }

    public string ChannelActorId { get; private set; }

    public string ConversationId { get; private set; }

    public string RequesterId { get; private set; }

    public string ClientId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RequestedRoleId { get; private set; }

    public string Justification { get; private set; }

    public string? IncidentId { get; private set; }

    public PreparedAccessRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedRequestId { get; private set; }

    public string CorrelationId { get; private set; }

    public long PersistenceVersion { get; private set; }

    public bool IsOwnedBy(
        string? channel,
        string? tenantId,
        string? channelActorId,
        string? conversationId,
        string? requesterId)
    {
        return Matches(Channel, channel)
            && Matches(TenantId, tenantId)
            && Matches(ChannelActorId, channelActorId)
            && Matches(ConversationId, conversationId)
            && Matches(RequesterId, requesterId);
    }

    public bool IsExpired(DateTimeOffset currentTime) =>
        currentTime.ToUniversalTime() >= ExpiresAt;

    public void MarkSubmitted(DateTimeOffset submittedAt)
    {
        EnsureReady();
        submittedAt = ValidateTransitionTime(submittedAt);

        if (submittedAt >= ExpiresAt)
        {
            throw new InvalidOperationException(
                "An expired prepared request cannot be submitted.");
        }

        Status = PreparedAccessRequestStatus.Submitted;
        SubmittedAt = submittedAt;
        SubmittedRequestId = ReservedRequestId;
        PersistenceVersion++;
    }

    public void MarkSuperseded(DateTimeOffset occurredAt)
    {
        EnsureReady();
        _ = ValidateTransitionTime(occurredAt);

        Status = PreparedAccessRequestStatus.Superseded;
        PersistenceVersion++;
    }

    public void MarkExpired(DateTimeOffset occurredAt)
    {
        EnsureReady();
        occurredAt = ValidateTransitionTime(occurredAt);

        if (occurredAt < ExpiresAt)
        {
            throw new InvalidOperationException(
                "A prepared request cannot expire before its confirmation deadline.");
        }

        Status = PreparedAccessRequestStatus.Expired;
        PersistenceVersion++;
    }

    public void MarkInvalidated(DateTimeOffset occurredAt)
    {
        EnsureReady();
        _ = ValidateTransitionTime(occurredAt);

        Status = PreparedAccessRequestStatus.Invalidated;
        PersistenceVersion++;
    }

    private void EnsureReady()
    {
        if (Status != PreparedAccessRequestStatus.Ready)
        {
            throw new InvalidOperationException(
                $"A prepared request in status '{Status}' cannot transition again.");
        }
    }

    private DateTimeOffset ValidateTransitionTime(DateTimeOffset occurredAt)
    {
        occurredAt = occurredAt.ToUniversalTime();
        if (occurredAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "A prepared-request transition cannot predate its creation.");
        }

        return occurredAt;
    }

    private static bool Matches(string expected, string? actual) =>
        !string.IsNullOrWhiteSpace(actual)
        && string.Equals(expected, actual.Trim(), StringComparison.Ordinal);

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

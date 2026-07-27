namespace GovernedAccess.Core.Domain;

public enum RequestPreparationConversationStatus
{
    Collecting,
    Ready,
    Submitted,
    Superseded,
    Expired,
}

/// <summary>
/// Compact application-owned preparation state for one authenticated channel actor
/// and personal conversation. It never stores raw messages or model responses.
/// </summary>
public sealed class RequestPreparationConversation
{
    public const string TeamsChannel = "msteams";

    public const int MaximumPendingClarificationLength = 500;

    public RequestPreparationConversation(
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
        tenantId = NormalizeRequired(tenantId, nameof(tenantId));
        channelActorId = NormalizeRequired(channelActorId, nameof(channelActorId));
        conversationId = NormalizeRequired(conversationId, nameof(conversationId));
        requesterId = NormalizeRequired(requesterId, nameof(requesterId));
        correlationId = NormalizeRequired(correlationId, nameof(correlationId));

        if (!string.Equals(channel, TeamsChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                $"The preparation channel must be '{TeamsChannel}'.");
        }

        Id = id;
        Channel = TeamsChannel;
        TenantId = tenantId;
        ChannelActorId = channelActorId;
        ConversationId = conversationId;
        RequesterId = requesterId;
        Status = RequestPreparationConversationStatus.Collecting;
        CreatedAt = createdAt.ToUniversalTime();
        LastTurnAt = CreatedAt;
        CorrelationId = correlationId;
        PersistenceVersion = 1;
    }

    public Guid Id { get; private set; }

    public string Channel { get; private set; }

    public string TenantId { get; private set; }

    public string ChannelActorId { get; private set; }

    public string ConversationId { get; private set; }

    public string RequesterId { get; private set; }

    public RequestPreparationConversationStatus Status { get; private set; }

    public string? ClientId { get; private set; }

    public string? EnvironmentId { get; private set; }

    public string? RequestedRoleId { get; private set; }

    public string? Justification { get; private set; }

    public string? IncidentId { get; private set; }

    public string? PendingClarification { get; private set; }

    public Guid? ActivePreparationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastTurnAt { get; private set; }

    public string CorrelationId { get; private set; }

    public long PersistenceVersion { get; private set; }

    public void UpdateCandidate(
        string? clientId,
        string? environmentId,
        string? requestedRoleId,
        string? justification,
        string? incidentId,
        string? pendingClarification,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestPreparationConversationStatus.Collecting);

        clientId = NormalizeOptional(clientId);
        environmentId = NormalizeOptional(environmentId);
        requestedRoleId = NormalizeOptional(requestedRoleId);
        justification = NormalizeOptional(justification);
        incidentId = NormalizeOptional(incidentId);
        pendingClarification = NormalizeOptional(pendingClarification);

        if (requestedRoleId is not null
            && !ProductionRoleIds.IsSupported(requestedRoleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRoleId),
                requestedRoleId,
                "The candidate role is not supported by this feature.");
        }

        if (justification?.Length > AccessRequest.MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"A candidate justification cannot exceed {AccessRequest.MaximumJustificationLength} characters.");
        }

        if (pendingClarification?.Length > MaximumPendingClarificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pendingClarification),
                pendingClarification.Length,
                $"A pending clarification cannot exceed {MaximumPendingClarificationLength} characters.");
        }

        var operation = PrepareOperation(occurredAt, correlationId);
        ClientId = clientId;
        EnvironmentId = environmentId;
        RequestedRoleId = requestedRoleId;
        Justification = justification;
        IncidentId = incidentId;
        PendingClarification = pendingClarification;
        RecordOperation(operation);
    }

    public void MarkReady(
        Guid preparationId,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestPreparationConversationStatus.Collecting);
        EnsureNotEmpty(preparationId, nameof(preparationId));

        var operation = PrepareOperation(occurredAt, correlationId);
        Status = RequestPreparationConversationStatus.Ready;
        ActivePreparationId = preparationId;
        PendingClarification = null;
        RecordOperation(operation);
    }

    public void MarkSubmitted(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestPreparationConversationStatus.Ready);

        var operation = PrepareOperation(occurredAt, correlationId);
        Status = RequestPreparationConversationStatus.Submitted;
        ClearActiveContent();
        RecordOperation(operation);
    }

    public void MarkSuperseded(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        if (Status is not RequestPreparationConversationStatus.Collecting
            and not RequestPreparationConversationStatus.Ready)
        {
            throw InvalidTransition(Status);
        }

        var operation = PrepareOperation(occurredAt, correlationId);
        Status = RequestPreparationConversationStatus.Superseded;
        ClearActiveContent();
        RecordOperation(operation);
    }

    public void MarkExpired(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        EnsureStatus(RequestPreparationConversationStatus.Ready);

        var operation = PrepareOperation(occurredAt, correlationId);
        Status = RequestPreparationConversationStatus.Expired;
        ClearActiveContent();
        RecordOperation(operation);
    }

    private (DateTimeOffset OccurredAt, string CorrelationId) PrepareOperation(
        DateTimeOffset occurredAt,
        string correlationId)
    {
        occurredAt = occurredAt.ToUniversalTime();
        if (occurredAt < LastTurnAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "A preparation operation cannot predate the previous turn.");
        }

        return (
            occurredAt,
            NormalizeRequired(correlationId, nameof(correlationId)));
    }

    private void RecordOperation(
        (DateTimeOffset OccurredAt, string CorrelationId) operation)
    {
        LastTurnAt = operation.OccurredAt;
        CorrelationId = operation.CorrelationId;
        PersistenceVersion++;
    }

    private void ClearActiveContent()
    {
        ClientId = null;
        EnvironmentId = null;
        RequestedRoleId = null;
        Justification = null;
        IncidentId = null;
        PendingClarification = null;
    }

    private void EnsureStatus(RequestPreparationConversationStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw InvalidTransition(Status);
        }
    }

    private static InvalidOperationException InvalidTransition(
        RequestPreparationConversationStatus status) =>
        new($"A preparation conversation in status '{status}' cannot perform this transition.");

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

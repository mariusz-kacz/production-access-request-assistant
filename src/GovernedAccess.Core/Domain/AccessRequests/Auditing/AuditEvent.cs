using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovernedAccess.Core.Domain.AccessRequests;

public sealed class AuditEvent
{
    private static readonly JsonSerializerOptions DetailsSerializerOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<RequestStatus>(),
            new JsonStringEnumConverter<ApprovalStage>(),
            new JsonStringEnumConverter<ApprovalOutcome>(),
            new JsonStringEnumConverter<ProvisioningOperationStatus>(),
        },
    };

    private AuditEvent(
        Guid id,
        Guid requestId,
        AuditEventType eventType,
        string? actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        string outcomeCode,
        string detailsJson)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        WorkflowEvidenceValidation.EnsureDefined(eventType, nameof(eventType));
        actorId = AccessRequestNormalization.NormalizeOptionalIdentifier(actorId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);
        outcomeCode = AccessRequestNormalization.NormalizeIdentifier(outcomeCode);
        detailsJson = WorkflowEvidenceValidation.EnsureJsonObject(detailsJson);

        Id = id;
        RequestId = requestId;
        EventType = eventType;
        ActorId = actorId;
        OccurredAt = occurredAt.ToUniversalTime();
        CorrelationId = correlationId;
        OutcomeCode = outcomeCode;
        DetailsJson = detailsJson;
    }

    public static AuditEvent CreateRequestCreated(
        Guid id,
        AccessRequest request,
        RequestCreatedAuditDetails details)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(details);

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.RequestCreated,
            request.RequesterId,
            request.CreatedAt,
            request.CorrelationId,
            "request_created",
            JsonSerializer.Serialize(details, DetailsSerializerOptions));
    }

    public static AuditEvent CreateBusinessDecision(
        Guid id,
        AccessRequest request,
        ApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var details = new BusinessDecisionAuditDetails(decision, request.Status);
        var outcomeCode = decision.Decision == ApprovalOutcome.Approved
            ? "business_decision_approved"
            : "business_decision_rejected";

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.BusinessDecision,
            decision.ApproverId,
            decision.DecidedAt,
            decision.CorrelationId,
            outcomeCode,
            JsonSerializer.Serialize(details, DetailsSerializerOptions));
    }

    public static AuditEvent CreateDevOpsDecision(
        Guid id,
        AccessRequest request,
        ApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var details = new DevOpsDecisionAuditDetails(decision, request.Status);
        var outcomeCode = decision.Decision == ApprovalOutcome.Approved
            ? "devops_decision_approved"
            : "devops_decision_rejected";

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.DevOpsDecision,
            decision.ApproverId,
            decision.DecidedAt,
            decision.CorrelationId,
            outcomeCode,
            JsonSerializer.Serialize(details, DetailsSerializerOptions));
    }

    public static AuditEvent CreateProvisioningAttempted(
        Guid id,
        AccessRequest request,
        ApprovalDecision devOpsDecision,
        ProvisioningOperation operation,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(devOpsDecision);
        ArgumentNullException.ThrowIfNull(operation);

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.ProvisioningAttempted,
            devOpsDecision.ApproverId,
            occurredAt,
            devOpsDecision.CorrelationId,
            "provisioning_attempted",
            JsonSerializer.Serialize(
                new ProvisioningAuditDetails(operation),
                DetailsSerializerOptions));
    }

    public static AuditEvent CreateProvisioningSucceeded(
        Guid id,
        AccessRequest request,
        ApprovalDecision devOpsDecision,
        ProvisioningOperation operation,
        AccessGrant grant,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(devOpsDecision);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(grant);

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.ProvisioningSucceeded,
            devOpsDecision.ApproverId,
            occurredAt,
            devOpsDecision.CorrelationId,
            "provisioning_succeeded",
            JsonSerializer.Serialize(
                new ProvisioningAuditDetails(operation, grant),
                DetailsSerializerOptions));
    }

    public static AuditEvent CreateProvisioningFailed(
        Guid id,
        AccessRequest request,
        ApprovalDecision devOpsDecision,
        ProvisioningOperation operation,
        DateTimeOffset occurredAt,
        string outcomeCode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(devOpsDecision);
        ArgumentNullException.ThrowIfNull(operation);

        return new AuditEvent(
            id,
            request.Id,
            AuditEventType.ProvisioningFailed,
            devOpsDecision.ApproverId,
            occurredAt,
            devOpsDecision.CorrelationId,
            outcomeCode,
            JsonSerializer.Serialize(
                new ProvisioningAuditDetails(operation),
                DetailsSerializerOptions));
    }

    public static AuditEvent CreateAuthorizationRejected(
        Guid id,
        AccessRequest request,
        ApprovalStage stage,
        string actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        string outcomeCode)
    {
        return CreateRejectedDecisionAttempt(
            id,
            request,
            AuditEventType.AuthorizationRejected,
            stage,
            actorId,
            occurredAt,
            correlationId,
            outcomeCode);
    }

    public static AuditEvent CreateInvalidTransitionRejected(
        Guid id,
        AccessRequest request,
        ApprovalStage stage,
        string actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        string outcomeCode)
    {
        return CreateRejectedDecisionAttempt(
            id,
            request,
            AuditEventType.InvalidTransitionRejected,
            stage,
            actorId,
            occurredAt,
            correlationId,
            outcomeCode);
    }

    private static AuditEvent CreateRejectedDecisionAttempt(
        Guid id,
        AccessRequest request,
        AuditEventType eventType,
        ApprovalStage stage,
        string actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        string outcomeCode)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = new DecisionAttemptRejectedAuditDetails(stage, request.Status);
        return new AuditEvent(
            id,
            request.Id,
            eventType,
            actorId,
            occurredAt,
            correlationId,
            outcomeCode,
            JsonSerializer.Serialize(details, DetailsSerializerOptions));
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public AuditEventType EventType { get; private set; }

    public string? ActorId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string CorrelationId { get; private set; }

    public string OutcomeCode { get; private set; }

    public string DetailsJson { get; private set; }
}

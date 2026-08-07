using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovernedAccess.Core.Domain;

public enum ApprovalStage
{
    Business,
    DevOps,
}

public enum ApprovalOutcome
{
    Approved,
    Rejected,
}

public enum ProvisioningOperationStatus
{
    Pending,
    Succeeded,
    Failed,
}

public enum AccessGrantOutcome
{
    Succeeded,
}

public enum AuditEventType
{
    RequestCreated,
    ValidationFailed,
    BusinessDecision,
    DevOpsDecision,
    AuthorizationRejected,
    InvalidTransitionRejected,
    ProvisioningAttempted,
    ProvisioningSucceeded,
    ProvisioningFailed,
    DuplicateRetryReturned,
}

public sealed record RequestCreatedAuditDetails
{
    public const int CurrentSchemaVersion = 1;

    public const string SuccessfulValidationOutcomeCode = "request_validation_succeeded";

    public RequestCreatedAuditDetails(RequestStatus status)
    {
        SchemaVersion = CurrentSchemaVersion;
        ValidationOutcomeCode = SuccessfulValidationOutcomeCode;
        Status = status;
    }

    public int SchemaVersion { get; }

    public string ValidationOutcomeCode { get; }

    public RequestStatus Status { get; }
}

public sealed record BusinessDecisionAuditDetails
{
    public const int CurrentSchemaVersion = 2;

    public BusinessDecisionAuditDetails(
        ApprovalDecision decision,
        RequestStatus status)
    {
        ArgumentNullException.ThrowIfNull(decision);

        SchemaVersion = CurrentSchemaVersion;
        Decision = decision.Decision;
        Status = status;
    }

    public int SchemaVersion { get; }

    public ApprovalOutcome Decision { get; }

    public RequestStatus Status { get; }
}

public sealed record DevOpsDecisionAuditDetails
{
    public const int CurrentSchemaVersion = 2;

    public DevOpsDecisionAuditDetails(
        ApprovalDecision decision,
        RequestStatus status)
    {
        ArgumentNullException.ThrowIfNull(decision);

        SchemaVersion = CurrentSchemaVersion;
        Decision = decision.Decision;
        Status = status;
    }

    public int SchemaVersion { get; }

    public ApprovalOutcome Decision { get; }

    public RequestStatus Status { get; }
}

public sealed record DecisionAttemptRejectedAuditDetails
{
    public const int CurrentSchemaVersion = 1;

    public DecisionAttemptRejectedAuditDetails(
        ApprovalStage stage,
        RequestStatus status)
    {
        SchemaVersion = CurrentSchemaVersion;
        Stage = stage;
        Status = status;
    }

    public int SchemaVersion { get; }

    public ApprovalStage Stage { get; }

    public RequestStatus Status { get; }
}

public sealed record ProvisioningAuditDetails
{
    public const int CurrentSchemaVersion = 3;

    public ProvisioningAuditDetails(
        ProvisioningOperation operation,
        AccessGrant? grant = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        SchemaVersion = CurrentSchemaVersion;
        Status = operation.Status;
        AttemptCount = operation.AttemptCount;
        GrantId = grant?.Id;
    }

    public int SchemaVersion { get; }

    public ProvisioningOperationStatus Status { get; }

    public int AttemptCount { get; }

    public Guid? GrantId { get; }
}

public sealed class ApprovalDecision
{
    public const int MaximumCommentLength = 1000;

    public ApprovalDecision(
        Guid id,
        Guid requestId,
        ApprovalStage stage,
        ApprovalOutcome decision,
        string approverId,
        string? comment,
        DateTimeOffset decidedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        WorkflowEvidenceValidation.EnsureDefined(stage, nameof(stage));
        WorkflowEvidenceValidation.EnsureDefined(decision, nameof(decision));

        approverId = AccessRequestNormalization.NormalizeIdentifier(approverId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);
        comment = WorkflowEvidenceValidation.NormalizeOptionalText(
            comment,
            MaximumCommentLength,
            nameof(comment));

        Id = id;
        RequestId = requestId;
        Stage = stage;
        Decision = decision;
        ApproverId = approverId;
        Comment = comment;
        DecidedAt = decidedAt.ToUniversalTime();
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public ApprovalStage Stage { get; private set; }

    public ApprovalOutcome Decision { get; private set; }

    public string ApproverId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string CorrelationId { get; private set; }
}

public sealed class ProvisioningOperation
{
    public ProvisioningOperation(
        Guid requestId,
        DateTimeOffset createdAt)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));

        RequestId = requestId;
        Status = ProvisioningOperationStatus.Pending;
        AttemptCount = 1;
        CreatedAt = createdAt.ToUniversalTime();
        LastAttemptAt = CreatedAt;
    }

    public Guid RequestId { get; private set; }

    public ProvisioningOperationStatus Status { get; internal set; }

    public int AttemptCount { get; internal set; }

    public string? LastOutcomeCode { get; internal set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastAttemptAt { get; internal set; }
}

public sealed class AccessGrant
{
    public static readonly TimeSpan FixedLifetime = TimeSpan.FromHours(8);

    public AccessGrant(
        Guid id,
        Guid requestId,
        DateTimeOffset activatedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        Id = id;
        RequestId = requestId;
        ActivatedAt = activatedAt.ToUniversalTime();
        ExpiresAt = ActivatedAt.Add(FixedLifetime);
        Outcome = AccessGrantOutcome.Succeeded;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public DateTimeOffset ActivatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public AccessGrantOutcome Outcome { get; private set; }

    public string CorrelationId { get; private set; }

    public bool IsExpired(DateTimeOffset currentTime)
    {
        return currentTime.ToUniversalTime() >= ExpiresAt;
    }
}

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

internal static class WorkflowEvidenceValidation
{
    public static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier must not be empty.", parameterName);
        }
    }

    public static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The enumeration value is not supported.");
        }
    }

    public static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"The value must not exceed {maximumLength} characters.");
        }

        return value;
    }

    public static string EnsureJsonObject(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        using var document = JsonDocument.Parse(value);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Audit details must be a JSON object.", nameof(value));
        }

        return value;
    }
}

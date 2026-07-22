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
        if (status != RequestStatus.AwaitingBusinessApproval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A newly submitted request must await business approval.");
        }

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
    public const int CurrentSchemaVersion = 1;

    public BusinessDecisionAuditDetails(
        ApprovalDecision decision,
        RequestStatus status)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Stage != ApprovalStage.Business)
        {
            throw new ArgumentException(
                "Business decision audit details require a business-stage decision.",
                nameof(decision));
        }

        var expectedStatus = decision.Decision == ApprovalOutcome.Approved
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        if (status != expectedStatus)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The audit status must match the business decision outcome.");
        }

        SchemaVersion = CurrentSchemaVersion;
        Decision = decision.Decision;
        Status = status;
        ApprovedRoleId = decision.ApprovedRoleId;
    }

    public int SchemaVersion { get; }

    public ApprovalOutcome Decision { get; }

    public RequestStatus Status { get; }

    public string? ApprovedRoleId { get; }

}

public sealed record DevOpsDecisionAuditDetails
{
    public const int CurrentSchemaVersion = 1;

    public DevOpsDecisionAuditDetails(
        ApprovalDecision decision,
        RequestStatus status)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Stage != ApprovalStage.DevOps)
        {
            throw new ArgumentException(
                "DevOps decision audit details require a DevOps-stage decision.",
                nameof(decision));
        }

        var expectedStatus = decision.Decision == ApprovalOutcome.Approved
            ? RequestStatus.AwaitingDevOpsApproval
            : RequestStatus.Rejected;
        if (status != expectedStatus)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The audit status must match the DevOps decision outcome.");
        }

        SchemaVersion = CurrentSchemaVersion;
        Decision = decision.Decision;
        Status = status;
        ApprovedRoleId = decision.ApprovedRoleId;
    }

    public int SchemaVersion { get; }

    public ApprovalOutcome Decision { get; }

    public RequestStatus Status { get; }

    public string? ApprovedRoleId { get; }
}

public sealed record DecisionAttemptRejectedAuditDetails
{
    public const int CurrentSchemaVersion = 1;

    public DecisionAttemptRejectedAuditDetails(
        ApprovalStage stage,
        RequestStatus status)
    {
        WorkflowEvidenceValidation.EnsureDefined(stage, nameof(stage));
        WorkflowEvidenceValidation.EnsureDefined(status, nameof(status));

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
    public const int CurrentSchemaVersion = 2;

    public ProvisioningAuditDetails(
        ProvisioningOperation operation,
        AccessGrant? grant = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (grant is not null && grant.RequestId != operation.RequestId)
        {
            throw new ArgumentException(
                "The grant must belong to the provisioning operation.",
                nameof(grant));
        }

        SchemaVersion = CurrentSchemaVersion;
        Status = operation.Status;
        AttemptCount = operation.AttemptCount;
        EnvironmentId = operation.EnvironmentId;
        RoleId = operation.RoleId;
        GrantId = grant?.Id;
    }

    public int SchemaVersion { get; }

    public ProvisioningOperationStatus Status { get; }

    public int AttemptCount { get; }

    public string EnvironmentId { get; }

    public string RoleId { get; }

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
        string? approvedRoleId,
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

        if (decision == ApprovalOutcome.Approved)
        {
            approvedRoleId = AccessRequestNormalization.NormalizeIdentifier(approvedRoleId!);

            if (!ProductionRoleIds.IsSupported(approvedRoleId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(approvedRoleId),
                    approvedRoleId,
                    "The approved role is not supported by this feature.");
            }

        }
        else if (approvedRoleId is not null)
        {
            throw new ArgumentException("A rejection must not carry an approved role.");
        }

        Id = id;
        RequestId = requestId;
        Stage = stage;
        Decision = decision;
        ApproverId = approverId;
        ApprovedRoleId = approvedRoleId;
        Comment = comment;
        DecidedAt = decidedAt.ToUniversalTime();
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public ApprovalStage Stage { get; private set; }

    public ApprovalOutcome Decision { get; private set; }

    public string ApproverId { get; private set; }

    public string? ApprovedRoleId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string CorrelationId { get; private set; }
}

public sealed class ProvisioningOperation
{
    public ProvisioningOperation(
        Guid requestId,
        string environmentId,
        string roleId,
        DateTimeOffset createdAt)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The provisioning role is not supported by this feature.");
        }

        RequestId = requestId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        Status = ProvisioningOperationStatus.Pending;
        AttemptCount = 1;
        CreatedAt = createdAt.ToUniversalTime();
        LastAttemptAt = CreatedAt;
    }

    public Guid RequestId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

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
        string requesterId,
        string environmentId,
        string roleId,
        DateTimeOffset activatedAt,
        string correlationId)
    {
        WorkflowEvidenceValidation.EnsureNotEmpty(id, nameof(id));
        WorkflowEvidenceValidation.EnsureNotEmpty(requestId, nameof(requestId));
        requesterId = AccessRequestNormalization.NormalizeIdentifier(requesterId);
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        correlationId = AccessRequestNormalization.NormalizeIdentifier(correlationId);

        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The granted role is not supported by this feature.");
        }

        Id = id;
        RequestId = requestId;
        RequesterId = requesterId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        ActivatedAt = activatedAt.ToUniversalTime();
        ExpiresAt = ActivatedAt.Add(FixedLifetime);
        Outcome = AccessGrantOutcome.Succeeded;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public string RequesterId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

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

        if (request.Status != details.Status)
        {
            throw new ArgumentException(
                "The audit details status must match the submitted request status.",
                nameof(details));
        }

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

        if (decision.RequestId != request.Id)
        {
            throw new ArgumentException(
                "The decision must belong to the audited request.",
                nameof(decision));
        }

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

        if (decision.RequestId != request.Id)
        {
            throw new ArgumentException(
                "The decision must belong to the audited request.",
                nameof(decision));
        }

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
        ValidateProvisioningEvidence(request, devOpsDecision, operation);

        if (operation.Status == ProvisioningOperationStatus.Succeeded)
        {
            throw new ArgumentException(
                "A completed operation cannot record a new provisioning attempt.",
                nameof(operation));
        }

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
        ValidateProvisioningEvidence(request, devOpsDecision, operation);
        ArgumentNullException.ThrowIfNull(grant);

        if (request.Status != RequestStatus.Active ||
            operation.Status != ProvisioningOperationStatus.Succeeded)
        {
            throw new ArgumentException(
                "Successful provisioning evidence requires an active request and succeeded operation.");
        }

        if (grant.RequestId != request.Id ||
            grant.RequesterId != request.RequesterId ||
            grant.EnvironmentId != operation.EnvironmentId ||
            grant.RoleId != operation.RoleId)
        {
            throw new ArgumentException(
                "The grant scope must match the successful provisioning operation.",
                nameof(grant));
        }

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
        ValidateProvisioningEvidence(request, devOpsDecision, operation);

        if (request.Status != RequestStatus.ProvisioningFailed ||
            operation.Status != ProvisioningOperationStatus.Failed ||
            operation.LastOutcomeCode != outcomeCode)
        {
            throw new ArgumentException(
                "Failed provisioning evidence requires matching failed request and operation state.");
        }

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

        if (eventType is not AuditEventType.AuthorizationRejected
            and not AuditEventType.InvalidTransitionRejected)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The event type is not a rejected decision attempt.");
        }

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

    private static void ValidateProvisioningEvidence(
        AccessRequest request,
        ApprovalDecision devOpsDecision,
        ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(devOpsDecision);
        ArgumentNullException.ThrowIfNull(operation);

        if (devOpsDecision.RequestId != request.Id ||
            devOpsDecision.Stage != ApprovalStage.DevOps ||
            devOpsDecision.Decision != ApprovalOutcome.Approved ||
            operation.RequestId != request.Id ||
            operation.EnvironmentId != request.EnvironmentId ||
            operation.RoleId != request.RequestedRoleId ||
            devOpsDecision.ApprovedRoleId != operation.RoleId)
        {
            throw new ArgumentException(
                "Provisioning evidence must match the approved immutable request scope.");
        }
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

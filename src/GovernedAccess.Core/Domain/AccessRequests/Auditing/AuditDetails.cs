namespace GovernedAccess.Core.Domain.AccessRequests;

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

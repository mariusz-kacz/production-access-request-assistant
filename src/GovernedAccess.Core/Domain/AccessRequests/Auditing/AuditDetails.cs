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



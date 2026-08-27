using System.Text.Json.Serialization;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

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

    public const int PreparationSchemaVersion = 2;

    public const string SuccessfulValidationOutcomeCode = "request_validation_succeeded";

    public RequestCreatedAuditDetails(RequestStatus status)
    {
        SchemaVersion = CurrentSchemaVersion;
        ValidationOutcomeCode = SuccessfulValidationOutcomeCode;
        Status = status;
    }

    private RequestCreatedAuditDetails(
        RequestStatus status,
        Guid preparationId,
        IReadOnlyList<RequestMaterialChangeAuditDetails> materialChanges)
    {
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request-created preparation evidence requires a preparation identifier.",
                nameof(preparationId));
        }

        ArgumentNullException.ThrowIfNull(materialChanges);
        SchemaVersion = PreparationSchemaVersion;
        ValidationOutcomeCode = SuccessfulValidationOutcomeCode;
        Status = status;
        PreparationId = preparationId;
        MaterialChanges = materialChanges;
    }

    public int SchemaVersion { get; }

    public string ValidationOutcomeCode { get; }

    public RequestStatus Status { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? PreparationId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RequestMaterialChangeAuditDetails>? MaterialChanges { get; }

    public static RequestCreatedAuditDetails FromPreparation(
        RequestStatus status,
        RequestPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        return new RequestCreatedAuditDetails(
            status,
            preparation.PreparationId,
            preparation.MaterialChangeAttributions
                .Select(static attribution =>
                    new RequestMaterialChangeAuditDetails(attribution))
                .ToArray());
    }
}

public sealed record RequestMaterialChangeAuditDetails
{
    internal RequestMaterialChangeAuditDetails(
        MaterialChangeAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        Fields = attribution.Fields;
        ModelDeployment = attribution.ModelDeployment;
        ProviderModelVersion = attribution.ProviderModelVersion;
        PromptContractVersion = attribution.PromptContractVersion;
        StructuredOutputSchemaVersion = attribution.StructuredOutputSchemaVersion;
    }

    public IReadOnlyList<ProposalField> Fields { get; }

    public string ModelDeployment { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderModelVersion { get; }

    public string PromptContractVersion { get; }

    public string StructuredOutputSchemaVersion { get; }
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

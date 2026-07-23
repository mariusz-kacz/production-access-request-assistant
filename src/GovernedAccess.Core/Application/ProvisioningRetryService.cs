using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public abstract record ProvisioningRetryOutcome;

public sealed record ProvisioningRetryCompleted(
    AccessRequest Request,
    ProvisioningOperation Operation,
    AccessGrant Grant) : ProvisioningRetryOutcome;

public sealed record ProvisioningRetryFailed(ApplicationFailure Failure)
    : ProvisioningRetryOutcome;

/// <summary>
/// Authorizes a retry from authenticated server context and delegates persisted
/// evidence validation and idempotent provider work to the protected handler.
/// </summary>
public sealed class ProvisioningRetryService
{
    public const string NotAuthorizedCode = "provisioning_retry_not_authorized";

    public const string InvalidTransitionCode =
        "provisioning_retry_invalid_transition";

    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly WorkflowCommandContextLoader commandContextLoader;
    private readonly RejectedWorkflowAttemptRecorder rejectedAttemptRecorder;

    public ProvisioningRetryService(
        ProtectedProvisioningService protectedProvisioning,
        WorkflowCommandContextLoader commandContextLoader,
        RejectedWorkflowAttemptRecorder rejectedAttemptRecorder)
    {
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(commandContextLoader);
        ArgumentNullException.ThrowIfNull(rejectedAttemptRecorder);

        this.protectedProvisioning = protectedProvisioning;
        this.commandContextLoader = commandContextLoader;
        this.rejectedAttemptRecorder = rejectedAttemptRecorder;
    }

    public async Task<ProvisioningRetryOutcome> RetryAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return new ProvisioningRetryFailed(commandContextResult.Failure!);
        }

        var commandContext = commandContextResult.Value;
        var request = commandContext.Request;
        var principal = commandContext.Principal;
        var normalizedCorrelationId = commandContext.CorrelationId;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            return new ProvisioningRetryFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.Unauthorized,
                        NotAuthorizedCode,
                        "Only the authenticated DevOps approver can retry provisioning."),
                    WorkflowAttemptRejectionKind.Authorization,
                    cancellationToken));
        }

        if (request.Status is not (
                RequestStatus.ProvisioningFailed or
                RequestStatus.Active))
        {
            return new ProvisioningRetryFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.InvalidTransition,
                        InvalidTransitionCode,
                        "Only a request with failed provisioning can be retried."),
                    WorkflowAttemptRejectionKind.InvalidTransition,
                    cancellationToken));
        }

        var outcome = await protectedProvisioning.RetryAsync(
            request.Id,
            cancellationToken);
        return outcome switch
        {
            ProtectedProvisioningCompleted completed =>
                new ProvisioningRetryCompleted(
                    completed.Request,
                    completed.Operation,
                    completed.Grant),
            ProtectedProvisioningFailed failed =>
                new ProvisioningRetryFailed(failed.Failure),
            _ => throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported."),
        };
    }

}

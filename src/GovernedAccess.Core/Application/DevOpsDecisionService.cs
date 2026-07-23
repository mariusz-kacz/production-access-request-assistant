using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public abstract record DevOpsDecisionOutcome;

public sealed record DevOpsDecisionCompleted(
    AccessRequest Request,
    ApprovalDecision Decision,
    ProvisioningOperation? Operation,
    AccessGrant? Grant) : DevOpsDecisionOutcome;

public sealed record DevOpsDecisionFailed(ApplicationFailure Failure)
    : DevOpsDecisionOutcome;

/// <summary>
/// Records an authenticated DevOps decision before invoking the protected
/// provisioning handler, which owns the resulting success or retryable failure
/// transition.
/// </summary>
public sealed class DevOpsDecisionService
{
    public const string ApproverNotAuthorizedCode =
        "devops_approver_not_authorized";

    public const string DuplicateDecisionCode =
        "devops_decision_already_recorded";

    public const string InvalidTransitionCode =
        "devops_decision_invalid_transition";

    public const string InvalidBusinessApprovalCode =
        "devops_business_approval_invalid";

    public const string BusinessApprovalScopeMismatchCode =
        "devops_business_approval_scope_mismatch";

    public const string RequestContextInvalidCode =
        "devops_request_context_invalid";

    private readonly IWorkflowStore workflowStore;
    private readonly RequestValidator requestValidator;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly WorkflowCommandContextLoader commandContextLoader;
    private readonly RejectedWorkflowAttemptRecorder rejectedAttemptRecorder;
    private readonly IClock clock;

    public DevOpsDecisionService(
        IWorkflowStore workflowStore,
        RequestValidator requestValidator,
        ProtectedProvisioningService protectedProvisioning,
        WorkflowCommandContextLoader commandContextLoader,
        RejectedWorkflowAttemptRecorder rejectedAttemptRecorder,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(commandContextLoader);
        ArgumentNullException.ThrowIfNull(rejectedAttemptRecorder);
        ArgumentNullException.ThrowIfNull(clock);

        this.workflowStore = workflowStore;
        this.requestValidator = requestValidator;
        this.protectedProvisioning = protectedProvisioning;
        this.commandContextLoader = commandContextLoader;
        this.rejectedAttemptRecorder = rejectedAttemptRecorder;
        this.clock = clock;
    }

    public async Task<DevOpsDecisionOutcome> DecideAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        ApprovalOutcome decision,
        string? comment,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(decision))
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "devops_decision_invalid",
                "The DevOps decision must be approve or reject.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        if (normalizedComment?.Length > ApprovalDecision.MaximumCommentLength)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "devops_decision_comment_too_long",
                $"The comment must not exceed {ApprovalDecision.MaximumCommentLength} characters.");
        }

        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return new DevOpsDecisionFailed(commandContextResult.Failure!);
        }

        var commandContext = commandContextResult.Value;
        var request = commandContext.Request;
        var principal = commandContext.Principal;
        var normalizedCorrelationId = commandContext.CorrelationId;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            var failure = new ApplicationFailure(
                ApplicationFailureKind.Unauthorized,
                ApproverNotAuthorizedCode,
                "Only the authenticated DevOps approver can decide this request.");
            return new DevOpsDecisionFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    WorkflowAttemptRejectionKind.Authorization,
                    cancellationToken));
        }

        var currentContextFailure = await ValidateCurrentContextAsync(
            request,
            cancellationToken);
        if (currentContextFailure is not null)
        {
            return currentContextFailure.Kind is
                ApplicationFailureKind.DependencyUnavailable or
                ApplicationFailureKind.DependencyFailure or
                ApplicationFailureKind.Timeout or
                ApplicationFailureKind.Cancelled
                ? new DevOpsDecisionFailed(currentContextFailure)
                : new DevOpsDecisionFailed(
                    await rejectedAttemptRecorder.RecordAsync(
                        request,
                        ApprovalStage.DevOps,
                        principal.Id,
                        normalizedCorrelationId,
                        currentContextFailure,
                        WorkflowAttemptRejectionKind.InvalidTransition,
                        cancellationToken));
        }

        var businessApprovalResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.Business,
            cancellationToken);
        if (businessApprovalResult.IsFailure)
        {
            if (businessApprovalResult.Failure!.Kind != ApplicationFailureKind.NotFound)
            {
                return new DevOpsDecisionFailed(businessApprovalResult.Failure);
            }

            return new DevOpsDecisionFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.InvalidTransition,
                        InvalidBusinessApprovalCode,
                        "A valid business approval is required before a DevOps decision."),
                    WorkflowAttemptRejectionKind.InvalidTransition,
                    cancellationToken));
        }

        var existingDecisionResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.DevOps,
            cancellationToken);
        var hasExistingDecision = existingDecisionResult.IsSuccess;
        if (existingDecisionResult.IsFailure
            && existingDecisionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return new DevOpsDecisionFailed(existingDecisionResult.Failure);
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var policyResult = DevOpsDecisionPolicy.Apply(
            request,
            businessApprovalResult.Value,
            new DevOpsDecisionCommand(
                Guid.NewGuid(),
                decision,
                principal.Id,
                normalizedComment,
                occurredAt,
                normalizedCorrelationId),
            hasExistingDecision);

        if (policyResult is DevOpsDecisionNotApplied notApplied)
        {
            var failure = MapPolicyFailure(notApplied.Error);
            return new DevOpsDecisionFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    WorkflowAttemptRejectionKind.InvalidTransition,
                    cancellationToken));
        }

        if (policyResult is not DevOpsDecisionApplied applied)
        {
            throw new InvalidOperationException(
                "The DevOps decision policy outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        workflowStore.AddApprovalDecision(applied.Decision);
        if (applied.Operation is not null)
        {
            workflowStore.AddProvisioningOperation(applied.Operation);
        }

        workflowStore.AddAuditEvent(AuditEvent.CreateDevOpsDecision(
            Guid.NewGuid(),
            request,
            applied.Decision));

        var decisionSaveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        if (decisionSaveResult.IsFailure)
        {
            return new DevOpsDecisionFailed(decisionSaveResult.Failure!);
        }

        if (applied.Operation is null)
        {
            return new DevOpsDecisionCompleted(
                request,
                applied.Decision,
                Operation: null,
                Grant: null);
        }

        var provisioningOutcome = await protectedProvisioning.ProvisionAsync(
            request.Id,
            cancellationToken);
        if (provisioningOutcome is ProtectedProvisioningCompleted completed)
        {
            return new DevOpsDecisionCompleted(
                completed.Request,
                applied.Decision,
                completed.Operation,
                completed.Grant);
        }

        if (provisioningOutcome is not ProtectedProvisioningFailed provisioningFailed)
        {
            throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported.");
        }

        return new DevOpsDecisionFailed(provisioningFailed.Failure);
    }

    private async Task<ApplicationFailure?> ValidateCurrentContextAsync(
        AccessRequest request,
        CancellationToken cancellationToken)
    {
        var validationOutcome = await requestValidator.ValidateAsync(
            new RequestValidationInput(
                request.ClientId,
                request.EnvironmentId,
                request.RequestedRoleId,
                request.Justification,
                request.IncidentId),
            cancellationToken);

        if (validationOutcome is RequestValidationFailed validationFailed)
        {
            return validationFailed.Failure;
        }

        if (validationOutcome is not RequestValidationSucceeded validationSucceeded)
        {
            return InvalidCurrentContext();
        }

        var fields = validationSucceeded.Fields;
        return fields.ClientId == request.ClientId
            && fields.EnvironmentId == request.EnvironmentId
            && fields.RequestedRoleId == request.RequestedRoleId
            && fields.Justification == request.Justification
            && fields.IncidentId == request.IncidentId
            ? null
            : InvalidCurrentContext();
    }

    private static ApplicationFailure MapPolicyFailure(
        DevOpsDecisionPolicyError error)
    {
        return error switch
        {
            DevOpsDecisionPolicyError.DuplicateStage => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                DuplicateDecisionCode,
                "A DevOps decision has already been recorded for this request."),
            DevOpsDecisionPolicyError.InvalidTransition => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                InvalidTransitionCode,
                "The request is not awaiting a DevOps decision."),
            DevOpsDecisionPolicyError.InvalidBusinessApproval => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                InvalidBusinessApprovalCode,
                "The required business approval is invalid."),
            DevOpsDecisionPolicyError.BusinessApprovalScopeMismatch =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    BusinessApprovalScopeMismatchCode,
                    "The business-approved role does not match the immutable request."),
            _ => throw new InvalidOperationException(
                "The DevOps decision policy failure is unsupported."),
        };
    }

    private static ApplicationFailure InvalidCurrentContext()
    {
        return new ApplicationFailure(
            ApplicationFailureKind.InvalidTransition,
            RequestContextInvalidCode,
            "Current request context no longer validates the immutable request.");
    }

    private static DevOpsDecisionFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return new DevOpsDecisionFailed(new ApplicationFailure(kind, code, message));
    }
}

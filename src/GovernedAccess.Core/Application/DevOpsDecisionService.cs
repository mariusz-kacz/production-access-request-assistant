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

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly RequestValidator requestValidator;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly IClock clock;

    public DevOpsDecisionService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        RequestValidator requestValidator,
        ProtectedProvisioningService protectedProvisioning,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.requestValidator = requestValidator;
        this.protectedProvisioning = protectedProvisioning;
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
        if (requestId == Guid.Empty)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "request_id_required",
                "An access request identifier is required.");
        }

        if (!Enum.IsDefined(decision))
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "devops_decision_invalid",
                "The DevOps decision must be approve or reject.");
        }

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated DevOps approver is required.");
        }

        var normalizedCorrelationId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            correlationId);
        if (normalizedCorrelationId is null)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "correlation_id_required",
                "A correlation identifier is required.");
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

        var principalResult = await requestContext.GetPrincipalAsync(
            principalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed(
                    ApplicationFailureKind.Unauthenticated,
                    "authenticated_principal_not_found",
                    "The authenticated principal is unavailable.")
                : new DevOpsDecisionFailed(principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return new DevOpsDecisionFailed(requestResult.Failure!);
        }

        var request = requestResult.Value;
        var principal = principalResult.Value;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            var failure = new ApplicationFailure(
                ApplicationFailureKind.Unauthorized,
                ApproverNotAuthorizedCode,
                "Only the authenticated DevOps approver can decide this request.");
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                failure,
                authorizationRejected: true,
                cancellationToken);
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
                : await RejectAndAuditAsync(
                    request,
                    principal.Id,
                    normalizedCorrelationId,
                    currentContextFailure,
                    authorizationRejected: false,
                    cancellationToken);
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

            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    InvalidBusinessApprovalCode,
                    "A valid business approval is required before a DevOps decision."),
                authorizationRejected: false,
                cancellationToken);
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
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                failure,
                authorizationRejected: false,
                cancellationToken);
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

    private async Task<DevOpsDecisionOutcome> RejectAndAuditAsync(
        AccessRequest request,
        string actorId,
        string correlationId,
        ApplicationFailure failure,
        bool authorizationRejected,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow.ToUniversalTime();
        var auditEvent = authorizationRejected
            ? AuditEvent.CreateAuthorizationRejected(
                Guid.NewGuid(),
                request,
                ApprovalStage.DevOps,
                actorId,
                occurredAt,
                correlationId,
                failure.Code)
            : AuditEvent.CreateInvalidTransitionRejected(
                Guid.NewGuid(),
                request,
                ApprovalStage.DevOps,
                actorId,
                occurredAt,
                correlationId,
                failure.Code);

        workflowStore.AddAuditEvent(auditEvent);
        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new DevOpsDecisionFailed(saveResult.Failure!)
            : new DevOpsDecisionFailed(failure);
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

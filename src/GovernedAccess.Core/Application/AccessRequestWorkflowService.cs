using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record BusinessDecisionResult(
    AccessRequest Request,
    ApprovalDecision Decision);

public sealed record DevOpsDecisionResult(
    AccessRequest Request,
    ApprovalDecision Decision,
    ProvisioningOperation? Operation,
    AccessGrant? Grant);

public sealed record ProvisioningRetryResult(
    AccessRequest Request,
    ProvisioningOperation Operation,
    AccessGrant Grant);

/// <summary>
/// Coordinates the authenticated human commands for the governed access workflow.
/// Domain policies remain deterministic, and protected provisioning independently
/// reloads and validates persisted authorization evidence.
/// </summary>
public sealed class AccessRequestWorkflowService
{
    public const string BusinessApproverNotResponsibleCode =
        "business_approver_not_responsible";

    public const string BusinessDuplicateDecisionCode =
        "business_decision_already_recorded";

    public const string BusinessInvalidTransitionCode =
        "business_decision_invalid_transition";

    public const string DevOpsApproverNotAuthorizedCode =
        "devops_approver_not_authorized";

    public const string DevOpsDuplicateDecisionCode =
        "devops_decision_already_recorded";

    public const string DevOpsInvalidTransitionCode =
        "devops_decision_invalid_transition";

    public const string DevOpsInvalidBusinessApprovalCode =
        "devops_business_approval_invalid";

    public const string DevOpsBusinessApprovalScopeMismatchCode =
        "devops_business_approval_scope_mismatch";

    public const string DevOpsRequestContextInvalidCode =
        "devops_request_context_invalid";

    public const string ProvisioningRetryNotAuthorizedCode =
        "provisioning_retry_not_authorized";

    public const string ProvisioningRetryInvalidTransitionCode =
        "provisioning_retry_invalid_transition";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly RequestValidator requestValidator;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly IClock clock;

    public AccessRequestWorkflowService(
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

    public async Task<ApplicationResult<BusinessDecisionResult>> DecideBusinessAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        ApprovalOutcome decision,
        string? comment,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(decision))
        {
            return Failed<BusinessDecisionResult>(
                ApplicationFailureKind.InvalidInput,
                "business_decision_invalid",
                "The business decision must be approve or reject.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        if (normalizedComment?.Length > ApprovalDecision.MaximumCommentLength)
        {
            return Failed<BusinessDecisionResult>(
                ApplicationFailureKind.InvalidInput,
                "business_decision_comment_too_long",
                $"The comment must not exceed {ApprovalDecision.MaximumCommentLength} characters.");
        }

        var commandContextResult = await LoadCommandContextAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                commandContextResult.Failure!);
        }

        var (request, principal, normalizedCorrelationId) =
            commandContextResult.Value;
        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            request.EnvironmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                environmentResult.Failure!);
        }

        var environment = environmentResult.Value;
        var isResponsibleApprover = principal.Kind == PrincipalKind.BusinessApprover
            && StringComparer.Ordinal.Equals(principal.ClientId, request.ClientId)
            && StringComparer.Ordinal.Equals(environment.ClientId, request.ClientId)
            && StringComparer.Ordinal.Equals(
                environment.BusinessApproverPrincipalId,
                principal.Id);
        if (!isResponsibleApprover)
        {
            var failure = new ApplicationFailure(
                ApplicationFailureKind.Unauthorized,
                BusinessApproverNotResponsibleCode,
                "Only the configured business approver can decide this request.");
            return ApplicationResult.Failed<BusinessDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.Business,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    authorizationRejected: true,
                    cancellationToken));
        }

        var existingDecisionResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.Business,
            cancellationToken);
        var hasExistingDecision = existingDecisionResult.IsSuccess;
        if (existingDecisionResult.IsFailure
            && existingDecisionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                existingDecisionResult.Failure);
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var policyResult = BusinessDecisionPolicy.Apply(
            request,
            new BusinessDecisionCommand(
                Guid.NewGuid(),
                decision,
                principal.Id,
                normalizedComment,
                occurredAt,
                normalizedCorrelationId),
            hasExistingDecision);

        if (policyResult is BusinessDecisionNotApplied notApplied)
        {
            var failure = notApplied.Error switch
            {
                BusinessDecisionPolicyError.DuplicateStage => new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    BusinessDuplicateDecisionCode,
                    "A business decision has already been recorded for this request."),
                BusinessDecisionPolicyError.InvalidTransition => new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    BusinessInvalidTransitionCode,
                    "The request is not awaiting a business decision."),
                _ => throw new InvalidOperationException(
                    "The business decision policy failure is unsupported."),
            };
            return ApplicationResult.Failed<BusinessDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.Business,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    authorizationRejected: false,
                    cancellationToken));
        }

        if (policyResult is not BusinessDecisionApplied applied)
        {
            throw new InvalidOperationException(
                "The business decision policy outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        workflowStore.AddApprovalDecision(applied.Decision);
        workflowStore.AddAuditEvent(AuditEvent.CreateBusinessDecision(
            Guid.NewGuid(),
            request,
            applied.Decision));

        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? ApplicationResult.Failed<BusinessDecisionResult>(saveResult.Failure!)
            : ApplicationResult.Succeeded(
                new BusinessDecisionResult(request, applied.Decision));
    }

    public async Task<ApplicationResult<DevOpsDecisionResult>> DecideDevOpsAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        ApprovalOutcome decision,
        string? comment,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(decision))
        {
            return Failed<DevOpsDecisionResult>(
                ApplicationFailureKind.InvalidInput,
                "devops_decision_invalid",
                "The DevOps decision must be approve or reject.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        if (normalizedComment?.Length > ApprovalDecision.MaximumCommentLength)
        {
            return Failed<DevOpsDecisionResult>(
                ApplicationFailureKind.InvalidInput,
                "devops_decision_comment_too_long",
                $"The comment must not exceed {ApprovalDecision.MaximumCommentLength} characters.");
        }

        var commandContextResult = await LoadCommandContextAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                commandContextResult.Failure!);
        }

        var (request, principal, normalizedCorrelationId) =
            commandContextResult.Value;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            var failure = new ApplicationFailure(
                ApplicationFailureKind.Unauthorized,
                DevOpsApproverNotAuthorizedCode,
                "Only the authenticated DevOps approver can decide this request.");
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    authorizationRejected: true,
                    cancellationToken));
        }

        var currentContextFailure = await ValidateCurrentContextAsync(
            request,
            cancellationToken);
        if (currentContextFailure is not null)
        {
            if (currentContextFailure.Kind is
                ApplicationFailureKind.DependencyUnavailable or
                ApplicationFailureKind.DependencyFailure or
                ApplicationFailureKind.Timeout or
                ApplicationFailureKind.Cancelled)
            {
                return ApplicationResult.Failed<DevOpsDecisionResult>(
                    currentContextFailure);
            }

            return ApplicationResult.Failed<DevOpsDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    currentContextFailure,
                    authorizationRejected: false,
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
                return ApplicationResult.Failed<DevOpsDecisionResult>(
                    businessApprovalResult.Failure);
            }

            return ApplicationResult.Failed<DevOpsDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.InvalidTransition,
                        DevOpsInvalidBusinessApprovalCode,
                        "A valid business approval is required before a DevOps decision."),
                    authorizationRejected: false,
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
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                existingDecisionResult.Failure);
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
            var failure = MapDevOpsPolicyFailure(notApplied.Error);
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    authorizationRejected: false,
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
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                decisionSaveResult.Failure!);
        }

        if (applied.Operation is null)
        {
            return ApplicationResult.Succeeded(
                new DevOpsDecisionResult(
                    request,
                    applied.Decision,
                    Operation: null,
                    Grant: null));
        }

        var provisioningOutcome = await protectedProvisioning.ProvisionAsync(
            request.Id,
            cancellationToken);
        if (provisioningOutcome is ProtectedProvisioningCompleted completed)
        {
            return ApplicationResult.Succeeded(
                new DevOpsDecisionResult(
                    completed.Request,
                    applied.Decision,
                    completed.Operation,
                    completed.Grant));
        }

        if (provisioningOutcome is not ProtectedProvisioningFailed provisioningFailed)
        {
            throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported.");
        }

        return ApplicationResult.Failed<DevOpsDecisionResult>(
            provisioningFailed.Failure);
    }

    public async Task<ApplicationResult<ProvisioningRetryResult>>
        RetryProvisioningAsync(
            Guid requestId,
            string? authenticatedPrincipalId,
            string? correlationId,
            CancellationToken cancellationToken)
    {
        var commandContextResult = await LoadCommandContextAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<ProvisioningRetryResult>(
                commandContextResult.Failure!);
        }

        var (request, principal, normalizedCorrelationId) =
            commandContextResult.Value;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            return ApplicationResult.Failed<ProvisioningRetryResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.Unauthorized,
                        ProvisioningRetryNotAuthorizedCode,
                        "Only the authenticated DevOps approver can retry provisioning."),
                    authorizationRejected: true,
                    cancellationToken));
        }

        if (request.Status != RequestStatus.ProvisioningFailed)
        {
            return ApplicationResult.Failed<ProvisioningRetryResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    ApprovalStage.DevOps,
                    principal.Id,
                    normalizedCorrelationId,
                    new ApplicationFailure(
                        ApplicationFailureKind.InvalidTransition,
                        ProvisioningRetryInvalidTransitionCode,
                        "Only a request with failed provisioning can be retried."),
                    authorizationRejected: false,
                    cancellationToken));
        }

        var provisioningOutcome = await protectedProvisioning.RetryAsync(
            request.Id,
            cancellationToken);
        return provisioningOutcome switch
        {
            ProtectedProvisioningCompleted completed =>
                ApplicationResult.Succeeded(
                    new ProvisioningRetryResult(
                        completed.Request,
                        completed.Operation,
                        completed.Grant)),
            ProtectedProvisioningFailed failed =>
                ApplicationResult.Failed<ProvisioningRetryResult>(failed.Failure),
            _ => throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported."),
        };
    }

    private async Task<ApplicationResult<(
        AccessRequest Request,
        AuthenticatedPrincipal Principal,
        string CorrelationId)>> LoadCommandContextAsync(
            Guid requestId,
            string? authenticatedPrincipalId,
            string? correlationId,
            CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return FailedCommandContext(
                ApplicationFailureKind.InvalidInput,
                "request_id_required",
                "An access request identifier is required.");
        }

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return FailedCommandContext(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated workflow actor is required.");
        }

        var normalizedCorrelationId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            correlationId);
        if (normalizedCorrelationId is null)
        {
            return FailedCommandContext(
                ApplicationFailureKind.InvalidInput,
                "correlation_id_required",
                "A correlation identifier is required.");
        }

        var principalResult = await requestContext.GetPrincipalAsync(
            principalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? FailedCommandContext(
                    ApplicationFailureKind.Unauthenticated,
                    "authenticated_principal_not_found",
                    "The authenticated principal is unavailable.")
                : ApplicationResult.Failed<(
                    AccessRequest,
                    AuthenticatedPrincipal,
                    string)>(principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return ApplicationResult.Failed<(
                AccessRequest,
                AuthenticatedPrincipal,
                string)>(requestResult.Failure!);
        }

        return ApplicationResult.Succeeded((
            requestResult.Value,
            principalResult.Value,
            normalizedCorrelationId));
    }

    private async Task<ApplicationFailure> RecordRejectedAttemptAsync(
        AccessRequest request,
        ApprovalStage stage,
        string actorId,
        string correlationId,
        ApplicationFailure rejection,
        bool authorizationRejected,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow.ToUniversalTime();
        var auditEvent = authorizationRejected
            ? AuditEvent.CreateAuthorizationRejected(
                Guid.NewGuid(),
                request,
                stage,
                actorId,
                occurredAt,
                correlationId,
                rejection.Code)
            : AuditEvent.CreateInvalidTransitionRejected(
                Guid.NewGuid(),
                request,
                stage,
                actorId,
                occurredAt,
                correlationId,
                rejection.Code);

        workflowStore.AddAuditEvent(auditEvent);
        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? saveResult.Failure!
            : rejection;
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

    private static ApplicationFailure MapDevOpsPolicyFailure(
        DevOpsDecisionPolicyError error)
    {
        return error switch
        {
            DevOpsDecisionPolicyError.DuplicateStage => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                DevOpsDuplicateDecisionCode,
                "A DevOps decision has already been recorded for this request."),
            DevOpsDecisionPolicyError.InvalidTransition => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                DevOpsInvalidTransitionCode,
                "The request is not awaiting a DevOps decision."),
            DevOpsDecisionPolicyError.InvalidBusinessApproval => new ApplicationFailure(
                ApplicationFailureKind.InvalidTransition,
                DevOpsInvalidBusinessApprovalCode,
                "The required business approval is invalid."),
            DevOpsDecisionPolicyError.BusinessApprovalScopeMismatch =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    DevOpsBusinessApprovalScopeMismatchCode,
                    "The business-approved role does not match the immutable request."),
            _ => throw new InvalidOperationException(
                "The DevOps decision policy failure is unsupported."),
        };
    }

    private static ApplicationFailure InvalidCurrentContext()
    {
        return new ApplicationFailure(
            ApplicationFailureKind.InvalidTransition,
            DevOpsRequestContextInvalidCode,
            "Current request context no longer validates the immutable request.");
    }

    private static ApplicationResult<T> Failed<T>(
        ApplicationFailureKind kind,
        string code,
        string message)
        where T : notnull
    {
        return ApplicationResult.Failed<T>(
            new ApplicationFailure(kind, code, message));
    }

    private static ApplicationResult<(
        AccessRequest Request,
        AuthenticatedPrincipal Principal,
        string CorrelationId)> FailedCommandContext(
            ApplicationFailureKind kind,
            string code,
            string message)
    {
        return ApplicationResult.Failed<(
            AccessRequest,
            AuthenticatedPrincipal,
            string)>(new ApplicationFailure(kind, code, message));
    }
}

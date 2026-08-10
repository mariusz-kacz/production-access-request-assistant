using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public sealed record BusinessDecisionResult(
    AccessRequest Request,
    ApprovalDecision Decision);

public sealed record DevOpsDecisionResult(
    AccessRequest Request,
    ApprovalDecision Decision,
    ProvisioningOperation? Operation,
    AccessGrant? Grant);

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

    private readonly AccessRequestCommandContextLoader commandContextLoader;
    private readonly IWorkflowStore workflowStore;
    private readonly AccessRequestValidator requestValidator;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly IClock clock;

    public AccessRequestWorkflowService(
        AccessRequestCommandContextLoader commandContextLoader,
        IWorkflowStore workflowStore,
        AccessRequestValidator requestValidator,
        ProtectedProvisioningService protectedProvisioning,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(commandContextLoader);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(clock);

        this.commandContextLoader = commandContextLoader;
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
        var inputResult = NormalizeDecisionInput(
            decision,
            comment,
            "business_decision_invalid",
            "The business decision must be approve or reject.",
            "business_decision_comment_too_long");
        if (inputResult.IsFailure)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                inputResult.Failure!);
        }

        var input = inputResult.Value;

        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                commandContextResult.Failure!);
        }

        var context = commandContextResult.Value;
        var request = context.Request;
        var principal = context.Principal;
        var normalizedCorrelationId = context.CorrelationId;
        var environmentContextResult = await commandContextLoader.LoadEnvironmentContextAsync(
            request,
            cancellationToken);
        if (environmentContextResult.IsFailure)
        {
            return ApplicationResult.Failed<BusinessDecisionResult>(
                environmentContextResult.Failure!);
        }

        var environmentContext = environmentContextResult.Value;
        var environment = environmentContext.Environment;
        var client = environmentContext.Client;
        var isResponsibleApprover = principal.Kind == PrincipalKind.BusinessApprover
            && StringComparer.Ordinal.Equals(principal.ClientId, request.ClientId)
            && StringComparer.Ordinal.Equals(environment.ClientId, request.ClientId)
            && StringComparer.Ordinal.Equals(client.Id, request.ClientId)
            && StringComparer.Ordinal.Equals(
                client.BusinessApproverPrincipalId,
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
            new ApprovalCommand(
                Guid.NewGuid(),
                input.Decision,
                principal.Id,
                input.Comment,
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
        var inputResult = NormalizeDecisionInput(
            decision,
            comment,
            "devops_decision_invalid",
            "The DevOps decision must be approve or reject.",
            "devops_decision_comment_too_long");
        if (inputResult.IsFailure)
        {
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                inputResult.Failure!);
        }

        var input = inputResult.Value;

        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                commandContextResult.Failure!);
        }

        var context = commandContextResult.Value;
        var request = context.Request;
        var principal = context.Principal;
        var normalizedCorrelationId = context.CorrelationId;
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
            new ApprovalCommand(
                Guid.NewGuid(),
                input.Decision,
                principal.Id,
                input.Comment,
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
        if (provisioningOutcome.IsFailure)
        {
            return ApplicationResult.Failed<DevOpsDecisionResult>(
                provisioningOutcome.Failure!);
        }

        var completed = provisioningOutcome.Value;
        return ApplicationResult.Succeeded(
            new DevOpsDecisionResult(
                completed.Request,
                applied.Decision,
                completed.Operation,
                completed.Grant));
    }

    public async Task<ApplicationResult<ProvisioningCompletion>>
        RetryProvisioningAsync(
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
            return ApplicationResult.Failed<ProvisioningCompletion>(
                commandContextResult.Failure!);
        }

        var context = commandContextResult.Value;
        var request = context.Request;
        var principal = context.Principal;
        var normalizedCorrelationId = context.CorrelationId;
        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            return ApplicationResult.Failed<ProvisioningCompletion>(
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
            return ApplicationResult.Failed<ProvisioningCompletion>(
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

        return await protectedProvisioning.RetryAsync(
            request.Id,
            cancellationToken);
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
            AccessRequestValidationInput.From(request),
            cancellationToken);

        if (validationOutcome is AccessRequestValidationFailed validationFailed)
        {
            return validationFailed.Failure;
        }

        if (validationOutcome is not AccessRequestValidationSucceeded validationSucceeded)
        {
            return InvalidCurrentContext();
        }

        return validationSucceeded.Fields.Matches(request)
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

    private static ApplicationResult<NormalizedDecisionInput>
        NormalizeDecisionInput(
            ApprovalOutcome decision,
            string? comment,
            string invalidDecisionCode,
            string invalidDecisionMessage,
            string commentTooLongCode)
    {
        if (!Enum.IsDefined(decision))
        {
            return Failed<NormalizedDecisionInput>(
                ApplicationFailureKind.InvalidInput,
                invalidDecisionCode,
                invalidDecisionMessage);
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        return normalizedComment?.Length > ApprovalDecision.MaximumCommentLength
            ? Failed<NormalizedDecisionInput>(
                ApplicationFailureKind.InvalidInput,
                commentTooLongCode,
                $"The comment must not exceed {ApprovalDecision.MaximumCommentLength} characters.")
            : ApplicationResult.Succeeded(
                new NormalizedDecisionInput(decision, normalizedComment));
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

    private sealed record NormalizedDecisionInput(
        ApprovalOutcome Decision,
        string? Comment);

}

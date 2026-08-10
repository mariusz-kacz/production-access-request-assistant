using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public sealed record ApprovalDecisionResult(
    AccessRequest Request,
    ApprovalDecision Decision,
    ProvisioningOperation? Operation,
    AccessGrant? Grant);

public sealed record ProvisioningRetryResult(
    AccessRequest Request,
    ProvisioningOperation Operation,
    AccessGrant Grant);

/// <summary>
/// Coordinates authenticated human decisions and DevOps provisioning retries.
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

    public const string DevOpsRequestContextInvalidCode =
        "devops_request_context_invalid";

    public const string ProvisioningRetryNotAuthorizedCode =
        "provisioning_retry_not_authorized";

    public const string ProvisioningRetryInvalidTransitionCode =
        "provisioning_retry_invalid_transition";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly AccessRequestCommandContextLoader commandContextLoader;
    private readonly AccessRequestValidator requestValidator;
    private readonly ProtectedProvisioningService protectedProvisioning;
    private readonly IClock clock;

    public AccessRequestWorkflowService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        AccessRequestCommandContextLoader commandContextLoader,
        AccessRequestValidator requestValidator,
        ProtectedProvisioningService protectedProvisioning,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(commandContextLoader);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(protectedProvisioning);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.commandContextLoader = commandContextLoader;
        this.requestValidator = requestValidator;
        this.protectedProvisioning = protectedProvisioning;
        this.clock = clock;
    }

    public async Task<ApplicationResult<ApprovalDecisionResult>> DecideAsync(
        ApprovalStage stage,
        Guid requestId,
        string? authenticatedPrincipalId,
        ApprovalOutcome decision,
        string? comment,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(stage))
        {
            return Failed<ApprovalDecisionResult>(
                ApplicationFailureKind.InvalidInput,
                "approval_stage_invalid",
                "The approval stage is invalid.");
        }

        var inputResult = NormalizeDecisionInput(stage, decision, comment);
        if (inputResult.IsFailure)
        {
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                inputResult.Failure!);
        }

        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                commandContextResult.Failure!);
        }

        var (request, principal, normalizedCorrelationId) =
            commandContextResult.Value;
        var authorizationFailure = await AuthorizeDecisionAsync(
            stage,
            request,
            principal,
            cancellationToken);
        if (authorizationFailure is not null)
        {
            if (authorizationFailure.Kind != ApplicationFailureKind.Unauthorized)
            {
                return ApplicationResult.Failed<ApprovalDecisionResult>(
                    authorizationFailure);
            }

            return ApplicationResult.Failed<ApprovalDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    stage,
                    principal.Id,
                    normalizedCorrelationId,
                    authorizationFailure,
                    authorizationRejected: true,
                    cancellationToken));
        }

        if (stage == ApprovalStage.DevOps)
        {
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
                    return ApplicationResult.Failed<ApprovalDecisionResult>(
                        currentContextFailure);
                }

                return ApplicationResult.Failed<ApprovalDecisionResult>(
                    await RecordRejectedAttemptAsync(
                        request,
                        stage,
                        principal.Id,
                        normalizedCorrelationId,
                        currentContextFailure,
                        authorizationRejected: false,
                        cancellationToken));
            }
        }

        var priorApprovalResult = await LoadPriorApprovalAsync(
            stage,
            request.Id,
            cancellationToken);
        if (priorApprovalResult.IsFailure)
        {
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                priorApprovalResult.Failure!);
        }

        var existingDecisionResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            stage,
            cancellationToken);
        var hasExistingDecision = existingDecisionResult.IsSuccess;
        if (existingDecisionResult.IsFailure
            && existingDecisionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                existingDecisionResult.Failure);
        }

        var input = inputResult.Value;
        var policyResult = ApprovalDecisionPolicy.Apply(
            request,
            stage,
            priorApprovalResult.Value.Decision,
            new ApprovalCommand(
                Guid.NewGuid(),
                input.Decision,
                principal.Id,
                input.Comment,
                clock.UtcNow.ToUniversalTime(),
                normalizedCorrelationId),
            hasExistingDecision);

        if (policyResult is ApprovalDecisionNotApplied notApplied)
        {
            var failure = MapPolicyFailure(stage, notApplied.Error);
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                await RecordRejectedAttemptAsync(
                    request,
                    stage,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    authorizationRejected: false,
                    cancellationToken));
        }

        if (policyResult is not ApprovalDecisionApplied applied)
        {
            throw new InvalidOperationException(
                "The approval decision policy outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        workflowStore.AddApprovalDecision(applied.Decision);
        if (applied.Operation is not null)
        {
            workflowStore.AddProvisioningOperation(applied.Operation);
        }

        workflowStore.AddAuditEvent(
            CreateDecisionAuditEvent(request, applied.Decision));

        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return ApplicationResult.Failed<ApprovalDecisionResult>(
                saveResult.Failure!);
        }

        if (applied.Operation is null)
        {
            return ApplicationResult.Succeeded(
                new ApprovalDecisionResult(
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
                new ApprovalDecisionResult(
                    completed.Request,
                    applied.Decision,
                    completed.Operation,
                    completed.Grant));
        }

        if (provisioningOutcome is not ProtectedProvisioningFailed failed)
        {
            throw new InvalidOperationException(
                "The protected provisioning outcome is unsupported.");
        }

        return ApplicationResult.Failed<ApprovalDecisionResult>(failed.Failure);
    }

    public async Task<ApplicationResult<ProvisioningRetryResult>>
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

    private async Task<ApplicationFailure?> AuthorizeDecisionAsync(
        ApprovalStage stage,
        AccessRequest request,
        AuthenticatedPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (stage == ApprovalStage.DevOps)
        {
            return principal.Kind == PrincipalKind.DevOpsApprover
                ? null
                : new ApplicationFailure(
                    ApplicationFailureKind.Unauthorized,
                    DevOpsApproverNotAuthorizedCode,
                    "Only the authenticated DevOps approver can decide this request.");
        }

        var environmentContextResult =
            await requestContext.GetProductionEnvironmentContextAsync(
                request.Details.EnvironmentId,
                cancellationToken);
        if (environmentContextResult.IsFailure)
        {
            return environmentContextResult.Failure;
        }

        var environmentContext = environmentContextResult.Value;
        var client = environmentContext.Client;
        var isResponsibleApprover = StringComparer.Ordinal.Equals(
                client.Id,
                request.Details.ClientId)
            && principal.IsResponsibleBusinessApproverFor(client);
        return isResponsibleApprover
            ? null
            : new ApplicationFailure(
                ApplicationFailureKind.Unauthorized,
                BusinessApproverNotResponsibleCode,
                "Only the configured business approver can decide this request.");
    }

    private async Task<ApplicationResult<PriorApprovalEvidence>> LoadPriorApprovalAsync(
        ApprovalStage stage,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (stage == ApprovalStage.Business)
        {
            return ApplicationResult.Succeeded(
                new PriorApprovalEvidence(Decision: null));
        }

        var priorApprovalResult = await workflowStore.GetApprovalDecisionAsync(
            requestId,
            ApprovalStage.Business,
            cancellationToken);
        return priorApprovalResult.IsSuccess
            ? ApplicationResult.Succeeded(
                new PriorApprovalEvidence(priorApprovalResult.Value))
            : priorApprovalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? ApplicationResult.Succeeded(
                    new PriorApprovalEvidence(Decision: null))
                : ApplicationResult.Failed<PriorApprovalEvidence>(
                    priorApprovalResult.Failure);
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
        var validationOutcome = await requestValidator.RevalidateAsync(
            request.Details,
            cancellationToken);

        if (validationOutcome is RequestValidationFailed validationFailed)
        {
            return validationFailed.Failure;
        }

        return validationOutcome is RequestValidationSucceeded
            ? null
            : InvalidCurrentContext();
    }

    private static AuditEvent CreateDecisionAuditEvent(
        AccessRequest request,
        ApprovalDecision decision) =>
        decision.Stage switch
        {
            ApprovalStage.Business => AuditEvent.CreateBusinessDecision(
                Guid.NewGuid(),
                request,
                decision),
            ApprovalStage.DevOps => AuditEvent.CreateDevOpsDecision(
                Guid.NewGuid(),
                request,
                decision),
            _ => throw new InvalidOperationException(
                "The approval decision stage is unsupported."),
        };

    private static ApplicationFailure MapPolicyFailure(
        ApprovalStage stage,
        ApprovalDecisionPolicyError error) =>
        (stage, error) switch
        {
            (ApprovalStage.Business, ApprovalDecisionPolicyError.DuplicateStage) =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    BusinessDuplicateDecisionCode,
                    "A business decision has already been recorded for this request."),
            (ApprovalStage.Business, ApprovalDecisionPolicyError.InvalidTransition) =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    BusinessInvalidTransitionCode,
                    "The request is not awaiting a business decision."),
            (ApprovalStage.DevOps, ApprovalDecisionPolicyError.DuplicateStage) =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    DevOpsDuplicateDecisionCode,
                    "A DevOps decision has already been recorded for this request."),
            (ApprovalStage.DevOps, ApprovalDecisionPolicyError.InvalidTransition) =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    DevOpsInvalidTransitionCode,
                    "The request is not awaiting a DevOps decision."),
            (ApprovalStage.DevOps, ApprovalDecisionPolicyError.InvalidPriorApproval) =>
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    DevOpsInvalidBusinessApprovalCode,
                    "A valid business approval is required before a DevOps decision."),
            _ => throw new InvalidOperationException(
                "The approval decision policy failure is unsupported for this stage."),
        };

    private static ApplicationFailure InvalidCurrentContext() =>
        new(
            ApplicationFailureKind.InvalidTransition,
            DevOpsRequestContextInvalidCode,
            "Current request context no longer validates the immutable request.");

    private static ApplicationResult<NormalizedDecisionInput>
        NormalizeDecisionInput(
            ApprovalStage stage,
            ApprovalOutcome decision,
            string? comment)
    {
        var stageName = stage == ApprovalStage.Business ? "business" : "DevOps";
        var codePrefix = stage == ApprovalStage.Business ? "business" : "devops";
        if (!Enum.IsDefined(decision))
        {
            return Failed<NormalizedDecisionInput>(
                ApplicationFailureKind.InvalidInput,
                $"{codePrefix}_decision_invalid",
                $"The {stageName} decision must be approve or reject.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
        return normalizedComment?.Length > ApprovalDecision.MaximumCommentLength
            ? Failed<NormalizedDecisionInput>(
                ApplicationFailureKind.InvalidInput,
                $"{codePrefix}_decision_comment_too_long",
                $"The comment must not exceed {ApprovalDecision.MaximumCommentLength} characters.")
            : ApplicationResult.Succeeded(
                new NormalizedDecisionInput(decision, normalizedComment));
    }

    private static ApplicationResult<T> Failed<T>(
        ApplicationFailureKind kind,
        string code,
        string message)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(kind, code, message));

    private sealed record NormalizedDecisionInput(
        ApprovalOutcome Decision,
        string? Comment);

    private sealed record PriorApprovalEvidence(ApprovalDecision? Decision);
}

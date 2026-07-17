using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public abstract record BusinessDecisionOutcome;

public sealed record BusinessDecisionCompleted(
    AccessRequest Request,
    ApprovalDecision Decision) : BusinessDecisionOutcome;

public sealed record BusinessDecisionFailed(ApplicationFailure Failure)
    : BusinessDecisionOutcome;

/// <summary>
/// Applies a business decision using only authoritative request context and an actor
/// identifier supplied by authenticated server context.
/// </summary>
public sealed class BusinessDecisionService
{
    public const string ApproverNotResponsibleCode =
        "business_approver_not_responsible";

    public const string DuplicateDecisionCode =
        "business_decision_already_recorded";

    public const string InvalidTransitionCode =
        "business_decision_invalid_transition";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly IClock clock;

    public BusinessDecisionService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.clock = clock;
    }

    public async Task<BusinessDecisionOutcome> DecideAsync(
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
                "business_decision_invalid",
                "The business decision must be approve or reject.");
        }

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated business approver is required.");
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
                "business_decision_comment_too_long",
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
                : new BusinessDecisionFailed(principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return new BusinessDecisionFailed(requestResult.Failure!);
        }

        var request = requestResult.Value;
        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            request.EnvironmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return new BusinessDecisionFailed(environmentResult.Failure!);
        }

        var principal = principalResult.Value;
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
                ApproverNotResponsibleCode,
                "Only the configured business approver can decide this request.");
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                failure,
                authorizationRejected: true,
                cancellationToken);
        }

        var existingDecisionResult = await workflowStore.GetApprovalDecisionAsync(
            request.Id,
            ApprovalStage.Business,
            cancellationToken);
        var hasExistingDecision = existingDecisionResult.IsSuccess;
        if (existingDecisionResult.IsFailure
            && existingDecisionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return new BusinessDecisionFailed(existingDecisionResult.Failure);
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
                    DuplicateDecisionCode,
                    "A business decision has already been recorded for this request."),
                BusinessDecisionPolicyError.InvalidTransition => new ApplicationFailure(
                    ApplicationFailureKind.InvalidTransition,
                    InvalidTransitionCode,
                    "The request is not awaiting a business decision."),
                _ => throw new InvalidOperationException(
                    "The business decision policy failure is unsupported."),
            };
            return await RejectAndAuditAsync(
                request,
                principal.Id,
                normalizedCorrelationId,
                failure,
                authorizationRejected: false,
                cancellationToken);
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
            ? new BusinessDecisionFailed(saveResult.Failure!)
            : new BusinessDecisionCompleted(request, applied.Decision);
    }

    private async Task<BusinessDecisionOutcome> RejectAndAuditAsync(
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
                ApprovalStage.Business,
                actorId,
                occurredAt,
                correlationId,
                failure.Code)
            : AuditEvent.CreateInvalidTransitionRejected(
                Guid.NewGuid(),
                request,
                ApprovalStage.Business,
                actorId,
                occurredAt,
                correlationId,
                failure.Code);

        workflowStore.AddAuditEvent(auditEvent);
        var saveResult = await workflowStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new BusinessDecisionFailed(saveResult.Failure!)
            : new BusinessDecisionFailed(failure);
    }

    private static BusinessDecisionFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return new BusinessDecisionFailed(new ApplicationFailure(kind, code, message));
    }
}

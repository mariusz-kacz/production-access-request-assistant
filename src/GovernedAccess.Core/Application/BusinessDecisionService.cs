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
    private readonly WorkflowCommandContextLoader commandContextLoader;
    private readonly RejectedWorkflowAttemptRecorder rejectedAttemptRecorder;
    private readonly IClock clock;

    public BusinessDecisionService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        WorkflowCommandContextLoader commandContextLoader,
        RejectedWorkflowAttemptRecorder rejectedAttemptRecorder,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(commandContextLoader);
        ArgumentNullException.ThrowIfNull(rejectedAttemptRecorder);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.commandContextLoader = commandContextLoader;
        this.rejectedAttemptRecorder = rejectedAttemptRecorder;
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
        if (!Enum.IsDefined(decision))
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "business_decision_invalid",
                "The business decision must be approve or reject.");
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

        var commandContextResult = await commandContextLoader.LoadAsync(
            requestId,
            authenticatedPrincipalId,
            correlationId,
            cancellationToken);
        if (commandContextResult.IsFailure)
        {
            return new BusinessDecisionFailed(commandContextResult.Failure!);
        }

        var commandContext = commandContextResult.Value;
        var request = commandContext.Request;
        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            request.EnvironmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return new BusinessDecisionFailed(environmentResult.Failure!);
        }

        var principal = commandContext.Principal;
        var normalizedCorrelationId = commandContext.CorrelationId;
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
            return new BusinessDecisionFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.Business,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    WorkflowAttemptRejectionKind.Authorization,
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
            return new BusinessDecisionFailed(
                await rejectedAttemptRecorder.RecordAsync(
                    request,
                    ApprovalStage.Business,
                    principal.Id,
                    normalizedCorrelationId,
                    failure,
                    WorkflowAttemptRejectionKind.InvalidTransition,
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
            ? new BusinessDecisionFailed(saveResult.Failure!)
            : new BusinessDecisionCompleted(request, applied.Decision);
    }

    private static BusinessDecisionFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return new BusinessDecisionFailed(new ApplicationFailure(kind, code, message));
    }
}

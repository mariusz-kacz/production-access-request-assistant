using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public sealed record RequestListItemView(
    Guid RequestId,
    string RequesterId,
    string ClientId,
    string EnvironmentId,
    RequestStatus Status,
    DateTimeOffset LastModifiedAt,
    bool Actionable);

/// <summary>
/// Compatibility projection of the validation invariant established at confirmation.
/// It is not a live reference-data health check.
/// </summary>
public sealed record RequestValidationView(
    bool IsValid,
    IReadOnlyList<FieldValidationError> FieldErrors);

public sealed record ApprovalDecisionView(
    Guid DecisionId,
    Guid RequestId,
    ApprovalStage Stage,
    ApprovalOutcome Decision,
    string ApproverId,
    string? ApprovedRoleId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);

public sealed record ProvisioningOperationView(
    Guid RequestId,
    string EnvironmentId,
    string RoleId,
    ProvisioningOperationStatus Status,
    int AttemptCount,
    string? LastOutcomeCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAttemptAt);

public sealed record AccessGrantView(
    Guid GrantId,
    Guid RequestId,
    string RequesterId,
    string EnvironmentId,
    string RoleId,
    DateTimeOffset ActivatedAt,
    DateTimeOffset ExpiresAt,
    AccessGrantOutcome Outcome,
    string CorrelationId,
    bool IsExpired);

public sealed record AuditEventView(
    Guid EventId,
    Guid RequestId,
    AuditEventType EventType,
    string? ActorId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string OutcomeCode,
    string DetailsJson);

public sealed record RequestDetailView(
    Guid RequestId,
    string RequesterId,
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    string Justification,
    string? IncidentId,
    RequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    IReadOnlyList<string> AvailableActions,
    RequestValidationView Validation,
    IReadOnlyList<ApprovalDecisionView> Decisions,
    ProvisioningOperationView? ProvisioningOperation,
    AccessGrantView? Grant,
    IReadOnlyList<AuditEventView> AuditEvents);

/// <summary>
/// Produces participant-authorized request lists, enriched details, and presentation
/// hints from current authoritative state. Available actions never replace command
/// authorization.
/// </summary>
public sealed class AccessRequestQueryService
{
    public const string BusinessDecisionAction =
        AccessRequestVisibilityPolicy.BusinessDecisionAction;
    public const string DevOpsDecisionAction =
        AccessRequestVisibilityPolicy.DevOpsDecisionAction;
    public const string RetryProvisioningAction =
        AccessRequestVisibilityPolicy.RetryProvisioningAction;

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly AccessRequestVisibilityPolicy visibilityPolicy;
    private readonly IClock clock;

    public AccessRequestQueryService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        AccessRequestVisibilityPolicy visibilityPolicy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(visibilityPolicy);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.visibilityPolicy = visibilityPolicy;
        this.clock = clock;
    }

    public async Task<ApplicationResult<IReadOnlyList<RequestListItemView>>> ListAsync(
        string? authenticatedPrincipalId,
        RequestStatus? status,
        CancellationToken cancellationToken)
    {
        if (status is not null && !Enum.IsDefined(status.Value))
        {
            return Failed<IReadOnlyList<RequestListItemView>>(
                ApplicationFailureKind.InvalidInput,
                "request_status_invalid",
                "The request status filter is invalid.");
        }

        var principalResult = await LoadPrincipalAsync(
            authenticatedPrincipalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return ApplicationResult.Failed<IReadOnlyList<RequestListItemView>>(
                principalResult.Failure!);
        }

        var requestsResult = await workflowStore.ListRequestsAsync(cancellationToken);
        if (requestsResult.IsFailure)
        {
            return ApplicationResult.Failed<IReadOnlyList<RequestListItemView>>(
                requestsResult.Failure!);
        }

        var principal = principalResult.Value;
        var items = new List<RequestListItemView>();
        foreach (var request in requestsResult.Value)
        {
            if (status is not null && request.Status != status.Value)
            {
                continue;
            }

            var participantResult = await visibilityPolicy.EvaluateAsync(
                principal,
                request,
                decisions: null,
                cancellationToken);
            if (participantResult.IsFailure)
            {
                return ApplicationResult.Failed<IReadOnlyList<RequestListItemView>>(
                    participantResult.Failure!);
            }

            var participant = participantResult.Value;
            if (!participant.IsParticipant)
            {
                continue;
            }

            items.Add(new RequestListItemView(
                request.Id,
                request.RequesterId,
                request.Details.ClientId,
                request.Details.EnvironmentId,
                request.Status,
                request.LastModifiedAt,
                participant.AvailableActions.Count > 0));
        }

        return ApplicationResult.Succeeded<IReadOnlyList<RequestListItemView>>(items);
    }

    public async Task<ApplicationResult<RequestDetailView>> GetDetailAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return Failed<RequestDetailView>(
                ApplicationFailureKind.InvalidInput,
                "request_id_required",
                "An access request identifier is required.");
        }

        var principalResult = await LoadPrincipalAsync(
            authenticatedPrincipalId,
            cancellationToken);
        if (principalResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                principalResult.Failure!);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(requestResult.Failure!);
        }

        var decisionsResult = await workflowStore.ListApprovalDecisionsAsync(
            requestId,
            cancellationToken);
        if (decisionsResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                decisionsResult.Failure!);
        }

        var principal = principalResult.Value;
        var request = requestResult.Value;
        var participantResult = await visibilityPolicy.EvaluateAsync(
            principal,
            request,
            decisionsResult.Value,
            cancellationToken);
        if (participantResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                participantResult.Failure!);
        }

        var participant = participantResult.Value;
        if (!participant.IsParticipant)
        {
            return NotFound<RequestDetailView>();
        }

        var operationResult = await workflowStore.GetProvisioningOperationAsync(
            requestId,
            cancellationToken);
        ProvisioningOperationView? operation = null;
        if (operationResult.IsSuccess)
        {
            operation = ToView(operationResult.Value, request);
        }
        else if (operationResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                operationResult.Failure);
        }

        var grantResult = await workflowStore.GetAccessGrantForRequestAsync(
            requestId,
            cancellationToken);
        AccessGrantView? grant = null;
        if (grantResult.IsSuccess)
        {
            grant = ToView(grantResult.Value, request, clock.UtcNow);
        }
        else if (grantResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed<RequestDetailView>(grantResult.Failure);
        }

        var auditEventsResult = await workflowStore.ListAuditEventsAsync(
            requestId,
            cancellationToken);
        if (auditEventsResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                auditEventsResult.Failure!);
        }

        return ApplicationResult.Succeeded(
            new RequestDetailView(
                request.Id,
                request.RequesterId,
                request.Details.ClientId,
                request.Details.EnvironmentId,
                request.Details.RoleId,
                request.Details.Justification,
                request.Details.IncidentId,
                request.Status,
                request.CreatedAt,
                request.LastModifiedAt,
                participant.AvailableActions,
                new RequestValidationView(
                    IsValid: true,
                    FieldErrors: []),
                decisionsResult.Value
                    .OrderBy(decision => decision.DecidedAt)
                    .ThenBy(decision => decision.Stage)
                    .ThenBy(decision => decision.Id)
                    .Select(decision => ToView(decision, request))
                    .ToArray(),
                operation,
                grant,
                auditEventsResult.Value.Select(ToView).ToArray()));
    }

    private async Task<ApplicationResult<AuthenticatedPrincipal>> LoadPrincipalAsync(
        string? authenticatedPrincipalId,
        CancellationToken cancellationToken)
    {
        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed<AuthenticatedPrincipal>(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated principal is required.");
        }

        var principalResult = await requestContext.GetPrincipalAsync(
            principalId,
            cancellationToken);
        return principalResult.IsFailure
            && principalResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed<AuthenticatedPrincipal>(
                    ApplicationFailureKind.Unauthenticated,
                    "authenticated_principal_not_found",
                    "The authenticated principal is unavailable.")
                : principalResult;
    }

    private static ApprovalDecisionView ToView(
        ApprovalDecision decision,
        AccessRequest request)
    {
        return new ApprovalDecisionView(
            decision.Id,
            decision.RequestId,
            decision.Stage,
            decision.Decision,
            decision.ApproverId,
            decision.Decision == ApprovalOutcome.Approved
                ? request.Details.RoleId
                : null,
            decision.Comment,
            decision.DecidedAt,
            decision.CorrelationId);
    }

    private static ProvisioningOperationView ToView(
        ProvisioningOperation operation,
        AccessRequest request)
    {
        return new ProvisioningOperationView(
            operation.RequestId,
            request.Details.EnvironmentId,
            request.Details.RoleId,
            operation.Status,
            operation.AttemptCount,
            operation.LastOutcomeCode,
            operation.CreatedAt,
            operation.LastAttemptAt);
    }

    private static AccessGrantView ToView(
        AccessGrant grant,
        AccessRequest request,
        DateTimeOffset currentTime)
    {
        return new AccessGrantView(
            grant.Id,
            grant.RequestId,
            request.RequesterId,
            request.Details.EnvironmentId,
            request.Details.RoleId,
            grant.ActivatedAt,
            grant.ExpiresAt,
            grant.Outcome,
            grant.CorrelationId,
            grant.IsExpired(currentTime));
    }

    private static AuditEventView ToView(AuditEvent auditEvent)
    {
        return new AuditEventView(
            auditEvent.Id,
            auditEvent.RequestId,
            auditEvent.EventType,
            auditEvent.ActorId,
            auditEvent.OccurredAt,
            auditEvent.CorrelationId,
            auditEvent.OutcomeCode,
            auditEvent.DetailsJson);
    }

    private static ApplicationResult<T> NotFound<T>()
        where T : notnull
    {
        return Failed<T>(
            ApplicationFailureKind.NotFound,
            "request_not_found",
            "The access request was not found.");
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

}

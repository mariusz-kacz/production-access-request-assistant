using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record RequestListItemView(
    Guid RequestId,
    string RequesterId,
    string ClientId,
    string EnvironmentId,
    RequestStatus Status,
    DateTimeOffset LastModifiedAt,
    bool Actionable);

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
public sealed class RequestQueryService
{
    public const string BusinessDecisionAction = "decideBusinessRequest";
    public const string DevOpsDecisionAction = "decideDevOpsRequest";
    public const string RetryProvisioningAction = "retryProvisioning";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;
    private readonly RequestValidator requestValidator;
    private readonly IClock clock;

    public RequestQueryService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore,
        RequestValidator requestValidator,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(clock);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
        this.requestValidator = requestValidator;
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

            var participantResult = await GetParticipantAccessAsync(
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

            var availableActions = GetAvailableActions(
                principal,
                request,
                participant.IsResponsibleBusinessApprover);
            items.Add(new RequestListItemView(
                request.Id,
                request.RequesterId,
                request.ClientId,
                request.EnvironmentId,
                request.Status,
                request.LastModifiedAt,
                availableActions.Count > 0));
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
        var participantResult = await GetParticipantAccessAsync(
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

        var validationResult = await GetCurrentValidationAsync(
            request,
            cancellationToken);
        if (validationResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(
                validationResult.Failure!);
        }

        var operationResult = await workflowStore.GetProvisioningOperationAsync(
            requestId,
            cancellationToken);
        ProvisioningOperationView? operation = null;
        if (operationResult.IsSuccess)
        {
            operation = ToView(operationResult.Value);
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
            grant = ToView(grantResult.Value, clock.UtcNow);
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

        var availableActions = GetAvailableActions(
            principal,
            request,
            participant.IsResponsibleBusinessApprover);
        return ApplicationResult.Succeeded(
            new RequestDetailView(
                request.Id,
                request.RequesterId,
                request.ClientId,
                request.EnvironmentId,
                request.RequestedRoleId,
                request.Justification,
                request.IncidentId,
                request.Status,
                request.CreatedAt,
                request.LastModifiedAt,
                availableActions,
                validationResult.Value,
                decisionsResult.Value
                    .OrderBy(decision => decision.DecidedAt)
                    .ThenBy(decision => decision.Stage)
                    .ThenBy(decision => decision.Id)
                    .Select(ToView)
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

    private async Task<ApplicationResult<ParticipantAccess>> GetParticipantAccessAsync(
        AuthenticatedPrincipal principal,
        AccessRequest request,
        IReadOnlyList<ApprovalDecision>? decisions,
        CancellationToken cancellationToken)
    {
        if (IsRequester(principal, request))
        {
            return ApplicationResult.Succeeded(
                new ParticipantAccess(
                    IsParticipant: true,
                    IsResponsibleBusinessApprover: false));
        }

        if (principal.Kind == PrincipalKind.DevOpsApprover)
        {
            if (request.Status is
                RequestStatus.AwaitingDevOpsApproval or
                RequestStatus.ProvisioningFailed or
                RequestStatus.Active)
            {
                return ApplicationResult.Succeeded(
                    new ParticipantAccess(
                        IsParticipant: true,
                        IsResponsibleBusinessApprover: false));
            }

            if (request.Status != RequestStatus.Rejected)
            {
                return ApplicationResult.Succeeded(ParticipantAccess.None);
            }

            if (decisions is null)
            {
                var decisionsResult = await workflowStore.ListApprovalDecisionsAsync(
                    request.Id,
                    cancellationToken);
                if (decisionsResult.IsFailure)
                {
                    return ApplicationResult.Failed<ParticipantAccess>(
                        decisionsResult.Failure!);
                }

                decisions = decisionsResult.Value;
            }

            var hasDevOpsDecision = decisions.Any(
                decision => decision.Stage == ApprovalStage.DevOps);
            return ApplicationResult.Succeeded(
                hasDevOpsDecision
                    ? new ParticipantAccess(
                        IsParticipant: true,
                        IsResponsibleBusinessApprover: false)
                    : ParticipantAccess.None);
        }

        if (principal.Kind != PrincipalKind.BusinessApprover
            || !StringComparer.Ordinal.Equals(principal.ClientId, request.ClientId))
        {
            return ApplicationResult.Succeeded(ParticipantAccess.None);
        }

        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            request.EnvironmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return environmentResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? ApplicationResult.Succeeded(ParticipantAccess.None)
                : ApplicationResult.Failed<ParticipantAccess>(
                    environmentResult.Failure);
        }

        var environment = environmentResult.Value;
        var isResponsibleApprover = StringComparer.Ordinal.Equals(
                environment.ClientId,
                request.ClientId)
            && StringComparer.Ordinal.Equals(
                environment.BusinessApproverPrincipalId,
                principal.Id);
        return ApplicationResult.Succeeded(
            isResponsibleApprover
                ? new ParticipantAccess(
                    IsParticipant: true,
                    IsResponsibleBusinessApprover: true)
                : ParticipantAccess.None);
    }

    private async Task<ApplicationResult<RequestValidationView>>
        GetCurrentValidationAsync(
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

        return validationOutcome switch
        {
            RequestValidationSucceeded succeeded
                when MatchesImmutableScope(request, succeeded.Fields) =>
                ApplicationResult.Succeeded(
                new RequestValidationView(
                    IsValid: true,
                    FieldErrors: [])),
            RequestValidationSucceeded => ApplicationResult.Succeeded(
                new RequestValidationView(
                    IsValid: false,
                    FieldErrors:
                    [
                        new FieldValidationError(
                            "request",
                            "request_context_mismatch",
                            "Current stored context does not match the immutable request."),
                    ])),
            RequestValidationRejected rejected => ApplicationResult.Succeeded(
                new RequestValidationView(
                    IsValid: false,
                    rejected.Errors)),
            RequestValidationFailed failed =>
                ApplicationResult.Failed<RequestValidationView>(failed.Failure),
            _ => throw new InvalidOperationException(
                "The request validation outcome is unsupported."),
        };
    }

    private static bool MatchesImmutableScope(
        AccessRequest request,
        ValidatedRequestFields fields)
    {
        return StringComparer.Ordinal.Equals(request.ClientId, fields.ClientId)
            && StringComparer.Ordinal.Equals(
                request.EnvironmentId,
                fields.EnvironmentId)
            && StringComparer.Ordinal.Equals(
                request.RequestedRoleId,
                fields.RequestedRoleId)
            && StringComparer.Ordinal.Equals(
                request.Justification,
                fields.Justification)
            && StringComparer.Ordinal.Equals(request.IncidentId, fields.IncidentId);
    }

    private static IReadOnlyList<string> GetAvailableActions(
        AuthenticatedPrincipal principal,
        AccessRequest request,
        bool isResponsibleBusinessApprover)
    {
        if (isResponsibleBusinessApprover
            && request.Status == RequestStatus.AwaitingBusinessApproval)
        {
            return [BusinessDecisionAction];
        }

        if (principal.Kind != PrincipalKind.DevOpsApprover)
        {
            return [];
        }

        return request.Status switch
        {
            RequestStatus.AwaitingDevOpsApproval => [DevOpsDecisionAction],
            RequestStatus.ProvisioningFailed => [RetryProvisioningAction],
            _ => [],
        };
    }

    private static bool IsRequester(
        AuthenticatedPrincipal principal,
        AccessRequest request)
    {
        return principal.Kind == PrincipalKind.Requester
            && StringComparer.Ordinal.Equals(principal.Id, request.RequesterId);
    }

    private static ApprovalDecisionView ToView(ApprovalDecision decision)
    {
        return new ApprovalDecisionView(
            decision.Id,
            decision.RequestId,
            decision.Stage,
            decision.Decision,
            decision.ApproverId,
            decision.ApprovedRoleId,
            decision.Comment,
            decision.DecidedAt,
            decision.CorrelationId);
    }

    private static ProvisioningOperationView ToView(
        ProvisioningOperation operation)
    {
        return new ProvisioningOperationView(
            operation.RequestId,
            operation.EnvironmentId,
            operation.RoleId,
            operation.Status,
            operation.AttemptCount,
            operation.LastOutcomeCode,
            operation.CreatedAt,
            operation.LastAttemptAt);
    }

    private static AccessGrantView ToView(
        AccessGrant grant,
        DateTimeOffset currentTime)
    {
        return new AccessGrantView(
            grant.Id,
            grant.RequestId,
            grant.RequesterId,
            grant.EnvironmentId,
            grant.RoleId,
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

    private readonly record struct ParticipantAccess(
        bool IsParticipant,
        bool IsResponsibleBusinessApprover)
    {
        public static ParticipantAccess None { get; } = new(
            IsParticipant: false,
            IsResponsibleBusinessApprover: false);
    }
}

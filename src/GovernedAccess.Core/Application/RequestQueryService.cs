using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record RequestDetailView(
    Guid RequestId,
    string RequesterId,
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    int RequestedDurationMinutes,
    string Justification,
    string? IncidentId,
    RequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    IReadOnlyList<string> AvailableActions);

/// <summary>
/// Produces participant-authorized request details and presentation hints from current
/// authoritative state. Available actions never replace command authorization.
/// </summary>
public sealed class RequestQueryService
{
    public const string BusinessDecisionAction = "decideBusinessRequest";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;

    public RequestQueryService(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
    }

    public async Task<ApplicationResult<RequestDetailView>> GetDetailAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return Failed(
                ApplicationFailureKind.InvalidInput,
                "request_id_required",
                "An access request identifier is required.");
        }

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated principal is required.");
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
                : ApplicationResult.Failed<RequestDetailView>(principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return ApplicationResult.Failed<RequestDetailView>(requestResult.Failure!);
        }

        var principal = principalResult.Value;
        var request = requestResult.Value;
        if (IsRequester(principal, request))
        {
            return ApplicationResult.Succeeded(ToDetail(request, []));
        }

        if (principal.Kind != PrincipalKind.BusinessApprover
            || !StringComparer.Ordinal.Equals(principal.ClientId, request.ClientId))
        {
            return NotFound();
        }

        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            request.EnvironmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return environmentResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? NotFound()
                : ApplicationResult.Failed<RequestDetailView>(environmentResult.Failure);
        }

        var environment = environmentResult.Value;
        var isResponsibleApprover = StringComparer.Ordinal.Equals(
                environment.ClientId,
                request.ClientId)
            && StringComparer.Ordinal.Equals(
                environment.BusinessApproverPrincipalId,
                principal.Id);
        if (!isResponsibleApprover)
        {
            return NotFound();
        }

        IReadOnlyList<string> availableActions =
            request.Status == RequestStatus.AwaitingBusinessApproval
                ? [BusinessDecisionAction]
                : [];
        return ApplicationResult.Succeeded(ToDetail(request, availableActions));
    }

    private static bool IsRequester(
        AuthenticatedPrincipal principal,
        AccessRequest request)
    {
        return principal.Kind == PrincipalKind.Requester
            && StringComparer.Ordinal.Equals(principal.Id, request.RequesterId);
    }

    private static RequestDetailView ToDetail(
        AccessRequest request,
        IReadOnlyList<string> availableActions)
    {
        return new RequestDetailView(
            request.Id,
            request.RequesterId,
            request.ClientId,
            request.EnvironmentId,
            request.RequestedRoleId,
            request.RequestedDurationMinutes,
            request.Justification,
            request.IncidentId,
            request.Status,
            request.CreatedAt,
            request.LastModifiedAt,
            availableActions);
    }

    private static ApplicationResult<RequestDetailView> NotFound()
    {
        return Failed(
            ApplicationFailureKind.NotFound,
            "request_not_found",
            "The access request was not found.");
    }

    private static ApplicationResult<RequestDetailView> Failed(
        ApplicationFailureKind kind,
        string code,
        string message)
    {
        return ApplicationResult.Failed<RequestDetailView>(
            new ApplicationFailure(kind, code, message));
    }
}

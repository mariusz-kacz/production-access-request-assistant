using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public readonly record struct AccessRequestAccess(
    bool IsParticipant,
    IReadOnlyList<string> AvailableActions)
{
    public static AccessRequestAccess None { get; } = new(
        IsParticipant: false,
        AvailableActions: []);
}

/// <summary>
/// Determines request visibility and presentation actions from authenticated and
/// authoritative participant evidence. Command authorization remains independent.
/// </summary>
public sealed class AccessRequestVisibilityPolicy
{
    public const string BusinessDecisionAction = "decideBusinessRequest";
    public const string DevOpsDecisionAction = "decideDevOpsRequest";
    public const string RetryProvisioningAction = "retryProvisioning";

    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;

    public AccessRequestVisibilityPolicy(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
    }

    public async Task<ApplicationResult<AccessRequestAccess>> EvaluateAsync(
        AuthenticatedPrincipal principal,
        AccessRequest request,
        IReadOnlyList<ApprovalDecision>? decisions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);

        if (IsRequester(principal, request))
        {
            return ApplicationResult.Succeeded(new AccessRequestAccess(
                IsParticipant: true,
                AvailableActions: []));
        }

        if (principal.Kind == PrincipalKind.DevOpsApprover)
        {
            if (request.Status is
                RequestStatus.AwaitingDevOpsApproval or
                RequestStatus.ProvisioningFailed or
                RequestStatus.Active)
            {
                return ApplicationResult.Succeeded(new AccessRequestAccess(
                    IsParticipant: true,
                    AvailableActions: GetAvailableActions(
                        principal,
                        request,
                        isResponsibleBusinessApprover: false)));
            }

            if (request.Status != RequestStatus.Rejected)
            {
                return ApplicationResult.Succeeded(AccessRequestAccess.None);
            }

            if (decisions is null)
            {
                var decisionsResult = await workflowStore.ListApprovalDecisionsAsync(
                    request.Id,
                    cancellationToken);
                if (decisionsResult.IsFailure)
                {
                    return ApplicationResult.Failed<AccessRequestAccess>(
                        decisionsResult.Failure!);
                }

                decisions = decisionsResult.Value;
            }

            var hasDevOpsDecision = decisions.Any(
                decision => decision.Stage == ApprovalStage.DevOps);
            return ApplicationResult.Succeeded(
                hasDevOpsDecision
                    ? new AccessRequestAccess(
                        IsParticipant: true,
                        AvailableActions: [])
                    : AccessRequestAccess.None);
        }

        if (principal.Kind != PrincipalKind.BusinessApprover
            || !StringComparer.Ordinal.Equals(
                principal.ClientId,
                request.Details.ClientId))
        {
            return ApplicationResult.Succeeded(AccessRequestAccess.None);
        }

        var environmentContextResult =
            await requestContext.GetProductionEnvironmentContextAsync(
                request.Details.EnvironmentId,
                cancellationToken);
        if (environmentContextResult.IsFailure)
        {
            return environmentContextResult.Failure!.Kind ==
                    ApplicationFailureKind.NotFound
                ? ApplicationResult.Succeeded(AccessRequestAccess.None)
                : ApplicationResult.Failed<AccessRequestAccess>(
                    environmentContextResult.Failure);
        }

        var environmentContext = environmentContextResult.Value;
        var environment = environmentContext.Environment;
        var isResponsibleApprover = StringComparer.Ordinal.Equals(
                environment.ClientId,
                request.Details.ClientId)
            && StringComparer.Ordinal.Equals(
                environmentContext.Client.Id,
                request.Details.ClientId)
            && StringComparer.Ordinal.Equals(
                environmentContext.Client.BusinessApproverPrincipalId,
                principal.Id);

        return ApplicationResult.Succeeded(
            isResponsibleApprover
                ? new AccessRequestAccess(
                    IsParticipant: true,
                    AvailableActions: GetAvailableActions(
                        principal,
                        request,
                        isResponsibleBusinessApprover: true))
                : AccessRequestAccess.None);
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
}

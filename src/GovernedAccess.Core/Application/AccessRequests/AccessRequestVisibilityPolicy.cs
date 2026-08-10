using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
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
/// Determines request visibility and presentation actions from the authenticated
/// principal, immutable request state, and authoritative participant evidence.
/// Command services independently repeat authorization before changing state.
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

        if (principal.Kind == PrincipalKind.Requester
            && StringComparer.Ordinal.Equals(principal.Id, request.RequesterId))
        {
            return ApplicationResult.Succeeded(new AccessRequestAccess(
                IsParticipant: true,
                AvailableActions: []));
        }

        if (principal.Kind == PrincipalKind.DevOpsApprover)
        {
            return await EvaluateDevOpsAccessAsync(
                request,
                decisions,
                cancellationToken);
        }

        if (principal.Kind != PrincipalKind.BusinessApprover
            || !StringComparer.Ordinal.Equals(principal.ClientId, request.ClientId))
        {
            return ApplicationResult.Succeeded(AccessRequestAccess.None);
        }

        var environmentContextResult =
            await requestContext.GetProductionEnvironmentContextAsync(
                request.EnvironmentId,
                cancellationToken);
        if (environmentContextResult.IsFailure)
        {
            return environmentContextResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? ApplicationResult.Succeeded(AccessRequestAccess.None)
                : ApplicationResult.Failed<AccessRequestAccess>(
                    environmentContextResult.Failure);
        }

        var environmentContext = environmentContextResult.Value;
        var isResponsibleApprover = StringComparer.Ordinal.Equals(
                environmentContext.Environment.ClientId,
                request.ClientId)
            && StringComparer.Ordinal.Equals(
                environmentContext.Client.Id,
                request.ClientId)
            && StringComparer.Ordinal.Equals(
                environmentContext.Client.BusinessApproverPrincipalId,
                principal.Id);

        return ApplicationResult.Succeeded(
            isResponsibleApprover
                ? new AccessRequestAccess(
                    IsParticipant: true,
                    AvailableActions:
                        request.Status == RequestStatus.AwaitingBusinessApproval
                            ? [BusinessDecisionAction]
                            : [])
                : AccessRequestAccess.None);
    }

    private async Task<ApplicationResult<AccessRequestAccess>>
        EvaluateDevOpsAccessAsync(
            AccessRequest request,
            IReadOnlyList<ApprovalDecision>? decisions,
            CancellationToken cancellationToken)
    {
        if (request.Status == RequestStatus.ProvisioningFailed)
        {
            return ApplicationResult.Succeeded(new AccessRequestAccess(
                IsParticipant: true,
                AvailableActions: [RetryProvisioningAction]));
        }

        if (request.Status == RequestStatus.Active)
        {
            return ApplicationResult.Succeeded(new AccessRequestAccess(
                IsParticipant: true,
                AvailableActions: []));
        }

        if (request.Status is not RequestStatus.AwaitingDevOpsApproval
            and not RequestStatus.Rejected)
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
        if (request.Status == RequestStatus.Rejected)
        {
            return ApplicationResult.Succeeded(
                hasDevOpsDecision
                    ? new AccessRequestAccess(
                        IsParticipant: true,
                        AvailableActions: [])
                    : AccessRequestAccess.None);
        }

        return ApplicationResult.Succeeded(
            new AccessRequestAccess(
                IsParticipant: true,
                AvailableActions: hasDevOpsDecision
                    ? []
                    : [DevOpsDecisionAction]));
    }
}

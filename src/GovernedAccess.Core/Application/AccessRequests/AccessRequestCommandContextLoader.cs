using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public sealed record AccessRequestCommandContext(
    AccessRequest Request,
    AuthenticatedPrincipal Principal,
    string CorrelationId);

/// <summary>
/// Loads the authenticated actor, immutable request, and normalized correlation
/// identity required before a workflow command can apply domain policy.
/// </summary>
public sealed class AccessRequestCommandContextLoader
{
    private readonly IRequestContextReader requestContext;
    private readonly IWorkflowStore workflowStore;

    public AccessRequestCommandContextLoader(
        IRequestContextReader requestContext,
        IWorkflowStore workflowStore)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(workflowStore);

        this.requestContext = requestContext;
        this.workflowStore = workflowStore;
    }

    public async Task<ApplicationResult<AccessRequestCommandContext>> LoadAsync(
        Guid requestId,
        string? authenticatedPrincipalId,
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

        var principalId = AccessRequestNormalization.NormalizeOptionalIdentifier(
            authenticatedPrincipalId);
        if (principalId is null)
        {
            return Failed(
                ApplicationFailureKind.Unauthenticated,
                "authentication_required",
                "An authenticated workflow actor is required.");
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
                : ApplicationResult.Failed<AccessRequestCommandContext>(
                    principalResult.Failure);
        }

        var requestResult = await workflowStore.GetRequestAsync(
            requestId,
            cancellationToken);
        if (requestResult.IsFailure)
        {
            return ApplicationResult.Failed<AccessRequestCommandContext>(
                requestResult.Failure!);
        }

        return ApplicationResult.Succeeded(new AccessRequestCommandContext(
            requestResult.Value,
            principalResult.Value,
            normalizedCorrelationId));
    }

    private static ApplicationResult<AccessRequestCommandContext> Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        ApplicationResult.Failed<AccessRequestCommandContext>(
            new ApplicationFailure(kind, code, message));
}

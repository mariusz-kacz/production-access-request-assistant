using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Reads the current context needed to prepare and validate a request.
/// Implementations must not substitute caller-supplied assertions for stored state.
/// </summary>
public interface IRequestContextReader
{
    Task<ApplicationResult<Client>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
        string environmentId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
        string environmentId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<Incident>> GetIncidentAsync(
        string incidentId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Stages and reads insert-only audit evidence.
/// Staged events are committed by the enclosing workflow store save operation.
/// </summary>
public interface IAuditStore
{
    void AddAuditEvent(AuditEvent auditEvent);

    Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
        Guid requestId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides focused persistence operations for the governed request workflow.
/// A save commits all tracked request changes and staged evidence atomically.
/// </summary>
public interface IWorkflowStore : IAuditStore
{
    void AddRequest(AccessRequest request);

    Task<ApplicationResult<AccessRequest>> GetRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
        CancellationToken cancellationToken);

    void AddApprovalDecision(ApprovalDecision decision);

    Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
        Guid requestId,
        int requestVersion,
        ApprovalStage stage,
        CancellationToken cancellationToken);

    Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    void AddProvisioningOperation(ProvisioningOperation operation);

    Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
        string operationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
        string operationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationForRequestAsync(
        Guid requestId,
        int requestVersion,
        CancellationToken cancellationToken);

    void AddAccessGrant(AccessGrant grant);

    Task<ApplicationResult<AccessGrant>> GetAccessGrantByOperationAsync(
        string operationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
        Guid requestId,
        int requestVersion,
        CancellationToken cancellationToken);

    Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken);
}

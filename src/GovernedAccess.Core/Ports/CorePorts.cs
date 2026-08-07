using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Provider-neutral authoritative context for one production environment.
/// This read projection is not persisted and is not approval or provisioning evidence.
/// </summary>
public sealed record ProductionEnvironmentContext
{
    public ProductionEnvironmentContext(
        ProductionEnvironment environment,
        Client client,
        IEnumerable<EnvironmentRole> assignedRoles)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(assignedRoles);

        if (!string.Equals(client.Id, environment.ClientId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The client must own the production environment.",
                nameof(client));
        }

        var roleSnapshot = assignedRoles.ToArray();
        foreach (var role in roleSnapshot)
        {
            ArgumentNullException.ThrowIfNull(role);
            if (!string.Equals(
                    role.EnvironmentId,
                    environment.Id,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every assigned role must belong to the production environment.",
                    nameof(assignedRoles));
            }
        }

        Array.Sort(
            roleSnapshot,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.RoleId,
                right.RoleId));

        Environment = environment;
        Client = client;
        AssignedRoles = Array.AsReadOnly(roleSnapshot);
    }

    public ProductionEnvironment Environment { get; }

    public Client Client { get; }

    public IReadOnlyList<EnvironmentRole> AssignedRoles { get; }
}

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

    Task<ApplicationResult<ProductionEnvironmentContext>> GetProductionEnvironmentContextAsync(
        string environmentId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
        ListProductionEnvironmentContextsAsync(CancellationToken cancellationToken);

    Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
        string environmentId,
        string roleId,
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
/// Provides focused persistence operations for the governed request workflow.
/// A save commits all tracked request changes and staged evidence atomically.
/// </summary>
public interface IWorkflowStore
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
        ApprovalStage stage,
        CancellationToken cancellationToken);

    Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    void AddProvisioningOperation(ProvisioningOperation operation);

    Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    void AddAccessGrant(AccessGrant grant);

    Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    void AddAuditEvent(AuditEvent auditEvent);

    Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken);
}

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;

namespace GovernedAccess.Core.Ports;

/// <summary>
/// Persists target preparation aggregates under a short optimistic commit boundary.
/// Implementations must enforce active-binding uniqueness durably.
/// </summary>
public interface IRequestPreparationStore
{
    void Add(RequestPreparation preparation);

    Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
        PreparationBinding binding,
        CancellationToken cancellationToken);

    Task<ApplicationResult<RequestPreparation>> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken);

    Task<ApplicationResult> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads authenticated synthetic identity snapshots from workflow-owned persistence.
/// </summary>
public interface IAuthenticatedPrincipalReader
{
    Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken);
}

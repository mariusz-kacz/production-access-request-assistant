using GovernedAccess.Core.Application;
using GovernedAccess.Core.Preparations.Authority;

namespace GovernedAccess.Core.Ports;

public interface IProductionEnvironmentSearchAuthority
{
    Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public interface IProductionEnvironmentAuthority
{
    Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
        string environmentId,
        CancellationToken cancellationToken);
}

public interface IEnvironmentRoleAuthority
{
    Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>> ListAsync(
        string environmentId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<EnvironmentRoleAuthorityProjection>> GetAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken);
}

public interface IIncidentAuthority
{
    Task<ApplicationResult<IncidentAuthorityProjection>> GetAsync(
        string incidentId,
        CancellationToken cancellationToken);
}

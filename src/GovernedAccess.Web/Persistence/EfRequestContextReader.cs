using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal sealed class EfRequestContextReader(GovernedAccessDbContext dbContext)
    : IRequestContextReader
{
    public Task<ApplicationResult<Client>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.Clients.Where(client => client.Id == clientId),
            "client-not-found",
            "The client was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.ProductionEnvironments.Where(environment => environment.Id == environmentId),
            "environment-not-found",
            "The production environment was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.EnvironmentRoles.Where(role =>
                role.EnvironmentId == environmentId && role.RoleId == roleId),
            "environment-role-not-found",
            "The role is not assigned to the production environment.",
            cancellationToken);
    }

    public async Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var environmentExists = await dbContext.ProductionEnvironments
                .AsNoTracking()
                .AnyAsync(environment => environment.Id == environmentId, cancellationToken);
            if (!environmentExists)
            {
                return NotFound<IReadOnlyList<EnvironmentRole>>(
                    "environment-not-found",
                    "The production environment was not found.");
            }

            var roles = await dbContext.EnvironmentRoles
                .AsNoTracking()
                .Where(role => role.EnvironmentId == environmentId)
                .OrderBy(role => role.RoleId)
                .ToArrayAsync(cancellationToken);

            return ApplicationResult.Succeeded<IReadOnlyList<EnvironmentRole>>(roles);
        }
        catch (DbException)
        {
            return Unavailable<IReadOnlyList<EnvironmentRole>>();
        }
    }

    public Task<ApplicationResult<Incident>> GetIncidentAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.Incidents.Where(incident => incident.Id == incidentId),
            "incident-not-found",
            "The incident was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.AuthenticatedPrincipals.Where(principal => principal.Id == principalId),
            "principal-not-found",
            "The authenticated principal was not found.",
            cancellationToken);
    }

    private static async Task<ApplicationResult<T>> FindAsync<T>(
        IQueryable<T> query,
        string notFoundCode,
        string notFoundMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var entity = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            return entity is null
                ? NotFound<T>(notFoundCode, notFoundMessage)
                : ApplicationResult.Succeeded(entity);
        }
        catch (DbException)
        {
            return Unavailable<T>();
        }
    }

    private static ApplicationResult<T> NotFound<T>(string code, string message)
        where T : notnull
    {
        return ApplicationResult.Failed<T>(
            new ApplicationFailure(ApplicationFailureKind.NotFound, code, message));
    }

    private static ApplicationResult<T> Unavailable<T>()
        where T : notnull
    {
        return ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "request-context-unavailable",
                "The stored request context is currently unavailable."));
    }
}

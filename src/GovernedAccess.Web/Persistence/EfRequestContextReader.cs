using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal sealed class EfRequestContextReader(GovernedAccessDbContext dbContext)
    : IRequestContextReader
{
    private const int MaximumEnvironmentCandidates = 20;

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

    public async Task<ApplicationResult<ProductionEnvironmentContext>>
        GetProductionEnvironmentContextAsync(
            string environmentId,
            CancellationToken cancellationToken)
    {
        try
        {
            var environment = await dbContext.ProductionEnvironments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == environmentId,
                    cancellationToken);
            if (environment is null)
            {
                return NotFound<ProductionEnvironmentContext>(
                    "environment-not-found",
                    "The production environment was not found.");
            }

            var contexts = await LoadProductionEnvironmentContextsAsync(
                [environment],
                cancellationToken);
            return contexts.IsFailure
                ? ApplicationResult.Failed<ProductionEnvironmentContext>(
                    contexts.Failure!)
                : ApplicationResult.Succeeded(contexts.Value[0]);
        }
        catch (DbException)
        {
            return Unavailable<ProductionEnvironmentContext>();
        }
    }

    public async Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
        ListProductionEnvironmentContextsAsync(
            CancellationToken cancellationToken)
    {
        try
        {
            var environments = await dbContext.ProductionEnvironments
                .AsNoTracking()
                .Take(MaximumEnvironmentCandidates + 1)
                .ToArrayAsync(cancellationToken);
            if (environments.Length > MaximumEnvironmentCandidates)
            {
                return ApplicationResult.Failed<
                    IReadOnlyList<ProductionEnvironmentContext>>(
                    new ApplicationFailure(
                        ApplicationFailureKind.DependencyUnavailable,
                        "environment-candidate-limit-exceeded",
                        "The production environment catalog exceeds the supported candidate limit."));
            }

            Array.Sort(
                environments,
                static (left, right) => StringComparer.Ordinal.Compare(
                    left.Id,
                    right.Id));

            return await LoadProductionEnvironmentContextsAsync(
                environments,
                cancellationToken);
        }
        catch (DbException)
        {
            return Unavailable<IReadOnlyList<ProductionEnvironmentContext>>();
        }
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

    private async Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
        LoadProductionEnvironmentContextsAsync(
            ProductionEnvironment[] environments,
            CancellationToken cancellationToken)
    {
        if (environments.Length == 0)
        {
            return ApplicationResult.Succeeded<
                IReadOnlyList<ProductionEnvironmentContext>>(
                Array.Empty<ProductionEnvironmentContext>());
        }

        var clientIds = environments
            .Select(environment => environment.ClientId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var clients = await dbContext.Clients
            .AsNoTracking()
            .Where(client => clientIds.Contains(client.Id))
            .ToArrayAsync(cancellationToken);
        var clientsById = clients.ToDictionary(
            client => client.Id,
            StringComparer.Ordinal);

        var environmentIds = environments
            .Select(environment => environment.Id)
            .ToArray();
        var roles = await dbContext.EnvironmentRoles
            .AsNoTracking()
            .Where(role => environmentIds.Contains(role.EnvironmentId))
            .ToArrayAsync(cancellationToken);
        var rolesByEnvironment = roles.ToLookup(
            role => role.EnvironmentId,
            StringComparer.Ordinal);

        var contexts = new ProductionEnvironmentContext[environments.Length];
        for (var index = 0; index < environments.Length; index++)
        {
            var environment = environments[index];
            if (!clientsById.TryGetValue(environment.ClientId, out var client))
            {
                return Unavailable<IReadOnlyList<ProductionEnvironmentContext>>();
            }

            contexts[index] = new ProductionEnvironmentContext(
                environment,
                client,
                rolesByEnvironment[environment.Id]);
        }

        return ApplicationResult.Succeeded<
            IReadOnlyList<ProductionEnvironmentContext>>(contexts);
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

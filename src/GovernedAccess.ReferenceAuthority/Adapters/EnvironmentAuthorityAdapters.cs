using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.ReferenceAuthority.Adapters;

internal sealed class EfProductionEnvironmentSearchAuthority(
    ReferenceAuthorityDbContext dbContext)
    : IProductionEnvironmentSearchAuthority
{
    private const string Source = "environment-search-authority";

    public async Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var environments = await dbContext.ProductionEnvironments
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            var clients = await dbContext.Clients
                .AsNoTracking()
                .ToDictionaryAsync(client => client.Id, cancellationToken);
            var documents = new EnvironmentSearchDocument[environments.Length];
            for (var index = 0; index < environments.Length; index++)
            {
                var environment = environments[index];
                if (!clients.TryGetValue(environment.ClientId, out var client))
                {
                    return AuthorityAdapterFailures.Malformed<EnvironmentSearchResult>(
                        Source);
                }

                documents[index] = new EnvironmentSearchDocument(
                    environment.Id,
                    environment.DisplayName,
                    client.Id,
                    client.DisplayName,
                    environment.Region,
                    environment.Classification,
                    environment.IsActive,
                    environment.IsProduction,
                    environment.IsEligibleForIntake);
            }

            return ApplicationResult.Succeeded(
                EnvironmentSearchPolicy.Search(query, documents));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AuthorityAdapterFailures.Cancelled<EnvironmentSearchResult>(Source);
        }
        catch (DbException)
        {
            return AuthorityAdapterFailures.Unavailable<EnvironmentSearchResult>(Source);
        }
        catch (InvalidOperationException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentSearchResult>(Source);
        }
        catch (ArgumentException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentSearchResult>(Source);
        }
    }
}

internal sealed class EfProductionEnvironmentAuthority(
    ReferenceAuthorityDbContext dbContext)
    : IProductionEnvironmentAuthority
{
    private const string Source = "environment-authority";

    public async Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        if (!AuthorityAdapterFailures.TryNormalizeIdentifier(
                environmentId,
                out var normalizedEnvironmentId))
        {
            return AuthorityAdapterFailures.InvalidInput<EnvironmentAuthorityProjection>(
                "environment-id-invalid",
                "The environment identifier is invalid.");
        }

        try
        {
            var environment = await dbContext.ProductionEnvironments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == normalizedEnvironmentId,
                    cancellationToken);
            if (environment is null)
            {
                return AuthorityAdapterFailures.NotFound<EnvironmentAuthorityProjection>(
                    "environment-not-found",
                    "The production environment was not found.");
            }

            var client = await dbContext.Clients
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == environment.ClientId,
                    cancellationToken);
            if (client is null)
            {
                return AuthorityAdapterFailures.Malformed<EnvironmentAuthorityProjection>(
                    Source);
            }

            return ApplicationResult.Succeeded(
                new EnvironmentAuthorityProjection(
                    environment.Id,
                    environment.DisplayName,
                    client.Id,
                    client.DisplayName,
                    client.BusinessApproverPrincipalId,
                    environment.IsActive,
                    environment.IsProduction,
                    environment.IsEligibleForIntake));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AuthorityAdapterFailures.Cancelled<EnvironmentAuthorityProjection>(
                Source);
        }
        catch (DbException)
        {
            return AuthorityAdapterFailures.Unavailable<EnvironmentAuthorityProjection>(
                Source);
        }
        catch (InvalidOperationException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentAuthorityProjection>(
                Source);
        }
        catch (ArgumentException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentAuthorityProjection>(
                Source);
        }
    }
}

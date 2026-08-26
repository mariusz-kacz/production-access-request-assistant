using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.ReferenceAuthority.Adapters;

internal sealed class EfIncidentAuthority(ReferenceAuthorityDbContext dbContext)
    : IIncidentAuthority
{
    private const string Source = "incident-authority";

    public async Task<ApplicationResult<IncidentAuthorityProjection>> GetAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        if (!AuthorityAdapterFailures.TryNormalizeIdentifier(
                incidentId,
                out var normalizedIncidentId))
        {
            return AuthorityAdapterFailures.InvalidInput<IncidentAuthorityProjection>(
                "incident-id-invalid",
                "The incident identifier is invalid.");
        }

        try
        {
            var incident = await dbContext.Incidents
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == normalizedIncidentId,
                    cancellationToken);
            if (incident is null)
            {
                return AuthorityAdapterFailures.NotFound<IncidentAuthorityProjection>(
                    "incident-not-found",
                    "The incident was not found.");
            }

            return ApplicationResult.Succeeded(
                new IncidentAuthorityProjection(
                    incident.Id,
                    incident.Title,
                    incident.IsActive,
                    incident.EnvironmentId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AuthorityAdapterFailures.Cancelled<IncidentAuthorityProjection>(Source);
        }
        catch (DbException)
        {
            return AuthorityAdapterFailures.Unavailable<IncidentAuthorityProjection>(Source);
        }
        catch (InvalidOperationException)
        {
            return AuthorityAdapterFailures.Malformed<IncidentAuthorityProjection>(Source);
        }
        catch (ArgumentException)
        {
            return AuthorityAdapterFailures.Malformed<IncidentAuthorityProjection>(Source);
        }
    }
}

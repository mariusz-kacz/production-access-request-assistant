using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Workflow.Persistence;

internal sealed class EfAuthenticatedPrincipalReader(WorkflowDbContext dbContext)
    : IAuthenticatedPrincipalReader
{
    public async Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        principalId = principalId.Trim();

        try
        {
            var record = await dbContext.AuthenticatedPrincipals
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    principal => principal.Id == principalId,
                    cancellationToken);
            if (record is null)
            {
                return ApplicationResult.Failed<AuthenticatedPrincipal>(
                    new ApplicationFailure(
                        ApplicationFailureKind.NotFound,
                        "principal_snapshot_not_found",
                        "The authenticated principal snapshot was not found."));
            }

            if (!Enum.TryParse<PrincipalKind>(record.Kind, out var kind)
                || !Enum.IsDefined(kind))
            {
                return Malformed();
            }

            try
            {
                return ApplicationResult.Succeeded(
                    new AuthenticatedPrincipal(
                        record.Id,
                        record.DisplayName,
                        kind,
                        record.ClientId));
            }
            catch (ArgumentException)
            {
                return Malformed();
            }
        }
        catch (DbException)
        {
            return ApplicationResult.Failed<AuthenticatedPrincipal>(
                WorkflowPersistenceFailures.Unavailable());
        }
    }

    private static ApplicationResult<AuthenticatedPrincipal> Malformed() =>
        ApplicationResult.Failed<AuthenticatedPrincipal>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                "principal_snapshot_malformed_state",
                "Stored authenticated-principal state is invalid."));
}

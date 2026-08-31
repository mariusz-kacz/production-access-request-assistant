using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.ReferenceAuthority.Adapters;

internal sealed class EfEnvironmentRoleAuthority(
    ReferenceAuthorityDbContext dbContext)
    : IEnvironmentRoleAuthority
{
    private const string Source = "environment-role-authority";

    public async Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>>
        ListAsync(
            string environmentId,
            CancellationToken cancellationToken)
    {
        if (!AuthorityAdapterFailures.TryNormalizeIdentifier(
                environmentId,
                out var normalizedEnvironmentId))
        {
            return AuthorityAdapterFailures.InvalidInput<
                IReadOnlyList<EnvironmentRoleAuthorityProjection>>(
                "environment-id-invalid",
                "The environment identifier is invalid.");
        }

        try
        {
            if (!await EnvironmentExistsAsync(
                    normalizedEnvironmentId,
                    cancellationToken))
            {
                return AuthorityAdapterFailures.NotFound<
                    IReadOnlyList<EnvironmentRoleAuthorityProjection>>(
                    "environment-not-found",
                    "The production environment was not found.");
            }

            var roles = await dbContext.EnvironmentRoles
                .AsNoTracking()
                .Where(role => role.EnvironmentId == normalizedEnvironmentId)
                .ToArrayAsync(cancellationToken);
            Array.Sort(
                roles,
                static (left, right) => StringComparer.Ordinal.Compare(
                    left.RoleId,
                    right.RoleId));
            IReadOnlyList<EnvironmentRoleAuthorityProjection> projections = roles
                .Select(ToProjection)
                .ToArray();
            return ApplicationResult.Succeeded(projections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AuthorityAdapterFailures.Cancelled<
                IReadOnlyList<EnvironmentRoleAuthorityProjection>>(Source);
        }
        catch (DbException)
        {
            return AuthorityAdapterFailures.Unavailable<
                IReadOnlyList<EnvironmentRoleAuthorityProjection>>(Source);
        }
        catch (InvalidOperationException)
        {
            return AuthorityAdapterFailures.Malformed<
                IReadOnlyList<EnvironmentRoleAuthorityProjection>>(Source);
        }
        catch (ArgumentException)
        {
            return AuthorityAdapterFailures.Malformed<
                IReadOnlyList<EnvironmentRoleAuthorityProjection>>(Source);
        }
    }

    public async Task<ApplicationResult<EnvironmentRoleAuthorityProjection>> GetAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken)
    {
        if (!AuthorityAdapterFailures.TryNormalizeIdentifier(
                environmentId,
                out var normalizedEnvironmentId)
            || !AuthorityAdapterFailures.TryNormalizeIdentifier(
                roleId,
                out var normalizedRoleId))
        {
            return AuthorityAdapterFailures.InvalidInput<
                EnvironmentRoleAuthorityProjection>(
                "environment-role-id-invalid",
                "The environment and role identifiers are required.");
        }

        try
        {
            var role = await dbContext.EnvironmentRoles
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.EnvironmentId == normalizedEnvironmentId
                        && candidate.RoleId == normalizedRoleId,
                    cancellationToken);
            return role is null
                ? AuthorityAdapterFailures.NotFound<EnvironmentRoleAuthorityProjection>(
                    "environment-role-not-found",
                    "The role is not assigned to the production environment.")
                : ApplicationResult.Succeeded(ToProjection(role));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AuthorityAdapterFailures.Cancelled<EnvironmentRoleAuthorityProjection>(
                Source);
        }
        catch (DbException)
        {
            return AuthorityAdapterFailures.Unavailable<EnvironmentRoleAuthorityProjection>(
                Source);
        }
        catch (InvalidOperationException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentRoleAuthorityProjection>(
                Source);
        }
        catch (ArgumentException)
        {
            return AuthorityAdapterFailures.Malformed<EnvironmentRoleAuthorityProjection>(
                Source);
        }
    }

    private Task<bool> EnvironmentExistsAsync(
        string environmentId,
        CancellationToken cancellationToken) =>
        dbContext.ProductionEnvironments
            .AsNoTracking()
            .AnyAsync(
                environment => environment.Id == environmentId,
                cancellationToken);

    private static EnvironmentRoleAuthorityProjection ToProjection(
        ReferenceEnvironmentRole role) =>
        new(
            role.EnvironmentId,
            role.RoleId,
            role.DisplayName,
            role.IsCurrentlyAssignable);
}

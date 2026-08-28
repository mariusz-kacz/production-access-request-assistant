using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal sealed class PreparationRoleEvaluator(IEnvironmentRoleAuthority roleAuthority)
{
    internal async Task<RetainedRoleEvaluation> EvaluateRetainedAsync(
        string environmentId,
        string? roleId,
        CancellationToken cancellationToken)
    {
        if (roleId is null)
        {
            return RetainedRoleEvaluation.Keep;
        }

        var roleResult = await roleAuthority.GetAsync(
            environmentId,
            roleId,
            cancellationToken);
        if (roleResult.IsFailure)
        {
            return roleResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? RetainedRoleEvaluation.Clear
                : RetainedRoleEvaluation.Rejected;
        }

        if (!string.Equals(
                roleResult.Value.EnvironmentId,
                environmentId,
                StringComparison.Ordinal)
            || !string.Equals(
                roleResult.Value.RoleId,
                roleId,
                StringComparison.Ordinal))
        {
            return RetainedRoleEvaluation.Rejected;
        }

        return roleResult.Value.IsCurrentlyAssignable
            ? RetainedRoleEvaluation.Keep
            : RetainedRoleEvaluation.Clear;
    }

    internal async Task<ExplicitRoleResolution> ResolveExplicitAsync(
        string? environmentId,
        RoleOperation? operation,
        CancellationToken cancellationToken)
    {
        if (operation is null)
        {
            return ExplicitRoleResolution.NotProposed();
        }

        if (operation is ClearRoleOperation)
        {
            return ExplicitRoleResolution.Clear();
        }

        if (operation is not SetRoleOperation set)
        {
            return ExplicitRoleResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid);
        }

        if (environmentId is null)
        {
            return ExplicitRoleResolution.Rejected(
                ApplicationGroupRejectionReason.MissingDependency);
        }

        var roleResult = await roleAuthority.GetAsync(
            environmentId,
            set.RoleId,
            cancellationToken);
        if (roleResult.IsSuccess)
        {
            if (!string.Equals(
                    roleResult.Value.EnvironmentId,
                    environmentId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    roleResult.Value.RoleId,
                    set.RoleId,
                    StringComparison.Ordinal))
            {
                return ExplicitRoleResolution.Rejected(
                    ApplicationGroupRejectionReason.Unavailable);
            }

            return roleResult.Value.IsCurrentlyAssignable
                ? ExplicitRoleResolution.Set(roleResult.Value.RoleId)
                : ExplicitRoleResolution.Rejected(
                    ApplicationGroupRejectionReason.Unavailable,
                    isAuthoritativelyInvalid: true);
        }

        return ExplicitRoleResolution.Rejected(
            ApplicationGroupRejectionReason.Unavailable,
            isAuthoritativelyInvalid:
                roleResult.Failure!.Kind == ApplicationFailureKind.NotFound);
    }

    internal async Task<RoleClarificationEvaluation> ResolveClarificationAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        var rolesResult = await roleAuthority.ListAsync(
            environmentId,
            cancellationToken);
        if (rolesResult.IsFailure
            || rolesResult.Value.Any(
                role => !string.Equals(
                    role.EnvironmentId,
                    environmentId,
                    StringComparison.Ordinal)))
        {
            return RoleClarificationEvaluation.Rejected(
                ApplicationGroupRejectionReason.Unavailable);
        }

        var roles = rolesResult.Value
            .Where(role => role.IsCurrentlyAssignable)
            .OrderBy(role => role.RoleId, StringComparer.Ordinal)
            .ToArray();
        if (roles
            .Select(role => role.RoleId)
            .Distinct(StringComparer.Ordinal)
            .Count() != roles.Length)
        {
            return RoleClarificationEvaluation.Rejected(
                ApplicationGroupRejectionReason.Unavailable);
        }

        if (roles.Length == 0)
        {
            return RoleClarificationEvaluation.Rejected(
                ApplicationGroupRejectionReason.NoAssignableRoles);
        }

        if (roles.Length == 1)
        {
            return RoleClarificationEvaluation.SoleRole(
                new SoleRoleSelection(
                    roles[0].RoleId,
                    roles[0].DisplayName));
        }

        if (roles.Length > RequestPreparation.MaximumClarificationChoices)
        {
            return RoleClarificationEvaluation.Rejected(
                ApplicationGroupRejectionReason.RoleChoiceLimitExceeded);
        }

        return RoleClarificationEvaluation.NeedsClarification(
            new ClarificationSeed(
                ClarificationTarget.Role,
                roles.Select(role => new RoleClarificationChoice(
                    role.RoleId,
                    role.DisplayName))));
    }
}

internal enum RetainedRoleEvaluation
{
    Keep,
    Clear,
    Rejected,
}

internal sealed record ExplicitRoleResolution(
    bool IsClear,
    string? RoleId,
    ApplicationGroupRejectionReason? RejectionReason,
    bool IsAuthoritativelyInvalid)
{
    internal bool IsRejected => RejectionReason.HasValue;

    internal static ExplicitRoleResolution NotProposed() =>
        new(
            IsClear: false,
            RoleId: null,
            RejectionReason: null,
            IsAuthoritativelyInvalid: false);

    internal static ExplicitRoleResolution Set(string roleId) =>
        new(
            IsClear: false,
            roleId,
            RejectionReason: null,
            IsAuthoritativelyInvalid: false);

    internal static ExplicitRoleResolution Clear() =>
        new(
            IsClear: true,
            RoleId: null,
            RejectionReason: null,
            IsAuthoritativelyInvalid: false);

    internal static ExplicitRoleResolution Rejected(
        ApplicationGroupRejectionReason reason,
        bool isAuthoritativelyInvalid = false) =>
        new(
            IsClear: false,
            RoleId: null,
            reason,
            isAuthoritativelyInvalid);
}

internal sealed record RoleClarificationEvaluation(
    SoleRoleSelection? SoleRoleSelection,
    ClarificationSeed? Clarification,
    ApplicationGroupRejectionReason? RejectionReason)
{
    internal static RoleClarificationEvaluation SoleRole(
        SoleRoleSelection selection) =>
        new(selection, Clarification: null, RejectionReason: null);

    internal static RoleClarificationEvaluation NeedsClarification(
        ClarificationSeed clarification) =>
        new(SoleRoleSelection: null, clarification, RejectionReason: null);

    internal static RoleClarificationEvaluation Rejected(
        ApplicationGroupRejectionReason reason) =>
        new(SoleRoleSelection: null, Clarification: null, reason);
}

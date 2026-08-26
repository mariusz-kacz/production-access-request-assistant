using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal sealed class PreparationRoleEvaluator(IEnvironmentRoleAuthority roleAuthority)
{
    internal async Task RevalidateRetainedAsync(
        PatchEvaluation evaluation,
        string environmentId,
        CancellationToken cancellationToken)
    {
        if (evaluation.RoleId is null)
        {
            return;
        }

        var roleResult = await roleAuthority.GetAsync(
            environmentId,
            evaluation.RoleId,
            cancellationToken);
        if (roleResult.IsFailure
            || !roleResult.Value.IsCurrentlyAssignable
            || !string.Equals(
                roleResult.Value.EnvironmentId,
                environmentId,
                StringComparison.Ordinal))
        {
            evaluation.RoleId = null;
            evaluation.Record(
                ProposalField.Role,
                OperationResultKind.Applied);
        }
    }

    internal async Task ApplyRequestedAsync(
        PatchEvaluation evaluation,
        RoleOperation? operation,
        RoleEvaluationDisposition roleEvaluation,
        CancellationToken cancellationToken)
    {
        if (operation is null)
        {
            return;
        }

        if (roleEvaluation == RoleEvaluationDisposition.Blocked
            || evaluation.EnvironmentId is null)
        {
            evaluation.Record(
                ProposalField.Role,
                OperationResultKind.RejectedDependency);
            return;
        }

        await ApplyAsync(evaluation, operation, cancellationToken);
    }

    internal async Task<ClarificationSeed?> ResolveClarificationAsync(
        string environmentId,
        PatchEvaluation evaluation,
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
            SetClarificationFailure(
                evaluation,
                OperationResultKind.RejectedUnavailable);
            return null;
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
            SetClarificationFailure(
                evaluation,
                OperationResultKind.RejectedUnavailable);
            return null;
        }

        if (roles.Length == 0)
        {
            SetClarificationFailure(
                evaluation,
                OperationResultKind.RejectedUnavailable);
            return null;
        }

        if (roles.Length > RequestPreparation.MaximumClarificationChoices)
        {
            SetClarificationFailure(
                evaluation,
                OperationResultKind.RejectedInvalid);
            return null;
        }

        evaluation.Record(
            ProposalField.Role,
            OperationResultKind.NeedsClarification);
        return new ClarificationSeed(
            ClarificationTarget.Role,
            roles.Select(role => new RoleClarificationChoice(
                role.RoleId,
                role.DisplayName)));
    }

    private async Task ApplyAsync(
        PatchEvaluation evaluation,
        RoleOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation is ClearRoleOperation)
        {
            var kind = evaluation.RoleId is null
                ? OperationResultKind.NoOpValueEqual
                : OperationResultKind.Applied;
            evaluation.RoleId = null;
            evaluation.Record(ProposalField.Role, kind);
            return;
        }

        if (operation is not SetRoleOperation set)
        {
            evaluation.Record(
                ProposalField.Role,
                OperationResultKind.RejectedInvalid);
            return;
        }

        var roleResult = await roleAuthority.GetAsync(
            evaluation.EnvironmentId!,
            set.RoleId,
            cancellationToken);
        if (roleResult.IsSuccess
            && roleResult.Value.IsCurrentlyAssignable
            && string.Equals(
                roleResult.Value.EnvironmentId,
                evaluation.EnvironmentId,
                StringComparison.Ordinal)
            && string.Equals(
                roleResult.Value.RoleId,
                set.RoleId,
                StringComparison.Ordinal))
        {
            var kind = string.Equals(
                evaluation.RoleId,
                roleResult.Value.RoleId,
                StringComparison.Ordinal)
                ? OperationResultKind.NoOpValueEqual
                : OperationResultKind.Applied;
            evaluation.RoleId = roleResult.Value.RoleId;
            evaluation.Record(ProposalField.Role, kind);
            return;
        }

        if (roleResult.IsSuccess
            || roleResult.Failure!.Kind == ApplicationFailureKind.NotFound)
        {
            evaluation.RecordAuthoritativelyInvalid(ProposalField.Role);
        }

        evaluation.Record(
            ProposalField.Role,
            OperationResultKind.RejectedUnavailable);
    }

    private static void SetClarificationFailure(
        PatchEvaluation evaluation,
        OperationResultKind failure)
    {
        if (!evaluation.HasResult(
                ProposalField.Role,
                OperationResultKind.Applied))
        {
            evaluation.Record(ProposalField.Role, failure);
        }
    }
}

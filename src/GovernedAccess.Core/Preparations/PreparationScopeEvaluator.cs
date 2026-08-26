using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

internal sealed class PreparationScopeEvaluator(
    IProductionEnvironmentSearchAuthority environmentSearch,
    IProductionEnvironmentAuthority environmentAuthority,
    PreparationRoleEvaluator roleEvaluator,
    IIncidentAuthority incidentAuthority)
{
    internal async Task<ScopeApplicationResult> EvaluateAsync(
        PatchEvaluation evaluation,
        DraftPatch patch,
        CancellationToken cancellationToken)
    {
        var resolvedPatch = await ResolvePatchAsync(patch, cancellationToken);
        return await ApplyScopeAsync(
            evaluation,
            resolvedPatch,
            cancellationToken);
    }

    private async Task<ResolvedPatch> ResolvePatchAsync(
        DraftPatch patch,
        CancellationToken cancellationToken)
    {
        var environment = patch.Environment is null
            ? null
            : await ResolveEnvironmentAsync(patch.Environment, cancellationToken);
        var incident = patch.Incident is null
            ? null
            : await ResolveIncidentAsync(patch.Incident, cancellationToken);

        return new ResolvedPatch(patch, environment, incident);
    }

    private async Task<ScopeApplicationResult> ApplyScopeAsync(
        PatchEvaluation evaluation,
        ResolvedPatch resolvedPatch,
        CancellationToken cancellationToken)
    {
        var environmentResolution = resolvedPatch.Environment;
        var incidentResolution = resolvedPatch.Incident;

        if (environmentResolution is not null)
        {
            evaluation.Record(ProposalField.Environment, environmentResolution.Result);
            if (environmentResolution.IsAuthoritativelyInvalid)
            {
                evaluation.RecordAuthoritativelyInvalid(ProposalField.Environment);
            }
        }

        if (incidentResolution is not null)
        {
            evaluation.Record(ProposalField.Incident, incidentResolution.Result);
        }

        if (resolvedPatch.Patch.Environment is ClearEnvironmentOperation
            && resolvedPatch.Patch.Incident is SetIncidentOperation)
        {
            evaluation.Record(
                ProposalField.Environment,
                OperationResultKind.RejectedConflict);
            evaluation.Record(
                ProposalField.Incident,
                OperationResultKind.RejectedConflict);
            return new ScopeApplicationResult(
                RoleEvaluationDisposition.Blocked,
                EnvironmentClarification: null);
        }

        if (resolvedPatch.Patch.Environment is SetEnvironmentOperation
            && resolvedPatch.Patch.Incident is SetIncidentOperation)
        {
            var roleEvaluation = await ApplyExplicitScopeGroupAsync(
                evaluation,
                environmentResolution!,
                incidentResolution!,
                cancellationToken);
            return new ScopeApplicationResult(
                roleEvaluation,
                environmentResolution!.Clarification);
        }

        var roleEvaluationDisposition = RoleEvaluationDisposition.Allowed;
        ClarificationSeed? environmentClarification = null;
        if (environmentResolution is not null)
        {
            roleEvaluationDisposition = await ApplyEnvironmentResolutionAsync(
                evaluation,
                environmentResolution,
                resolvedPatch.Patch.Incident is null
                    ? RetainedIncidentPolicy.Revalidate
                    : RetainedIncidentPolicy.PreserveWithoutValidation,
                cancellationToken);
            environmentClarification = environmentResolution.Clarification;
        }

        if (incidentResolution is not null)
        {
            await ApplyIncidentResolutionAsync(
                evaluation,
                incidentResolution,
                cancellationToken);
        }

        return new ScopeApplicationResult(
            roleEvaluationDisposition,
            environmentClarification);
    }

    private async Task<RoleEvaluationDisposition> ApplyExplicitScopeGroupAsync(
        PatchEvaluation evaluation,
        EnvironmentResolution environment,
        IncidentResolution incident,
        CancellationToken cancellationToken)
    {
        if (environment.Environment is null || incident.Incident is null)
        {
            if (environment.Clarification is not null)
            {
                evaluation.Record(
                    ProposalField.Incident,
                    OperationResultKind.RejectedDependency);
            }
            else if (environment.Environment is not null)
            {
                evaluation.Record(
                    ProposalField.Environment,
                    OperationResultKind.RejectedDependency);
            }
            else if (incident.Incident is not null)
            {
                evaluation.Record(
                    ProposalField.Incident,
                    OperationResultKind.RejectedDependency);
            }

            return RoleEvaluationDisposition.Blocked;
        }

        if (!string.Equals(
                incident.Incident.EnvironmentId,
                environment.Environment.EnvironmentId,
                StringComparison.Ordinal))
        {
            evaluation.Record(
                ProposalField.Environment,
                OperationResultKind.RejectedConflict);
            evaluation.Record(
                ProposalField.Incident,
                OperationResultKind.RejectedConflict);
            return RoleEvaluationDisposition.Blocked;
        }

        await ApplyEnvironmentAsync(
            evaluation,
            environment.Environment,
            RetainedIncidentPolicy.PreserveWithoutValidation,
            cancellationToken);
        ApplyIncident(evaluation, incident.Incident);
        return RoleEvaluationDisposition.Allowed;
    }

    private async Task<RoleEvaluationDisposition> ApplyEnvironmentResolutionAsync(
        PatchEvaluation evaluation,
        EnvironmentResolution resolution,
        RetainedIncidentPolicy retainedIncidentPolicy,
        CancellationToken cancellationToken)
    {
        if (resolution.Environment is not null)
        {
            await ApplyEnvironmentAsync(
                evaluation,
                resolution.Environment,
                retainedIncidentPolicy,
                cancellationToken);
            return RoleEvaluationDisposition.Allowed;
        }

        if (resolution.IsClear)
        {
            ApplyEnvironmentClear(evaluation);
            return RoleEvaluationDisposition.Allowed;
        }

        return RoleEvaluationDisposition.Blocked;
    }

    private async Task ApplyEnvironmentAsync(
        PatchEvaluation evaluation,
        EnvironmentAuthorityProjection environment,
        RetainedIncidentPolicy retainedIncidentPolicy,
        CancellationToken cancellationToken)
    {
        var environmentChanged = !string.Equals(
                evaluation.EnvironmentId,
                environment.EnvironmentId,
                StringComparison.Ordinal)
            || !string.Equals(
                evaluation.ClientId,
                environment.ClientId,
                StringComparison.Ordinal);
        evaluation.Record(
            ProposalField.Environment,
            environmentChanged
                ? OperationResultKind.Applied
                : OperationResultKind.NoOpValueEqual);
        evaluation.EnvironmentId = environment.EnvironmentId;
        evaluation.ClientId = environment.ClientId;
        if (!environmentChanged)
        {
            return;
        }

        await roleEvaluator.RevalidateRetainedAsync(
            evaluation,
            environment.EnvironmentId,
            cancellationToken);

        if (retainedIncidentPolicy == RetainedIncidentPolicy.Revalidate
            && evaluation.IncidentId is not null)
        {
            var incidentResult = await incidentAuthority.GetAsync(
                evaluation.IncidentId,
                cancellationToken);
            if (incidentResult.IsFailure
                || !incidentResult.Value.IsActive
                || !string.Equals(
                    incidentResult.Value.EnvironmentId,
                    environment.EnvironmentId,
                    StringComparison.Ordinal))
            {
                evaluation.IncidentId = null;
                evaluation.Record(
                    ProposalField.Incident,
                    OperationResultKind.Applied);
            }
        }
    }

    private static void ApplyEnvironmentClear(PatchEvaluation evaluation)
    {
        var hadEnvironment = evaluation.EnvironmentId is not null;
        var hadRole = evaluation.RoleId is not null;
        var hadIncident = evaluation.IncidentId is not null;
        evaluation.EnvironmentId = null;
        evaluation.ClientId = null;
        evaluation.RoleId = null;
        evaluation.IncidentId = null;
        evaluation.Record(
            ProposalField.Environment,
            hadEnvironment
                ? OperationResultKind.Applied
                : OperationResultKind.NoOpValueEqual);
        if (hadRole)
        {
            evaluation.Record(ProposalField.Role, OperationResultKind.Applied);
        }

        if (hadIncident)
        {
            evaluation.Record(ProposalField.Incident, OperationResultKind.Applied);
        }
    }

    private async Task ApplyIncidentResolutionAsync(
        PatchEvaluation evaluation,
        IncidentResolution resolution,
        CancellationToken cancellationToken)
    {
        if (resolution.IsClear)
        {
            var changed = evaluation.IncidentId is not null;
            evaluation.IncidentId = null;
            if (changed
                || evaluation.HasResultOtherThan(
                    ProposalField.Incident,
                    OperationResultKind.Applied))
            {
                evaluation.Record(
                    ProposalField.Incident,
                    changed
                        ? OperationResultKind.Applied
                        : OperationResultKind.NoOpValueEqual);
            }

            return;
        }

        if (resolution.Incident is null)
        {
            return;
        }

        if (evaluation.EnvironmentId is not null)
        {
            if (string.Equals(
                    resolution.Incident.EnvironmentId,
                    evaluation.EnvironmentId,
                    StringComparison.Ordinal))
            {
                ApplyIncident(evaluation, resolution.Incident);
            }
            else
            {
                evaluation.Record(
                    ProposalField.Incident,
                    OperationResultKind.RejectedConflict);
            }

            return;
        }

        if (resolution.Incident.EnvironmentId is null)
        {
            evaluation.Record(
                ProposalField.Incident,
                OperationResultKind.RejectedUnavailable);
            return;
        }

        var environmentResolution = await ExactEnvironmentAsync(
            resolution.Incident.EnvironmentId,
            cancellationToken);
        if (environmentResolution.Environment is null)
        {
            evaluation.Record(
                ProposalField.Incident,
                OperationResultKind.RejectedUnavailable);
            return;
        }

        await ApplyEnvironmentAsync(
            evaluation,
            environmentResolution.Environment,
            RetainedIncidentPolicy.PreserveWithoutValidation,
            cancellationToken);
        ApplyIncident(evaluation, resolution.Incident);
    }

    private static void ApplyIncident(
        PatchEvaluation evaluation,
        IncidentAuthorityProjection incident)
    {
        var changed = !string.Equals(
            evaluation.IncidentId,
            incident.IncidentId,
            StringComparison.Ordinal);
        evaluation.IncidentId = incident.IncidentId;
        evaluation.Record(
            ProposalField.Incident,
            changed
                ? OperationResultKind.Applied
                : OperationResultKind.NoOpValueEqual);
    }

    private async Task<EnvironmentResolution> ResolveEnvironmentAsync(
        EnvironmentOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation is ClearEnvironmentOperation)
        {
            return EnvironmentResolution.Clear();
        }

        if (operation is not SetEnvironmentOperation set)
        {
            return EnvironmentResolution.Rejected(OperationResultKind.RejectedInvalid);
        }

        if (set.Reference is ExactEnvironmentId exact)
        {
            return await ExactEnvironmentAsync(exact.Id, cancellationToken);
        }

        if (set.Reference is not EnvironmentSearchQuery search)
        {
            return EnvironmentResolution.Rejected(OperationResultKind.RejectedInvalid);
        }

        var searchResult = await environmentSearch.SearchAsync(
            search.Query,
            cancellationToken);
        if (searchResult.IsFailure)
        {
            return EnvironmentResolution.Rejected(OperationResultKind.RejectedUnavailable);
        }

        return searchResult.Value.Kind switch
        {
            EnvironmentSearchResultKind.UniqueMatch => await ExactEnvironmentAsync(
                searchResult.Value.Matches[0].EnvironmentId,
                cancellationToken),
            EnvironmentSearchResultKind.ClarificationRequired =>
                EnvironmentResolution.NeedsClarification(
                    searchResult.Value.Matches),
            EnvironmentSearchResultKind.NoMatches =>
                EnvironmentResolution.Rejected(OperationResultKind.RejectedUnavailable),
            EnvironmentSearchResultKind.InvalidQuery
                or EnvironmentSearchResultKind.TooBroad =>
                EnvironmentResolution.Rejected(OperationResultKind.RejectedInvalid),
            _ => EnvironmentResolution.Rejected(OperationResultKind.RejectedInvalid),
        };
    }

    private async Task<EnvironmentResolution> ExactEnvironmentAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        var environmentResult = await environmentAuthority.GetAsync(
            environmentId,
            cancellationToken);
        return environmentResult.IsSuccess
            && environmentResult.Value.CanBecomeCanonical
            && string.Equals(
                environmentResult.Value.EnvironmentId,
                environmentId,
                StringComparison.Ordinal)
                ? EnvironmentResolution.Applied(environmentResult.Value)
                : EnvironmentResolution.Rejected(
                    OperationResultKind.RejectedUnavailable,
                    isAuthoritativelyInvalid: environmentResult.IsSuccess
                        || environmentResult.Failure!.Kind
                            == ApplicationFailureKind.NotFound);
    }

    private async Task<IncidentResolution> ResolveIncidentAsync(
        IncidentOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation is ClearIncidentOperation)
        {
            return IncidentResolution.Clear();
        }

        if (operation is not SetIncidentOperation set)
        {
            return IncidentResolution.Rejected(OperationResultKind.RejectedInvalid);
        }

        var incidentResult = await incidentAuthority.GetAsync(
            set.IncidentId,
            cancellationToken);
        return incidentResult.IsSuccess
            && incidentResult.Value.IsActive
            && string.Equals(
                incidentResult.Value.IncidentId,
                set.IncidentId,
                StringComparison.Ordinal)
            ? IncidentResolution.Applied(incidentResult.Value)
            : IncidentResolution.Rejected(OperationResultKind.RejectedUnavailable);
    }

    private enum RetainedIncidentPolicy
    {
        PreserveWithoutValidation,
        Revalidate,
    }

    private sealed record ResolvedPatch(
        DraftPatch Patch,
        EnvironmentResolution? Environment,
        IncidentResolution? Incident);

    private sealed record EnvironmentResolution(
        OperationResultKind Result,
        EnvironmentAuthorityProjection? Environment,
        ClarificationSeed? Clarification,
        bool IsClear,
        bool IsAuthoritativelyInvalid)
    {
        internal static EnvironmentResolution Applied(
            EnvironmentAuthorityProjection environment) =>
            new(
                OperationResultKind.Applied,
                environment,
                null,
                IsClear: false,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution Clear() =>
            new(
                OperationResultKind.Applied,
                null,
                null,
                IsClear: true,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution NeedsClarification(
            IEnumerable<EnvironmentSearchMatch> environments) =>
            new(
                OperationResultKind.NeedsClarification,
                null,
                new ClarificationSeed(
                    ClarificationTarget.Environment,
                    environments.Select(environment =>
                        new EnvironmentClarificationChoice(
                            environment.EnvironmentId,
                            environment.DisplayName,
                            environment.ClientId,
                            environment.ClientDisplayName,
                            environment.Region,
                            environment.Classification))),
                IsClear: false,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution Rejected(
            OperationResultKind kind,
            bool isAuthoritativelyInvalid = false) =>
            new(
                kind,
                null,
                null,
                IsClear: false,
                IsAuthoritativelyInvalid: isAuthoritativelyInvalid);
    }

    private sealed record IncidentResolution(
        OperationResultKind Result,
        IncidentAuthorityProjection? Incident,
        bool IsClear)
    {
        internal static IncidentResolution Applied(
            IncidentAuthorityProjection incident) =>
            new(OperationResultKind.Applied, incident, IsClear: false);

        internal static IncidentResolution Clear() =>
            new(OperationResultKind.Applied, null, IsClear: true);

        internal static IncidentResolution Rejected(OperationResultKind kind) =>
            new(kind, null, IsClear: false);
    }
}

internal enum RoleEvaluationDisposition
{
    Allowed,
    Blocked,
}

internal sealed record ScopeApplicationResult(
    RoleEvaluationDisposition RoleEvaluation,
    ClarificationSeed? EnvironmentClarification);

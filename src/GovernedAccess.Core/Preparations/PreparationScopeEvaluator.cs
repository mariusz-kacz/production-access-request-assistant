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
        PreparationCandidate current,
        DraftPatch patch,
        CancellationToken cancellationToken)
    {
        var hasScopeOperation = patch.Environment is not null
            || patch.Incident is not null
            || patch.Role is not null;
        var environment = patch.Environment is null
            ? null
            : await ResolveEnvironmentAsync(patch.Environment, cancellationToken);
        var incident = patch.Incident is null
            ? null
            : await ResolveIncidentAsync(patch.Incident, cancellationToken);

        if (environment?.Clarification is not null)
        {
            return ScopeApplicationResult.NeedsClarification(
                current,
                environment.Clarification!);
        }

        if (environment?.IsRejected == true)
        {
            return ScopeApplicationResult.Rejected(
                current,
                environment.RejectionReason!.Value,
                InvalidEnvironmentProposal(environment));
        }

        if (incident?.IsRejected == true)
        {
            return ScopeApplicationResult.Rejected(
                current,
                incident.RejectionReason!.Value);
        }

        if (patch.Environment is ClearEnvironmentOperation
            && patch.Incident is SetIncidentOperation)
        {
            return ScopeApplicationResult.Rejected(
                current,
                ApplicationGroupRejectionReason.Conflict);
        }

        var scope = new TemporaryScope(current);
        var scopeFailure = await ApplyEnvironmentAndIncidentAsync(
            scope,
            patch,
            environment,
            incident,
            cancellationToken);
        if (scopeFailure.HasValue)
        {
            return ScopeApplicationResult.Rejected(
                current,
                scopeFailure.Value);
        }

        var changedBeforeRole = scope.HasChangedFrom(current);
        var role = await roleEvaluator.ResolveExplicitAsync(
            scope.EnvironmentId,
            patch.Role,
            cancellationToken);
        if (role.IsRejected)
        {
            var canClarifyRole = patch.Role is SetRoleOperation
                && !changedBeforeRole
                && role.IsAuthoritativelyInvalid
                && string.Equals(
                    scope.EnvironmentId,
                    current.EnvironmentId,
                    StringComparison.Ordinal);
            var invalidProposal = canClarifyRole
                && role.IsAuthoritativelyInvalid
                && patch.Role is SetRoleOperation set
                    ? new InvalidClarificationProposal(
                        ClarificationTarget.Role,
                        set.RoleId)
                    : null;
            return ScopeApplicationResult.Rejected(
                current,
                role.RejectionReason!.Value,
                invalidProposal,
                shouldResolveRoleClarification: canClarifyRole);
        }

        if (role.RoleId is not null)
        {
            scope.RoleId = role.RoleId;
        }
        else if (role.IsClear)
        {
            scope.RoleId = null;
        }

        var candidate = scope.ToCandidate(current.Justification);
        var result = hasScopeOperation
            ? new ApplicationGroupResult(
                candidate.ChangedFieldsFrom(current).Count > 0
                    ? ApplicationGroupResultKind.Applied
                    : ApplicationGroupResultKind.NoOp)
            : null;
        return new ScopeApplicationResult(
            candidate,
            result,
            EnvironmentClarification: null,
            ShouldResolveRoleClarification:
                candidate.EnvironmentId is not null && candidate.RoleId is null,
            InvalidClarificationProposal: null);
    }

    internal Task<RoleClarificationEvaluation> ResolveRoleClarificationAsync(
        string environmentId,
        CancellationToken cancellationToken) =>
        roleEvaluator.ResolveClarificationAsync(environmentId, cancellationToken);

    private async Task<ApplicationGroupRejectionReason?>
        ApplyEnvironmentAndIncidentAsync(
            TemporaryScope scope,
            DraftPatch patch,
            EnvironmentResolution? environment,
            IncidentResolution? incident,
            CancellationToken cancellationToken)
    {
        if (environment?.Environment is not null)
        {
            if (incident?.Incident is not null
                && !string.Equals(
                    incident.Incident!.EnvironmentId,
                    environment.Environment!.EnvironmentId,
                    StringComparison.Ordinal))
            {
                return ApplicationGroupRejectionReason.Conflict;
            }

            var environmentFailure = await ApplyEnvironmentAsync(
                scope,
                environment.Environment!,
                revalidateRetainedIncident: patch.Incident is null,
                cancellationToken);
            if (environmentFailure.HasValue)
            {
                return environmentFailure;
            }
        }
        else if (environment?.IsClear == true)
        {
            scope.ClearEnvironment();
        }

        if (incident?.IsClear == true)
        {
            scope.IncidentId = null;
            return null;
        }

        if (incident?.Incident is null)
        {
            return null;
        }

        if (incident.Incident!.EnvironmentId is null)
        {
            return ApplicationGroupRejectionReason.Unavailable;
        }

        if (patch.Environment is null)
        {
            var relatedEnvironment = await ExactEnvironmentAsync(
                incident.Incident.EnvironmentId,
                requestedExactId: null,
                cancellationToken);
            if (relatedEnvironment.Environment is null)
            {
                return relatedEnvironment.RejectionReason
                    ?? ApplicationGroupRejectionReason.Unavailable;
            }

            var environmentFailure = await ApplyEnvironmentAsync(
                scope,
                relatedEnvironment.Environment!,
                revalidateRetainedIncident: false,
                cancellationToken);
            if (environmentFailure.HasValue)
            {
                return environmentFailure;
            }
        }

        if (!string.Equals(
                scope.EnvironmentId,
                incident.Incident.EnvironmentId,
                StringComparison.Ordinal))
        {
            return ApplicationGroupRejectionReason.Conflict;
        }

        scope.IncidentId = incident.Incident.IncidentId;
        return null;
    }

    private async Task<ApplicationGroupRejectionReason?> ApplyEnvironmentAsync(
        TemporaryScope scope,
        EnvironmentAuthorityProjection environment,
        bool revalidateRetainedIncident,
        CancellationToken cancellationToken)
    {
        var environmentChanged = !string.Equals(
                scope.EnvironmentId,
                environment.EnvironmentId,
                StringComparison.Ordinal)
            || !string.Equals(
                scope.ClientId,
                environment.ClientId,
                StringComparison.Ordinal);
        if (!environmentChanged)
        {
            return null;
        }

        var retainedRole = await roleEvaluator.EvaluateRetainedAsync(
            environment.EnvironmentId,
            scope.RoleId,
            cancellationToken);
        if (retainedRole == RetainedRoleEvaluation.Rejected)
        {
            return ApplicationGroupRejectionReason.Unavailable;
        }

        var retainIncident = true;
        if (revalidateRetainedIncident && scope.IncidentId is not null)
        {
            var incidentResult = await incidentAuthority.GetAsync(
                scope.IncidentId,
                cancellationToken);
            if (incidentResult.IsFailure)
            {
                if (incidentResult.Failure!.Kind != ApplicationFailureKind.NotFound)
                {
                    return ApplicationGroupRejectionReason.Unavailable;
                }

                retainIncident = false;
            }
            else if (!string.Equals(
                    incidentResult.Value.IncidentId,
                    scope.IncidentId,
                    StringComparison.Ordinal))
            {
                return ApplicationGroupRejectionReason.Unavailable;
            }
            else
            {
                retainIncident = incidentResult.Value.IsActive
                    && string.Equals(
                        incidentResult.Value.EnvironmentId,
                        environment.EnvironmentId,
                        StringComparison.Ordinal);
            }
        }

        scope.EnvironmentId = environment.EnvironmentId;
        scope.ClientId = environment.ClientId;
        if (retainedRole == RetainedRoleEvaluation.Clear)
        {
            scope.RoleId = null;
        }

        if (!retainIncident)
        {
            scope.IncidentId = null;
        }

        return null;
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
            return EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid);
        }

        if (set.Reference is ExactEnvironmentId exact)
        {
            return await ExactEnvironmentAsync(
                exact.Id,
                exact.Id,
                cancellationToken);
        }

        if (set.Reference is not EnvironmentSearchQuery search)
        {
            return EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid);
        }

        var searchResult = await environmentSearch.SearchAsync(
            search.Query,
            cancellationToken);
        if (searchResult.IsFailure)
        {
            return EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Unavailable);
        }

        return searchResult.Value.Kind switch
        {
            EnvironmentSearchResultKind.UniqueMatch => await ExactEnvironmentAsync(
                searchResult.Value.Matches[0].EnvironmentId,
                requestedExactId: null,
                cancellationToken),
            EnvironmentSearchResultKind.ClarificationRequired =>
                EnvironmentResolution.NeedsClarification(
                    searchResult.Value.Matches),
            EnvironmentSearchResultKind.NoMatches => EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Unavailable),
            EnvironmentSearchResultKind.TooBroad => EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.EnvironmentQueryTooBroad),
            EnvironmentSearchResultKind.InvalidQuery => EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid),
            _ => EnvironmentResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid),
        };
    }

    private async Task<EnvironmentResolution> ExactEnvironmentAsync(
        string environmentId,
        string? requestedExactId,
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
                ? EnvironmentResolution.Set(environmentResult.Value)
                : EnvironmentResolution.Rejected(
                    ApplicationGroupRejectionReason.Unavailable,
                    requestedExactId,
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
            return IncidentResolution.Rejected(
                ApplicationGroupRejectionReason.Invalid);
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
                ? IncidentResolution.Set(incidentResult.Value)
                : IncidentResolution.Rejected(
                    ApplicationGroupRejectionReason.Unavailable);
    }

    private static InvalidClarificationProposal? InvalidEnvironmentProposal(
        EnvironmentResolution environment) =>
        environment.IsAuthoritativelyInvalid
        && environment.RequestedExactId is not null
            ? new InvalidClarificationProposal(
                ClarificationTarget.Environment,
                environment.RequestedExactId)
            : null;

    private sealed class TemporaryScope(PreparationCandidate current)
    {
        internal string? ClientId { get; set; } = current.ClientId;

        internal string? EnvironmentId { get; set; } = current.EnvironmentId;

        internal string? RoleId { get; set; } = current.RoleId;

        internal string? IncidentId { get; set; } = current.IncidentId;

        internal void ClearEnvironment()
        {
            ClientId = null;
            EnvironmentId = null;
            RoleId = null;
            IncidentId = null;
        }

        internal bool HasChangedFrom(PreparationCandidate candidate) =>
            !string.Equals(ClientId, candidate.ClientId, StringComparison.Ordinal)
            || !string.Equals(
                EnvironmentId,
                candidate.EnvironmentId,
                StringComparison.Ordinal)
            || !string.Equals(RoleId, candidate.RoleId, StringComparison.Ordinal)
            || !string.Equals(
                IncidentId,
                candidate.IncidentId,
                StringComparison.Ordinal);

        internal PreparationCandidate ToCandidate(string? justification) =>
            new(ClientId, EnvironmentId, RoleId, justification, IncidentId);
    }

    private sealed record EnvironmentResolution(
        EnvironmentAuthorityProjection? Environment,
        ClarificationSeed? Clarification,
        ApplicationGroupRejectionReason? RejectionReason,
        bool IsClear,
        string? RequestedExactId,
        bool IsAuthoritativelyInvalid)
    {
        internal bool IsRejected => RejectionReason.HasValue;

        internal static EnvironmentResolution Set(
            EnvironmentAuthorityProjection environment) =>
            new(
                environment,
                Clarification: null,
                RejectionReason: null,
                IsClear: false,
                RequestedExactId: null,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution Clear() =>
            new(
                Environment: null,
                Clarification: null,
                RejectionReason: null,
                IsClear: true,
                RequestedExactId: null,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution NeedsClarification(
            IEnumerable<EnvironmentSearchMatch> environments) =>
            new(
                Environment: null,
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
                RejectionReason: null,
                IsClear: false,
                RequestedExactId: null,
                IsAuthoritativelyInvalid: false);

        internal static EnvironmentResolution Rejected(
            ApplicationGroupRejectionReason reason,
            string? requestedExactId = null,
            bool isAuthoritativelyInvalid = false) =>
            new(
                Environment: null,
                Clarification: null,
                reason,
                IsClear: false,
                requestedExactId,
                isAuthoritativelyInvalid);
    }

    private sealed record IncidentResolution(
        IncidentAuthorityProjection? Incident,
        ApplicationGroupRejectionReason? RejectionReason,
        bool IsClear)
    {
        internal bool IsRejected => RejectionReason.HasValue;

        internal static IncidentResolution Set(
            IncidentAuthorityProjection incident) =>
            new(incident, RejectionReason: null, IsClear: false);

        internal static IncidentResolution Clear() =>
            new(
                Incident: null,
                RejectionReason: null,
                IsClear: true);

        internal static IncidentResolution Rejected(
            ApplicationGroupRejectionReason reason) =>
            new(Incident: null, reason, IsClear: false);
    }
}

internal sealed record InvalidClarificationProposal(
    ClarificationTarget Target,
    string CanonicalId);

internal sealed record ScopeApplicationResult(
    PreparationCandidate Candidate,
    ApplicationGroupResult? Result,
    ClarificationSeed? EnvironmentClarification,
    bool ShouldResolveRoleClarification,
    InvalidClarificationProposal? InvalidClarificationProposal)
{
    internal static ScopeApplicationResult Rejected(
        PreparationCandidate current,
        ApplicationGroupRejectionReason reason,
        InvalidClarificationProposal? invalidClarificationProposal = null,
        bool shouldResolveRoleClarification = false) =>
        new(
            current,
            new ApplicationGroupResult(
                ApplicationGroupResultKind.Rejected,
                reason),
            EnvironmentClarification: null,
            shouldResolveRoleClarification,
            invalidClarificationProposal);

    internal static ScopeApplicationResult NeedsClarification(
        PreparationCandidate current,
        ClarificationSeed clarification) =>
        new(
            current,
            new ApplicationGroupResult(
                ApplicationGroupResultKind.NeedsClarification),
            clarification,
            ShouldResolveRoleClarification: false,
            InvalidClarificationProposal: null);
}

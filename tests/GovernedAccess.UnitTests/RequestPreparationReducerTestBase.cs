using System.Reflection;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public abstract class RequestPreparationReducerTestBase
{
    protected static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    protected static RequestPreparationReducer Reducer(
        FakePreparationAuthority authority) =>
        new(authority, authority, authority, authority);

    protected static RequestPreparation EmptyPreparation() =>
        RequestPreparation.CreateRoot(Binding(), CreatedAt, "reducer-test");

    protected static RequestPreparation Preparation(PreparationCandidate candidate) =>
        RequestPreparation.CreateRoot(
            Binding(),
            candidate,
            clarification: null,
            Attribution(candidate.ChangedFieldsFrom(PreparationCandidate.Empty)),
            CreatedAt,
            "reducer-test");

    protected static PreparationBinding Binding() =>
        new("msteams", "tenant", "actor", "conversation", "requester");

    protected static PreparationCandidate Candidate(
        string? environmentId,
        string? clientId,
        string? roleId = null,
        string? justification = null,
        string? incidentId = null) =>
        new(clientId, environmentId, roleId, justification, incidentId);

    protected static TurnProposal Update(
        EnvironmentOperation? environment = null,
        RoleOperation? role = null,
        JustificationOperation? justification = null,
        IncidentOperation? incident = null) =>
        new(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.UpdateDraft,
            patch: new DraftPatch(environment, role, justification, incident));

    protected static SetJustificationOperation Justification(string text) =>
        new(new JustificationProposal(text));

    protected static EnvironmentAuthorityProjection Environment(
        string environmentId,
        string clientId,
        bool eligible = true) =>
        new(
            environmentId,
            $"{environmentId} display",
            clientId,
            $"{clientId} display",
            $"{clientId}-approver",
            isActive: eligible,
            isProduction: eligible,
            isEligibleForIntake: eligible);

    protected static EnvironmentSearchResult SearchResult(int matchCount) =>
        EnvironmentSearchResult.FromMatches(
            Enumerable.Range(1, matchCount)
                .Select(
                    index => new EnvironmentSearchMatch(
                        $"PROD-{index:D2}",
                        $"Environment {index}",
                        $"client-{index:D2}",
                        $"Client {index}",
                        $"region-{index:D2}",
                        EnvironmentClassification.Primary)));

    protected static IncidentAuthorityProjection Incident(
        string incidentId,
        string? environmentId = null) =>
        new(
            incidentId,
            $"{incidentId} title",
            isActive: true,
            environmentId);

    protected static EnvironmentRoleAuthorityProjection Role(
        string environmentId,
        string roleId,
        bool assignable = true) =>
        new(
            environmentId,
            roleId,
            $"{roleId} display",
            assignable);

    protected static EnvironmentRoleAuthorityProjection[] Roles(
        string environmentId,
        int count) =>
        Enumerable.Range(1, count)
            .Select(
                index => new EnvironmentRoleAuthorityProjection(
                    environmentId,
                    $"Role-{index:D2}",
                    $"Role {index}",
                    isCurrentlyAssignable: true))
            .ToArray();

    protected static ApplicationFailure Failure(
        ApplicationFailureKind kind,
        string code) =>
        new(kind, code, "Safe authority failure.");

    protected static MaterialChangeAttribution Attribution(
        IEnumerable<ProposalField> fields) =>
        new(
            fields,
            "model-deployment",
            "provider-version",
            "prompt-v1",
            "schema-v1",
            CreatedAt,
            "reducer-test");

    protected static void AssertScopeResult(
        RequestPreparationReduction result,
        ApplicationGroupResultKind kind,
        ApplicationGroupRejectionReason? rejectionReason = null) =>
        AssertGroupResult(result.ScopeResult, kind, rejectionReason);

    protected static void AssertJustificationResult(
        RequestPreparationReduction result,
        ApplicationGroupResultKind kind,
        ApplicationGroupRejectionReason? rejectionReason = null) =>
        AssertGroupResult(result.JustificationResult, kind, rejectionReason);

    private static void AssertGroupResult(
        ApplicationGroupResult? result,
        ApplicationGroupResultKind kind,
        ApplicationGroupRejectionReason? rejectionReason)
    {
        Assert.NotNull(result);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(rejectionReason, result.RejectionReason);
    }

    protected static void AssertSnapshotUnchanged(
        RequestPreparation preparation,
        PreparationCandidate expectedCandidate)
    {
        Assert.Same(expectedCandidate, preparation.Candidate);
        Assert.Equal(1, preparation.ConcurrencyVersion);
        Assert.Null(preparation.Clarification);
    }

    protected static RequestPreparation PreparationWithClarification(
        PreparationCandidate candidate,
        ClarificationTarget target,
        params string[] choices) =>
        RequestPreparation.CreateRoot(
            Binding(),
            candidate,
            new ClarificationSeed(
                target,
                choices.Select(choice => target switch
                {
                    ClarificationTarget.Environment =>
                        (ClarificationChoice)new EnvironmentClarificationChoice(
                            choice,
                            $"{choice} display",
                            "client-context",
                            "Context Client",
                            "context-region",
                            EnvironmentClassification.Primary),
                    ClarificationTarget.Role =>
                        new RoleClarificationChoice(choice, $"{choice} display"),
                    _ => throw new InvalidOperationException(),
                })),
            Attribution(candidate.ChangedFieldsFrom(PreparationCandidate.Empty)),
            CreatedAt,
            "reducer-test");

    protected static void AssertSnapshotWithContextUnchanged(
        RequestPreparation preparation)
    {
        Assert.Equal(PreparationLifecycle.Collecting, preparation.Lifecycle);
        Assert.NotNull(preparation.Clarification);
        Assert.Equal(1, preparation.ConcurrencyVersion);
    }

    protected static void SetPrivateProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var field = target.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    protected sealed class FakePreparationAuthority :
        IProductionEnvironmentSearchAuthority,
        IProductionEnvironmentAuthority,
        IEnvironmentRoleAuthority,
        IIncidentAuthority
    {
        public Dictionary<string, EnvironmentAuthorityProjection> Environments { get; } =
            new(StringComparer.Ordinal);

        public Dictionary<string, IncidentAuthorityProjection> Incidents { get; } =
            new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<EnvironmentRoleAuthorityProjection>>
            RoleLists { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string EnvironmentId, string RoleId),
            EnvironmentRoleAuthorityProjection> Roles { get; } = [];

        public EnvironmentSearchResult SearchResult { get; set; } = SearchResult(0);

        public ApplicationFailure? EnvironmentFailure { get; set; }

        public ApplicationFailure? SearchFailure { get; set; }

        public ApplicationFailure? RoleFailure { get; set; }

        public ApplicationFailure? IncidentFailure { get; set; }

        public int SearchCallCount { get; private set; }

        public List<string> EnvironmentGetCalls { get; } = [];

        public int RoleGetCallCount { get; private set; }

        public List<(string EnvironmentId, string RoleId)> RoleGetCalls { get; } = [];

        public int RoleListCallCount { get; private set; }

        public int IncidentGetCallCount { get; private set; }

        public Task<ApplicationResult<EnvironmentSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchCallCount++;
            if (SearchFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<EnvironmentSearchResult>(SearchFailure));
            }

            return Task.FromResult(ApplicationResult.Succeeded(SearchResult));
        }

        public Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnvironmentGetCalls.Add(environmentId);
            if (EnvironmentFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<EnvironmentAuthorityProjection>(
                        EnvironmentFailure));
            }

            return Task.FromResult(
                Environments.TryGetValue(environmentId, out var environment)
                    ? ApplicationResult.Succeeded(environment)
                    : ApplicationResult.Failed<EnvironmentAuthorityProjection>(
                        Failure(ApplicationFailureKind.NotFound, "environment-not-found")));
        }

        Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>>
            IEnvironmentRoleAuthority.ListAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoleListCallCount++;
            if (RoleFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<
                        IReadOnlyList<EnvironmentRoleAuthorityProjection>>(RoleFailure));
            }

            return Task.FromResult(
                ApplicationResult.Succeeded(
                    RoleLists.TryGetValue(environmentId, out var roles)
                        ? roles
                        : []));
        }

        Task<ApplicationResult<EnvironmentRoleAuthorityProjection>>
            IEnvironmentRoleAuthority.GetAsync(
                string environmentId,
                string roleId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoleGetCallCount++;
            RoleGetCalls.Add((environmentId, roleId));
            if (RoleFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<EnvironmentRoleAuthorityProjection>(
                        RoleFailure));
            }

            return Task.FromResult(
                Roles.TryGetValue((environmentId, roleId), out var role)
                    ? ApplicationResult.Succeeded(role)
                    : ApplicationResult.Failed<EnvironmentRoleAuthorityProjection>(
                        Failure(ApplicationFailureKind.NotFound, "role-not-found")));
        }

        Task<ApplicationResult<IncidentAuthorityProjection>> IIncidentAuthority.GetAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IncidentGetCallCount++;
            if (IncidentFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<IncidentAuthorityProjection>(
                        IncidentFailure));
            }

            return Task.FromResult(
                Incidents.TryGetValue(incidentId, out var incident)
                    ? ApplicationResult.Succeeded(incident)
                    : ApplicationResult.Failed<IncidentAuthorityProjection>(
                        Failure(ApplicationFailureKind.NotFound, "incident-not-found")));
        }
    }

    public enum AuthorityFailurePoint
    {
        Search,
        Incident,
        RoleGet,
        RoleList,
    }
}

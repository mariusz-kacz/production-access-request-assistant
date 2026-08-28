using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class PreparationReviewServiceTests
{
    [Fact]
    public async Task ReloadsEveryDisplayedFactAndReturnsTheExactReadyScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var preparation = CreateReadyPreparation(incidentId: "INC-1");
        var authority = new StubAuthority();
        var service = CreateService(authority);

        var result = await service.LoadAsync(
            new PreparationSnapshot(preparation),
            cancellationToken);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(["requester"], authority.PrincipalReads);
        Assert.Equal(["PROD-1"], authority.EnvironmentReads);
        Assert.Equal([("PROD-1", "ROLE-1")], authority.RoleReads);
        Assert.Equal(["INC-1"], authority.IncidentReads);
        Assert.Equal(preparation.PreparationId, result.Value.PreparationId);
        Assert.Equal("Demo Requester", result.Value.RequesterDisplayName);
        Assert.Equal("requester", result.Value.RequesterId);
        Assert.Equal("Client One", result.Value.ClientDisplayName);
        Assert.Equal("CLIENT-1", result.Value.ClientId);
        Assert.Equal("Primary Production", result.Value.EnvironmentDisplayName);
        Assert.Equal("PROD-1", result.Value.EnvironmentId);
        Assert.Equal("Read only", result.Value.RoleDisplayName);
        Assert.Equal("ROLE-1", result.Value.RoleId);
        Assert.Equal("Incident One", result.Value.IncidentDisplayName);
        Assert.Equal("INC-1", result.Value.IncidentId);
        Assert.Equal(
            "Investigate the production fault.",
            result.Value.Justification);
        Assert.Equal(preparation.ReadyDeadline, result.Value.ReadyDeadline);
    }

    [Fact]
    public async Task NoIncidentReturnsExplicitAbsenceWithoutAuthorityRead()
    {
        var authority = new StubAuthority();
        var service = CreateService(authority);

        var result = await service.LoadAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Null(result.Value.IncidentDisplayName);
        Assert.Null(result.Value.IncidentId);
        Assert.Empty(authority.IncidentReads);
    }

    [Fact]
    public async Task StaleAuthoritativeRoleFailsClosed()
    {
        var authority = new StubAuthority
        {
            Role = new EnvironmentRoleAuthorityProjection(
                "PROD-1",
                "ROLE-1",
                "Read only",
                isCurrentlyAssignable: false),
        };
        var service = CreateService(authority);

        var result = await service.LoadAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("prepared_card_context_mismatch", result.Failure!.Code);
    }

    [Fact]
    public async Task AuthorityFailureIsPreservedAsATypedFailure()
    {
        var failure = new ApplicationFailure(
            ApplicationFailureKind.DependencyUnavailable,
            "reference-source-unavailable",
            "Reference source is unavailable.");
        var authority = new StubAuthority
        {
            EnvironmentFailure = failure,
        };
        var service = CreateService(authority);

        var result = await service.LoadAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TestContext.Current.CancellationToken);

        Assert.Same(failure, result.Failure);
        Assert.Empty(authority.RoleReads);
    }

    private static PreparationReviewService CreateService(
        StubAuthority authority) =>
        new(authority, authority, authority, authority);

    private static RequestPreparation CreateReadyPreparation(string? incidentId) =>
        RequestPreparation.CreateRoot(
            new PreparationBinding(
                PreparationBinding.TeamsChannel,
                "tenant",
                "actor",
                "conversation",
                "requester"),
            new PreparationCandidate(
                "CLIENT-1",
                "PROD-1",
                "ROLE-1",
                "Investigate the production fault.",
                incidentId),
            clarification: null,
            new MaterialChangeAttribution(
                incidentId is null
                    ?
                    [
                        ProposalField.Environment,
                        ProposalField.Role,
                        ProposalField.Justification,
                    ]
                    :
                    [
                        ProposalField.Environment,
                        ProposalField.Incident,
                        ProposalField.Role,
                        ProposalField.Justification,
                    ],
                "test-model",
                providerModelVersion: null,
                "test-prompt",
                "test-schema",
                new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
                "test-correlation"),
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            "test-correlation");

    private sealed class StubAuthority :
        IAuthenticatedPrincipalReader,
        IProductionEnvironmentAuthority,
        IEnvironmentRoleAuthority,
        IIncidentAuthority
    {
        internal List<string> PrincipalReads { get; } = [];

        internal List<string> EnvironmentReads { get; } = [];

        internal List<(string EnvironmentId, string RoleId)> RoleReads { get; } = [];

        internal List<string> IncidentReads { get; } = [];

        internal ApplicationFailure? EnvironmentFailure { get; init; }

        internal EnvironmentRoleAuthorityProjection Role { get; init; } =
            new("PROD-1", "ROLE-1", "Read only", isCurrentlyAssignable: true);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            PrincipalReads.Add(principalId);
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new AuthenticatedPrincipal(
                        "requester",
                        "Demo Requester",
                        PrincipalKind.Requester)));
        }

        public Task<ApplicationResult<EnvironmentAuthorityProjection>> GetAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            EnvironmentReads.Add(environmentId);
            return Task.FromResult(
                EnvironmentFailure is null
                    ? ApplicationResult.Succeeded(
                        new EnvironmentAuthorityProjection(
                            "PROD-1",
                            "Primary Production",
                            "CLIENT-1",
                            "Client One",
                            "approver",
                            isActive: true,
                            isProduction: true,
                            isEligibleForIntake: true))
                    : ApplicationResult.Failed<EnvironmentAuthorityProjection>(
                        EnvironmentFailure));
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRoleAuthorityProjection>>>
            ListAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Review assembly must use exact role reload.");

        Task<ApplicationResult<EnvironmentRoleAuthorityProjection>>
            IEnvironmentRoleAuthority.GetAsync(
                string environmentId,
                string roleId,
                CancellationToken cancellationToken)
        {
            RoleReads.Add((environmentId, roleId));
            return Task.FromResult(ApplicationResult.Succeeded(Role));
        }

        Task<ApplicationResult<IncidentAuthorityProjection>>
            IIncidentAuthority.GetAsync(
                string incidentId,
                CancellationToken cancellationToken)
        {
            IncidentReads.Add(incidentId);
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new IncidentAuthorityProjection(
                        "INC-1",
                        "Incident One",
                        isActive: true,
                        "PROD-1")));
        }
    }
}

using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TargetPreparedRequestCardFactoryTests
{
    [Fact]
    public async Task ReloadsEveryDisplayedFactAndRendersTheExactReadyScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var preparation = CreateReadyPreparation(incidentId: "INC-1");
        var authority = new StubAuthority();
        var factory = new TargetPreparedRequestCardFactory(
            authority,
            authority,
            authority,
            authority);

        var result = await factory.CreateAsync(
            new PreparationSnapshot(preparation),
            "en-US",
            cancellationToken);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(["requester"], authority.PrincipalReads);
        Assert.Equal(["PROD-1"], authority.EnvironmentReads);
        Assert.Equal([("PROD-1", "ROLE-1")], authority.RoleReads);
        Assert.Equal(["INC-1"], authority.IncidentReads);
        var card = Assert.IsType<JsonElement>(result.Value.Content);
        var serialized = card.GetRawText();
        var facts = card.GetProperty("body")[2].GetProperty("facts");
        Assert.Equal(
            "Demo <Requester> (requester)",
            facts[0].GetProperty("value").GetString());
        Assert.Equal(
            "Client & One (CLIENT-1)",
            facts[1].GetProperty("value").GetString());
        Assert.Equal(
            "Primary <Production> (PROD-1)",
            facts[2].GetProperty("value").GetString());
        Assert.Equal(
            "Incident \"One\" (INC-1)",
            facts[4].GetProperty("value").GetString());
        Assert.Equal(
            "Investigate </TextBlock> exactly",
            card.GetProperty("body")[4].GetProperty("text").GetString());
        Assert.DoesNotContain("</TextBlock>", serialized, StringComparison.Ordinal);
        Assert.Contains("\\u003C/TextBlock\\u003E", serialized, StringComparison.Ordinal);
        Assert.Contains(
            $"\"preparationId\":\"{preparation.PreparationId:D}\"",
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoIncidentRendersExplicitAbsenceWithoutAuthorityRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var authority = new StubAuthority();
        var factory = new TargetPreparedRequestCardFactory(
            authority,
            authority,
            authority,
            authority);

        var result = await factory.CreateAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TeamsLocale.Default,
            cancellationToken);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Empty(authority.IncidentReads);
        var card = Assert.IsType<JsonElement>(result.Value.Content);
        Assert.Contains("No incident", card.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleAuthoritativeRoleFailsClosedWithoutRenderingACard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var authority = new StubAuthority
        {
            Role = new EnvironmentRoleAuthorityProjection(
                "PROD-1",
                "ROLE-1",
                "Read only",
                isCurrentlyAssignable: false),
        };
        var factory = new TargetPreparedRequestCardFactory(
            authority,
            authority,
            authority,
            authority);

        var result = await factory.CreateAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TeamsLocale.Default,
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "target_prepared_card_context_mismatch",
            result.Failure!.Code);
    }

    [Fact]
    public async Task AuthorityFailureIsPreservedAsATypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new ApplicationFailure(
            ApplicationFailureKind.DependencyUnavailable,
            "reference-source-unavailable",
            "Reference source is unavailable.");
        var authority = new StubAuthority
        {
            EnvironmentFailure = failure,
        };
        var factory = new TargetPreparedRequestCardFactory(
            authority,
            authority,
            authority,
            authority);

        var result = await factory.CreateAsync(
            new PreparationSnapshot(CreateReadyPreparation(incidentId: null)),
            TeamsLocale.Default,
            cancellationToken);

        Assert.Same(failure, result.Failure);
        Assert.Empty(authority.RoleReads);
    }

    private static RequestPreparation CreateReadyPreparation(string? incidentId) =>
        RequestPreparation.CreateRoot(
            new PreparationBinding(
                PreparationBinding.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                "requester"),
            new PreparationCandidate(
                "CLIENT-1",
                "PROD-1",
                "ROLE-1",
                "Investigate </TextBlock> exactly",
                incidentId),
            clarification: null,
            new MaterialChangeAttribution(
                incidentId is null
                    ? [
                        ProposalField.Environment,
                        ProposalField.Role,
                        ProposalField.Justification,
                    ]
                    : [
                        ProposalField.Environment,
                        ProposalField.Incident,
                        ProposalField.Role,
                        ProposalField.Justification,
                    ],
                "test-model",
                providerModelVersion: null,
                "test-prompt",
                "test-schema",
                new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
                "test-correlation"),
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
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
            new("PROD-1", "ROLE-1", "Read & only", isCurrentlyAssignable: true);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            PrincipalReads.Add(principalId);
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new AuthenticatedPrincipal(
                        "requester",
                        "Demo <Requester>",
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
                            "Primary <Production>",
                            "CLIENT-1",
                            "Client & One",
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
            throw new InvalidOperationException("Card assembly must use exact role reload.");

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
                        "Incident \"One\"",
                        isActive: true,
                        "PROD-1")));
        }
    }
}

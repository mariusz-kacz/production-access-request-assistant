using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class ReferenceAuthorityAdapterTests
{
    [Fact]
    public async Task SearchUsesSharedPolicyOverOnlyCurrentEligibleFacts()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var authority = scope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentSearchAuthority>();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();

        var initial = await authority.SearchAsync(
            "alpha EU primary",
            TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"UPDATE ProductionEnvironments SET IsEligibleForIntake = 0 WHERE Id = 'PROD-ALPHA-EU'",
            TestContext.Current.CancellationToken);
        var afterEligibilityChange = await authority.SearchAsync(
            "alpha EU primary",
            TestContext.Current.CancellationToken);

        Assert.Equal(EnvironmentSearchResultKind.UniqueMatch, initial.Value.Kind);
        Assert.Equal("PROD-ALPHA-EU", Assert.Single(initial.Value.Matches).EnvironmentId);
        Assert.Equal(EnvironmentSearchResultKind.NoMatches, afterEligibilityChange.Value.Kind);
    }

    [Fact]
    public async Task ExactEnvironmentDerivesClientAndHiddenBusinessApproverFact()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var authority = scope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentAuthority>();

        var result = await authority.GetAsync(
            "PROD-ALPHA-EU",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("client-alpha", result.Value.ClientId);
        Assert.Equal("Client Alpha", result.Value.ClientDisplayName);
        Assert.Equal(
            "client-alpha-business-approver",
            result.Value.BusinessApproverPrincipalId);
        Assert.True(result.Value.CanBecomeCanonical);
    }

    [Fact]
    public async Task EntitlementAuthorityListsAndLoadsExactAssignmentsInOrdinalOrder()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var authority = scope.ServiceProvider
            .GetRequiredService<IEnvironmentRoleAuthority>();

        var listed = await authority.ListAsync(
            "PROD-ALPHA-EU",
            TestContext.Current.CancellationToken);
        var exact = await authority.GetAsync(
            "PROD-ALPHA-EU",
            ProductionRoleIds.Support,
            TestContext.Current.CancellationToken);
        var missing = await authority.GetAsync(
            "PROD-BETA-UK",
            ProductionRoleIds.Deployment,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ProductionRoleIds.Deployment,
                ProductionRoleIds.ReadOnly,
                ProductionRoleIds.Support,
            ],
            listed.Value.Select(role => role.RoleId));
        Assert.True(exact.Value.IsCurrentlyAssignable);
        Assert.Equal(ApplicationFailureKind.NotFound, missing.Failure!.Kind);
        Assert.Equal("environment-role-not-found", missing.Failure.Code);
    }

    [Fact]
    public async Task IncidentAuthorityPreservesZeroOneAndManyCurrentEligibleLinks()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        context.Incidents.AddRange(
            new ReferenceIncident("INC-ZERO", "No eligible environment", isActive: true),
            new ReferenceIncident("INC-MANY", "Multiple environments", isActive: true));
        context.IncidentEnvironmentLinks.AddRange(
            new ReferenceIncidentEnvironmentLink("INC-MANY", "PROD-BETA-UK"),
            new ReferenceIncidentEnvironmentLink("INC-MANY", "PROD-ALPHA-EU"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var authority = scope.ServiceProvider.GetRequiredService<IIncidentAuthority>();

        var zero = await authority.GetAsync(
            "INC-ZERO",
            TestContext.Current.CancellationToken);
        var one = await authority.GetAsync(
            "INC-1042",
            TestContext.Current.CancellationToken);
        var many = await authority.GetAsync(
            "INC-MANY",
            TestContext.Current.CancellationToken);

        Assert.Empty(zero.Value.EligibleEnvironmentIds);
        Assert.Equal(["PROD-ALPHA-EU"], one.Value.EligibleEnvironmentIds);
        Assert.Equal(
            ["PROD-ALPHA-EU", "PROD-BETA-UK"],
            many.Value.EligibleEnvironmentIds);
    }

    [Fact]
    public async Task EntitlementSourceFailureDoesNotDisableEnvironmentAuthority()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        await context.Database.ExecuteSqlAsync(
            $"DROP TABLE EnvironmentRoles",
            TestContext.Current.CancellationToken);
        var environments = scope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentAuthority>();
        var roles = scope.ServiceProvider.GetRequiredService<IEnvironmentRoleAuthority>();

        var environmentResult = await environments.GetAsync(
            "PROD-ALPHA-EU",
            TestContext.Current.CancellationToken);
        var roleResult = await roles.ListAsync(
            "PROD-ALPHA-EU",
            TestContext.Current.CancellationToken);

        Assert.True(environmentResult.IsSuccess);
        Assert.Equal(ApplicationFailureKind.DependencyUnavailable, roleResult.Failure!.Kind);
        Assert.Equal("environment-role-authority-unavailable", roleResult.Failure.Code);
    }

    [Fact]
    public async Task CancellationIsReturnedAsASourceSpecificTypedFailure()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var authority = scope.ServiceProvider
            .GetRequiredService<IProductionEnvironmentAuthority>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await authority.GetAsync(
            "PROD-ALPHA-EU",
            cancellation.Token);

        Assert.Equal(ApplicationFailureKind.Cancelled, result.Failure!.Kind);
        Assert.Equal("environment-authority-cancelled", result.Failure.Code);
    }
}

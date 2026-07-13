using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Infrastructure;

public sealed class GovernedAccessWebFactoryTests
{
    [Fact]
    public async Task ResetDatabaseAsyncRestoresTheExactSyntheticDataset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();

        await factory.ResetDatabaseAsync(cancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
            dbContext.Clients.Add(new Client("client-test", "Test client"));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await factory.ResetDatabaseAsync(cancellationToken);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();

        Assert.Equal(2, await verificationContext.Clients.CountAsync(cancellationToken));
        Assert.Equal(
            4,
            await verificationContext.AuthenticatedPrincipals.CountAsync(cancellationToken));
        Assert.False(
            await verificationContext.Clients.AnyAsync(
                client => client.Id == "client-test",
                cancellationToken));
    }

    [Fact]
    public void ClockCanBeAdvancedWithoutUsingWallClockTime()
    {
        var initialTime = new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
        var clock = new DeterministicClock(initialTime);

        clock.Advance(TimeSpan.FromMinutes(45));

        Assert.Equal(initialTime.AddMinutes(45), clock.UtcNow);
    }

    [Theory]
    [InlineData(DemoPrincipalKeys.Requester, PrincipalKind.Requester)]
    [InlineData(DemoPrincipalKeys.ClientAlphaApprover, PrincipalKind.BusinessApprover)]
    [InlineData(DemoPrincipalKeys.ClientBetaApprover, PrincipalKind.BusinessApprover)]
    [InlineData(DemoPrincipalKeys.DevOpsApprover, PrincipalKind.DevOpsApprover)]
    public void ResolvePrincipalReturnsOnlyServerConfiguredIdentity(
        string principalKey,
        PrincipalKind expectedKind)
    {
        var principal = GovernedAccessWebFactory.ResolvePrincipal(principalKey);

        Assert.Equal(principalKey, principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(
            expectedKind.ToString(),
            principal.FindFirst(DemoClaimTypes.PrincipalKind)?.Value);
    }

    [Fact]
    public void ResolvePrincipalRejectsAnUnknownIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => GovernedAccessWebFactory.ResolvePrincipal("browser-controlled-identity"));
    }

    [Fact]
    public async Task FactoryReplacesTheApplicationClockWithItsDeterministicClock()
    {
        await using var factory = new GovernedAccessWebFactory();

        var registeredClock = factory.Services.GetRequiredService<IClock>();

        Assert.Same(factory.Clock, registeredClock);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, registeredClock.UtcNow);
    }
}

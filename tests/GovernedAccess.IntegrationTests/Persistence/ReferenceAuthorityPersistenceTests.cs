using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class ReferenceAuthorityPersistenceTests
{
    [Fact]
    public async Task ForeignKeysRejectIncidentsForUnknownEnvironments()
    {
        await using var fixture = await ReferenceAuthorityFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        context.Incidents.Add(
            new ReferenceIncident(
                "INC-UNKNOWN",
                "Incident with an invalid environment",
                isActive: true,
                environmentId: "PROD-UNKNOWN"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReferenceDataRemainsUsableAfterIdempotentInitializationAndRestart()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"reference-authority-{Guid.NewGuid():N}.db");

        try
        {
            await using (var first = await ReferenceAuthorityFixture.CreateAsync(databasePath))
            {
                await ReferenceAuthorityDatabase.InitializeAsync(
                    first.Services,
                    TestContext.Current.CancellationToken);
            }

            await using var restarted = await ReferenceAuthorityFixture.CreateAsync(databasePath);
            await using var scope = restarted.Services.CreateAsyncScope();
            var authority = scope.ServiceProvider
                .GetRequiredService<IProductionEnvironmentAuthority>();

            var environment = await authority.GetAsync(
                "PROD-ALPHA-EU",
                TestContext.Current.CancellationToken);

            Assert.True(environment.IsSuccess, environment.Failure?.Message);
            Assert.Equal("client-alpha", environment.Value.ClientId);
            Assert.True(environment.Value.CanBecomeCanonical);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

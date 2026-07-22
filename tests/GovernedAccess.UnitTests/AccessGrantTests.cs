using GovernedAccess.Core.Domain;

namespace GovernedAccess.UnitTests;

public sealed class AccessGrantTests
{
    [Fact]
    public void GrantAlwaysExpiresEightHoursAfterActivation()
    {
        var activation = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        var requestId = Guid.Parse("4f1d09cb-87ac-43bb-aa98-75156f5f35e4");
        var grant = new AccessGrant(
            Guid.Parse("bddc152c-f65c-4afd-8173-955ffafae55c"),
            requestId,
            "requester",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            activation,
            "grant-correlation");

        Assert.Equal(TimeSpan.FromHours(8), AccessGrant.FixedLifetime);
        Assert.Equal(activation.AddHours(8), grant.ExpiresAt);
        Assert.Equal(requestId, grant.RequestId);
    }
}

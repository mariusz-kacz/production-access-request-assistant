using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;

namespace GovernedAccess.UnitTests;

public sealed class AccessRequestPreparationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TargetRequestRequiresAndRetainsPreparationId()
    {
        var preparationId = Guid.NewGuid();

        var request = new AccessRequest(
            Guid.NewGuid(),
            preparationId,
            "requester",
            Details(),
            CreatedAt,
            "target-confirmation");

        Assert.Equal(preparationId, request.PreparationId);
        Assert.Throws<ArgumentException>(() => new AccessRequest(
            Guid.NewGuid(),
            Guid.Empty,
            "requester",
            Details(),
            CreatedAt,
            "target-confirmation"));
    }

    [Fact]
    public void DeliveredRequestCreationTemporarilyRemainsPreparationNeutral()
    {
        var request = new AccessRequest(
            Guid.NewGuid(),
            "requester",
            Details(),
            CreatedAt,
            "delivered-confirmation");

        Assert.Null(request.PreparationId);
    }

    private static ValidatedRequestDetails Details() =>
        new(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042");
}

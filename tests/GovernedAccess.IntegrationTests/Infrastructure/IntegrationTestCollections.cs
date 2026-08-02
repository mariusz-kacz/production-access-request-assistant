[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GovernedAccess.IntegrationTests.Infrastructure;

public static class IntegrationTestCollections
{
    public const string TestLevelTrait = "TestLevel";
    public const string FullHostLevel = "FullHost";
}

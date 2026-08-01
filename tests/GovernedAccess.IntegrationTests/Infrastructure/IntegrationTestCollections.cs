namespace GovernedAccess.IntegrationTests.Infrastructure;

public static class IntegrationTestCollections
{
    public const string FullApplication = "Full application integration";
    public const string TestLevelTrait = "TestLevel";
    public const string FullHostLevel = "FullHost";
}

[CollectionDefinition(
    IntegrationTestCollections.FullApplication,
    DisableParallelization = true)]
public sealed class FullApplicationIntegrationGroup
    : ICollectionFixture<DefaultWebApplicationFixture>,
      ICollectionFixture<ConfigurableTeamsFixture>,
      ICollectionFixture<HistorySensitiveTeamsFixture>;

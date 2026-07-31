namespace GovernedAccess.IntegrationTests.Infrastructure;

public static class IntegrationTestCollections
{
    public const string FullApplication = "Full application integration";
}

[CollectionDefinition(
    IntegrationTestCollections.FullApplication,
    DisableParallelization = true)]
public sealed class FullApplicationIntegrationGroup;

using GovernedAccess.Core.Preparations.Authority;

namespace GovernedAccess.UnitTests;

public sealed class PreparationAuthorityContractTests
{
    [Fact]
    public void EnvironmentProjectionsKeepSearchAndExactAuthorityDistinct()
    {
        var searchDocument = new EnvironmentSearchDocument(
            " PROD-ALPHA-EU ",
            " Client Alpha EU Production ",
            " client-alpha ",
            " Client Alpha ",
            " EU ",
            EnvironmentClassification.Primary,
            isActive: true,
            isProduction: true,
            isEligibleForIntake: true);
        var exactEnvironment = new EnvironmentAuthorityProjection(
            " PROD-ALPHA-EU ",
            " Client Alpha EU Production ",
            " client-alpha ",
            " Client Alpha ",
            " client-alpha-business-approver ",
            isActive: true,
            isProduction: true,
            isEligibleForIntake: true);

        Assert.Equal("PROD-ALPHA-EU", searchDocument.EnvironmentId);
        Assert.Equal("Client Alpha EU Production", searchDocument.DisplayName);
        Assert.Equal("client-alpha", searchDocument.ClientId);
        Assert.Equal("Client Alpha", searchDocument.ClientDisplayName);
        Assert.Equal("EU", searchDocument.Region);
        Assert.Equal(EnvironmentClassification.Primary, searchDocument.Classification);
        Assert.True(searchDocument.CanBecomeCanonical);

        Assert.Equal("PROD-ALPHA-EU", exactEnvironment.EnvironmentId);
        Assert.Equal("client-alpha", exactEnvironment.ClientId);
        Assert.Equal(
            "client-alpha-business-approver",
            exactEnvironment.BusinessApproverPrincipalId);
        Assert.True(exactEnvironment.CanBecomeCanonical);
        Assert.NotEqual(searchDocument.GetType(), exactEnvironment.GetType());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void EnvironmentEligibilityRequiresEveryAuthoritativeFact(
        bool isActive,
        bool isProduction,
        bool isEligibleForIntake)
    {
        var searchDocument = CreateSearchDocument(
            isActive: isActive,
            isProduction: isProduction,
            isEligibleForIntake: isEligibleForIntake);
        var exactEnvironment = new EnvironmentAuthorityProjection(
            "PROD-ALPHA-EU",
            "Client Alpha EU Production",
            "client-alpha",
            "Client Alpha",
            "client-alpha-business-approver",
            isActive,
            isProduction,
            isEligibleForIntake);

        Assert.False(searchDocument.CanBecomeCanonical);
        Assert.False(exactEnvironment.CanBecomeCanonical);
    }

    [Fact]
    public void RoleAuthorityBindsCurrentAssignmentToOneExactEnvironment()
    {
        var assignable = new EnvironmentRoleAuthorityProjection(
            " PROD-ALPHA-EU ",
            " ProductionSupport ",
            " Production support ",
            isCurrentlyAssignable: true);
        var unavailable = new EnvironmentRoleAuthorityProjection(
            "PROD-ALPHA-EU",
            "ProductionDeployment",
            "Production deployment",
            isCurrentlyAssignable: false);

        Assert.Equal("PROD-ALPHA-EU", assignable.EnvironmentId);
        Assert.Equal("ProductionSupport", assignable.RoleId);
        Assert.Equal("Production support", assignable.DisplayName);
        Assert.True(assignable.IsCurrentlyAssignable);
        Assert.False(unavailable.IsCurrentlyAssignable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" PROD-ALPHA-EU ")]
    public void IncidentAuthorityCarriesOneNullableEnvironment(string? environmentId)
    {
        var incident = new IncidentAuthorityProjection(
            " INC-1042 ",
            " Elevated customer errors ",
            isActive: true,
            environmentId: environmentId);

        Assert.Equal("INC-1042", incident.IncidentId);
        Assert.Equal("Elevated customer errors", incident.Title);
        Assert.True(incident.IsActive);
        Assert.Equal(environmentId?.Trim(), incident.EnvironmentId);
    }

    [Fact]
    public void IncidentAuthorityRejectsBlankEnvironmentEvidence()
    {
        Assert.Throws<ArgumentException>(
            () => new IncidentAuthorityProjection(
                "INC-1042",
                "Elevated customer errors",
                isActive: true,
                environmentId: " "));
    }

    [Fact]
    public void AuthorityProjectionValuesRejectMissingOrUnknownFacts()
    {
        Assert.Throws<ArgumentException>(
            () => CreateSearchDocument(environmentId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateSearchDocument(
                classification: (EnvironmentClassification)int.MaxValue));
    }

    private static EnvironmentSearchDocument CreateSearchDocument(
        string environmentId = "PROD-ALPHA-EU",
        EnvironmentClassification classification = EnvironmentClassification.Primary,
        bool isActive = true,
        bool isProduction = true,
        bool isEligibleForIntake = true) =>
        new(
            environmentId,
            "Client Alpha EU Production",
            "client-alpha",
            "Client Alpha",
            "EU",
            classification,
            isActive,
            isProduction,
            isEligibleForIntake);
}

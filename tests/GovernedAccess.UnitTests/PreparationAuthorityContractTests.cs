using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void IncidentAuthorityPreservesEveryEligibleEnvironmentLink(int linkCount)
    {
        var environmentIds = Enumerable.Range(1, linkCount)
            .Reverse()
            .Select(index => $"PROD-ALPHA-{index}")
            .ToArray();

        var incident = new IncidentAuthorityProjection(
            " INC-1042 ",
            " Elevated customer errors ",
            isActive: true,
            environmentIds);

        Assert.Equal("INC-1042", incident.IncidentId);
        Assert.Equal("Elevated customer errors", incident.Title);
        Assert.True(incident.IsActive);
        Assert.Equal(
            environmentIds.Order(StringComparer.Ordinal),
            incident.EligibleEnvironmentIds);
    }

    [Fact]
    public void IncidentAuthorityRejectsDuplicateRelationshipEvidence()
    {
        Assert.Throws<ArgumentException>(
            () => new IncidentAuthorityProjection(
                "INC-1042",
                "Elevated customer errors",
                isActive: true,
                ["PROD-ALPHA-EU", "PROD-ALPHA-EU"]));
    }

    [Fact]
    public void AuthorityPortsKeepEnterpriseSourcesSeparate()
    {
        Assert.Equal(
            typeof(Task<ApplicationResult<EnvironmentSearchResult>>),
            typeof(IProductionEnvironmentSearchAuthority)
                .GetMethod(nameof(IProductionEnvironmentSearchAuthority.SearchAsync))!
                .ReturnType);
        Assert.Equal(
            typeof(Task<ApplicationResult<EnvironmentAuthorityProjection>>),
            typeof(IProductionEnvironmentAuthority)
                .GetMethod(nameof(IProductionEnvironmentAuthority.GetAsync))!
                .ReturnType);
        Assert.Equal(
            2,
            typeof(IEnvironmentRoleAuthority).GetMethods().Length);
        Assert.Equal(
            typeof(Task<ApplicationResult<IncidentAuthorityProjection>>),
            typeof(IIncidentAuthority)
                .GetMethod(nameof(IIncidentAuthority.GetAsync))!
                .ReturnType);
    }

    [Fact]
    public void AuthorityProjectionValuesRejectMissingOrUnknownFacts()
    {
        Assert.Throws<ArgumentException>(
            () => CreateSearchDocument(environmentId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateSearchDocument(
                classification: (EnvironmentClassification)int.MaxValue));
        Assert.Throws<ArgumentException>(
            () => new IncidentAuthorityProjection(
                "INC-1042",
                "Elevated customer errors",
                isActive: true,
                [" "]));
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

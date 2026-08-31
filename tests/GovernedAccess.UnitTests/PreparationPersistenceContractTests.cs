using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class PreparationPersistenceContractTests
{
    [Fact]
    public void StoreContractExposesOnlyProviderNeutralPreparationOperations()
    {
        var storeMethods = typeof(IRequestPreparationStore).GetMethods();

        Assert.Equal(
            [
                "Add",
                "GetActiveAsync",
                "GetAsync",
                "GetLatestAsync",
                "SaveChangesAsync",
            ],
            storeMethods.Select(method => method.Name).Order().ToArray());
        Assert.All(
            storeMethods.SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)),
            type => Assert.DoesNotContain("EntityFrameworkCore", type.FullName ?? type.Name));

        Assert.Equal(
            typeof(Task<ApplicationResult<AuthenticatedPrincipal>>),
            typeof(IAuthenticatedPrincipalReader)
                .GetMethod(nameof(IAuthenticatedPrincipalReader.GetPrincipalAsync))!
                .ReturnType);
    }

}

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestValidationTests
{
    [Fact]
    public async Task ValidateAsyncReturnsNormalizedAuthoritativeFieldsForAValidRequest()
    {
        var context = new StubAuthoritativeContextReader();
        var validator = new RequestValidator(context);

        var result = await validator.ValidateAsync(
            new RequestValidationInput(
                " client-alpha ",
                " PROD-ALPHA-EU ",
                $" {ProductionRoleIds.ReadOnly} ",
                240,
                "  Investigate the active production incident.  ",
                " INC-1042 "),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("client-alpha", result.Value.ClientId);
        Assert.Equal("PROD-ALPHA-EU", result.Value.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, result.Value.RequestedRoleId);
        Assert.Equal(240, result.Value.RequestedDurationMinutes);
        Assert.Equal("Investigate the active production incident.", result.Value.Justification);
        Assert.Equal("INC-1042", result.Value.IncidentId);
    }

    [Fact]
    public async Task ValidateAsyncAllowsAnOmittedIncidentAndTheMaximumDuration()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(durationMinutes: 480, incidentId: "   "),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(480, result.Value.RequestedDurationMinutes);
        Assert.Null(result.Value.IncidentId);
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownClient()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(clientId: "client-unknown"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "clientId", "client_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownEnvironment()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(environmentId: "PROD-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnEnvironmentOwnedByAnotherClient()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(environmentId: "PROD-BETA-UK"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_client_mismatch");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnInactiveEnvironment()
    {
        var context = new StubAuthoritativeContextReader();
        context.AlphaEnvironment.UpdateOperationalState(false, 480);
        var validator = new RequestValidator(context);

        var result = await validator.ValidateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_inactive");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnavailableRole()
    {
        var context = new StubAuthoritativeContextReader();
        context.AlphaReadOnlyRole.SetAvailability(false);
        var validator = new RequestValidator(context);

        var result = await validator.ValidateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedRoleId", "role_unavailable");
    }

    [Fact]
    public async Task ValidateAsyncRejectsARoleThatIsNotAllowedForTheEnvironment()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(requestedRoleId: ProductionRoleIds.Support),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedRoleId", "role_unavailable");
    }

    [Theory]
    [InlineData(0, "duration_must_be_positive")]
    [InlineData(-1, "duration_must_be_positive")]
    [InlineData(481, "duration_exceeds_environment_maximum")]
    public async Task ValidateAsyncRejectsAnInvalidDuration(int durationMinutes, string expectedCode)
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(durationMinutes: durationMinutes),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedDurationMinutes", expectedCode);
    }

    [Theory]
    [InlineData(null, "justification_required")]
    [InlineData("", "justification_required")]
    [InlineData("        ", "justification_required")]
    [InlineData("Too short", "justification_length_invalid")]
    public async Task ValidateAsyncRejectsAMissingOrShortJustification(
        string? justification,
        string expectedCode)
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(justification: justification),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "justification", expectedCode);
    }

    [Fact]
    public async Task ValidateAsyncRejectsAJustificationLongerThanTheDomainMaximum()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(justification: new string('a', AccessRequest.MaximumJustificationLength + 1)),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "justification", "justification_length_invalid");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownIncident()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnInactiveIncident()
    {
        var context = new StubAuthoritativeContextReader();
        context.AlphaIncident.SetStatus(IncidentStatus.Inactive);
        var validator = new RequestValidator(context);

        var result = await validator.ValidateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_inactive");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnIncidentOwnedByAnotherClient()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-BETA"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_client_mismatch");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnIncidentAssociatedWithAnotherEnvironment()
    {
        var validator = new RequestValidator(new StubAuthoritativeContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-ALPHA-OTHER"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_environment_mismatch");
    }

    private static RequestValidationInput ValidInput(
        string? clientId = "client-alpha",
        string? environmentId = "PROD-ALPHA-EU",
        string? requestedRoleId = ProductionRoleIds.ReadOnly,
        int durationMinutes = 240,
        string? justification = "Investigate the active production incident.",
        string? incidentId = "INC-1042")
    {
        return new RequestValidationInput(
            clientId,
            environmentId,
            requestedRoleId,
            durationMinutes,
            justification,
            incidentId);
    }

    private static void AssertFieldError(
        ApplicationResult<ValidatedRequestFields> result,
        string expectedField,
        string expectedCode)
    {
        Assert.True(result.IsFailure);
        var failure = Assert.IsType<ApplicationFailure>(result.Failure);
        Assert.Equal(ApplicationFailureKind.Validation, failure.Kind);
        Assert.Contains(
            failure.FieldErrors,
            error => error.Field == expectedField && error.Code == expectedCode);
    }

    private sealed class StubAuthoritativeContextReader : IAuthoritativeContextReader
    {
        private readonly Dictionary<string, Client> clients;
        private readonly Dictionary<string, ProductionEnvironment> environments;
        private readonly Dictionary<(string EnvironmentId, string RoleId), EnvironmentRole> roles;
        private readonly Dictionary<string, Incident> incidents;

        public StubAuthoritativeContextReader()
        {
            var alphaClient = new Client("client-alpha", "Client Alpha");
            var betaClient = new Client("client-beta", "Client Beta");
            AlphaEnvironment = new ProductionEnvironment(
                "PROD-ALPHA-EU",
                alphaClient.Id,
                "Client Alpha Production EU",
                true,
                480,
                "alpha-approver");
            var betaEnvironment = new ProductionEnvironment(
                "PROD-BETA-UK",
                betaClient.Id,
                "Client Beta Production UK",
                true,
                240,
                "beta-approver");
            AlphaReadOnlyRole = new EnvironmentRole(
                AlphaEnvironment.Id,
                ProductionRoleIds.ReadOnly,
                true);
            var betaReadOnlyRole = new EnvironmentRole(
                betaEnvironment.Id,
                ProductionRoleIds.ReadOnly,
                true);
            AlphaIncident = new Incident(
                "INC-1042",
                alphaClient.Id,
                AlphaEnvironment.Id,
                "Active Alpha incident",
                IncidentStatus.Active);
            var betaIncident = new Incident(
                "INC-BETA",
                betaClient.Id,
                betaEnvironment.Id,
                "Active Beta incident",
                IncidentStatus.Active);
            var otherAlphaEnvironmentIncident = new Incident(
                "INC-ALPHA-OTHER",
                alphaClient.Id,
                "PROD-ALPHA-OTHER",
                "Incident for another Alpha environment",
                IncidentStatus.Active);

            clients = new Dictionary<string, Client>(StringComparer.Ordinal)
            {
                [alphaClient.Id] = alphaClient,
                [betaClient.Id] = betaClient,
            };
            environments = new Dictionary<string, ProductionEnvironment>(StringComparer.Ordinal)
            {
                [AlphaEnvironment.Id] = AlphaEnvironment,
                [betaEnvironment.Id] = betaEnvironment,
            };
            roles = new Dictionary<(string EnvironmentId, string RoleId), EnvironmentRole>
            {
                [(AlphaReadOnlyRole.EnvironmentId, AlphaReadOnlyRole.RoleId)] = AlphaReadOnlyRole,
                [(betaReadOnlyRole.EnvironmentId, betaReadOnlyRole.RoleId)] = betaReadOnlyRole,
            };
            incidents = new Dictionary<string, Incident>(StringComparer.Ordinal)
            {
                [AlphaIncident.Id] = AlphaIncident,
                [betaIncident.Id] = betaIncident,
                [otherAlphaEnvironmentIncident.Id] = otherAlphaEnvironmentIncident,
            };
        }

        public ProductionEnvironment AlphaEnvironment { get; }

        public EnvironmentRole AlphaReadOnlyRole { get; }

        public Incident AlphaIncident { get; }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            return GetAsync(clients, clientId, "client_not_found", cancellationToken);
        }

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            return GetAsync(
                environments,
                environmentId,
                "environment_not_found",
                cancellationToken);
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken)
        {
            return GetAsync(
                roles,
                (environmentId, roleId),
                "role_not_found",
                cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<EnvironmentRole>>> GetEnvironmentRolesAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EnvironmentRole> environmentRoles = roles.Values
                .Where(role => string.Equals(role.EnvironmentId, environmentId, StringComparison.Ordinal))
                .ToArray();
            return Task.FromResult(ApplicationResult.Succeeded(environmentRoles));
        }

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
            return GetAsync(incidents, incidentId, "incident_not_found", cancellationToken);
        }

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(NotFound<AuthenticatedPrincipal>("principal_not_found"));
        }

        private static Task<ApplicationResult<TValue>> GetAsync<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> values,
            TKey key,
            string notFoundCode,
            CancellationToken cancellationToken)
            where TKey : notnull
            where TValue : notnull
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = values.TryGetValue(key, out var value)
                ? ApplicationResult.Succeeded(value)
                : NotFound<TValue>(notFoundCode);
            return Task.FromResult(result);
        }

        private static ApplicationResult<T> NotFound<T>(string code)
            where T : notnull
        {
            return ApplicationResult.Failed<T>(
                new ApplicationFailure(
                    ApplicationFailureKind.NotFound,
                    code,
                    "The authoritative record was not found."));
        }
    }
}

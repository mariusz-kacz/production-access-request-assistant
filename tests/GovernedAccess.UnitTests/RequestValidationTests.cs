using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestValidationTests
{
    [Fact]
    public async Task CandidateAssessmentClearsAnUnknownPartialClientImmediately()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.AssessCandidateAsync(
            new RequestCandidate(
                "ClientA",
                environmentId: null,
                requestedRoleId: null,
                justification: null,
                incidentId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentRejected>(
            result.Value);
        Assert.Null(assessment.Candidate.ClientId);
        var error = Assert.Single(assessment.Errors);
        Assert.Equal("clientId", error.Field);
        Assert.Equal("client_not_found", error.Code);
    }

    [Fact]
    public async Task CandidateAssessmentDerivesCanonicalClientFromEnvironment()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.AssessCandidateAsync(
            new RequestCandidate(
                "Client Alpha",
                "PROD-ALPHA-EU",
                requestedRoleId: null,
                justification: null,
                incidentId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentIncomplete>(
            result.Value);
        Assert.Equal("client-alpha", assessment.Candidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", assessment.Candidate.EnvironmentId);
    }

    [Fact]
    public async Task CandidateAssessmentDerivesCanonicalScopeFromActiveIncident()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.AssessCandidateAsync(
            new RequestCandidate(
                clientId: null,
                environmentId: null,
                requestedRoleId: null,
                justification: null,
                "INC-1042"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentIncomplete>(
            result.Value);
        Assert.Equal("client-alpha", assessment.Candidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", assessment.Candidate.EnvironmentId);
        Assert.Equal("INC-1042", assessment.Candidate.IncidentId);
    }

    [Fact]
    public async Task CandidateAssessmentClearsOnlyAnUnknownIncident()
    {
        var validator = new RequestValidator(new StubRequestContextReader());
        var candidate = new RequestCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-UNKNOWN");

        var result = await validator.AssessCandidateAsync(
            candidate,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentRejected>(
            result.Value);
        var sanitized = assessment.Candidate;
        Assert.Equal(candidate.ClientId, sanitized.ClientId);
        Assert.Equal(candidate.EnvironmentId, sanitized.EnvironmentId);
        Assert.Equal(candidate.RequestedRoleId, sanitized.RequestedRoleId);
        Assert.Equal(candidate.Justification, sanitized.Justification);
        Assert.Null(sanitized.IncidentId);
        var error = Assert.Single(assessment.Errors);
        Assert.Equal("incidentId", error.Field);
        Assert.Equal("incident_not_found", error.Code);
    }

    [Fact]
    public async Task CandidateAssessmentClearsARoleUnavailableForCanonicalEnvironment()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.AssessCandidateAsync(
            new RequestCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.Support,
                justification: null,
                incidentId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentRejected>(
            result.Value);
        Assert.Null(assessment.Candidate.RequestedRoleId);
        var error = Assert.Single(assessment.Errors);
        Assert.Equal("requestedRoleId", error.Field);
        Assert.Equal("role_unavailable", error.Code);
    }

    [Fact]
    public async Task CandidateAssessmentReturnsReadyAfterOneAuthoritativePass()
    {
        var requestContext = new StubRequestContextReader();
        var validator = new RequestValidator(requestContext);

        var result = await validator.AssessCandidateAsync(
            new RequestCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var assessment = Assert.IsType<RequestCandidateAssessmentReady>(
            result.Value);
        Assert.Equal("client-alpha", assessment.Fields.ClientId);
        Assert.Equal("PROD-ALPHA-EU", assessment.Fields.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, assessment.Fields.RequestedRoleId);
        Assert.Equal("INC-1042", assessment.Fields.IncidentId);
        Assert.Equal(1, requestContext.ClientLookupCount);
        Assert.Equal(1, requestContext.EnvironmentLookupCount);
        Assert.Equal(1, requestContext.RoleLookupCount);
        Assert.Equal(1, requestContext.IncidentLookupCount);
    }

    [Fact]
    public async Task ValidateAsyncReturnsNormalizedFieldsForAValidRequest()
    {
        var requestContext = new StubRequestContextReader();
        var validator = new RequestValidator(requestContext);

        var result = await validator.ValidateAsync(
            new RequestValidationInput(
                " client-alpha ",
                " PROD-ALPHA-EU ",
                $" {ProductionRoleIds.ReadOnly} ",
                "  Investigate the active production incident.  ",
                " INC-1042 "),
            TestContext.Current.CancellationToken);

        var fields = AssertValid(result);
        Assert.Equal("client-alpha", fields.ClientId);
        Assert.Equal("PROD-ALPHA-EU", fields.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, fields.RequestedRoleId);
        Assert.Equal("Investigate the active production incident.", fields.Justification);
        Assert.Equal("INC-1042", fields.IncidentId);
    }

    [Fact]
    public async Task ValidateAsyncAllowsAnOmittedIncident()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "   "),
            TestContext.Current.CancellationToken);

        var fields = AssertValid(result);
        Assert.Null(fields.IncidentId);
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownClient()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(clientId: "client-unknown"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "clientId", "client_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownEnvironment()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(environmentId: "PROD-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnEnvironmentOwnedByAnotherClient()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(environmentId: "PROD-BETA-UK"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_client_mismatch");
    }

    [Fact]
    public async Task ValidateAsyncRejectsARoleThatIsNotAllowedForTheEnvironment()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(requestedRoleId: ProductionRoleIds.Support),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedRoleId", "role_unavailable");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAGloballyUnsupportedRole()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(requestedRoleId: "ProductionAdministrator"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedRoleId", "role_unsupported");
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
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(justification: justification),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "justification", expectedCode);
    }

    [Fact]
    public async Task ValidateAsyncRejectsAJustificationLongerThanTheDomainMaximum()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(justification: new string('a', AccessRequest.MaximumJustificationLength + 1)),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "justification", "justification_length_invalid");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnknownIncident()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_not_found");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnInactiveIncident()
    {
        var requestContext = new StubRequestContextReader();
        requestContext.AlphaIncident.SetStatus(IncidentStatus.Inactive);
        var validator = new RequestValidator(requestContext);

        var result = await validator.ValidateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_inactive");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnIncidentOwnedByAnotherClient()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-BETA"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_client_mismatch");
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnIncidentAssociatedWithAnotherEnvironment()
    {
        var validator = new RequestValidator(new StubRequestContextReader());

        var result = await validator.ValidateAsync(
            ValidInput(incidentId: "INC-ALPHA-OTHER"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_environment_mismatch");
    }

    [Fact]
    public async Task ValidateAsyncPreservesAnOperationLevelContextFailure()
    {
        var requestContext = new StubRequestContextReader
        {
            ClientFailure = new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "request_context_unavailable",
                "Request context is unavailable."),
        };
        var validator = new RequestValidator(requestContext);

        var result = await validator.ValidateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<RequestValidationFailed>(result);
        Assert.Same(requestContext.ClientFailure, failure.Failure);
    }

    private static RequestValidationInput ValidInput(
        string? clientId = "client-alpha",
        string? environmentId = "PROD-ALPHA-EU",
        string? requestedRoleId = ProductionRoleIds.ReadOnly,
        string? justification = "Investigate the active production incident.",
        string? incidentId = "INC-1042")
    {
        return new RequestValidationInput(
            clientId,
            environmentId,
            requestedRoleId,
            justification,
            incidentId);
    }

    private static void AssertFieldError(
        RequestValidationOutcome result,
        string expectedField,
        string expectedCode)
    {
        var rejection = Assert.IsType<RequestValidationRejected>(result);
        Assert.Contains(
            rejection.Errors,
            error => error.Field == expectedField && error.Code == expectedCode);
    }

    private static ValidatedRequestFields AssertValid(RequestValidationOutcome result)
    {
        return Assert.IsType<RequestValidationSucceeded>(result).Fields;
    }

    private sealed class StubRequestContextReader : IRequestContextReader
    {
        private readonly Dictionary<string, Client> clients;
        private readonly Dictionary<string, ProductionEnvironment> environments;
        private readonly Dictionary<(string EnvironmentId, string RoleId), EnvironmentRole> roles;
        private readonly Dictionary<string, Incident> incidents;

        public StubRequestContextReader()
        {
            var alphaClient = new Client("client-alpha", "Client Alpha");
            var betaClient = new Client("client-beta", "Client Beta");
            AlphaEnvironment = new ProductionEnvironment(
                "PROD-ALPHA-EU",
                alphaClient.Id,
                "Client Alpha Production EU",
                "alpha-approver");
            var betaEnvironment = new ProductionEnvironment(
                "PROD-BETA-UK",
                betaClient.Id,
                "Client Beta Production UK",
                "beta-approver");
            var alphaReadOnlyRole = new EnvironmentRole(
                AlphaEnvironment.Id,
                ProductionRoleIds.ReadOnly);
            var betaReadOnlyRole = new EnvironmentRole(
                betaEnvironment.Id,
                ProductionRoleIds.ReadOnly);
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
                [(alphaReadOnlyRole.EnvironmentId, alphaReadOnlyRole.RoleId)] = alphaReadOnlyRole,
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

        public Incident AlphaIncident { get; }

        public ApplicationFailure? ClientFailure { get; init; }

        public int ClientLookupCount { get; private set; }

        public int EnvironmentLookupCount { get; private set; }

        public int RoleLookupCount { get; private set; }

        public int IncidentLookupCount { get; private set; }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
            ClientLookupCount++;
            if (ClientFailure is not null)
            {
                return Task.FromResult(ApplicationResult.Failed<Client>(ClientFailure));
            }

            return GetAsync(clients, clientId, "client_not_found", cancellationToken);
        }

        public Task<ApplicationResult<ProductionEnvironment>> GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
        {
            EnvironmentLookupCount++;
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
            RoleLookupCount++;
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
            IncidentLookupCount++;
            return GetAsync(
                incidents,
                incidentId,
                "incident_not_found",
                cancellationToken);
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
                    "The stored record was not found."));
        }
    }
}

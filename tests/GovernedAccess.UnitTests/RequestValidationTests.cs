using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestValidationTests
{
    [Fact]
    public async Task CandidateAssessmentClearsAnUnknownPartialClientImmediately()
    {
        var validator = new RequestDraftValidator(new StubRequestContextReader());

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
        var validator = new RequestDraftValidator(new StubRequestContextReader());

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
        var validator = new RequestDraftValidator(new StubRequestContextReader());

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
        var validator = new RequestDraftValidator(new StubRequestContextReader());
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
        var validator = new RequestDraftValidator(new StubRequestContextReader());

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
        var validator = new RequestDraftValidator(requestContext);

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
        Assert.Equal("client-alpha", assessment.Details.ClientId);
        Assert.Equal("PROD-ALPHA-EU", assessment.Details.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, assessment.Details.RoleId);
        Assert.Equal("INC-1042", assessment.Details.IncidentId);
    }

    [Fact]
    public async Task RevalidateAsyncReturnsTheCanonicalDetailsForAValidRequest()
    {
        var requestContext = new StubRequestContextReader();
        var validator = new AccessRequestValidator(requestContext);

        var expected = ValidDetails();
        var result = await validator.RevalidateAsync(
            expected,
            TestContext.Current.CancellationToken);

        var details = AssertValid(result);
        Assert.Same(expected, details);
    }

    [Fact]
    public async Task RevalidateAsyncAllowsAnOmittedIncident()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(incidentId: null),
            TestContext.Current.CancellationToken);

        var details = AssertValid(result);
        Assert.Null(details.IncidentId);
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnUnknownClient()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(clientId: "client-unknown"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "clientId", "client_not_found");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnUnknownEnvironment()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(environmentId: "PROD-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_not_found");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnEnvironmentOwnedByAnotherClient()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(environmentId: "PROD-BETA-UK"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "environmentId", "environment_client_mismatch");
    }

    [Theory]
    [InlineData(ProductionRoleIds.Support)]
    [InlineData(ProductionRoleIds.Deployment)]
    public async Task RevalidateAsyncRejectsASupportedRoleThatIsNotAllowedForTheEnvironment(
        string roleId)
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(roleId: roleId),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "requestedRoleId", "role_unavailable");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnUnknownIncident()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(incidentId: "INC-UNKNOWN"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_not_found");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnInactiveIncident()
    {
        var requestContext = new StubRequestContextReader();
        requestContext.AlphaIncident.SetStatus(IncidentStatus.Inactive);
        var validator = new AccessRequestValidator(requestContext);

        var result = await validator.RevalidateAsync(
            ValidDetails(),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_inactive");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnIncidentOwnedByAnotherClient()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(incidentId: "INC-BETA"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_client_mismatch");
    }

    [Fact]
    public async Task RevalidateAsyncRejectsAnIncidentAssociatedWithAnotherEnvironment()
    {
        var validator = new AccessRequestValidator(new StubRequestContextReader());

        var result = await validator.RevalidateAsync(
            ValidDetails(incidentId: "INC-ALPHA-OTHER"),
            TestContext.Current.CancellationToken);

        AssertFieldError(result, "incidentId", "incident_environment_mismatch");
    }

    [Fact]
    public async Task RevalidateAsyncPreservesAnOperationLevelContextFailure()
    {
        var requestContext = new StubRequestContextReader
        {
            ClientFailure = new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "request_context_unavailable",
                "Request context is unavailable."),
        };
        var validator = new AccessRequestValidator(requestContext);

        var result = await validator.RevalidateAsync(
            ValidDetails(),
            TestContext.Current.CancellationToken);

        var failure = Assert.IsType<RequestValidationFailed>(result);
        Assert.Same(requestContext.ClientFailure, failure.Failure);
    }

    [Fact]
    public void ValidatedDetailsOwnShapeValidationAndNormalization()
    {
        var details = new ValidatedRequestDetails(
            " client-alpha ",
            " PROD-ALPHA-EU ",
            $" {ProductionRoleIds.ReadOnly} ",
            "  Investigate the active production incident.  ",
            " INC-1042 ");

        Assert.Equal("client-alpha", details.ClientId);
        Assert.Equal("PROD-ALPHA-EU", details.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, details.RoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            details.Justification);
        Assert.Equal("INC-1042", details.IncidentId);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionAdministrator",
                "Investigate the active production incident.",
                null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Too short",
                null));
    }

    private static ValidatedRequestDetails ValidDetails(
        string clientId = "client-alpha",
        string environmentId = "PROD-ALPHA-EU",
        string roleId = ProductionRoleIds.ReadOnly,
        string? incidentId = "INC-1042")
    {
        return new ValidatedRequestDetails(
            clientId,
            environmentId,
            roleId,
            "Investigate the active production incident.",
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

    private static ValidatedRequestDetails AssertValid(RequestValidationOutcome result)
    {
        return Assert.IsType<RequestValidationSucceeded>(result).Details;
    }

    private sealed class StubRequestContextReader : IRequestContextReader
    {
        private readonly Dictionary<string, Client> clients;
        private readonly Dictionary<string, ProductionEnvironment> environments;
        private readonly Dictionary<(string EnvironmentId, string RoleId), EnvironmentRole> roles;
        private readonly Dictionary<string, Incident> incidents;

        public StubRequestContextReader()
        {
            var alphaClient = new Client(
                "client-alpha",
                "Client Alpha",
                "alpha-approver");
            var betaClient = new Client(
                "client-beta",
                "Client Beta",
                "beta-approver");
            AlphaEnvironment = new ProductionEnvironment(
                "PROD-ALPHA-EU",
                alphaClient.Id,
                "Primary Production EU");
            var betaEnvironment = new ProductionEnvironment(
                "PROD-BETA-UK",
                betaClient.Id,
                "Primary Production UK");
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

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken)
        {
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
            return GetAsync(
                environments,
                environmentId,
                "environment_not_found",
                cancellationToken);
        }

        public Task<ApplicationResult<ProductionEnvironmentContext>>
            GetProductionEnvironmentContextAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!environments.TryGetValue(environmentId, out var environment)
                || !clients.TryGetValue(environment.ClientId, out var client))
            {
                return Task.FromResult(
                    NotFound<ProductionEnvironmentContext>(
                        "environment_not_found"));
            }

            var environmentRoles = roles.Values
                .Where(role => string.Equals(
                    role.EnvironmentId,
                    environmentId,
                    StringComparison.Ordinal));
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new ProductionEnvironmentContext(
                        environment,
                        client,
                        environmentRoles)));
        }

        public Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
            ListProductionEnvironmentContextsAsync(
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ProductionEnvironmentContext> contexts = environments.Values
                .OrderBy(environment => environment.Id, StringComparer.Ordinal)
                .Select(environment => new ProductionEnvironmentContext(
                    environment,
                    clients[environment.ClientId],
                    roles.Values.Where(role => string.Equals(
                        role.EnvironmentId,
                        environment.Id,
                        StringComparison.Ordinal))))
                .ToArray();
            return Task.FromResult(ApplicationResult.Succeeded(contexts));
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

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken)
        {
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

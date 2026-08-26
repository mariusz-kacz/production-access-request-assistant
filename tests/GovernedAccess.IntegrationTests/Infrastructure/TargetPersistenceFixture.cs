using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Infrastructure;

internal sealed class TargetPersistenceFixture : IAsyncDisposable
{
    private TargetPersistenceFixture(
        ServiceProvider services,
        string referenceDatabasePath,
        string workflowDatabasePath)
    {
        Services = services;
        ReferenceDatabasePath = referenceDatabasePath;
        WorkflowDatabasePath = workflowDatabasePath;
    }

    internal ServiceProvider Services { get; }

    internal string ReferenceDatabasePath { get; }

    internal string WorkflowDatabasePath { get; }

    internal static async Task<TargetPersistenceFixture> CreateAsync()
    {
        var referenceDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-reference-{Guid.NewGuid():N}.db");
        var workflowDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"target-workflow-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={referenceDatabasePath};Pooling=False",
                ["ConnectionStrings:WorkflowPersistence"] =
                    $"Data Source={workflowDatabasePath};Pooling=False",
            })
            .Build();
        var services = new ServiceCollection()
            .AddReferenceAuthority(configuration)
            .AddWorkflowPersistence(configuration)
            .AddScoped<IRequestContextReader, TargetAuthorityRequestContextReader>()
            .BuildServiceProvider(validateScopes: true);

        try
        {
            await ReferenceAuthorityDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken);
            await WorkflowPersistenceDatabase.InitializeAsync(
                services,
                TestContext.Current.CancellationToken);
            return new TargetPersistenceFixture(
                services,
                referenceDatabasePath,
                workflowDatabasePath);
        }
        catch
        {
            await services.DisposeAsync();
            File.Delete(referenceDatabasePath);
            File.Delete(workflowDatabasePath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        File.Delete(ReferenceDatabasePath);
        File.Delete(WorkflowDatabasePath);
    }
}

internal sealed class TargetAuthorityRequestContextReader(
    IProductionEnvironmentSearchAuthority environmentSearch,
    IProductionEnvironmentAuthority environmentAuthority,
    IEnvironmentRoleAuthority roleAuthority,
    IIncidentAuthority incidentAuthority,
    IAuthenticatedPrincipalReader principalReader) : IRequestContextReader
{
    public async Task<ApplicationResult<Client>> GetClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var searchResult = await environmentSearch.SearchAsync(
            clientId,
            cancellationToken);
        if (searchResult.IsFailure)
        {
            return ApplicationResult.Failed<Client>(searchResult.Failure!);
        }

        var match = searchResult.Value.Matches.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.ClientId, clientId));
        if (match is null)
        {
            return NotFound<Client>("client_not_found", "The client was not found.");
        }

        var environmentResult = await environmentAuthority.GetAsync(
            match.EnvironmentId,
            cancellationToken);
        return environmentResult.IsFailure
            ? ApplicationResult.Failed<Client>(environmentResult.Failure!)
            : ApplicationResult.Succeeded(ToClient(environmentResult.Value));
    }

    public async Task<ApplicationResult<ProductionEnvironment>>
        GetProductionEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken)
    {
        var result = await GetProductionEnvironmentContextAsync(
            environmentId,
            cancellationToken);
        return result.IsFailure
            ? ApplicationResult.Failed<ProductionEnvironment>(result.Failure!)
            : ApplicationResult.Succeeded(result.Value.Environment);
    }

    public async Task<ApplicationResult<ProductionEnvironmentContext>>
        GetProductionEnvironmentContextAsync(
            string environmentId,
            CancellationToken cancellationToken)
    {
        var environmentResult = await environmentAuthority.GetAsync(
            environmentId,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return ApplicationResult.Failed<ProductionEnvironmentContext>(
                environmentResult.Failure!);
        }

        if (!environmentResult.Value.CanBecomeCanonical)
        {
            return NotFound<ProductionEnvironmentContext>(
                "environment_not_found",
                "The production environment was not found.");
        }

        var rolesResult = await roleAuthority.ListAsync(
            environmentResult.Value.EnvironmentId,
            cancellationToken);
        if (rolesResult.IsFailure)
        {
            return ApplicationResult.Failed<ProductionEnvironmentContext>(
                rolesResult.Failure!);
        }

        return ApplicationResult.Succeeded(
            ToContext(environmentResult.Value, rolesResult.Value));
    }

    public async Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
        ListProductionEnvironmentContextsAsync(CancellationToken cancellationToken)
    {
        var searchResult = await environmentSearch.SearchAsync(
            "production",
            cancellationToken);
        if (searchResult.IsFailure)
        {
            return ApplicationResult.Failed<
                IReadOnlyList<ProductionEnvironmentContext>>(searchResult.Failure!);
        }

        var contexts = new List<ProductionEnvironmentContext>();
        foreach (var match in searchResult.Value.Matches)
        {
            var contextResult = await GetProductionEnvironmentContextAsync(
                match.EnvironmentId,
                cancellationToken);
            if (contextResult.IsFailure)
            {
                return ApplicationResult.Failed<
                    IReadOnlyList<ProductionEnvironmentContext>>(
                    contextResult.Failure!);
            }

            contexts.Add(contextResult.Value);
        }

        return ApplicationResult.Succeeded<
            IReadOnlyList<ProductionEnvironmentContext>>(contexts);
    }

    public async Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
        string environmentId,
        string roleId,
        CancellationToken cancellationToken)
    {
        var result = await roleAuthority.GetAsync(
            environmentId,
            roleId,
            cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed<EnvironmentRole>(result.Failure!);
        }

        return result.Value.IsCurrentlyAssignable
            ? ApplicationResult.Succeeded(
                new EnvironmentRole(result.Value.EnvironmentId, result.Value.RoleId))
            : NotFound<EnvironmentRole>(
                "environment-role-not-found",
                "The role is not assigned to the production environment.");
    }

    public async Task<ApplicationResult<Incident>> GetIncidentAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var result = await incidentAuthority.GetAsync(incidentId, cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed<Incident>(result.Failure!);
        }

        if (result.Value.EligibleEnvironmentIds.Count != 1)
        {
            return ApplicationResult.Failed<Incident>(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "incident_authority_cardinality_invalid",
                    "The incident does not resolve to one eligible environment."));
        }

        return ApplicationResult.Succeeded(
            new Incident(
                result.Value.IncidentId,
                result.Value.EligibleEnvironmentIds[0],
                result.Value.Title,
                result.Value.IsActive
                    ? IncidentStatus.Active
                    : IncidentStatus.Inactive));
    }

    public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken) =>
        principalReader.GetPrincipalAsync(principalId, cancellationToken);

    private static ProductionEnvironmentContext ToContext(
        EnvironmentAuthorityProjection environment,
        IEnumerable<EnvironmentRoleAuthorityProjection> roles) =>
        new(
            new ProductionEnvironment(
                environment.EnvironmentId,
                environment.ClientId,
                environment.DisplayName),
            ToClient(environment),
            roles
                .Where(role => role.IsCurrentlyAssignable)
                .Select(role => new EnvironmentRole(
                    role.EnvironmentId,
                    role.RoleId)));

    private static Client ToClient(EnvironmentAuthorityProjection environment) =>
        new(
            environment.ClientId,
            environment.ClientDisplayName,
            environment.BusinessApproverPrincipalId);

    private static ApplicationResult<T> NotFound<T>(string code, string message)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(ApplicationFailureKind.NotFound, code, message));
}

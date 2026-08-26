using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GovernedAccess.Mcp;

public sealed class TargetEnvironmentSearchTools(
    IProductionEnvironmentSearchAuthority authority,
    TargetMcpToolExecutor executor)
{
    [McpServerTool(
        Name = "search_production_environments",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TargetEnvironmentSearchToolResult))]
    [Description("Searches the bounded projection of active production environments eligible for access-request intake using one structured agent-proposed query.")]
    public Task<CallToolResult> SearchProductionEnvironmentsAsync(
        [Description("Structured environment query interpreted from requester intent.")]
        [MinLength(1)]
        [MaxLength(EnvironmentSearchPolicy.MaximumQueryLength)]
        string query,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            "search_production_environments",
            token => SearchAsync(query, token),
            cancellationToken);
    }

    private async Task<ApplicationResult<TargetEnvironmentSearchToolResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var result = await authority.SearchAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed<TargetEnvironmentSearchToolResult>(
                result.Failure!);
        }

        if (result.Value.Kind == EnvironmentSearchResultKind.InvalidQuery)
        {
            return ApplicationResult.Failed<TargetEnvironmentSearchToolResult>(
                new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    result.Value.FailureCode ?? "environment_query_invalid",
                    "The environment search query must contain between 1 and 200 characters."));
        }

        if (result.Value.Kind == EnvironmentSearchResultKind.TooBroad)
        {
            return ApplicationResult.Failed<TargetEnvironmentSearchToolResult>(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyUnavailable,
                    result.Value.FailureCode ?? "environment_query_too_broad",
                    "The environment search query returned more than 20 matches."));
        }

        return ApplicationResult.Succeeded(
            new TargetEnvironmentSearchToolResult(
                result.Value.Matches
                    .Select(TargetMcpProjection.CreateEnvironment)
                    .ToArray()));
    }
}

public sealed class TargetProductionEnvironmentTools(
    IProductionEnvironmentAuthority authority,
    TargetMcpToolExecutor executor)
{
    [McpServerTool(
        Name = "get_production_environment",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TargetProductionEnvironmentToolResult))]
    [Description("Gets one exact active production environment eligible for access-request intake and its authoritative owning client.")]
    public Task<CallToolResult> GetProductionEnvironmentAsync(
        [Description("Exact stable production-environment identifier.")]
        [MinLength(1)]
        string environmentId,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            "get_production_environment",
            token => GetAsync(environmentId, token),
            cancellationToken);
    }

    private async Task<ApplicationResult<TargetProductionEnvironmentToolResult>> GetAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        var result = await authority.GetAsync(environmentId, cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed<TargetProductionEnvironmentToolResult>(
                result.Failure!);
        }

        return result.Value.CanBecomeCanonical
            ? ApplicationResult.Succeeded(
                TargetMcpProjection.CreateEnvironment(result.Value))
            : ApplicationResult.Failed<TargetProductionEnvironmentToolResult>(
                TargetMcpProjection.EnvironmentNotFound());
    }
}

public sealed class TargetEnvironmentRoleTools(
    IProductionEnvironmentAuthority environmentAuthority,
    IEnvironmentRoleAuthority roleAuthority,
    TargetMcpToolExecutor executor)
{
    [McpServerTool(
        Name = "get_environment_roles",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TargetEnvironmentRolesToolResult))]
    [Description("Gets roles currently assignable in one exact eligible production environment; it does not determine requester eligibility.")]
    public Task<CallToolResult> GetEnvironmentRolesAsync(
        [Description("Exact stable production-environment identifier.")]
        [MinLength(1)]
        string environmentId,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            "get_environment_roles",
            token => GetAsync(environmentId, token),
            cancellationToken);
    }

    private async Task<ApplicationResult<TargetEnvironmentRolesToolResult>> GetAsync(
        string environmentId,
        CancellationToken cancellationToken)
    {
        var environment = await environmentAuthority.GetAsync(
            environmentId,
            cancellationToken);
        if (environment.IsFailure)
        {
            return ApplicationResult.Failed<TargetEnvironmentRolesToolResult>(
                environment.Failure!);
        }

        if (!environment.Value.CanBecomeCanonical)
        {
            return ApplicationResult.Failed<TargetEnvironmentRolesToolResult>(
                TargetMcpProjection.EnvironmentNotFound());
        }

        var roles = await roleAuthority.ListAsync(
            environment.Value.EnvironmentId,
            cancellationToken);
        if (roles.IsFailure)
        {
            return ApplicationResult.Failed<TargetEnvironmentRolesToolResult>(
                roles.Failure!);
        }

        var projections = new List<TargetEnvironmentRoleToolProjection>();
        foreach (var role in roles.Value)
        {
            if (!StringComparer.Ordinal.Equals(
                    environment.Value.EnvironmentId,
                    role.EnvironmentId))
            {
                return ApplicationResult.Failed<TargetEnvironmentRolesToolResult>(
                    TargetMcpProjection.MalformedEnvironmentRoles());
            }

            if (!role.IsCurrentlyAssignable)
            {
                continue;
            }

            if (!TargetMcpProjection.TryCreateRole(role, out var projection))
            {
                return ApplicationResult.Failed<TargetEnvironmentRolesToolResult>(
                    TargetMcpProjection.MalformedEnvironmentRoles());
            }

            projections.Add(projection);
        }

        projections.Sort(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.RoleId.ToString(),
                right.RoleId.ToString()));
        return ApplicationResult.Succeeded(
            new TargetEnvironmentRolesToolResult(
                environment.Value.EnvironmentId,
                projections));
    }
}

public sealed class TargetIncidentTools(
    IIncidentAuthority authority,
    TargetMcpToolExecutor executor)
{
    [McpServerTool(
        Name = "get_incident",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TargetIncidentToolResult))]
    [Description("Gets one incident using an exact stable identifier proposed by the agent; no incident discovery is provided.")]
    public Task<CallToolResult> GetIncidentAsync(
        [Description("Exact stable incident identifier.")]
        [MinLength(1)]
        string incidentId,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            "get_incident",
            token => GetAsync(incidentId, token),
            cancellationToken);
    }

    private async Task<ApplicationResult<TargetIncidentToolResult>> GetAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var result = await authority.GetAsync(incidentId, cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed<TargetIncidentToolResult>(result.Failure!);
        }

        if (result.Value.EligibleEnvironmentIds.Count != 1)
        {
            return ApplicationResult.Failed<TargetIncidentToolResult>(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "incident-environment-link-invalid",
                    "The incident does not identify one eligible production environment."));
        }

        return ApplicationResult.Succeeded(
            new TargetIncidentToolResult(
                result.Value.IncidentId,
                result.Value.Title,
                result.Value.IsActive
                    ? TargetIncidentStatus.Active
                    : TargetIncidentStatus.Inactive,
                result.Value.EligibleEnvironmentIds[0]));
    }
}

internal static class TargetMcpProjection
{
    internal static TargetProductionEnvironmentToolResult CreateEnvironment(
        EnvironmentSearchMatch environment) =>
        new(
            environment.EnvironmentId,
            environment.DisplayName,
            environment.ClientId,
            environment.ClientDisplayName);

    internal static TargetProductionEnvironmentToolResult CreateEnvironment(
        EnvironmentAuthorityProjection environment) =>
        new(
            environment.EnvironmentId,
            environment.DisplayName,
            environment.ClientId,
            environment.ClientDisplayName);

    internal static bool TryCreateRole(
        EnvironmentRoleAuthorityProjection role,
        out TargetEnvironmentRoleToolProjection projection)
    {
        var roleId = role.RoleId switch
        {
            ProductionRoleIds.ReadOnly => TargetProductionRoleId.ProductionReadOnly,
            ProductionRoleIds.Support => TargetProductionRoleId.ProductionSupport,
            ProductionRoleIds.Deployment => TargetProductionRoleId.ProductionDeployment,
            _ => (TargetProductionRoleId?)null,
        };
        projection = roleId is null
            ? null!
            : new TargetEnvironmentRoleToolProjection(roleId.Value, role.DisplayName);
        return roleId is not null;
    }

    internal static ApplicationFailure EnvironmentNotFound() =>
        new(
            ApplicationFailureKind.NotFound,
            "environment-not-found",
            "The production environment was not found.");

    internal static ApplicationFailure MalformedEnvironmentRoles() =>
        new(
            ApplicationFailureKind.DependencyFailure,
            "environment-role-authority-malformed",
            "The environment-role authority returned inconsistent data.");
}

public sealed record TargetEnvironmentSearchToolResult(
    [property: MaxLength(EnvironmentSearchPolicy.MaximumResultCount)]
    IReadOnlyList<TargetProductionEnvironmentToolResult> Environments);

public sealed record TargetProductionEnvironmentToolResult(
    string EnvironmentId,
    string DisplayName,
    string ClientId,
    string ClientDisplayName);

public sealed record TargetEnvironmentRolesToolResult(
    string EnvironmentId,
    IReadOnlyList<TargetEnvironmentRoleToolProjection> Roles);

public sealed record TargetEnvironmentRoleToolProjection(
    TargetProductionRoleId RoleId,
    string DisplayName);

public enum TargetProductionRoleId
{
    ProductionReadOnly,
    ProductionSupport,
    ProductionDeployment,
}

public sealed record TargetIncidentToolResult(
    string IncidentId,
    string Title,
    TargetIncidentStatus Status,
    string EnvironmentId);

public enum TargetIncidentStatus
{
    Active,
    Inactive,
}

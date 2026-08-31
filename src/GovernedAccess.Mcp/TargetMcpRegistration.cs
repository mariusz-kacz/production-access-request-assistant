using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GovernedAccess.Mcp;

public static class McpRegistration
{
    public static IServiceCollection AddGovernedAccessMcp(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddTransient<TargetMcpToolExecutor>();
        services.AddTransient<TargetEnvironmentSearchTools>();
        services.AddTransient<TargetProductionEnvironmentTools>();
        services.AddTransient<TargetEnvironmentRoleTools>();
        services.AddTransient<TargetIncidentTools>();
        services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools(
            [
                CreateTool<TargetEnvironmentSearchTools>(
                    nameof(TargetEnvironmentSearchTools.SearchProductionEnvironmentsAsync)),
                CreateTool<TargetProductionEnvironmentTools>(
                    nameof(TargetProductionEnvironmentTools.GetProductionEnvironmentAsync)),
                CreateTool<TargetEnvironmentRoleTools>(
                    nameof(TargetEnvironmentRoleTools.GetEnvironmentRolesAsync)),
                CreateTool<TargetIncidentTools>(
                    nameof(TargetIncidentTools.GetIncidentAsync)),
            ]);

        return services;
    }

    public static IEndpointConventionBuilder MapGovernedAccessMcp(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMcp("/mcp");
    }

    private static McpServerTool CreateTool<TTool>(string methodName)
        where TTool : class
    {
        var method = typeof(TTool).GetMethod(methodName)
            ?? throw new InvalidOperationException(
                $"The configured MCP tool method '{methodName}' does not exist.");

        return McpServerTool.Create(
            method,
            static context => GetRequiredTool<TTool>(context),
            new McpServerToolCreateOptions
            {
                SchemaCreateOptions = new AIJsonSchemaCreateOptions
                {
                    TransformOptions = new AIJsonSchemaTransformOptions
                    {
                        DisallowAdditionalProperties = true,
                    },
                },
                SerializerOptions = new JsonSerializerOptions(
                    TargetMcpToolExecutor.SerializerOptions),
            });
    }

    private static TTool GetRequiredTool<TTool>(
        RequestContext<CallToolRequestParams> context)
        where TTool : class
    {
        var services = context.Server.Services
            ?? throw new InvalidOperationException(
                "The MCP server does not have an invocation service provider.");
        return services.GetRequiredService<TTool>();
    }
}

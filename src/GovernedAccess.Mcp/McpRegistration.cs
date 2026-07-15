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
    public static IServiceCollection AddGovernedAccessMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddTransient<RequestContextTools>();
        services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools(
            [
                CreateTool(nameof(RequestContextTools.GetProductionEnvironmentAsync)),
                CreateTool(nameof(RequestContextTools.GetIncidentAsync)),
                CreateTool(nameof(RequestContextTools.GetAvailableRolesAsync)),
            ]);

        return services;
    }

    public static IEndpointConventionBuilder MapGovernedAccessMcp(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMcp("/mcp");
    }

    private static McpServerTool CreateTool(string methodName)
    {
        var method = typeof(RequestContextTools).GetMethod(methodName)
            ?? throw new InvalidOperationException(
                $"The configured MCP tool method '{methodName}' does not exist.");

        return McpServerTool.Create(
            method,
            static context => GetRequiredTool(context),
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
                    RequestContextTools.SerializerOptions),
            });
    }

    private static RequestContextTools GetRequiredTool(
        RequestContext<CallToolRequestParams> context)
    {
        var services = context.Server.Services
            ?? throw new InvalidOperationException(
                "The MCP server does not have an invocation service provider.");
        return services.GetRequiredService<RequestContextTools>();
    }
}

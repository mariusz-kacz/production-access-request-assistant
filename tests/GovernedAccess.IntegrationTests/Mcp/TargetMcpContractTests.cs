using System.Text.Json;
using GovernedAccess.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class TargetMcpContractTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "get_environment_roles",
        "get_incident",
        "get_production_environment",
        "search_production_environments",
    ];

    [Fact]
    public async Task TargetServerAdvertisesExactlyTheFourClosedReadOnlyTools()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-target-contract-tests",
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedToolNames, tools.Select(tool => tool.Name).Order());
        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.Null(client.ServerCapabilities.Prompts);
        Assert.Null(client.ServerCapabilities.Resources);
        Assert.All(tools, AssertReadOnlyAnnotations);

        var searchTool = Assert.Single(
            tools,
            tool => tool.Name == "search_production_environments");
        var environmentTool = Assert.Single(
            tools,
            tool => tool.Name == "get_production_environment");
        var roleTool = Assert.Single(
            tools,
            tool => tool.Name == "get_environment_roles");
        var incidentTool = Assert.Single(
            tools,
            tool => tool.Name == "get_incident");

        AssertInputSchema(
            searchTool,
            "query",
            maximumLength: 200);
        AssertInputSchema(environmentTool, "environmentId");
        AssertInputSchema(roleTool, "environmentId");
        AssertInputSchema(incidentTool, "incidentId");
        AssertSearchOutputSchema(searchTool);
        AssertEnvironmentOutputSchema(environmentTool);
        AssertRoleOutputSchema(roleTool);
        AssertIncidentOutputSchema(incidentTool);
    }

    [Fact]
    public async Task TargetToolsReturnOnlyTheirClosedBoundedWireProjections()
    {
        await using var host = await TargetMcpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-target-result-tests",
            TestContext.Current.CancellationToken);

        var search = await CallAsync(
            client,
            "search_production_environments",
            "query",
            "alpha EU primary");
        var searchContent = AssertSuccessfulStructuredContent(search);
        AssertExactProperties(searchContent, "environments");
        var searchEnvironment = Assert.Single(
            searchContent.GetProperty("environments").EnumerateArray());
        AssertEnvironmentProjection(searchEnvironment);
        Assert.Equal(
            "PROD-ALPHA-EU",
            searchEnvironment.GetProperty("environmentId").GetString());

        var exact = await CallAsync(
            client,
            "get_production_environment",
            "environmentId",
            "PROD-ALPHA-EU");
        var exactContent = AssertSuccessfulStructuredContent(exact);
        AssertEnvironmentProjection(exactContent);
        Assert.Equal(
            "client-alpha",
            exactContent.GetProperty("clientId").GetString());
        Assert.DoesNotContain(
            exactContent.EnumerateObject(),
            property => property.Name is "roles" or "businessApproverPrincipalId");

        var roles = await CallAsync(
            client,
            "get_environment_roles",
            "environmentId",
            "PROD-ALPHA-EU");
        var rolesContent = AssertSuccessfulStructuredContent(roles);
        AssertExactProperties(rolesContent, "environmentId", "roles");
        Assert.Equal(
            [
                "ProductionDeployment",
                "ProductionReadOnly",
                "ProductionSupport",
            ],
            rolesContent
                .GetProperty("roles")
                .EnumerateArray()
                .Select(role => role.GetProperty("roleId").GetString()));
        Assert.All(
            rolesContent.GetProperty("roles").EnumerateArray(),
            role => AssertExactProperties(role, "roleId", "displayName"));

        var incident = await CallAsync(
            client,
            "get_incident",
            "incidentId",
            "INC-1042");
        var incidentContent = AssertSuccessfulStructuredContent(incident);
        AssertExactProperties(
            incidentContent,
            "incidentId",
            "title",
            "status",
            "environmentId");
        Assert.Equal("INC-1042", incidentContent.GetProperty("incidentId").GetString());
        Assert.Equal("Active", incidentContent.GetProperty("status").GetString());
        Assert.Equal(
            "PROD-ALPHA-EU",
            incidentContent.GetProperty("environmentId").GetString());
    }

    private static void AssertReadOnlyAnnotations(McpClientTool tool)
    {
        var annotations = Assert.IsType<ToolAnnotations>(tool.ProtocolTool.Annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    private static void AssertInputSchema(
        McpClientTool tool,
        string parameterName,
        int? maximumLength = null)
    {
        var schema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            [parameterName],
            schema
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));
        var properties = schema.GetProperty("properties");
        AssertExactProperties(properties, parameterName);
        var parameter = properties.GetProperty(parameterName);
        Assert.Equal("string", parameter.GetProperty("type").GetString());
        Assert.Equal(1, parameter.GetProperty("minLength").GetInt32());
        if (maximumLength is not null)
        {
            Assert.Equal(
                maximumLength,
                parameter.GetProperty("maxLength").GetInt32());
        }
    }

    private static async Task<CallToolResult> CallAsync(
        McpClient client,
        string toolName,
        string argumentName,
        string argumentValue)
    {
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var tool = Assert.Single(tools, candidate => candidate.Name == toolName);
        return await tool.CallAsync(
            new Dictionary<string, object?>
            {
                [argumentName] = argumentValue,
            },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static void AssertSearchOutputSchema(McpClientTool tool)
    {
        var root = JsonSerializer.SerializeToElement(tool.ProtocolTool.OutputSchema);
        var schema = ResolveSchema(root, root);
        var properties = AssertClosedObjectSchema(
            root,
            schema,
            "environments");
        var environments = ResolveSchema(root, properties.GetProperty("environments"));
        Assert.Equal("array", environments.GetProperty("type").GetString());
        Assert.Equal(5, environments.GetProperty("maxItems").GetInt32());
        var environmentProperties = AssertClosedObjectSchema(
            root,
            ResolveSchema(root, environments.GetProperty("items")),
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
        AssertNonemptyStringSchemas(
            root,
            environmentProperties,
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
    }

    private static void AssertEnvironmentOutputSchema(McpClientTool tool)
    {
        var root = JsonSerializer.SerializeToElement(tool.ProtocolTool.OutputSchema);
        var properties = AssertClosedObjectSchema(
            root,
            ResolveSchema(root, root),
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
        AssertNonemptyStringSchemas(
            root,
            properties,
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
    }

    private static void AssertRoleOutputSchema(McpClientTool tool)
    {
        var root = JsonSerializer.SerializeToElement(tool.ProtocolTool.OutputSchema);
        var properties = AssertClosedObjectSchema(
            root,
            ResolveSchema(root, root),
            "environmentId",
            "roles");
        AssertNonemptyStringSchemas(root, properties, "environmentId");
        var roles = ResolveSchema(root, properties.GetProperty("roles"));
        Assert.Equal("array", roles.GetProperty("type").GetString());
        var roleProperties = AssertClosedObjectSchema(
            root,
            ResolveSchema(root, roles.GetProperty("items")),
            "roleId",
            "displayName");
        Assert.Equal(
            [
                "ProductionReadOnly",
                "ProductionSupport",
                "ProductionDeployment",
            ],
            ResolveSchema(root, roleProperties.GetProperty("roleId"))
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()));
        AssertNonemptyStringSchemas(root, roleProperties, "displayName");
    }

    private static void AssertIncidentOutputSchema(McpClientTool tool)
    {
        var root = JsonSerializer.SerializeToElement(tool.ProtocolTool.OutputSchema);
        var properties = AssertClosedObjectSchema(
            root,
            ResolveSchema(root, root),
            "incidentId",
            "title",
            "status",
            "environmentId");
        AssertNonemptyStringSchemas(
            root,
            properties,
            "incidentId",
            "title");
        var environmentId = ResolveSchema(
            root,
            properties.GetProperty("environmentId"));
        Assert.Equal(
            ["string", "null"],
            environmentId
                .GetProperty("type")
                .EnumerateArray()
                .Select(type => type.GetString()));
        Assert.Equal(1, environmentId.GetProperty("minLength").GetInt32());
        Assert.Equal(
            ["Active", "Inactive"],
            ResolveSchema(root, properties.GetProperty("status"))
                .GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    private static JsonElement AssertClosedObjectSchema(
        JsonElement root,
        JsonElement schema,
        params string[] expectedProperties)
    {
        schema = ResolveSchema(root, schema);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            expectedProperties.Order(),
            schema
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order());
        var properties = schema.GetProperty("properties");
        AssertExactProperties(properties, expectedProperties);
        return properties;
    }

    private static void AssertNonemptyStringSchemas(
        JsonElement root,
        JsonElement properties,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = ResolveSchema(root, properties.GetProperty(propertyName));
            Assert.Equal("string", property.GetProperty("type").GetString());
            Assert.Equal(1, property.GetProperty("minLength").GetInt32());
        }
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        const string definitionPrefix = "#/$defs/";
        var referenceValue = Assert.IsType<string>(reference.GetString());
        Assert.StartsWith(definitionPrefix, referenceValue, StringComparison.Ordinal);
        return root
            .GetProperty("$defs")
            .GetProperty(referenceValue[definitionPrefix.Length..]);
    }

    private static void AssertEnvironmentProjection(JsonElement environment)
    {
        AssertExactProperties(
            environment,
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
        Assert.All(
            environment.EnumerateObject(),
            property => Assert.Equal(JsonValueKind.String, property.Value.ValueKind));
    }

    private static JsonElement AssertSuccessfulStructuredContent(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(JsonValueKind.Object, content.ValueKind);
        return content;
    }

    private static void AssertExactProperties(
        JsonElement value,
        params string[] expectedNames)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(
            expectedNames.Order(),
            value.EnumerateObject().Select(property => property.Name).Order());
    }
}

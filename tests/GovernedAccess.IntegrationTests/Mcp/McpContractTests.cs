using System.Text.Json;
using GovernedAccess.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class McpContractTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "get_incident",
        "get_production_environment",
    ];

    [Fact]
    public async Task ServerAdvertisesOnlyTheTwoClosedReadOnlyToolContracts()
    {
        await using var host = await McpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-contract-tests",
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedToolNames, tools.Select(tool => tool.Name).Order());
        Assert.All(
            tools,
            tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.Null(client.ServerCapabilities.Prompts);
        Assert.Null(client.ServerCapabilities.Resources);
        AssertInputSchema(
            Assert.Single(tools, tool => tool.Name == "get_production_environment"),
            "environmentId",
            required: false);
        AssertInputSchema(
            Assert.Single(tools, tool => tool.Name == "get_incident"),
            "incidentId",
            required: true);
    }

    [Fact]
    public async Task ToolsReturnTheEnvironmentCatalogExactContextAndExactIncident()
    {
        await using var host = await McpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-contract-tests",
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        var content = AssertSuccessfulStructuredContent(result);
        AssertExactProperties(content, "environments");
        var environments = content.GetProperty("environments")
            .EnumerateArray()
            .ToArray();
        var environmentIds = environments
            .Select(environment => environment.GetProperty("environmentId").GetString())
            .ToArray();
        Assert.NotEmpty(environmentIds);
        Assert.Equal(environmentIds.Order(), environmentIds);
        Assert.Equal(environmentIds.Length, environmentIds.Distinct().Count());
        Assert.InRange(environmentIds.Length, 1, 20);

        var alphaPrimary = Assert.Single(environments, environment =>
            environment.GetProperty("environmentId").GetString() == "PROD-ALPHA-EU");
        AssertEnvironment(
            alphaPrimary,
            "PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha",
            "Primary Production EU",
            ["ProductionDeployment", "ProductionReadOnly", "ProductionSupport"]);

        var alphaRecovery = Assert.Single(environments, environment =>
            environment.GetProperty("environmentId").GetString()
                == "RECOVERY-PROD-ALPHA-EU");
        AssertEnvironment(
            alphaRecovery,
            "RECOVERY-PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha",
            "Recovery Production EU",
            ["ProductionReadOnly", "ProductionSupport"]);

        result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-ALPHA-EU",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        content = AssertSuccessfulStructuredContent(result);
        AssertExactProperties(content, "environments");
        var exactEnvironment = Assert.Single(
            content.GetProperty("environments").EnumerateArray());
        AssertEnvironment(
            exactEnvironment,
            "PROD-ALPHA-EU",
            "client-alpha",
            "Client Alpha",
            "Primary Production EU",
            ["ProductionDeployment", "ProductionReadOnly", "ProductionSupport"]);

        tool = await GetToolAsync(client, "get_incident");
        result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["incidentId"] = "INC-1042",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        content = AssertSuccessfulStructuredContent(result);
        AssertExactProperties(
            content,
            "incidentId",
            "title",
            "status",
            "clientId",
            "environmentId");
        Assert.Equal("INC-1042", content.GetProperty("incidentId").GetString());
        Assert.Equal(JsonValueKind.String, content.GetProperty("title").ValueKind);
        Assert.Equal("Active", content.GetProperty("status").GetString());
        Assert.Equal("client-alpha", content.GetProperty("clientId").GetString());
        Assert.Equal("PROD-ALPHA-EU", content.GetProperty("environmentId").GetString());
    }

    [Fact]
    public async Task InvalidAndMissingStoredValuesReturnTypedFailureEnvelopes()
    {
        await using var host = await McpTestHost.CreateSeededAsync(
            TestContext.Current.CancellationToken);
        await using var client = await host.CreateClientAsync(
            "governed-access-contract-tests",
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        var invalidInput = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "   ",
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var notFound = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-UNKNOWN",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        AssertTypedFailure(invalidInput, "InvalidInput");
        AssertTypedFailure(notFound, "NotFound");
    }

    private static async Task<McpClientTool> GetToolAsync(McpClient client, string name)
    {
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tools, tool => tool.Name == name);
    }

    private static void AssertInputSchema(
        McpClientTool tool,
        string parameterName,
        bool required)
    {
        var schema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var requiredNames = schema.TryGetProperty("required", out var requiredSchema)
            ? requiredSchema.EnumerateArray().Select(item => item.GetString()).ToArray()
            : [];
        Assert.Equal(required ? [parameterName] : [], requiredNames);

        var properties = schema.GetProperty("properties");
        AssertExactProperties(properties, parameterName);
        var parameter = properties.GetProperty(parameterName);
        Assert.Equal("string", parameter.GetProperty("type").GetString());
        Assert.Equal(1, parameter.GetProperty("minLength").GetInt32());
    }

    private static void AssertEnvironment(
        JsonElement environment,
        string environmentId,
        string clientId,
        string clientDisplayName,
        string displayName,
        string[] expectedRoleIds)
    {
        AssertExactProperties(
            environment,
            "environmentId",
            "clientId",
            "clientDisplayName",
            "displayName",
            "roles");
        Assert.Equal(
            environmentId,
            environment.GetProperty("environmentId").GetString());
        Assert.Equal(clientId, environment.GetProperty("clientId").GetString());
        Assert.Equal(
            clientDisplayName,
            environment.GetProperty("clientDisplayName").GetString());
        Assert.Equal(displayName, environment.GetProperty("displayName").GetString());
        var roles = environment.GetProperty("roles").EnumerateArray().ToArray();
        Assert.Equal(
            expectedRoleIds,
            roles.Select(role => role.GetProperty("roleId").GetString()));
        Assert.All(
            roles,
            role =>
            {
                AssertExactProperties(role, "roleId", "displayName");
                Assert.False(string.IsNullOrWhiteSpace(
                    role.GetProperty("displayName").GetString()));
                Assert.DoesNotContain(
                    role.EnumerateObject(),
                    property => property.Name is
                        "rank" or "privilegeLevel" or "implies");
            });
    }

    private static JsonElement AssertSuccessfulStructuredContent(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(JsonValueKind.Object, content.ValueKind);
        return content;
    }

    private static void AssertTypedFailure(CallToolResult result, string expectedOutcome)
    {
        Assert.True(result.IsError);
        Assert.NotNull(result.StructuredContent);
        var content = JsonSerializer.SerializeToElement(result.StructuredContent);

        AssertExactProperties(content, "outcome", "code", "message", "correlationId");
        Assert.Equal(expectedOutcome, content.GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(content.GetProperty("correlationId").GetString()));
    }

    private static void AssertExactProperties(JsonElement value, params string[] expectedNames)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(expectedNames.Order(), value.EnumerateObject().Select(property => property.Name).Order());
    }
}

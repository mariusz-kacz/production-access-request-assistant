using System.Text.Json;
using GovernedAccess.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.IntegrationTests.Mcp;

public sealed class McpContractTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "get_available_roles",
        "get_incident",
        "get_production_environment",
    ];

    [Fact]
    public async Task ServerAdvertisesExactlyTheThreeReadOnlyContextTools()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
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
    }

    [Fact]
    public async Task AdvertisedToolsExposeTheExactClosedInputSchemas()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        AssertInputSchema(
            Assert.Single(tools, tool => tool.Name == "get_production_environment"),
            "environmentId");
        AssertInputSchema(
            Assert.Single(tools, tool => tool.Name == "get_incident"),
            "incidentId");
        AssertInputSchema(
            Assert.Single(tools, tool => tool.Name == "get_available_roles"),
            "environmentId");
    }

    [Fact]
    public async Task ProductionEnvironmentReturnsTypedAuthoritativeStableIdentifiers()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_production_environment");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-ALPHA-EU",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = AssertSuccessfulStructuredContent(result);
        AssertExactProperties(
            content,
            "environmentId",
            "clientId",
            "displayName",
            "isActive",
            "maximumDurationMinutes",
            "businessApproverResponsibilityId");
        Assert.Equal("PROD-ALPHA-EU", content.GetProperty("environmentId").GetString());
        Assert.Equal("client-alpha", content.GetProperty("clientId").GetString());
        Assert.Equal("Client Alpha Production EU", content.GetProperty("displayName").GetString());
        Assert.True(content.GetProperty("isActive").GetBoolean());
        Assert.Equal(480, content.GetProperty("maximumDurationMinutes").GetInt32());
        Assert.Equal(
            "client-alpha-business-approver",
            content.GetProperty("businessApproverResponsibilityId").GetString());
    }

    [Fact]
    public async Task IncidentReturnsTypedAuthoritativeStableIdentifiers()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_incident");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["incidentId"] = "INC-1042",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = AssertSuccessfulStructuredContent(result);
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
    public async Task AvailableRolesReturnTypedStableIdentifiersWithoutPrivilegeOrdering()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);
        var tool = await GetToolAsync(client, "get_available_roles");

        var result = await tool.CallAsync(
            new Dictionary<string, object?>
            {
                ["environmentId"] = "PROD-ALPHA-EU",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = AssertSuccessfulStructuredContent(result);
        AssertExactProperties(content, "environmentId", "roles");
        Assert.Equal("PROD-ALPHA-EU", content.GetProperty("environmentId").GetString());

        var roles = content.GetProperty("roles").EnumerateArray().ToArray();
        Assert.Equal(2, roles.Length);
        Assert.All(
            roles,
            role => AssertExactProperties(role, "roleId", "displayName", "isAvailable"));
        Assert.Equal(
            ["ProductionReadOnly", "ProductionSupport"],
            roles.Select(role => role.GetProperty("roleId").GetString()).Order());
        Assert.All(roles, role => Assert.True(role.GetProperty("isAvailable").GetBoolean()));
        Assert.All(roles, role => Assert.Equal(JsonValueKind.String, role.GetProperty("displayName").ValueKind));
        Assert.All(
            roles,
            role => Assert.DoesNotContain(
                role.EnumerateObject(),
                property => property.Name is "rank" or "privilegeLevel" or "implies"));
    }

    [Fact]
    public async Task InvalidAndMissingAuthoritativeValuesReturnTypedFailureEnvelopes()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
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

    [Fact]
    public async Task AdvertisementContainsNoForbiddenStateChangingOrGenericCapabilities()
    {
        await using var factory = new GovernedAccessWebFactory();
        await using var client = await CreateMcpClientAsync(
            factory,
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var advertisedNames = tools.Select(tool => tool.Name).ToArray();
        string[] forbiddenTerms =
        [
            "approve",
            "decision",
            "provision",
            "revoke",
            "transition",
            "workflow",
            "database",
            "query",
        ];

        Assert.Equal(ExpectedToolNames, advertisedNames.Order());
        Assert.DoesNotContain(
            advertisedNames,
            name => forbiddenTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<McpClient> CreateMcpClientAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        var httpClient = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
        });
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://localhost/mcp"),
                Name = "governed-access-contract-tests",
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            ownsHttpClient: true);

        try
        {
            return await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    private static async Task<McpClientTool> GetToolAsync(McpClient client, string name)
    {
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(tools, tool => tool.Name == name);
    }

    private static void AssertInputSchema(McpClientTool tool, string parameterName)
    {
        var schema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            [parameterName],
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));

        var properties = schema.GetProperty("properties");
        AssertExactProperties(properties, parameterName);
        var parameter = properties.GetProperty(parameterName);
        Assert.Equal("string", parameter.GetProperty("type").GetString());
        Assert.Equal(1, parameter.GetProperty("minLength").GetInt32());
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

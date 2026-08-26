using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace GovernedAccess.Web.Ai;

internal static class TargetAgentMcpCatalog
{
    internal static readonly string[] ToolNames =
    [
        "get_environment_roles",
        "get_incident",
        "get_production_environment",
        "search_production_environments",
    ];

    internal static bool IsValid(IReadOnlyList<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        try
        {
            var discoveredNames = tools
                .Select(tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!discoveredNames.SequenceEqual(
                    ToolNames,
                    StringComparer.Ordinal)
                || tools.Any(static tool => tool.Annotations is not
                    {
                        ReadOnlyHint: true,
                        DestructiveHint: false,
                        IdempotentHint: true,
                        OpenWorldHint: false,
                    }))
            {
                return false;
            }

            foreach (var tool in tools)
            {
                if (!HasExpectedInputSchema(tool)
                    || !HasExpectedOutputSchema(tool))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or KeyNotFoundException
            or FormatException)
        {
            return false;
        }
    }

    private static bool HasExpectedInputSchema(Tool tool)
    {
        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        var expectedParameter = tool.Name switch
        {
            "search_production_environments" => "query",
            "get_environment_roles" or "get_production_environment" =>
                "environmentId",
            "get_incident" => "incidentId",
            _ => throw new InvalidOperationException(
                "An unexpected target MCP tool was discovered."),
        };
        var properties = GetClosedObjectProperties(schema, schema, expectedParameter);
        var parameter = ResolveSchema(schema, properties.GetProperty(expectedParameter));
        if (!HasStringTypeWithMinimumLength(parameter))
        {
            return false;
        }

        if (tool.Name == "search_production_environments")
        {
            return parameter.TryGetProperty("maxLength", out var maximumLength)
                && maximumLength.GetInt32() == 200;
        }

        return !parameter.TryGetProperty("maxLength", out _);
    }

    private static bool HasExpectedOutputSchema(Tool tool)
    {
        var root = JsonSerializer.SerializeToElement(tool.OutputSchema);
        return tool.Name switch
        {
            "search_production_environments" =>
                HasSearchOutputSchema(root),
            "get_production_environment" =>
                HasEnvironmentProjection(root, ResolveSchema(root, root)),
            "get_environment_roles" => HasRoleOutputSchema(root),
            "get_incident" => HasIncidentOutputSchema(root),
            _ => false,
        };
    }

    private static bool HasSearchOutputSchema(JsonElement root)
    {
        var properties = GetClosedObjectProperties(
            root,
            ResolveSchema(root, root),
            "environments");
        var environments = ResolveSchema(root, properties.GetProperty("environments"));
        return environments.GetProperty("type").GetString() == "array"
            && environments.GetProperty("maxItems").GetInt32() == 20
            && HasEnvironmentProjection(
                root,
                ResolveSchema(root, environments.GetProperty("items")));
    }

    private static bool HasEnvironmentProjection(
        JsonElement root,
        JsonElement schema)
    {
        var properties = GetClosedObjectProperties(
            root,
            schema,
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
        return HasStringProperties(
            root,
            properties,
            "environmentId",
            "displayName",
            "clientId",
            "clientDisplayName");
    }

    private static bool HasRoleOutputSchema(JsonElement root)
    {
        var properties = GetClosedObjectProperties(
            root,
            ResolveSchema(root, root),
            "environmentId",
            "roles");
        if (!HasStringProperties(root, properties, "environmentId"))
        {
            return false;
        }

        var roles = ResolveSchema(root, properties.GetProperty("roles"));
        if (roles.GetProperty("type").GetString() != "array")
        {
            return false;
        }

        var roleProperties = GetClosedObjectProperties(
            root,
            ResolveSchema(root, roles.GetProperty("items")),
            "roleId",
            "displayName");
        var roleId = ResolveSchema(root, roleProperties.GetProperty("roleId"));
        return roleId.GetProperty("type").GetString() == "string"
            && roleId.GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString())
                .SequenceEqual(
                [
                    "ProductionReadOnly",
                    "ProductionSupport",
                    "ProductionDeployment",
                ],
                StringComparer.Ordinal)
            && HasStringProperties(root, roleProperties, "displayName");
    }

    private static bool HasIncidentOutputSchema(JsonElement root)
    {
        var properties = GetClosedObjectProperties(
            root,
            ResolveSchema(root, root),
            "incidentId",
            "title",
            "status",
            "environmentId");
        var status = ResolveSchema(root, properties.GetProperty("status"));
        return HasStringProperties(
                root,
                properties,
                "incidentId",
                "title",
                "environmentId")
            && status.GetProperty("type").GetString() == "string"
            && status.GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString())
                .SequenceEqual(
                    ["Active", "Inactive"],
                    StringComparer.Ordinal);
    }

    private static bool HasStringProperties(
        JsonElement root,
        JsonElement properties,
        params string[] propertyNames) =>
        propertyNames.All(propertyName => HasStringTypeWithMinimumLength(
            ResolveSchema(root, properties.GetProperty(propertyName))));

    private static bool HasStringTypeWithMinimumLength(JsonElement schema) =>
        schema.GetProperty("type").GetString() == "string"
        && schema.GetProperty("minLength").GetInt32() == 1;

    private static JsonElement GetClosedObjectProperties(
        JsonElement root,
        JsonElement schema,
        params string[] expectedProperties)
    {
        schema = ResolveSchema(root, schema);
        if (schema.GetProperty("type").GetString() != "object"
            || schema.GetProperty("additionalProperties").GetBoolean()
            || !schema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    expectedProperties.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The MCP object schema is not closed.");
        }

        var properties = schema.GetProperty("properties");
        if (!properties.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                expectedProperties.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The MCP object schema has unexpected properties.");
        }

        return properties;
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var referenceValue = reference.GetString();
        const string prefix = "#/$defs/";
        if (referenceValue is null
            || !referenceValue.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The MCP schema reference is unsupported.");
        }

        return root
            .GetProperty("$defs")
            .GetProperty(referenceValue[prefix.Length..]);
    }
}

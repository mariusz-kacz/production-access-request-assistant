using System.Globalization;

namespace GovernedAccess.Web.Ai;

internal sealed record RouterModelOptions(
    Uri Endpoint, string DeploymentName, TimeSpan Timeout, int MaximumOutputTokens);

internal sealed record PolicyAdvisorModelOptions(
    Uri Endpoint, string DeploymentName, TimeSpan Timeout, int MaximumOutputTokens);

internal sealed record PolicyRetrievalOptions(
    Uri Endpoint, string IndexName, TimeSpan Timeout,
    int MaximumChunks, int MaximumApproximateTokens);

internal sealed record PolicyEmbeddingOptions(
    Uri Endpoint, string DeploymentName, TimeSpan Timeout, int Dimensions);

internal sealed record RoutedTurnOptions(TimeSpan Timeout);

internal static class RoutedAssistantOptions
{
    internal static IServiceCollection AddRoutedAssistantOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Resolve only the selected component. Unconfigured future routes must not
        // prevent the existing access specialist from starting or choose its client.
        services.AddSingleton(_ => LoadRouter(configuration));
        services.AddSingleton(_ => LoadPolicyAdvisor(configuration));
        services.AddSingleton(_ => LoadRetrieval(configuration));
        services.AddSingleton(_ => LoadEmbedding(configuration));
        services.AddSingleton(_ => LoadTurn(configuration));
        return services;
    }

    internal static RouterModelOptions LoadRouter(IConfiguration configuration)
    {
        var section = configuration.GetSection("RoutedAssistant:Router");
        ValidateFields(section, "Endpoint", "DeploymentName", "Timeout", "MaximumOutputTokens");
        return new RouterModelOptions(
            FoundryEndpoint(section), RequiredName(section, "DeploymentName"),
            RequiredTimeout(section, 8), RequiredInt(section, "MaximumOutputTokens", 100));
    }

    internal static PolicyAdvisorModelOptions LoadPolicyAdvisor(IConfiguration configuration)
    {
        var section = configuration.GetSection("RoutedAssistant:PolicyAdvisor");
        ValidateFields(section, "Endpoint", "DeploymentName", "Timeout", "MaximumOutputTokens");
        return new PolicyAdvisorModelOptions(
            FoundryEndpoint(section), RequiredName(section, "DeploymentName"),
            RequiredTimeout(section, 30), RequiredInt(section, "MaximumOutputTokens", 800));
    }

    internal static PolicyRetrievalOptions LoadRetrieval(IConfiguration configuration)
    {
        var section = configuration.GetSection("RoutedAssistant:Retrieval");
        ValidateFields(section, "Endpoint", "IndexName", "Timeout", "MaximumChunks", "MaximumApproximateTokens");
        var endpoint = RequiredEndpoint(section);
        const string searchSuffix = ".search.windows.net";
        if (!endpoint.IdnHost.EndsWith(searchSuffix, StringComparison.OrdinalIgnoreCase)
            || endpoint.IdnHost.Length <= searchSuffix.Length
            || endpoint.AbsolutePath != "/")
        {
            throw Invalid(section, "Endpoint");
        }

        var indexName = RequiredName(section, "IndexName");
        if (indexName.Length < 2
            || !char.IsAsciiLetterOrDigit(indexName[0])
            || indexName.Any(character => !char.IsAsciiDigit(character)
                && character is not (>= 'a' and <= 'z') && character is not ('-' or '_'))
            || indexName.Contains("--", StringComparison.Ordinal)
            || indexName.Contains("__", StringComparison.Ordinal))
        {
            throw Invalid(section, "IndexName");
        }

        return new PolicyRetrievalOptions(
            endpoint, indexName, RequiredTimeout(section, 30),
            RequiredInt(section, "MaximumChunks", 3),
            RequiredInt(section, "MaximumApproximateTokens", 1500));
    }

    internal static PolicyEmbeddingOptions LoadEmbedding(IConfiguration configuration)
    {
        var section = configuration.GetSection("RoutedAssistant:Embedding");
        ValidateFields(section, "Endpoint", "DeploymentName", "Timeout", "Dimensions");
        return new PolicyEmbeddingOptions(
            FoundryEndpoint(section), RequiredName(section, "DeploymentName"),
            RequiredTimeout(section, 30), RequiredInt(section, "Dimensions", 3072));
    }

    internal static RoutedTurnOptions LoadTurn(IConfiguration configuration)
    {
        var section = configuration.GetSection("RoutedAssistant:Turn");
        ValidateFields(section, "Timeout");
        return new RoutedTurnOptions(RequiredTimeout(section, 70));
    }

    private static void ValidateFields(IConfigurationSection section, params string[] allowedFields)
    {
        if (section.Value is not null || section.GetChildren().Any(child =>
                !allowedFields.Contains(child.Key, StringComparer.OrdinalIgnoreCase)
                || child.GetChildren().Any()))
        {
            throw Invalid(section, "Settings");
        }
    }

    private static Uri FoundryEndpoint(IConfigurationSection section)
    {
        if (!RequestPreparationModelOptions.TryGetTrustedFoundryResponsesEndpoint(
                section["Endpoint"], out var endpoint))
        {
            throw Invalid(section, "Endpoint");
        }

        return endpoint!;
    }

    private static Uri RequiredEndpoint(IConfigurationSection section)
    {
        var value = section["Endpoint"];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw Invalid(section, "Endpoint");
        }

        return endpoint;
    }

    private static string RequiredName(IConfigurationSection section, string field)
    {
        var value = section[field];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw Invalid(section, field);
        }

        return value;
    }

    private static int RequiredInt(IConfigurationSection section, string field, int maximum)
    {
        if (!int.TryParse(section[field], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value)
            || value <= 0 || value > maximum)
        {
            throw Invalid(section, field);
        }

        return value;
    }

    private static TimeSpan RequiredTimeout(IConfigurationSection section, int maximumSeconds)
    {
        if (!TimeSpan.TryParse(section["Timeout"], CultureInfo.InvariantCulture, out var value)
            || value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(maximumSeconds))
        {
            throw Invalid(section, "Timeout");
        }

        return value;
    }

    private static InvalidOperationException Invalid(IConfigurationSection section, string field) =>
        new($"{section.Path}:{field} is missing or invalid.");
}

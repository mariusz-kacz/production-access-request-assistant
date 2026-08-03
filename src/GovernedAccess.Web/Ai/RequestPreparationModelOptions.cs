using Microsoft.Extensions.Configuration;

namespace GovernedAccess.Web.Ai;

public sealed record RequestPreparationModelMetadata(
    string ProfileId,
    string? DeploymentName);

internal sealed class RequestPreparationModelOptions
{
    internal const string SectionName = "RequestPreparationModel";

    public string? ExecutionProfile { get; init; }

    public FoundryResponsesModelOptions FoundryResponses { get; init; } = new();

    internal static RequestPreparationModelOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        return new RequestPreparationModelOptions
        {
            ExecutionProfile = section["ExecutionProfile"],
            FoundryResponses = new FoundryResponsesModelOptions
            {
                Endpoint = section["FoundryResponses:Endpoint"],
                DeploymentName = section["FoundryResponses:DeploymentName"],
            },
        };
    }

    internal RequestPreparationModelResolution Validate()
    {
        var profile = ExecutionProfile switch
        {
            "Deterministic" => RequestPreparationModelProfile.Deterministic,
            "FoundryResponses" => RequestPreparationModelProfile.FoundryResponses,
            _ => (RequestPreparationModelProfile?)null,
        };
        if (profile is null)
        {
            return RequestPreparationModelResolution.Invalid("ExecutionProfile");
        }

        if (profile == RequestPreparationModelProfile.Deterministic)
        {
            return RequestPreparationModelResolution.ValidDeterministic();
        }

        if (!TryGetTrustedFoundryResponsesEndpoint(
                FoundryResponses.Endpoint,
                out var endpoint))
        {
            return RequestPreparationModelResolution.Invalid(
                "FoundryResponses.Endpoint");
        }

        if (!IsBoundedValue(FoundryResponses.DeploymentName))
        {
            return RequestPreparationModelResolution.Invalid(
                "FoundryResponses.DeploymentName");
        }

        return RequestPreparationModelResolution.ValidFoundryResponses(
            endpoint!,
            FoundryResponses.DeploymentName!);
    }

    private static bool TryGetTrustedFoundryResponsesEndpoint(
        string? value,
        out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !candidate.IsDefaultPort
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment)
            || !string.Equals(
                candidate.AbsolutePath.TrimEnd('/'),
                "/openai/v1",
                StringComparison.Ordinal))
        {
            return false;
        }

        const string foundryServicesSuffix = ".services.ai.azure.com";
        if (!candidate.IdnHost.EndsWith(
                foundryServicesSuffix,
                StringComparison.OrdinalIgnoreCase)
            || candidate.IdnHost.Length <= foundryServicesSuffix.Length)
        {
            return false;
        }

        endpoint = candidate;
        return true;
    }

    private static bool IsBoundedValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}

internal sealed class FoundryResponsesModelOptions
{
    public string? Endpoint { get; init; }

    public string? DeploymentName { get; init; }
}

internal enum RequestPreparationModelProfile
{
    Deterministic,
    FoundryResponses,
}

internal sealed record RequestPreparationModelResolution(
    RequestPreparationModelProfile? Profile,
    Uri? Endpoint,
    string? DeploymentName,
    string? ValidationFailure)
{
    public bool IsValid => ValidationFailure is null;

    public static RequestPreparationModelResolution Invalid(string fieldName) =>
        new(
            null,
            null,
            null,
            $"RequestPreparationModel.{fieldName} is missing or invalid.");

    public static RequestPreparationModelResolution ValidDeterministic() =>
        new(
            RequestPreparationModelProfile.Deterministic,
            null,
            null,
            null);

    public static RequestPreparationModelResolution ValidFoundryResponses(
        Uri endpoint,
        string deploymentName) =>
        new(
            RequestPreparationModelProfile.FoundryResponses,
            endpoint,
            deploymentName,
            null);
}

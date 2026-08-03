using Microsoft.Extensions.Configuration;

namespace GovernedAccess.Web.Ai;

public sealed record RequestPreparationModelMetadata(
    string ProfileId,
    string? ModelId);

internal sealed class RequestPreparationModelOptions
{
    internal const string SectionName = "RequestPreparationModel";

    public string? ExecutionProfile { get; init; }

    public IReadOnlyList<string> ApprovedModelIds { get; init; } = [];

    public AzureOpenAIModelOptions AzureOpenAI { get; init; } = new();

    internal static RequestPreparationModelOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        return new RequestPreparationModelOptions
        {
            ExecutionProfile = section["ExecutionProfile"],
            ApprovedModelIds = section
                .GetSection("ApprovedModelIds")
                .GetChildren()
                .Select(child => child.Value ?? string.Empty)
                .ToArray(),
            AzureOpenAI = new AzureOpenAIModelOptions
            {
                Endpoint = section["AzureOpenAI:Endpoint"],
                TenantId = section["AzureOpenAI:TenantId"],
                DeploymentName = section["AzureOpenAI:DeploymentName"],
                ModelId = section["AzureOpenAI:ModelId"],
            },
        };
    }

    internal RequestPreparationModelResolution Validate()
    {
        var profile = ExecutionProfile switch
        {
            "Deterministic" => RequestPreparationModelProfile.Deterministic,
            "AzureOpenAI" => RequestPreparationModelProfile.AzureOpenAI,
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

        if (!TryGetTrustedAzureOpenAIEndpoint(
                AzureOpenAI.Endpoint,
                out var endpoint))
        {
            return RequestPreparationModelResolution.Invalid(
                "AzureOpenAI.Endpoint");
        }

        if (!Guid.TryParse(AzureOpenAI.TenantId, out var tenantId)
            || tenantId == Guid.Empty)
        {
            return RequestPreparationModelResolution.Invalid(
                "AzureOpenAI.TenantId");
        }

        if (!IsBoundedValue(AzureOpenAI.DeploymentName))
        {
            return RequestPreparationModelResolution.Invalid(
                "AzureOpenAI.DeploymentName");
        }

        if (!IsBoundedValue(AzureOpenAI.ModelId)
            || !ApprovedModelIds.Contains(
                AzureOpenAI.ModelId,
                StringComparer.Ordinal))
        {
            return RequestPreparationModelResolution.Invalid(
                "AzureOpenAI.ModelId");
        }

        return RequestPreparationModelResolution.ValidAzureOpenAI(
            endpoint!,
            tenantId,
            AzureOpenAI.DeploymentName!,
            AzureOpenAI.ModelId!);
    }

    private static bool TryGetTrustedAzureOpenAIEndpoint(
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
            || (candidate.AbsolutePath.Length > 0
                && candidate.AbsolutePath != "/"))
        {
            return false;
        }

        const string azureOpenAISuffix = ".openai.azure.com";
        if (!candidate.IdnHost.EndsWith(
                azureOpenAISuffix,
                StringComparison.OrdinalIgnoreCase)
            || candidate.IdnHost.Length <= azureOpenAISuffix.Length)
        {
            return false;
        }

        endpoint = candidate;
        return true;
    }

    private static bool IsBoundedValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}

internal sealed class AzureOpenAIModelOptions
{
    public string? Endpoint { get; init; }

    public string? TenantId { get; init; }

    public string? DeploymentName { get; init; }

    public string? ModelId { get; init; }
}

internal enum RequestPreparationModelProfile
{
    Deterministic,
    AzureOpenAI,
}

internal sealed record RequestPreparationModelResolution(
    RequestPreparationModelProfile? Profile,
    Uri? Endpoint,
    Guid? TenantId,
    string? DeploymentName,
    string? ModelId,
    string? ValidationFailure)
{
    public bool IsValid => ValidationFailure is null;

    public static RequestPreparationModelResolution Invalid(string fieldName) =>
        new(
            null,
            null,
            null,
            null,
            null,
            $"RequestPreparationModel.{fieldName} is missing or invalid.");

    public static RequestPreparationModelResolution ValidDeterministic() =>
        new(
            RequestPreparationModelProfile.Deterministic,
            null,
            null,
            null,
            null,
            null);

    public static RequestPreparationModelResolution ValidAzureOpenAI(
        Uri endpoint,
        Guid tenantId,
        string deploymentName,
        string modelId) =>
        new(
            RequestPreparationModelProfile.AzureOpenAI,
            endpoint,
            tenantId,
            deploymentName,
            modelId,
            null);
}

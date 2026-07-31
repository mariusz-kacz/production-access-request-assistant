using Microsoft.Extensions.Options;

namespace GovernedAccess.Web.Teams;

public sealed class TeamsAccessRequestOptions
{
    public const string SectionName = "TeamsAccessRequest";

    public static readonly TimeSpan MaximumModelTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumMcpTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RequiredPreparationLifetime = TimeSpan.FromMinutes(30);

    public string AllowedTenantId { get; init; } = string.Empty;

    public string BotConnectionName { get; init; } = string.Empty;

    public Uri? TrustedWebBaseUri { get; init; }

    public TimeSpan ModelTimeout { get; init; }

    public TimeSpan McpTimeout { get; init; }

    public TimeSpan PreparationLifetime { get; init; }
}

public sealed class TeamsAccessRequestOptionsValidator(IConfiguration configuration)
    : IValidateOptions<TeamsAccessRequestOptions>
{
    private const string ClientSecretAuthenticationType = "ClientSecret";
    private const string BotFrameworkAuthority =
        "https://login.microsoftonline.com/botframework.com";
    private const string BotFrameworkScope = "https://api.botframework.com/.default";
    private const string TokenValidationSectionName = "TokenValidation";
    private const string ConnectionsSectionName = "Connections";
    private const string ConnectionsMapSectionName = "ConnectionsMap";

    public ValidateOptionsResult Validate(
        string? name,
        TeamsAccessRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();

        var tenantIsValid = TryParseNonEmptyGuid(
            options.AllowedTenantId,
            out var allowedTenantId);
        if (!tenantIsValid)
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:AllowedTenantId must be a non-empty GUID.");
        }

        var connectionName = (options.BotConnectionName ?? string.Empty).Trim();
        if (connectionName.Length == 0
            || connectionName.Length > 128
            || connectionName.Contains(
                ConfigurationPath.KeyDelimiter,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:BotConnectionName must be a valid configuration segment.");
        }
        else
        {
            ValidateBotAuthentication(
                connectionName,
                tenantIsValid,
                allowedTenantId,
                failures);
        }

        ValidateTrustedWebBaseUri(options.TrustedWebBaseUri, failures);
        ValidateDeadlines(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateBotAuthentication(
        string connectionName,
        bool tenantIsValid,
        Guid allowedTenantId,
        List<string> failures)
    {
        var tokenValidationSection =
            configuration.GetSection(TokenValidationSectionName);
        if (!bool.TryParse(
                tokenValidationSection["Enabled"],
                out var tokenValidationEnabled)
            || !tokenValidationEnabled)
        {
            failures.Add(
                $"{TokenValidationSectionName}:Enabled must be true.");
        }

        var tokenTenantIsValid = TryParseNonEmptyGuid(
            tokenValidationSection["TenantId"],
            out var tokenTenantId);
        if (!tokenTenantIsValid
            || (tenantIsValid && tokenTenantId != allowedTenantId))
        {
            failures.Add(
                $"{TokenValidationSectionName}:TenantId must match the allowed Teams tenant.");
        }

        var connectionSettingsSection = configuration.GetSection(
            ConfigurationPath.Combine(
                ConnectionsSectionName,
                connectionName,
                "Settings"));

        if (!string.Equals(
                connectionSettingsSection["AuthType"],
                ClientSecretAuthenticationType,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:AuthType must be {ClientSecretAuthenticationType}.");
        }

        if (!string.Equals(
                connectionSettingsSection["Authority"],
                BotFrameworkAuthority,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:Authority must be the Bot Framework multitenant authority.");
        }

        var botClientIdIsValid = TryParseNonEmptyGuid(
            connectionSettingsSection["ClientId"],
            out var botClientId);
        if (!botClientIdIsValid)
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:ClientId must be a non-empty GUID.");
        }

        var connectionTenantIsValid = TryParseNonEmptyGuid(
            connectionSettingsSection["TenantId"],
            out var connectionTenantId);
        if (!connectionTenantIsValid
            || (tenantIsValid && connectionTenantId != allowedTenantId))
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:TenantId must match the allowed Teams tenant.");
        }

        if (string.IsNullOrWhiteSpace(connectionSettingsSection["ClientSecret"]))
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:ClientSecret must be supplied through secure configuration.");
        }

        var configuredScopes = ReadArray(connectionSettingsSection, "Scopes");
        if (configuredScopes.Length != 1
            || !string.Equals(
                configuredScopes[0],
                BotFrameworkScope,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{ConnectionsSectionName}:{connectionName}:Settings:Scopes must contain only the Bot Framework default scope.");
        }

        var audiences = ReadArray(tokenValidationSection, "Audiences");
        if (!botClientIdIsValid
            || audiences.Length != 1
            || !TryParseNonEmptyGuid(audiences[0], out var audience)
            || audience != botClientId)
        {
            failures.Add(
                $"{TokenValidationSectionName}:Audiences must contain only the configured bot client ID.");
        }

        var connectionMaps = configuration
            .GetSection(ConnectionsMapSectionName)
            .GetChildren()
            .ToArray();
        if (connectionMaps.Length != 1
            || !string.Equals(
                connectionMaps[0]["ServiceUrl"],
                "*",
                StringComparison.Ordinal)
            || !string.Equals(
                connectionMaps[0]["Connection"],
                connectionName,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{ConnectionsMapSectionName} must map all service URLs to the configured bot connection.");
        }
    }

    private static void ValidateTrustedWebBaseUri(
        Uri? trustedWebBaseUri,
        List<string> failures)
    {
        if (trustedWebBaseUri is null
            || !trustedWebBaseUri.IsAbsoluteUri
            || !string.Equals(
                trustedWebBaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(trustedWebBaseUri.Host)
            || trustedWebBaseUri.AbsolutePath != "/"
            || trustedWebBaseUri.Query.Length != 0
            || trustedWebBaseUri.Fragment.Length != 0
            || trustedWebBaseUri.UserInfo.Length != 0)
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:TrustedWebBaseUri must be an absolute HTTPS origin without credentials, path, query, or fragment.");
        }
    }

    private static void ValidateDeadlines(
        TeamsAccessRequestOptions options,
        List<string> failures)
    {
        if (options.ModelTimeout <= TimeSpan.Zero
            || options.ModelTimeout > TeamsAccessRequestOptions.MaximumModelTimeout)
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:ModelTimeout must be positive and no greater than 30 seconds.");
        }

        if (options.McpTimeout <= TimeSpan.Zero
            || options.McpTimeout > TeamsAccessRequestOptions.MaximumMcpTimeout
            || options.McpTimeout > options.ModelTimeout)
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:McpTimeout must be positive, no greater than 5 seconds, and no greater than ModelTimeout.");
        }

        if (options.PreparationLifetime
            != TeamsAccessRequestOptions.RequiredPreparationLifetime)
        {
            failures.Add(
                $"{TeamsAccessRequestOptions.SectionName}:PreparationLifetime must be exactly 30 minutes.");
        }
    }

    private static string?[] ReadArray(
        IConfigurationSection parent,
        string name) =>
        parent
            .GetSection(name)
            .GetChildren()
            .Select(child => child.Value)
            .ToArray();

    private static bool TryParseNonEmptyGuid(
        string? value,
        out Guid identifier) =>
        Guid.TryParseExact(value?.Trim(), "D", out identifier)
        && identifier != Guid.Empty;
}

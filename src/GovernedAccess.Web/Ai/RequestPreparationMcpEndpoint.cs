namespace GovernedAccess.Web.Ai;

internal sealed class RequestPreparationMcpEndpoint(
    Func<Uri?> baseUriResolver)
{
    private const string McpPath = "mcp";

    internal Uri Resolve()
    {
        ArgumentNullException.ThrowIfNull(baseUriResolver);

        var baseUri = baseUriResolver()
            ?? throw new InvalidOperationException(
                "The request-preparation MCP base URI is unavailable.");

        if (!baseUri.IsAbsoluteUri)
        {
            throw InvalidBaseUri();
        }

        var usesSupportedScheme = string.Equals(
                baseUri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                baseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

        if (!usesSupportedScheme
            || string.IsNullOrWhiteSpace(baseUri.Host)
            || baseUri.AbsolutePath != "/"
            || baseUri.Query.Length != 0
            || baseUri.Fragment.Length != 0
            || baseUri.UserInfo.Length != 0)
        {
            throw InvalidBaseUri();
        }

        return new Uri(baseUri, McpPath);
    }

    private static InvalidOperationException InvalidBaseUri() =>
        new(
            "The request-preparation MCP base URI must be an absolute HTTP or HTTPS origin.");
}

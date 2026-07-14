namespace GovernedAccess.Web.Demo;

/// <summary>
/// Stable identifiers for the fixed local demonstration dataset.
/// These values are shared by synthetic persistence and authentication adapters,
/// but are not part of the application's public contract.
/// </summary>
internal static class DemoDataIds
{
    internal const string ClientAlphaId = "client-alpha";
    internal const string ClientBetaId = "client-beta";

    internal const string ClientAlphaEnvironmentId = "PROD-ALPHA-EU";
    internal const string ClientBetaEnvironmentId = "PROD-BETA-UK";

    internal const string RequesterPrincipalId = "requester";
    internal const string ClientAlphaApproverPrincipalId =
        "client-alpha-business-approver";
    internal const string ClientBetaApproverPrincipalId =
        "client-beta-business-approver";
    internal const string DevOpsApproverPrincipalId = "devops-approver";

    internal const string PrimaryIncidentId = "INC-1042";
    internal const string InactiveIncidentId = "INC-1041";
    internal const string ClientBetaIncidentId = "INC-2042";
}

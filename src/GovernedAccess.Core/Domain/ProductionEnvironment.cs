namespace GovernedAccess.Core.Domain;

public sealed class ProductionEnvironment
{
    public ProductionEnvironment(
        string id,
        string clientId,
        string displayName,
        string businessApproverPrincipalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessApproverPrincipalId);
        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
        BusinessApproverPrincipalId = businessApproverPrincipalId;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }

    public string BusinessApproverPrincipalId { get; private set; }
}

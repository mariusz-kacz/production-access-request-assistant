namespace GovernedAccess.Core.Domain.ReferenceData;

public sealed class ProductionEnvironment
{
    public ProductionEnvironment(
        string id,
        string clientId,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }
}

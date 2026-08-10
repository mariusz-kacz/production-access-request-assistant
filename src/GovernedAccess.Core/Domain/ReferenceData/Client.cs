namespace GovernedAccess.Core.Domain.ReferenceData;

public sealed class Client
{
    public Client(
        string id,
        string displayName,
        string businessApproverPrincipalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessApproverPrincipalId);

        Id = id;
        DisplayName = displayName;
        BusinessApproverPrincipalId = businessApproverPrincipalId;
    }

    public string Id { get; private set; }

    public string DisplayName { get; private set; }

    public string BusinessApproverPrincipalId { get; private set; }
}

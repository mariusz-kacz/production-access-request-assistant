namespace GovernedAccess.Core.Domain;

public sealed class Client
{
    public Client(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; private set; }

    public string DisplayName { get; private set; }
}

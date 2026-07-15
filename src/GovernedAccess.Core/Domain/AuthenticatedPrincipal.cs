namespace GovernedAccess.Core.Domain;

public enum PrincipalKind
{
    Requester,
    BusinessApprover,
    DevOpsApprover,
}

public sealed class AuthenticatedPrincipal
{
    public AuthenticatedPrincipal(
        string id,
        string displayName,
        PrincipalKind kind,
        string? clientId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The principal kind is not supported.");
        }

        if (kind == PrincipalKind.BusinessApprover)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        }
        else if (clientId is not null)
        {
            throw new ArgumentException(
                "Only a business approver can have a client responsibility.",
                nameof(clientId));
        }

        Id = id;
        DisplayName = displayName;
        Kind = kind;
        ClientId = clientId;
    }

    public string Id { get; private set; }

    public string DisplayName { get; private set; }

    public PrincipalKind Kind { get; private set; }

    public string? ClientId { get; private set; }
}

namespace GovernedAccess.Core.Domain;

public enum IncidentStatus
{
    Active,
    Inactive,
}

public sealed class Incident
{
    public Incident(
        string id,
        string clientId,
        string? environmentId,
        string title,
        IncidentStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (environmentId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        }

        EnsureValidStatus(status);

        Id = id;
        ClientId = clientId;
        EnvironmentId = environmentId;
        Title = title;
        Status = status;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string? EnvironmentId { get; private set; }

    public string Title { get; private set; }

    public IncidentStatus Status { get; private set; }

    public void SetStatus(IncidentStatus status)
    {
        EnsureValidStatus(status);
        Status = status;
    }

    private static void EnsureValidStatus(IncidentStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The incident status is not supported.");
        }
    }
}

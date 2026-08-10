namespace GovernedAccess.Core.Domain.ReferenceData;

public enum IncidentStatus
{
    Active,
    Inactive,
}

public sealed class Incident
{
    public Incident(
        string id,
        string environmentId,
        string title,
        IncidentStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        EnsureValidStatus(status);

        Id = id;
        EnvironmentId = environmentId;
        Title = title;
        Status = status;
    }

    public string Id { get; private set; }

    public string EnvironmentId { get; private set; }

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

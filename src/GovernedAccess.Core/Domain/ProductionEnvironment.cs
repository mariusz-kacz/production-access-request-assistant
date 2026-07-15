namespace GovernedAccess.Core.Domain;

public sealed class ProductionEnvironment
{
    public ProductionEnvironment(
        string id,
        string clientId,
        string displayName,
        int maximumDurationMinutes,
        string businessApproverPrincipalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessApproverPrincipalId);
        EnsurePositiveDuration(maximumDurationMinutes);

        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
        MaximumDurationMinutes = maximumDurationMinutes;
        BusinessApproverPrincipalId = businessApproverPrincipalId;
    }

    public string Id { get; private set; }

    public string ClientId { get; private set; }

    public string DisplayName { get; private set; }

    public int MaximumDurationMinutes { get; private set; }

    public string BusinessApproverPrincipalId { get; private set; }

    public void UpdateMaximumDuration(int maximumDurationMinutes)
    {
        EnsurePositiveDuration(maximumDurationMinutes);

        MaximumDurationMinutes = maximumDurationMinutes;
    }

    private static void EnsurePositiveDuration(int maximumDurationMinutes)
    {
        if (maximumDurationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDurationMinutes),
                maximumDurationMinutes,
                "The maximum duration must be positive.");
        }
    }
}

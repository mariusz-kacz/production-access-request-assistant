namespace GovernedAccess.Web.Teams;

internal sealed record TeamsReadyCardPresentation
{
    internal TeamsReadyCardPresentation(
        string requesterDisplayName,
        string requesterId,
        string clientDisplayName,
        string clientId,
        string environmentDisplayName,
        string environmentId,
        string roleDisplayName,
        string roleId,
        string? incidentDisplayName,
        string? incidentId,
        string justification,
        DateTimeOffset readyDeadline,
        string locale,
        Guid preparationId)
    {
        RequesterDisplayName = Normalize(
            requesterDisplayName,
            nameof(requesterDisplayName));
        RequesterId = Normalize(requesterId, nameof(requesterId));
        ClientDisplayName = Normalize(clientDisplayName, nameof(clientDisplayName));
        ClientId = Normalize(clientId, nameof(clientId));
        EnvironmentDisplayName = Normalize(
            environmentDisplayName,
            nameof(environmentDisplayName));
        EnvironmentId = Normalize(environmentId, nameof(environmentId));
        RoleDisplayName = Normalize(roleDisplayName, nameof(roleDisplayName));
        RoleId = Normalize(roleId, nameof(roleId));
        if ((incidentDisplayName is null) != (incidentId is null))
        {
            throw new ArgumentException(
                "Incident display name and identifier must be present or absent together.");
        }

        IncidentDisplayName = incidentDisplayName is null
            ? null
            : Normalize(incidentDisplayName, nameof(incidentDisplayName));
        IncidentId = incidentId is null
            ? null
            : Normalize(incidentId, nameof(incidentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        Justification = justification;
        ReadyDeadline = readyDeadline;
        Locale = TeamsLocale.Resolve(locale);
        if (preparationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preparation identifier must not be empty.",
                nameof(preparationId));
        }

        PreparationId = preparationId;
    }

    internal string RequesterDisplayName { get; }

    internal string RequesterId { get; }

    internal string ClientDisplayName { get; }

    internal string ClientId { get; }

    internal string EnvironmentDisplayName { get; }

    internal string EnvironmentId { get; }

    internal string RoleDisplayName { get; }

    internal string RoleId { get; }

    internal string? IncidentDisplayName { get; }

    internal string? IncidentId { get; }

    internal string Justification { get; }

    internal DateTimeOffset ReadyDeadline { get; }

    internal string Locale { get; }

    internal Guid PreparationId { get; }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

internal sealed record TeamsStatusCardPresentation
{
    internal TeamsStatusCardPresentation(string title, string message)
    {
        Title = Normalize(title, nameof(title));
        Message = Normalize(message, nameof(message));
    }

    internal string Title { get; }

    internal string Message { get; }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

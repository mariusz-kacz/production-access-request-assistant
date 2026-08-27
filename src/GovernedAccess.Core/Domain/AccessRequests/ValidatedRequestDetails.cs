using GovernedAccess.Core.Domain.ReferenceData;

namespace GovernedAccess.Core.Domain.AccessRequests;

/// <summary>
/// Canonical request details produced after deterministic validation. Client,
/// environment, and role form the access scope; justification and incident provide
/// governance context.
/// </summary>
public sealed record ValidatedRequestDetails
{
    private ValidatedRequestDetails()
    {
        ClientId = null!;
        EnvironmentId = null!;
        RoleId = null!;
        Justification = null!;
    }

    internal ValidatedRequestDetails(
        string clientId,
        string environmentId,
        string roleId,
        string justification,
        string? incidentId)
        : this(
            clientId,
            environmentId,
            roleId,
            justification,
            incidentId,
            AccessRequest.MinimumJustificationLength)
    {
    }

    private ValidatedRequestDetails(
        string clientId,
        string environmentId,
        string roleId,
        string justification,
        string? incidentId,
        int minimumJustificationLength)
    {
        clientId = AccessRequestNormalization.NormalizeIdentifier(clientId);
        environmentId = AccessRequestNormalization.NormalizeIdentifier(environmentId);
        roleId = AccessRequestNormalization.NormalizeIdentifier(roleId);
        justification = AccessRequestNormalization.NormalizeJustification(justification);
        incidentId = AccessRequestNormalization.NormalizeOptionalIdentifier(incidentId);

        if (!ProductionRoleIds.IsSupported(roleId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roleId),
                roleId,
                "The requested role is not supported by this feature.");
        }

        if (justification.Length < minimumJustificationLength
            || justification.Length > AccessRequest.MaximumJustificationLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(justification),
                justification.Length,
                $"The justification must be between {minimumJustificationLength} and {AccessRequest.MaximumJustificationLength} characters.");
        }

        ClientId = clientId;
        EnvironmentId = environmentId;
        RoleId = roleId;
        Justification = justification;
        IncidentId = incidentId;
    }

    internal static ValidatedRequestDetails CreateFromPreparation(
        string clientId,
        string environmentId,
        string roleId,
        string justification,
        string? incidentId) =>
        new(
            clientId,
            environmentId,
            roleId,
            justification,
            incidentId,
            minimumJustificationLength: 1);

    internal static ValidatedRequestDetails? RestorePreparedSnapshot(
        string? clientId,
        string? environmentId,
        string? roleId,
        string? justification,
        string? incidentId)
    {
        try
        {
            return new ValidatedRequestDetails(
                clientId!,
                environmentId!,
                roleId!,
                justification!,
                incidentId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public string ClientId { get; private set; }

    public string EnvironmentId { get; private set; }

    public string RoleId { get; private set; }

    public string Justification { get; private set; }

    public string? IncidentId { get; private set; }
}

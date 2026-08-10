using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public sealed record AccessRequestValidationInput(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRoleId,
    string? Justification,
    string? IncidentId)
{
    public static AccessRequestValidationInput From(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AccessRequestValidationInput(
            request.ClientId,
            request.EnvironmentId,
            request.RequestedRoleId,
            request.Justification,
            request.IncidentId);
    }
}

public sealed record ValidatedAccessRequestFields(
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    string Justification,
    string? IncidentId)
{
    public bool Matches(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StringComparer.Ordinal.Equals(ClientId, request.ClientId)
            && StringComparer.Ordinal.Equals(EnvironmentId, request.EnvironmentId)
            && StringComparer.Ordinal.Equals(
                RequestedRoleId,
                request.RequestedRoleId)
            && StringComparer.Ordinal.Equals(Justification, request.Justification)
            && StringComparer.Ordinal.Equals(IncidentId, request.IncidentId);
    }
}

public abstract record AccessRequestValidationOutcome;

public sealed record AccessRequestValidationSucceeded(ValidatedAccessRequestFields Fields)
    : AccessRequestValidationOutcome;

public sealed record AccessRequestValidationRejected : AccessRequestValidationOutcome
{
    public AccessRequestValidationRejected(IEnumerable<FieldValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var errorList = errors.ToArray();
        if (errorList.Length == 0)
        {
            throw new ArgumentException(
                "At least one field validation error is required.",
                nameof(errors));
        }

        Errors = Array.AsReadOnly(errorList);
    }

    public IReadOnlyList<FieldValidationError> Errors { get; }
}

public sealed record AccessRequestValidationFailed(ApplicationFailure Failure)
    : AccessRequestValidationOutcome;

/// <summary>
/// Strictly validates a complete request scope against current authoritative
/// context for submission and later workflow revalidation.
/// </summary>
public sealed class AccessRequestValidator
{
    private readonly IRequestContextReader requestContext;

    public AccessRequestValidator(IRequestContextReader requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        this.requestContext = requestContext;
    }

    public async Task<AccessRequestValidationOutcome> ValidateAsync(
        AccessRequestValidationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var clientId = NormalizeRequired(input.ClientId);
        var environmentId = NormalizeRequired(input.EnvironmentId);
        var requestedRoleId = NormalizeRequired(input.RequestedRoleId);
        var justification = NormalizeRequired(input.Justification);
        var incidentId = AccessRequestNormalization.NormalizeOptionalIdentifier(input.IncidentId);

        var fieldErrors = RequestFieldRules.ValidateRequiredFields(
            clientId,
            environmentId,
            requestedRoleId,
            justification);
        if (fieldErrors.Count > 0)
        {
            return Invalid(fieldErrors);
        }

        var clientResult = await requestContext.GetClientAsync(clientId!, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapLookupFailure(
                clientResult.Failure!,
                "clientId",
                "client_not_found",
                "The selected client does not exist.");
        }

        var client = clientResult.Value;
        var environmentResult = await requestContext.GetProductionEnvironmentAsync(
            environmentId!,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return MapLookupFailure(
                environmentResult.Failure!,
                "environmentId",
                "environment_not_found",
                "The selected production environment does not exist.");
        }

        var environment = environmentResult.Value;
        if (!string.Equals(environment.ClientId, client.Id, StringComparison.Ordinal))
        {
            return Invalid(new FieldValidationError(
                "environmentId",
                "environment_client_mismatch",
                "The selected production environment does not belong to the client."));
        }

        var roleResult = await requestContext.GetEnvironmentRoleAsync(
            environment.Id,
            requestedRoleId!,
            cancellationToken);
        if (roleResult.IsFailure)
        {
            return roleResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Invalid(RequestFieldRules.RoleUnavailableError())
                : new AccessRequestValidationFailed(roleResult.Failure);
        }

        var role = roleResult.Value;
        string? canonicalIncidentId = null;
        if (incidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(
                incidentId,
                cancellationToken);
            if (incidentResult.IsFailure)
            {
                return MapLookupFailure(
                    incidentResult.Failure!,
                    "incidentId",
                    "incident_not_found",
                    "The supplied incident does not exist.");
            }

            var incident = incidentResult.Value;
            if (incident.Status != IncidentStatus.Active)
            {
                return Invalid(new FieldValidationError(
                    "incidentId",
                    "incident_inactive",
                    "The supplied incident is inactive."));
            }

            if (!string.Equals(incident.ClientId, client.Id, StringComparison.Ordinal))
            {
                return Invalid(new FieldValidationError(
                    "incidentId",
                    "incident_client_mismatch",
                    "The supplied incident does not belong to the client."));
            }

            if (incident.EnvironmentId is not null
                && !string.Equals(
                    incident.EnvironmentId,
                    environment.Id,
                    StringComparison.Ordinal))
            {
                return Invalid(new FieldValidationError(
                    "incidentId",
                    "incident_environment_mismatch",
                    "The supplied incident is associated with another environment."));
            }

            canonicalIncidentId = incident.Id;
        }

        return new AccessRequestValidationSucceeded(new ValidatedAccessRequestFields(
            client.Id,
            environment.Id,
            role.RoleId,
            justification!,
            canonicalIncidentId));
    }

    private static AccessRequestValidationOutcome MapLookupFailure(
        ApplicationFailure failure,
        string field,
        string notFoundCode,
        string notFoundMessage) =>
        failure.Kind == ApplicationFailureKind.NotFound
            ? Invalid(new FieldValidationError(field, notFoundCode, notFoundMessage))
            : new AccessRequestValidationFailed(failure);

    private static AccessRequestValidationRejected Invalid(
        params FieldValidationError[] errors) =>
        Invalid((IEnumerable<FieldValidationError>)errors);

    private static AccessRequestValidationRejected Invalid(
        IEnumerable<FieldValidationError> errors) =>
        new(errors);

    private static string? NormalizeRequired(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

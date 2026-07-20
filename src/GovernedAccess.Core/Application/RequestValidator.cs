using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record RequestValidationInput(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRoleId,
    string? Justification,
    string? IncidentId);

public sealed record ValidatedRequestFields(
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    string Justification,
    string? IncidentId);

public sealed class FieldValidationError
{
    public FieldValidationError(string field, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Field = field;
        Code = code;
        Message = message;
    }

    public string Field { get; }

    public string Code { get; }

    public string Message { get; }
}

public abstract record RequestValidationOutcome;

public sealed record RequestValidationSucceeded(ValidatedRequestFields Fields)
    : RequestValidationOutcome;

public sealed record RequestValidationRejected : RequestValidationOutcome
{
    public RequestValidationRejected(IEnumerable<FieldValidationError> errors)
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

public sealed record RequestValidationFailed(ApplicationFailure Failure)
    : RequestValidationOutcome;

/// <summary>
/// Validates proposed request fields against current request context and
/// returns canonical values suitable for request creation or revalidation.
/// </summary>
public sealed class RequestValidator
{
    private readonly IRequestContextReader requestContext;

    public RequestValidator(IRequestContextReader requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        this.requestContext = requestContext;
    }

    public async Task<RequestValidationOutcome> ValidateAsync(
        RequestValidationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var clientId = NormalizeRequired(input.ClientId);
        var environmentId = NormalizeRequired(input.EnvironmentId);
        var requestedRoleId = NormalizeRequired(input.RequestedRoleId);
        var justification = NormalizeRequired(input.Justification);
        var incidentId = AccessRequestNormalization.NormalizeOptionalIdentifier(input.IncidentId);

        var fieldErrors = ValidateRequiredFields(
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
            return Invalid(
                new FieldValidationError(
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
                ? Invalid(RoleUnavailableError())
                : new RequestValidationFailed(roleResult.Failure);
        }

        var role = roleResult.Value;

        string? canonicalIncidentId = null;
        if (incidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(incidentId, cancellationToken);
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
                return Invalid(
                    new FieldValidationError(
                        "incidentId",
                        "incident_inactive",
                        "The supplied incident is inactive."));
            }

            if (!string.Equals(incident.ClientId, client.Id, StringComparison.Ordinal))
            {
                return Invalid(
                    new FieldValidationError(
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
                return Invalid(
                    new FieldValidationError(
                        "incidentId",
                        "incident_environment_mismatch",
                        "The supplied incident is associated with another environment."));
            }

            canonicalIncidentId = incident.Id;
        }

        return new RequestValidationSucceeded(
            new ValidatedRequestFields(
                client.Id,
                environment.Id,
                role.RoleId,
                justification!,
                canonicalIncidentId));
    }

    private static List<FieldValidationError> ValidateRequiredFields(
        string? clientId,
        string? environmentId,
        string? requestedRoleId,
        string? justification)
    {
        var errors = new List<FieldValidationError>();

        if (clientId is null)
        {
            errors.Add(new FieldValidationError(
                "clientId",
                "client_required",
                "A client is required."));
        }

        if (environmentId is null)
        {
            errors.Add(new FieldValidationError(
                "environmentId",
                "environment_required",
                "A production environment is required."));
        }

        if (requestedRoleId is null)
        {
            errors.Add(new FieldValidationError(
                "requestedRoleId",
                "requested_role_required",
                "A requested role is required."));
        }
        else if (!ProductionRoleIds.IsSupported(requestedRoleId))
        {
            errors.Add(UnsupportedRoleError());
        }

        if (justification is null)
        {
            errors.Add(new FieldValidationError(
                "justification",
                "justification_required",
                "A justification is required."));
        }
        else if (justification.Length is
                 < AccessRequest.MinimumJustificationLength or
                 > AccessRequest.MaximumJustificationLength)
        {
            errors.Add(new FieldValidationError(
                "justification",
                "justification_length_invalid",
                $"The justification must be between {AccessRequest.MinimumJustificationLength} and {AccessRequest.MaximumJustificationLength} characters."));
        }

        return errors;
    }

    private static FieldValidationError RoleUnavailableError()
    {
        return new FieldValidationError(
            "requestedRoleId",
            "role_unavailable",
            "The requested role is unavailable for the selected environment.");
    }

    private static FieldValidationError UnsupportedRoleError()
    {
        return new FieldValidationError(
            "requestedRoleId",
            "role_unsupported",
            "The requested role is not supported.");
    }

    private static RequestValidationOutcome MapLookupFailure(
        ApplicationFailure failure,
        string field,
        string notFoundCode,
        string notFoundMessage)
    {
        return failure.Kind == ApplicationFailureKind.NotFound
            ? Invalid(new FieldValidationError(field, notFoundCode, notFoundMessage))
            : new RequestValidationFailed(failure);
    }

    private static RequestValidationRejected Invalid(
        params FieldValidationError[] errors)
    {
        return Invalid((IEnumerable<FieldValidationError>)errors);
    }

    private static RequestValidationRejected Invalid(
        IEnumerable<FieldValidationError> errors)
    {
        return new RequestValidationRejected(errors);
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

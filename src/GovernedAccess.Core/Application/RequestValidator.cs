using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

public sealed record RequestValidationInput(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRoleId,
    int RequestedDurationMinutes,
    string? Justification,
    string? IncidentId);

public sealed record ValidatedRequestFields(
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    int RequestedDurationMinutes,
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

public sealed class RequestValidationResult
{
    private readonly ValidatedRequestFields? validatedFields;

    public RequestValidationResult(ValidatedRequestFields validatedFields)
    {
        ArgumentNullException.ThrowIfNull(validatedFields);

        this.validatedFields = validatedFields;
        Errors = Array.Empty<FieldValidationError>();
    }

    public RequestValidationResult(IEnumerable<FieldValidationError> errors)
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

    public bool IsValid => validatedFields is not null;

    public ValidatedRequestFields ValidatedFields => validatedFields
        ?? throw new InvalidOperationException("Invalid request fields have no validated value.");

    public IReadOnlyList<FieldValidationError> Errors { get; }
}

/// <summary>
/// Validates proposed request fields against current authoritative context and
/// returns canonical values suitable for request creation or revalidation.
/// </summary>
public sealed class RequestValidator
{
    private readonly IAuthoritativeContextReader context;

    public RequestValidator(IAuthoritativeContextReader context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public async Task<ApplicationResult<RequestValidationResult>> ValidateAsync(
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
            input.RequestedDurationMinutes,
            justification);

        if (fieldErrors.Count > 0)
        {
            return Invalid(fieldErrors);
        }

        var clientResult = await context.GetClientAsync(clientId!, cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapLookupFailure(
                clientResult.Failure!,
                "clientId",
                "client_not_found",
                "The selected client does not exist.");
        }

        var client = clientResult.Value;
        var environmentResult = await context.GetProductionEnvironmentAsync(
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

        var roleResult = await context.GetEnvironmentRoleAsync(
            environment.Id,
            requestedRoleId!,
            cancellationToken);
        if (roleResult.IsFailure)
        {
            return roleResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Invalid(RoleUnavailableError())
                : ApplicationResult.Failed<RequestValidationResult>(roleResult.Failure);
        }

        var role = roleResult.Value;

        if (input.RequestedDurationMinutes > environment.MaximumDurationMinutes)
        {
            return Invalid(
                new FieldValidationError(
                    "requestedDurationMinutes",
                    "duration_exceeds_environment_maximum",
                    "The requested duration exceeds the environment maximum."));
        }

        string? canonicalIncidentId = null;
        if (incidentId is not null)
        {
            var incidentResult = await context.GetIncidentAsync(incidentId, cancellationToken);
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

        return ApplicationResult.Succeeded(
            new RequestValidationResult(
                new ValidatedRequestFields(
                    client.Id,
                    environment.Id,
                    role.RoleId,
                    input.RequestedDurationMinutes,
                    justification!,
                    canonicalIncidentId)));
    }

    private static List<FieldValidationError> ValidateRequiredFields(
        string? clientId,
        string? environmentId,
        string? requestedRoleId,
        int requestedDurationMinutes,
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

        if (requestedDurationMinutes <= 0)
        {
            errors.Add(new FieldValidationError(
                "requestedDurationMinutes",
                "duration_must_be_positive",
                "The requested duration must be positive."));
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

    private static ApplicationResult<RequestValidationResult> MapLookupFailure(
        ApplicationFailure failure,
        string field,
        string notFoundCode,
        string notFoundMessage)
    {
        return failure.Kind == ApplicationFailureKind.NotFound
            ? Invalid(new FieldValidationError(field, notFoundCode, notFoundMessage))
            : ApplicationResult.Failed<RequestValidationResult>(failure);
    }

    private static ApplicationResult<RequestValidationResult> Invalid(
        params FieldValidationError[] errors)
    {
        return Invalid((IEnumerable<FieldValidationError>)errors);
    }

    private static ApplicationResult<RequestValidationResult> Invalid(
        IEnumerable<FieldValidationError> errors)
    {
        return ApplicationResult.Succeeded(new RequestValidationResult(errors));
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

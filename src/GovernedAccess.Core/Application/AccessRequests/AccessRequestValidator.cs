using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.AccessRequests;

public abstract record RequestValidationOutcome;

public sealed record RequestValidationSucceeded(ValidatedRequestDetails Details)
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
/// Revalidates an already canonical request snapshot against mutable authoritative
/// context before submission, approval decisions, and provisioning retries.
/// </summary>
public sealed class AccessRequestValidator
{
    private readonly IRequestContextReader requestContext;

    public AccessRequestValidator(IRequestContextReader requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        this.requestContext = requestContext;
    }

    public async Task<RequestValidationOutcome> RevalidateAsync(
        ValidatedRequestDetails details,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        var clientResult = await requestContext.GetClientAsync(
            details.ClientId,
            cancellationToken);
        if (clientResult.IsFailure)
        {
            return MapLookupFailure(
                clientResult.Failure!,
                "clientId",
                "client_not_found",
                "The selected client does not exist.");
        }

        var client = clientResult.Value;
        var environmentResult =
            await requestContext.GetProductionEnvironmentContextAsync(
                details.EnvironmentId,
                cancellationToken);
        if (environmentResult.IsFailure)
        {
            return MapLookupFailure(
                environmentResult.Failure!,
                "environmentId",
                "environment_not_found",
                "The selected production environment does not exist.");
        }

        var environmentContext = environmentResult.Value;
        var environment = environmentContext.Environment;
        if (!string.Equals(
                environmentContext.Client.Id,
                client.Id,
                StringComparison.Ordinal))
        {
            return Invalid(
                new FieldValidationError(
                    "environmentId",
                    "environment_client_mismatch",
                    "The selected production environment does not belong to the client."));
        }

        var hasAssignedRole = environmentContext.AssignedRoles.Any(
            candidate => string.Equals(
                candidate.RoleId,
                details.RoleId,
                StringComparison.Ordinal));
        if (!hasAssignedRole)
        {
            return Invalid(RequestFieldRules.RoleUnavailableError());
        }

        if (details.IncidentId is not null)
        {
            var incidentResult = await requestContext.GetIncidentAsync(
                details.IncidentId,
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
        }

        return new RequestValidationSucceeded(details);
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
}

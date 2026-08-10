using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;

namespace GovernedAccess.Core.Application;

internal static class RequestFieldRules
{
    public static List<FieldValidationError> ValidateRequiredFields(
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

    public static FieldValidationError RoleUnavailableError() =>
        new(
            "requestedRoleId",
            "role_unavailable",
            "The requested role is unavailable for the selected environment.");

    private static FieldValidationError UnsupportedRoleError() =>
        new(
            "requestedRoleId",
            "role_unsupported",
            "The requested role is not supported.");
}

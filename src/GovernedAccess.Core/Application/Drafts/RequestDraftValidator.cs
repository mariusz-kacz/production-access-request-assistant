using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.Drafts;

public abstract record RequestCandidateAssessment
{
    private protected RequestCandidateAssessment()
    {
    }
}

public sealed record RequestCandidateAssessmentRejected
    : RequestCandidateAssessment
{
    public RequestCandidateAssessmentRejected(
        RequestCandidate candidate,
        IEnumerable<FieldValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(errors);

        var errorList = errors.ToArray();
        if (errorList.Length == 0)
        {
            throw new ArgumentException(
                "At least one field validation error is required.",
                nameof(errors));
        }

        Candidate = candidate;
        Errors = Array.AsReadOnly(errorList);
    }

    public RequestCandidate Candidate { get; }

    public IReadOnlyList<FieldValidationError> Errors { get; }
}

public sealed record RequestCandidateAssessmentIncomplete
    : RequestCandidateAssessment
{
    public RequestCandidateAssessmentIncomplete(
        RequestCandidate candidate,
        IEnumerable<FieldValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(errors);

        var errorList = errors.ToArray();
        if (errorList.Length == 0)
        {
            throw new ArgumentException(
                "At least one missing-field error is required.",
                nameof(errors));
        }

        Candidate = candidate;
        Errors = Array.AsReadOnly(errorList);
    }

    public RequestCandidate Candidate { get; }

    public IReadOnlyList<FieldValidationError> Errors { get; }
}

public sealed record RequestCandidateAssessmentReady(
    ValidatedRequestDetails Details)
    : RequestCandidateAssessment;

/// <summary>
/// Validates proposed request fields against current request context and
/// returns canonical values suitable for request creation or revalidation.
/// </summary>
public sealed class RequestDraftValidator
{
    private readonly IRequestContextReader requestContext;

    public RequestDraftValidator(IRequestContextReader requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        this.requestContext = requestContext;
    }

    /// <summary>
    /// Validates every identifier supplied by the model, clears rejected values,
    /// derives canonical ownership, and determines readiness in one authoritative
    /// pass. Missing fields produce an incomplete assessment so the application can
    /// honor a valid clarification proposal without repeating authoritative lookups.
    /// </summary>
    public async Task<ApplicationResult<RequestCandidateAssessment>>
        AssessCandidateAsync(
            RequestCandidate candidate,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var state = new CandidateAssessmentState(candidate);

        var environmentResolution = await ResolveEnvironmentAsync(
            state,
            cancellationToken);
        if (environmentResolution.IsFailure)
        {
            return AssessmentFailed(environmentResolution);
        }

        var incidentResolution = await ResolveIncidentAsync(
            state,
            cancellationToken);
        if (incidentResolution.IsFailure)
        {
            return AssessmentFailed(incidentResolution);
        }

        var derivedEnvironmentResolution =
            await ResolveEnvironmentFromIncidentAsync(
                state,
                cancellationToken);
        if (derivedEnvironmentResolution.IsFailure)
        {
            return AssessmentFailed(derivedEnvironmentResolution);
        }

        RejectIncidentEnvironmentMismatch(state);

        var clientResolution = await ResolveClientAsync(
            state,
            cancellationToken);
        if (clientResolution.IsFailure)
        {
            return AssessmentFailed(clientResolution);
        }

        var roleResolution = await ResolveRoleAsync(
            state,
            cancellationToken);
        if (roleResolution.IsFailure)
        {
            return AssessmentFailed(roleResolution);
        }

        return BuildAssessment(state);
    }

    private async Task<ApplicationResult> ResolveEnvironmentAsync(
        CandidateAssessmentState state,
        CancellationToken cancellationToken)
    {
        if (state.EnvironmentId is null)
        {
            return ApplicationResult.Succeeded();
        }

        var result = await requestContext.GetProductionEnvironmentAsync(
            state.EnvironmentId,
            cancellationToken);
        if (result.IsSuccess)
        {
            state.Environment = result.Value;
            state.EnvironmentId = result.Value.Id;
            return ApplicationResult.Succeeded();
        }

        if (result.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed(result.Failure);
        }

        state.Errors.Add(
            new FieldValidationError(
                "environmentId",
                "environment_not_found",
                "The selected production environment does not exist."));
        state.EnvironmentId = null;
        return ApplicationResult.Succeeded();
    }

    private async Task<ApplicationResult> ResolveIncidentAsync(
        CandidateAssessmentState state,
        CancellationToken cancellationToken)
    {
        if (state.IncidentId is null)
        {
            return ApplicationResult.Succeeded();
        }

        var result = await requestContext.GetIncidentAsync(
            state.IncidentId,
            cancellationToken);
        if (result.IsSuccess)
        {
            state.Incident = result.Value;
            state.IncidentId = result.Value.Id;
            if (result.Value.Status == IncidentStatus.Active)
            {
                return ApplicationResult.Succeeded();
            }

            state.Errors.Add(
                new FieldValidationError(
                    "incidentId",
                    "incident_inactive",
                    "The supplied incident is inactive."));
            ClearIncident(state);
            return ApplicationResult.Succeeded();
        }

        if (result.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed(result.Failure);
        }

        state.Errors.Add(
            new FieldValidationError(
                "incidentId",
                "incident_not_found",
                "The supplied incident does not exist."));
        state.IncidentId = null;
        return ApplicationResult.Succeeded();
    }

    private async Task<ApplicationResult> ResolveEnvironmentFromIncidentAsync(
        CandidateAssessmentState state,
        CancellationToken cancellationToken)
    {
        var incidentEnvironmentId = state.Incident?.EnvironmentId;
        if (state.Environment is not null || incidentEnvironmentId is null)
        {
            return ApplicationResult.Succeeded();
        }

        var result = await requestContext.GetProductionEnvironmentAsync(
            incidentEnvironmentId,
            cancellationToken);
        if (result.IsFailure)
        {
            return ApplicationResult.Failed(result.Failure!);
        }

        state.Environment = result.Value;
        state.EnvironmentId = result.Value.Id;
        return ApplicationResult.Succeeded();
    }

    private static void RejectIncidentEnvironmentMismatch(
        CandidateAssessmentState state)
    {
        if (state.Environment is null || state.Incident is null)
        {
            return;
        }

        if (!string.Equals(
                state.Incident.EnvironmentId,
                state.Environment.Id,
                StringComparison.Ordinal))
        {
            state.Errors.Add(
                new FieldValidationError(
                    "incidentId",
                    "incident_environment_mismatch",
                    "The supplied incident is associated with another environment."));
            ClearIncident(state);
        }
    }

    private async Task<ApplicationResult> ResolveClientAsync(
        CandidateAssessmentState state,
        CancellationToken cancellationToken)
    {
        var derivedClientId = state.Environment?.ClientId;
        Client? resolvedClient = null;

        if (state.SuppliedClientId is not null)
        {
            var suppliedClientResult = await requestContext.GetClientAsync(
                state.SuppliedClientId,
                cancellationToken);
            if (suppliedClientResult.IsSuccess)
            {
                resolvedClient = suppliedClientResult.Value;
                state.ClientId = resolvedClient.Id;
                RejectScopeNotOwnedByClient(
                    state,
                    resolvedClient.Id,
                    derivedClientId);
            }
            else if (suppliedClientResult.Failure!.Kind
                     == ApplicationFailureKind.NotFound)
            {
                UseDerivedClientOrRejectUnknownClient(state, derivedClientId);
            }
            else
            {
                return ApplicationResult.Failed(
                    suppliedClientResult.Failure);
            }
        }
        else
        {
            state.ClientId = derivedClientId;
        }

        if (resolvedClient is not null || state.ClientId is null)
        {
            return ApplicationResult.Succeeded();
        }

        var derivedClientResult = await requestContext.GetClientAsync(
            state.ClientId,
            cancellationToken);
        if (derivedClientResult.IsSuccess)
        {
            state.ClientId = derivedClientResult.Value.Id;
            return ApplicationResult.Succeeded();
        }

        if (derivedClientResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed(derivedClientResult.Failure);
        }

        state.Errors.Add(ClientNotFoundError());
        state.ClientId = null;
        return ApplicationResult.Succeeded();
    }

    private static void UseDerivedClientOrRejectUnknownClient(
        CandidateAssessmentState state,
        string? derivedClientId)
    {
        if (derivedClientId is not null)
        {
            state.ClientId = derivedClientId;
            return;
        }

        state.Errors.Add(ClientNotFoundError());
        state.ClientId = null;
    }

    private static void RejectScopeNotOwnedByClient(
        CandidateAssessmentState state,
        string clientId,
        string? derivedClientId)
    {
        if (derivedClientId is null
            || string.Equals(
                clientId,
                derivedClientId,
                StringComparison.Ordinal))
        {
            return;
        }

        if (state.Environment is not null)
        {
            state.Errors.Add(
                new FieldValidationError(
                    "environmentId",
                    "environment_client_mismatch",
                    "The selected production environment does not belong to the client."));
            state.Environment = null;
            state.EnvironmentId = null;

            if (state.Incident is not null)
            {
                ClearIncident(state);
            }

            return;
        }

    }

    private async Task<ApplicationResult> ResolveRoleAsync(
        CandidateAssessmentState state,
        CancellationToken cancellationToken)
    {
        if (state.Environment is null || state.RequestedRoleId is null)
        {
            return ApplicationResult.Succeeded();
        }

        var result = await requestContext.GetEnvironmentRoleAsync(
            state.Environment.Id,
            state.RequestedRoleId,
            cancellationToken);
        if (result.IsSuccess)
        {
            state.RequestedRoleId = result.Value.RoleId;
            return ApplicationResult.Succeeded();
        }

        if (result.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return ApplicationResult.Failed(result.Failure);
        }

        state.Errors.Add(RequestFieldRules.RoleUnavailableError());
        state.RequestedRoleId = null;
        return ApplicationResult.Succeeded();
    }

    private static ApplicationResult<RequestCandidateAssessment> BuildAssessment(
        CandidateAssessmentState state)
    {
        var candidate = state.ToCandidate();
        if (state.Errors.Count > 0)
        {
            return ApplicationResult.Succeeded<RequestCandidateAssessment>(
                new RequestCandidateAssessmentRejected(
                    candidate,
                    state.Errors));
        }

        var readinessErrors = RequestFieldRules.ValidateRequiredFields(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification);
        if (readinessErrors.Count > 0)
        {
            var assessment = candidate.IsStructurallyComplete
                ? (RequestCandidateAssessment)
                    new RequestCandidateAssessmentRejected(
                        candidate,
                        readinessErrors)
                : new RequestCandidateAssessmentIncomplete(
                    candidate,
                    readinessErrors);

            return ApplicationResult.Succeeded(assessment);
        }

        return ApplicationResult.Succeeded<RequestCandidateAssessment>(
            new RequestCandidateAssessmentReady(
                new ValidatedRequestDetails(
                    candidate.ClientId!,
                    candidate.EnvironmentId!,
                    candidate.RequestedRoleId!,
                    candidate.Justification!,
                    candidate.IncidentId)));
    }

    private static ApplicationResult<RequestCandidateAssessment> AssessmentFailed(
        ApplicationResult resolution) =>
        ApplicationResult.Failed<RequestCandidateAssessment>(
            resolution.Failure!);

    private static FieldValidationError ClientNotFoundError() =>
        new(
            "clientId",
            "client_not_found",
            "The selected client does not exist.");

    private static void ClearIncident(CandidateAssessmentState state)
    {
        state.Incident = null;
        state.IncidentId = null;
    }

    private sealed class CandidateAssessmentState
    {
        public CandidateAssessmentState(RequestCandidate candidate)
        {
            SuppliedClientId = candidate.ClientId;
            ClientId = candidate.ClientId;
            EnvironmentId = candidate.EnvironmentId;
            RequestedRoleId = candidate.RequestedRoleId;
            Justification = candidate.Justification;
            IncidentId = candidate.IncidentId;
        }

        public string? SuppliedClientId { get; }

        public string? ClientId { get; set; }

        public string? EnvironmentId { get; set; }

        public string? RequestedRoleId { get; set; }

        public string? Justification { get; }

        public string? IncidentId { get; set; }

        public ProductionEnvironment? Environment { get; set; }

        public Incident? Incident { get; set; }

        public List<FieldValidationError> Errors { get; } = [];

        public RequestCandidate ToCandidate() =>
            new(
                ClientId,
                EnvironmentId,
                RequestedRoleId,
                Justification,
                IncidentId);
    }

}

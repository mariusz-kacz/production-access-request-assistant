using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.Drafts;

public sealed record ValidatedRequestDraft(
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    string Justification,
    string? IncidentId);

public abstract record RequestDraftValidationOutcome
{
    private protected RequestDraftValidationOutcome()
    {
    }
}

public sealed record RequestDraftRejected : RequestDraftValidationOutcome
{
    public RequestDraftRejected(
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

public sealed record RequestDraftIncomplete : RequestDraftValidationOutcome
{
    public RequestDraftIncomplete(
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

public sealed record RequestDraftReady(ValidatedRequestDraft Fields)
    : RequestDraftValidationOutcome;

/// <summary>
/// Validates and canonicalizes a mutable request draft against authoritative
/// context. Invalid identifiers are cleared so a later conversation turn can
/// correct them; missing fields remain a valid incomplete draft state.
/// </summary>
public sealed class RequestDraftValidator
{
    private readonly IRequestContextReader requestContext;

    public RequestDraftValidator(IRequestContextReader requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        this.requestContext = requestContext;
    }

    public async Task<ApplicationResult<RequestDraftValidationOutcome>> ValidateAsync(
        RequestCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var state = new DraftValidationState(candidate);

        var environmentResolution = await ResolveEnvironmentAsync(state, cancellationToken);
        if (environmentResolution.IsFailure)
        {
            return ValidationFailed(environmentResolution);
        }

        var incidentResolution = await ResolveIncidentAsync(state, cancellationToken);
        if (incidentResolution.IsFailure)
        {
            return ValidationFailed(incidentResolution);
        }

        var derivedEnvironmentResolution = await ResolveEnvironmentFromIncidentAsync(
            state,
            cancellationToken);
        if (derivedEnvironmentResolution.IsFailure)
        {
            return ValidationFailed(derivedEnvironmentResolution);
        }

        RejectIncidentEnvironmentMismatch(state);

        var clientResolution = await ResolveClientAsync(state, cancellationToken);
        if (clientResolution.IsFailure)
        {
            return ValidationFailed(clientResolution);
        }

        var roleResolution = await ResolveRoleAsync(state, cancellationToken);
        if (roleResolution.IsFailure)
        {
            return ValidationFailed(roleResolution);
        }

        return BuildOutcome(state);
    }

    private async Task<ApplicationResult> ResolveEnvironmentAsync(
        DraftValidationState state,
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

        state.Errors.Add(new FieldValidationError(
            "environmentId",
            "environment_not_found",
            "The selected production environment does not exist."));
        state.EnvironmentId = null;
        return ApplicationResult.Succeeded();
    }

    private async Task<ApplicationResult> ResolveIncidentAsync(
        DraftValidationState state,
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

            state.Errors.Add(new FieldValidationError(
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

        state.Errors.Add(new FieldValidationError(
            "incidentId",
            "incident_not_found",
            "The supplied incident does not exist."));
        state.IncidentId = null;
        return ApplicationResult.Succeeded();
    }

    private async Task<ApplicationResult> ResolveEnvironmentFromIncidentAsync(
        DraftValidationState state,
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

    private static void RejectIncidentEnvironmentMismatch(DraftValidationState state)
    {
        if (state.Environment is null || state.Incident is null)
        {
            return;
        }

        if (!string.Equals(
                state.Incident.ClientId,
                state.Environment.ClientId,
                StringComparison.Ordinal))
        {
            state.Errors.Add(IncidentClientMismatchError());
            ClearIncident(state);
            return;
        }

        if (state.Incident.EnvironmentId is not null
            && !string.Equals(
                state.Incident.EnvironmentId,
                state.Environment.Id,
                StringComparison.Ordinal))
        {
            state.Errors.Add(new FieldValidationError(
                "incidentId",
                "incident_environment_mismatch",
                "The supplied incident is associated with another environment."));
            ClearIncident(state);
        }
    }

    private async Task<ApplicationResult> ResolveClientAsync(
        DraftValidationState state,
        CancellationToken cancellationToken)
    {
        var derivedClientId = state.Environment?.ClientId ?? state.Incident?.ClientId;
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
                RejectScopeNotOwnedByClient(state, resolvedClient.Id, derivedClientId);
            }
            else if (suppliedClientResult.Failure!.Kind == ApplicationFailureKind.NotFound)
            {
                UseDerivedClientOrRejectUnknownClient(state, derivedClientId);
            }
            else
            {
                return ApplicationResult.Failed(suppliedClientResult.Failure);
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
        DraftValidationState state,
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
        DraftValidationState state,
        string clientId,
        string? derivedClientId)
    {
        if (derivedClientId is null
            || string.Equals(clientId, derivedClientId, StringComparison.Ordinal))
        {
            return;
        }

        if (state.Environment is not null)
        {
            state.Errors.Add(new FieldValidationError(
                "environmentId",
                "environment_client_mismatch",
                "The selected production environment does not belong to the client."));
            state.Environment = null;
            state.EnvironmentId = null;

            if (state.Incident is not null
                && !string.Equals(
                    state.Incident.ClientId,
                    clientId,
                    StringComparison.Ordinal))
            {
                state.Errors.Add(IncidentClientMismatchError());
                ClearIncident(state);
            }

            return;
        }

        if (state.Incident is not null)
        {
            state.Errors.Add(IncidentClientMismatchError());
            ClearIncident(state);
        }
    }

    private async Task<ApplicationResult> ResolveRoleAsync(
        DraftValidationState state,
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

    private static ApplicationResult<RequestDraftValidationOutcome> BuildOutcome(
        DraftValidationState state)
    {
        var candidate = state.ToCandidate();
        if (state.Errors.Count > 0)
        {
            return ApplicationResult.Succeeded<RequestDraftValidationOutcome>(
                new RequestDraftRejected(candidate, state.Errors));
        }

        var readinessErrors = RequestFieldRules.ValidateRequiredFields(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification);
        if (readinessErrors.Count > 0)
        {
            RequestDraftValidationOutcome outcome = candidate.IsStructurallyComplete
                ? new RequestDraftRejected(candidate, readinessErrors)
                : new RequestDraftIncomplete(candidate, readinessErrors);
            return ApplicationResult.Succeeded(outcome);
        }

        return ApplicationResult.Succeeded<RequestDraftValidationOutcome>(
            new RequestDraftReady(new ValidatedRequestDraft(
                candidate.ClientId!,
                candidate.EnvironmentId!,
                candidate.RequestedRoleId!,
                candidate.Justification!,
                candidate.IncidentId)));
    }

    private static ApplicationResult<RequestDraftValidationOutcome> ValidationFailed(
        ApplicationResult resolution) =>
        ApplicationResult.Failed<RequestDraftValidationOutcome>(resolution.Failure!);

    private static FieldValidationError ClientNotFoundError() =>
        new("clientId", "client_not_found", "The selected client does not exist.");

    private static FieldValidationError IncidentClientMismatchError() =>
        new(
            "incidentId",
            "incident_client_mismatch",
            "The supplied incident does not belong to the client.");

    private static void ClearIncident(DraftValidationState state)
    {
        state.Incident = null;
        state.IncidentId = null;
    }

    private sealed class DraftValidationState
    {
        public DraftValidationState(RequestCandidate candidate)
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

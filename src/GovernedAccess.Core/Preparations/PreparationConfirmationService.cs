using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed class PreparationConfirmationService : IPreparationConfirmationService
{
    private readonly IRequestPreparationConfirmationStore store;
    private readonly IProductionEnvironmentAuthority environmentAuthority;
    private readonly IEnvironmentRoleAuthority roleAuthority;
    private readonly IIncidentAuthority incidentAuthority;
    private readonly IAuthenticatedPrincipalReader principalReader;
    private readonly IClock clock;

    public PreparationConfirmationService(
        IRequestPreparationConfirmationStore store,
        IProductionEnvironmentAuthority environmentAuthority,
        IEnvironmentRoleAuthority roleAuthority,
        IIncidentAuthority incidentAuthority,
        IAuthenticatedPrincipalReader principalReader,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(environmentAuthority);
        ArgumentNullException.ThrowIfNull(roleAuthority);
        ArgumentNullException.ThrowIfNull(incidentAuthority);
        ArgumentNullException.ThrowIfNull(principalReader);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.environmentAuthority = environmentAuthority;
        this.roleAuthority = roleAuthority;
        this.incidentAuthority = incidentAuthority;
        this.principalReader = principalReader;
        this.clock = clock;
    }

    public async Task<PreparationConfirmationResult> ConfirmAsync(
        PreparationConfirmationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var preparationResult = await store.GetAsync(
            command.PreparationId,
            cancellationToken);
        if (preparationResult.IsFailure)
        {
            return preparationResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? FailedNotFound()
                : new PreparationConfirmationFailed(preparationResult.Failure);
        }

        var preparation = preparationResult.Value;
        if (preparation.Binding != command.Binding)
        {
            return FailedNotFound();
        }

        var lifecycleResult = await HandleTerminalOrUnreadyAsync(
            preparation,
            cancellationToken);
        if (lifecycleResult is not null)
        {
            return lifecycleResult;
        }

        var observedAt = clock.UtcNow.ToUniversalTime();
        if (preparation.IsExpired(observedAt))
        {
            preparation.MarkExpired(observedAt, command.CorrelationId);
            var expirySave = await store.SaveChangesAsync(cancellationToken);
            return expirySave.IsFailure
                ? new PreparationConfirmationFailed(expirySave.Failure!)
                : Failed(
                    ApplicationFailureKind.InvalidTransition,
                    "request-preparation-expired",
                    "The request preparation has expired and cannot be confirmed.");
        }

        var requesterValidation = await ValidateRequesterAsync(
            command.Binding.RequesterId,
            cancellationToken);
        if (requesterValidation is not null)
        {
            return requesterValidation;
        }

        var revalidation = await RevalidateAsync(
            preparation.Candidate,
            cancellationToken);
        if (revalidation is ConfirmationSourceUnavailable unavailable)
        {
            return new PreparationConfirmationSourceUnavailable(unavailable.Failure);
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (preparation.IsExpired(occurredAt))
        {
            preparation.MarkExpired(occurredAt, command.CorrelationId);
            var expirySave = await store.SaveChangesAsync(cancellationToken);
            return expirySave.IsFailure
                ? new PreparationConfirmationFailed(expirySave.Failure!)
                : Failed(
                    ApplicationFailureKind.InvalidTransition,
                    "request-preparation-expired",
                    "The request preparation has expired and cannot be confirmed.");
        }

        if (revalidation is ConfirmationFactsChanged changed)
        {
            return await PersistCorrectionAsync(
                preparation,
                changed.Candidate,
                command,
                occurredAt,
                cancellationToken);
        }

        var valid = (ConfirmationFactsValid)revalidation;
        var request = new AccessRequest(
            Guid.NewGuid(),
            preparation.PreparationId,
            preparation.Binding.RequesterId,
            valid.Details,
            occurredAt,
            command.CorrelationId);
        store.AddRequest(request);
        store.AddAuditEvent(AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            RequestCreatedAuditDetails.FromPreparation(request.Status, preparation)));
        preparation.MarkSubmitted(occurredAt, command.CorrelationId);
        var save = await store.SaveChangesAsync(cancellationToken);
        if (save.IsSuccess)
        {
            return new PreparationConfirmationSubmitted(
                request,
                WasAlreadySubmitted: false);
        }

        return await RecoverAfterSaveFailureAsync(
            preparation.PreparationId,
            save.Failure!,
            cancellationToken);
    }

    private async Task<PreparationConfirmationResult?> HandleTerminalOrUnreadyAsync(
        RequestPreparation preparation,
        CancellationToken cancellationToken)
    {
        if (preparation.Lifecycle == PreparationLifecycle.Ready)
        {
            return null;
        }

        if (preparation.Lifecycle == PreparationLifecycle.Submitted)
        {
            var requestResult = await store.GetRequestByPreparationIdAsync(
                preparation.PreparationId,
                cancellationToken);
            return requestResult.IsSuccess
                ? new PreparationConfirmationSubmitted(
                    requestResult.Value,
                    WasAlreadySubmitted: true)
                : new PreparationConfirmationFailed(requestResult.Failure!);
        }

        var (code, message) = preparation.Lifecycle switch
        {
            PreparationLifecycle.Superseded => (
                "request-preparation-superseded",
                "The request preparation was superseded and cannot be confirmed."),
            PreparationLifecycle.Expired => (
                "request-preparation-expired",
                "The request preparation has expired and cannot be confirmed."),
            _ => (
                "request-preparation-not-ready",
                "The request preparation is not ready for confirmation."),
        };
        return Failed(ApplicationFailureKind.InvalidTransition, code, message);
    }

    private async Task<ConfirmationRevalidation> RevalidateAsync(
        PreparationCandidate candidate,
        CancellationToken cancellationToken)
    {
        var environmentResult = await environmentAuthority.GetAsync(
            candidate.EnvironmentId!,
            cancellationToken);
        if (environmentResult.IsFailure)
        {
            return environmentResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? new ConfirmationFactsChanged(ClearEnvironment(candidate))
                : new ConfirmationSourceUnavailable(environmentResult.Failure);
        }

        var environment = environmentResult.Value;
        if (!string.Equals(
                environment.EnvironmentId,
                candidate.EnvironmentId,
                StringComparison.Ordinal))
        {
            return SourceMalformed("environment-authority-mismatched");
        }

        if (!environment.CanBecomeCanonical)
        {
            return new ConfirmationFactsChanged(ClearEnvironment(candidate));
        }

        var approverResult = await principalReader.GetPrincipalAsync(
            environment.BusinessApproverPrincipalId,
            cancellationToken);
        if (approverResult.IsFailure)
        {
            return new ConfirmationSourceUnavailable(
                approverResult.Failure!.Kind == ApplicationFailureKind.NotFound
                    ? new ApplicationFailure(
                        ApplicationFailureKind.DependencyFailure,
                        "business-approver-snapshot-invalid",
                        "The owning client's business-approver snapshot is unavailable.")
                    : approverResult.Failure);
        }

        var approver = approverResult.Value;
        if (!string.Equals(
                approver.Id,
                environment.BusinessApproverPrincipalId,
                StringComparison.Ordinal)
            || approver.Kind != PrincipalKind.BusinessApprover
            || !string.Equals(
                approver.ClientId,
                environment.ClientId,
                StringComparison.Ordinal))
        {
            return SourceMalformed("business-approver-snapshot-invalid");
        }

        var roleResult = await roleAuthority.GetAsync(
            environment.EnvironmentId,
            candidate.RoleId!,
            cancellationToken);
        if (roleResult.IsFailure
            && roleResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return new ConfirmationSourceUnavailable(roleResult.Failure);
        }

        var roleId = roleResult.IsSuccess
            && roleResult.Value.IsCurrentlyAssignable
            && string.Equals(
                roleResult.Value.EnvironmentId,
                environment.EnvironmentId,
                StringComparison.Ordinal)
            && string.Equals(
                roleResult.Value.RoleId,
                candidate.RoleId,
                StringComparison.Ordinal)
                ? roleResult.Value.RoleId
                : null;
        if (roleResult.IsSuccess
            && (!string.Equals(
                    roleResult.Value.EnvironmentId,
                    environment.EnvironmentId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    roleResult.Value.RoleId,
                    candidate.RoleId,
                    StringComparison.Ordinal)))
        {
            return SourceMalformed("environment-role-authority-mismatched");
        }

        var incidentId = candidate.IncidentId;
        if (incidentId is not null)
        {
            var incidentResult = await incidentAuthority.GetAsync(
                incidentId,
                cancellationToken);
            if (incidentResult.IsFailure
                && incidentResult.Failure!.Kind != ApplicationFailureKind.NotFound)
            {
                return new ConfirmationSourceUnavailable(incidentResult.Failure);
            }

            if (incidentResult.IsSuccess)
            {
                var incident = incidentResult.Value;
                if (!string.Equals(
                        incident.IncidentId,
                        incidentId,
                        StringComparison.Ordinal))
                {
                    return SourceMalformed("incident-authority-mismatched");
                }

                if (!incident.IsActive
                    || !string.Equals(
                        incident.EnvironmentId,
                        environment.EnvironmentId,
                        StringComparison.Ordinal))
                {
                    incidentId = null;
                }
            }
            else
            {
                incidentId = null;
            }
        }

        var corrected = new PreparationCandidate(
            environment.ClientId,
            environment.EnvironmentId,
            roleId,
            candidate.Justification,
            incidentId);
        if (corrected != candidate)
        {
            return new ConfirmationFactsChanged(corrected);
        }

        try
        {
            return new ConfirmationFactsValid(
                ValidatedRequestDetails.CreateFromPreparation(
                    corrected.ClientId!,
                    corrected.EnvironmentId!,
                    corrected.RoleId!,
                    corrected.Justification!,
                    corrected.IncidentId));
        }
        catch (ArgumentException)
        {
            return SourceMalformed("request-preparation-invalid-state");
        }
    }

    private async Task<PreparationConfirmationResult?> ValidateRequesterAsync(
        string requesterId,
        CancellationToken cancellationToken)
    {
        var requesterResult = await principalReader.GetPrincipalAsync(
            requesterId,
            cancellationToken);
        if (requesterResult.IsFailure)
        {
            return requesterResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? Failed(
                    ApplicationFailureKind.Unauthenticated,
                    "authenticated-requester-snapshot-missing",
                    "The authenticated requester snapshot is unavailable.")
                : new PreparationConfirmationSourceUnavailable(
                    requesterResult.Failure);
        }

        var requester = requesterResult.Value;
        return string.Equals(requester.Id, requesterId, StringComparison.Ordinal)
            && requester.Kind == PrincipalKind.Requester
                ? null
                : Failed(
                    ApplicationFailureKind.Unauthorized,
                    "authenticated-requester-invalid",
                    "Only an authenticated requester can confirm this preparation.");
    }

    private async Task<PreparationConfirmationResult> PersistCorrectionAsync(
        RequestPreparation predecessor,
        PreparationCandidate correctedCandidate,
        PreparationConfirmationCommand command,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var successor = RequestPreparation.CreateAuthoritativeRevision(
            predecessor,
            correctedCandidate,
            occurredAt,
            command.CorrelationId);
        predecessor.MarkSuperseded(occurredAt, command.CorrelationId);
        store.Add(successor);
        var save = await store.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return new PreparationConfirmationFailed(save.Failure!);
        }

        var status = successor.Lifecycle == PreparationLifecycle.Ready
            ? RevalidatedPreparationStatus.Ready
            : RevalidatedPreparationStatus.Collecting;
        return new PreparationConfirmationRevalidationFailed(
            new PreparationTurnResult(
                new PreparationSnapshot(successor),
                new PreparationResponse(
                    new ConfirmationRevalidationFailed(
                        successor.PreparationId,
                        status))));
    }

    private async Task<PreparationConfirmationResult> RecoverAfterSaveFailureAsync(
        Guid preparationId,
        ApplicationFailure saveFailure,
        CancellationToken cancellationToken)
    {
        if (saveFailure.Kind is not (
                ApplicationFailureKind.ConcurrencyConflict
                or ApplicationFailureKind.DependencyFailure))
        {
            return new PreparationConfirmationFailed(saveFailure);
        }

        var existingRequest = await store.GetRequestByPreparationIdAsync(
            preparationId,
            cancellationToken);
        if (existingRequest.IsSuccess)
        {
            return new PreparationConfirmationSubmitted(
                existingRequest.Value,
                WasAlreadySubmitted: true);
        }

        var reloaded = await store.ReloadAsync(preparationId, cancellationToken);
        if (reloaded.IsFailure)
        {
            return new PreparationConfirmationFailed(saveFailure);
        }

        return reloaded.Value.Lifecycle switch
        {
            PreparationLifecycle.Superseded => Failed(
                ApplicationFailureKind.InvalidTransition,
                "request-preparation-superseded",
                "The request preparation was superseded and cannot be confirmed."),
            PreparationLifecycle.Expired => Failed(
                ApplicationFailureKind.InvalidTransition,
                "request-preparation-expired",
                "The request preparation has expired and cannot be confirmed."),
            _ => new PreparationConfirmationFailed(saveFailure),
        };
    }

    private static PreparationCandidate ClearEnvironment(
        PreparationCandidate candidate) =>
        new(
            clientId: null,
            environmentId: null,
            roleId: null,
            candidate.Justification,
            incidentId: null);

    private static ConfirmationSourceUnavailable SourceMalformed(string code) =>
        new(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                code,
                "Authoritative confirmation data is inconsistent."));

    private static PreparationConfirmationFailed FailedNotFound() =>
        new(
            new ApplicationFailure(
                ApplicationFailureKind.NotFound,
                "request-preparation-not-found",
                "The request preparation was not found for this authenticated conversation."));

    private static PreparationConfirmationFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        new(new ApplicationFailure(kind, code, message));

    private abstract record ConfirmationRevalidation;

    private sealed record ConfirmationFactsValid(ValidatedRequestDetails Details) :
        ConfirmationRevalidation;

    private sealed record ConfirmationFactsChanged(PreparationCandidate Candidate) :
        ConfirmationRevalidation;

    private sealed record ConfirmationSourceUnavailable(ApplicationFailure Failure) :
        ConfirmationRevalidation;
}

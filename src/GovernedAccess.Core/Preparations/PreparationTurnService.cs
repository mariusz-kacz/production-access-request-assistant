using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed class PreparationTurnService
{
    private readonly IClock clock;
    private readonly RequestPreparationReducer reducer;
    private readonly IRequestPreparationStore store;

    public PreparationTurnService(
        IRequestPreparationStore store,
        RequestPreparationReducer reducer,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reducer);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.reducer = reducer;
        this.clock = clock;
    }

    public async Task<ApplicationResult<PreparationTurnContext>> BeginAsync(
        PreparationBinding binding,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        correlationId = MaterialChangeAttribution.NormalizeCorrelationId(correlationId);

        var latestResult = await store.GetLatestAsync(binding, cancellationToken);
        if (latestResult.IsFailure)
        {
            return latestResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? ApplicationResult.Succeeded(
                    new PreparationTurnContext(
                        binding,
                        correlationId,
                        preparation: null))
                : ApplicationResult.Failed<PreparationTurnContext>(
                    latestResult.Failure);
        }

        var preparation = latestResult.Value;
        var observedAt = clock.UtcNow.ToUniversalTime();
        if (preparation.IsExpired(observedAt))
        {
            preparation.MarkExpired(observedAt, correlationId);
            var expirySave = await store.SaveChangesAsync(cancellationToken);
            if (expirySave.IsFailure)
            {
                return ApplicationResult.Failed<PreparationTurnContext>(
                    expirySave.Failure!);
            }
        }

        return ApplicationResult.Succeeded(
            new PreparationTurnContext(
                binding,
                correlationId,
                preparation));
    }

    public async Task<PreparationTurnResult> ApplyAsync(
        PreparationTurnContext turn,
        TurnProposal proposal,
        PreparationTurnAttribution attribution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(attribution);
        cancellationToken.ThrowIfCancellationRequested();

        var preparation = turn.TrackedPreparation
            ?? RequestPreparation.CreateRoot(
                turn.Binding,
                clock.UtcNow,
                turn.CorrelationId);
        if (preparation.Lifecycle is not PreparationLifecycle.Collecting
            and not PreparationLifecycle.Ready)
        {
            return Terminal(turn, preparation);
        }

        var reduction = await reducer.ReduceAsync(
            preparation,
            proposal,
            cancellationToken);
        if (reduction.Outcome is Failed)
        {
            return new PreparationTurnResult(
                turn.Preparation,
                new PreparationResponse(reduction.Outcome));
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (preparation.IsExpired(occurredAt))
        {
            preparation.MarkExpired(occurredAt, turn.CorrelationId);
            var expirySave = await store.SaveChangesAsync(cancellationToken);
            return expirySave.IsFailure
                ? SaveFailed(turn, expirySave.Failure!)
                : Terminal(turn, preparation);
        }

        if (turn.TrackedPreparation is null)
        {
            return await ApplyFirstTurnAsync(
                turn,
                preparation,
                reduction,
                attribution,
                occurredAt,
                cancellationToken);
        }

        return preparation.Lifecycle == PreparationLifecycle.Ready
            ? await ApplyReadyTurnAsync(
                turn,
                preparation,
                reduction,
                attribution,
                occurredAt,
                cancellationToken)
            : await ApplyCollectingTurnAsync(
                turn,
                preparation,
                reduction,
                attribution,
                occurredAt,
                cancellationToken);
    }

    public static PreparationTurnResult Reject(
        PreparationTurnContext turn,
        ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(failure);
        return turn.TrackedPreparation is { Lifecycle: not PreparationLifecycle.Collecting
            and not PreparationLifecycle.Ready } terminal
                ? Terminal(turn, terminal)
                : new PreparationTurnResult(
                    turn.Preparation,
                    new PreparationResponse(new Failed(failure)));
    }

    public async Task<PreparationTurnResult> ResetAsync(
        ResetPreparationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var latestResult = await store.GetLatestAsync(
            command.Binding,
            cancellationToken);
        if (latestResult.IsFailure
            && latestResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return FailureWithoutPreparation(latestResult.Failure);
        }

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var previous = latestResult.IsSuccess ? latestResult.Value : null;
        var previousSnapshot = previous is null
            ? null
            : new PreparationSnapshot(previous);
        if (previous is { Lifecycle: PreparationLifecycle.Collecting
                or PreparationLifecycle.Ready })
        {
            if (previous.IsExpired(occurredAt))
            {
                previous.MarkExpired(occurredAt, command.CorrelationId);
            }
            else
            {
                previous.MarkSuperseded(occurredAt, command.CorrelationId);
            }
        }

        var replacement = RequestPreparation.CreateRoot(
            command.Binding,
            occurredAt,
            command.CorrelationId);
        store.Add(replacement);
        var save = await store.SaveChangesAsync(cancellationToken);
        if (save.IsSuccess)
        {
            return ResetSucceeded(replacement);
        }

        if (IsActiveCreationRace(save.Failure!))
        {
            var winner = await store.GetActiveAsync(
                command.Binding,
                cancellationToken);
            if (winner.IsSuccess && IsCleanResetPreparation(winner.Value))
            {
                return ResetSucceeded(winner.Value);
            }
        }

        return new PreparationTurnResult(
            previousSnapshot,
            new PreparationResponse(new Failed(save.Failure!)));
    }

    private async Task<PreparationTurnResult> ApplyFirstTurnAsync(
        PreparationTurnContext turn,
        RequestPreparation preparation,
        RequestPreparationReduction reduction,
        PreparationTurnAttribution attribution,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (reduction.ChangedFields.Count == 0
            && reduction.ClarificationDisposition
                != ClarificationContextDisposition.Replace)
        {
            return new PreparationTurnResult(
                preparation: null,
                new PreparationResponse(reduction.Outcome));
        }

        ApplyReduction(
            preparation,
            reduction,
            attribution,
            occurredAt,
            turn.CorrelationId);
        store.Add(preparation);
        var save = await store.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return await HandleInitialRaceOrFailureAsync(
                turn,
                save.Failure!,
                cancellationToken);
        }

        return Succeeded(turn, preparation, reduction, becameReady: preparation.Lifecycle == PreparationLifecycle.Ready);
    }

    private async Task<PreparationTurnResult> ApplyCollectingTurnAsync(
        PreparationTurnContext turn,
        RequestPreparation preparation,
        RequestPreparationReduction reduction,
        PreparationTurnAttribution attribution,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var requiresCommit = reduction.ChangedFields.Count > 0
            || reduction.ClarificationDisposition
                != ClarificationContextDisposition.Preserve;
        if (!requiresCommit)
        {
            return Succeeded(
                turn,
                preparation,
                reduction,
                becameReady: false);
        }

        ApplyReduction(
            preparation,
            reduction,
            attribution,
            occurredAt,
            turn.CorrelationId);
        var save = await store.SaveChangesAsync(cancellationToken);
        return save.IsFailure
            ? SaveFailed(turn, save.Failure!)
            : Succeeded(
                turn,
                preparation,
                reduction,
                becameReady: preparation.Lifecycle == PreparationLifecycle.Ready);
    }

    private async Task<PreparationTurnResult> ApplyReadyTurnAsync(
        PreparationTurnContext turn,
        RequestPreparation preparation,
        RequestPreparationReduction reduction,
        PreparationTurnAttribution attribution,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var requiresSuccessor = reduction.ChangedFields.Count > 0
            || reduction.ClarificationDisposition
                == ClarificationContextDisposition.Replace;
        if (!requiresSuccessor)
        {
            return Succeeded(turn, preparation, reduction, becameReady: false);
        }

        var materialAttribution = CreateMaterialAttribution(
            reduction,
            attribution,
            occurredAt,
            turn.CorrelationId);
        var successor = RequestPreparation.CreateRevision(
            preparation,
            reduction.Candidate,
            reduction.ClarificationDisposition
                == ClarificationContextDisposition.Replace
                    ? reduction.Clarification
                    : null,
            materialAttribution,
            occurredAt,
            turn.CorrelationId);
        preparation.MarkSuperseded(occurredAt, turn.CorrelationId);
        store.Add(successor);
        var save = await store.SaveChangesAsync(cancellationToken);
        return save.IsFailure
            ? SaveFailed(turn, save.Failure!)
            : Succeeded(
                turn,
                successor,
                reduction,
                becameReady: successor.Lifecycle == PreparationLifecycle.Ready);
    }

    private static void ApplyReduction(
        RequestPreparation preparation,
        RequestPreparationReduction reduction,
        PreparationTurnAttribution attribution,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        if (reduction.ChangedFields.Count > 0)
        {
            preparation.ApplyCandidateChange(
                reduction.Candidate,
                reduction.ClarificationDisposition
                    == ClarificationContextDisposition.Replace
                        ? reduction.Clarification
                        : null,
                CreateMaterialAttribution(
                    reduction,
                    attribution,
                    occurredAt,
                    correlationId)!,
                occurredAt,
                correlationId);
            return;
        }

        if (reduction.ClarificationDisposition
            == ClarificationContextDisposition.Replace)
        {
            preparation.SetClarification(
                reduction.Clarification!,
                occurredAt,
                correlationId);
        }
        else if (reduction.ClarificationDisposition
            == ClarificationContextDisposition.Clear)
        {
            preparation.ClearClarification(occurredAt, correlationId);
        }
    }

    private static MaterialChangeAttribution? CreateMaterialAttribution(
        RequestPreparationReduction reduction,
        PreparationTurnAttribution attribution,
        DateTimeOffset occurredAt,
        string correlationId) =>
        reduction.ChangedFields.Count == 0
            ? null
            : new MaterialChangeAttribution(
                reduction.ChangedFields,
                attribution.ModelDeployment,
                attribution.ProviderModelVersion,
                attribution.PromptContractVersion,
                attribution.StructuredOutputSchemaVersion,
                occurredAt,
                correlationId);

    private async Task<PreparationTurnResult> HandleInitialRaceOrFailureAsync(
        PreparationTurnContext turn,
        ApplicationFailure failure,
        CancellationToken cancellationToken)
    {
        if (IsActiveCreationRace(failure))
        {
            var winner = await store.GetActiveAsync(
                turn.Binding,
                cancellationToken);
            if (winner.IsSuccess)
            {
                return new PreparationTurnResult(
                    new PreparationSnapshot(winner.Value),
                    new PreparationResponse(new Failed(failure)));
            }
        }

        return SaveFailed(turn, failure);
    }

    private static bool IsActiveCreationRace(ApplicationFailure failure) =>
        failure.Kind == ApplicationFailureKind.ConcurrencyConflict
        && string.Equals(
            failure.Code,
            "request_preparation_active_race",
            StringComparison.Ordinal);

    private static bool IsCleanResetPreparation(
        RequestPreparation preparation) =>
        preparation.Lifecycle == PreparationLifecycle.Collecting
        && preparation.Candidate.IsEmpty
        && preparation.Clarification is null
        && preparation.PredecessorPreparationId is null;

    private static PreparationTurnResult Succeeded(
        PreparationTurnContext turn,
        RequestPreparation preparation,
        RequestPreparationReduction reduction,
        bool becameReady)
    {
        var outcome = becameReady
            ? new ReadyForConfirmation(preparation.PreparationId)
            : reduction.Outcome;
        return new PreparationTurnResult(
            new PreparationSnapshot(preparation),
            new PreparationResponse(outcome));
    }

    private static PreparationTurnResult Terminal(
        PreparationTurnContext turn,
        RequestPreparation preparation) =>
        new(
            new PreparationSnapshot(preparation),
            new PreparationResponse(new TerminalPreparationGuidance()));

    private static PreparationTurnResult SaveFailed(
        PreparationTurnContext turn,
        ApplicationFailure failure) =>
        new(
            turn.Preparation,
            new PreparationResponse(new Failed(failure)));

    private static PreparationTurnResult FailureWithoutPreparation(
        ApplicationFailure failure) =>
        new(
            preparation: null,
            new PreparationResponse(new Failed(failure)));

    private static PreparationTurnResult ResetSucceeded(
        RequestPreparation preparation) =>
        new(
            new PreparationSnapshot(preparation),
            new PreparationResponse(new ResetGuidance()));
}

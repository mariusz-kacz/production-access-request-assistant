using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed partial class RequestPreparationReducer
{
    private readonly PreparationScopeEvaluator scopeEvaluator;
    private readonly PreparationRoleEvaluator roleEvaluator;

    public RequestPreparationReducer(
        IProductionEnvironmentSearchAuthority environmentSearch,
        IProductionEnvironmentAuthority environmentAuthority,
        IEnvironmentRoleAuthority roleAuthority,
        IIncidentAuthority incidentAuthority)
    {
        ArgumentNullException.ThrowIfNull(environmentSearch);
        ArgumentNullException.ThrowIfNull(environmentAuthority);
        ArgumentNullException.ThrowIfNull(roleAuthority);
        ArgumentNullException.ThrowIfNull(incidentAuthority);

        roleEvaluator = new PreparationRoleEvaluator(roleAuthority);
        scopeEvaluator = new PreparationScopeEvaluator(
            environmentSearch,
            environmentAuthority,
            roleEvaluator,
            incidentAuthority);
    }

    public async Task<RequestPreparationReduction> ReduceAsync(
        RequestPreparation preparation,
        TurnProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsStructurallyValid(proposal))
        {
            return Unchanged(preparation, StructuralFailure());
        }

        if (preparation.Lifecycle is not PreparationLifecycle.Collecting
            and not PreparationLifecycle.Ready)
        {
            return Unchanged(
                preparation,
                new Failed(
                    new ApplicationFailure(
                        ApplicationFailureKind.InvalidTransition,
                        "request-preparation-terminal",
                        "The request preparation is no longer active.")));
        }

        return proposal.DialogueAct switch
        {
            DialogueAct.UpdateDraft => await ReducePatchAsync(
                preparation,
                proposal.Patch!,
                cancellationToken),
            DialogueAct.SelectClarification => await ReduceSelectionAsync(
                preparation,
                proposal.ClarificationSelection!,
                cancellationToken),
            DialogueAct.DiscussDraft => Unchanged(
                preparation,
                new DraftDiscussion(proposal.DiscussionTopic!.Value)),
            DialogueAct.RequestSubmission => Unchanged(
                preparation,
                new SubmissionGuidance()),
            DialogueAct.Unrelated => Unchanged(
                preparation,
                new UnrelatedGuidance()),
            DialogueAct.Unclear => Unchanged(
                preparation,
                new UnclearGuidance()),
            _ => Unchanged(preparation, StructuralFailure()),
        };
    }

    private async Task<RequestPreparationReduction> ReduceSelectionAsync(
        RequestPreparation preparation,
        ClarificationSelection selection,
        CancellationToken cancellationToken)
    {
        var mapping = ClarificationSelectionMapper.Map(preparation, selection);
        if (mapping is SelectionMapping.Rejected rejected)
        {
            return RejectedSelection(
                preparation,
                rejected.Field,
                rejected.ClarificationDisposition);
        }

        var accepted = (SelectionMapping.Accepted)mapping;
        var reduction = await ReducePatchAsync(
            preparation,
            accepted.Patch,
            cancellationToken);
        return ConsumeSelectionContext(reduction);
    }

    private static RequestPreparationReduction ConsumeSelectionContext(
        RequestPreparationReduction reduction)
    {
        if (reduction.ClarificationDisposition
            != ClarificationContextDisposition.Preserve)
        {
            return reduction;
        }

        return new RequestPreparationReduction(
            reduction.Candidate,
            ClarificationContextDisposition.Clear,
            clarification: null,
            reduction.OperationResults,
            reduction.ChangedFields,
            reduction.Outcome);
    }

    private static RequestPreparationReduction RejectedSelection(
        RequestPreparation preparation,
        ProposalField field,
        ClarificationContextDisposition clarificationDisposition)
    {
        OperationResult[] operationResults =
        [
            new(field, OperationResultKind.RejectedInvalid),
        ];
        return new RequestPreparationReduction(
            preparation.Candidate,
            clarificationDisposition,
            clarification: null,
            operationResults,
            changedFields: [],
            new DraftUnchanged(operationResults));
    }

    private async Task<RequestPreparationReduction> ReducePatchAsync(
        RequestPreparation preparation,
        DraftPatch patch,
        CancellationToken cancellationToken)
    {
        var current = preparation.Candidate;
        var evaluation = new PatchEvaluation(current);
        var scopeResult = await scopeEvaluator.EvaluateAsync(
            evaluation,
            patch,
            cancellationToken);

        await roleEvaluator.ApplyRequestedAsync(
            evaluation,
            patch.Role,
            scopeResult.RoleEvaluation,
            cancellationToken);
        PreparationJustificationPolicy.ApplyRequested(
            evaluation,
            patch.Justification);

        var candidate = evaluation.ToCandidate();
        var clarification = await DetermineClarificationAsync(
            preparation,
            current,
            candidate,
            evaluation,
            scopeResult.EnvironmentClarification,
            cancellationToken);
        return CompleteReduction(
            preparation,
            candidate,
            clarification,
            evaluation);
    }

    private async Task<ClarificationSeed?> DetermineClarificationAsync(
        RequestPreparation preparation,
        PreparationCandidate current,
        PreparationCandidate candidate,
        PatchEvaluation evaluation,
        ClarificationSeed? environmentClarification,
        CancellationToken cancellationToken)
    {
        if (environmentClarification is not null)
        {
            return environmentClarification;
        }

        var canReplaceExistingContext = preparation.Clarification is null
            || !ReferenceEquals(candidate, current);
        if (!canReplaceExistingContext
            || evaluation.EnvironmentId is null
            || (evaluation.RoleId is not null
                && !evaluation.HasResult(
                    ProposalField.Role,
                    OperationResultKind.RejectedUnavailable)))
        {
            return null;
        }

        return await roleEvaluator.ResolveClarificationAsync(
            evaluation.EnvironmentId,
            evaluation,
            cancellationToken);
    }

    private static RequestPreparationReduction CompleteReduction(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        PatchEvaluation evaluation)
    {
        var changedFields = evaluation.GetChangedFields(candidate);
        var orderedOperationResults = evaluation.GetOperationResults();
        var clarificationDisposition = clarification is not null
            ? ClarificationContextDisposition.Replace
            : changedFields.Length > 0
                ? ClarificationContextDisposition.Clear
                : ClarificationContextDisposition.Preserve;
        var outcome = CreateOutcome(
            preparation,
            candidate,
            clarification,
            changedFields,
            orderedOperationResults);

        return new RequestPreparationReduction(
            candidate,
            clarificationDisposition,
            clarification,
            orderedOperationResults,
            changedFields,
            outcome);
    }

    private static ApplicationOutcome CreateOutcome(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        ProposalField[] changedFields,
        IReadOnlyList<OperationResult> operationResults)
    {
        if (clarification is not null)
        {
            return new ClarificationRequired(
                clarification.Target,
                clarification.OrderedCanonicalIds.Select(
                    identifier => new ClarificationChoice(identifier)));
        }

        if (candidate.IsComplete && preparation.Lifecycle == PreparationLifecycle.Collecting)
        {
            return new ReadyForConfirmation(preparation.PreparationId);
        }

        return changedFields.Length > 0
            ? new DraftUpdated(operationResults)
            : new DraftUnchanged(operationResults);
    }

    private static RequestPreparationReduction Unchanged(
        RequestPreparation preparation,
        ApplicationOutcome outcome) =>
        new(
            preparation.Candidate,
            ClarificationContextDisposition.Preserve,
            clarification: null,
            operationResults: [],
            changedFields: [],
            outcome);

    private static Failed StructuralFailure() =>
        new(
            new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                "request-preparation-proposal-structural-invalid",
                "The structured request proposal is invalid."));
}

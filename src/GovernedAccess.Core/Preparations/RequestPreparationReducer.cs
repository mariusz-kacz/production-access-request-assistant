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
        var contextConsumed = IsClarificationConsumed(
            preparation.Clarification,
            candidate.ChangedFieldsFrom(current));
        var contextInvalidated = IsClarificationInvalidated(
            preparation.Clarification,
            patch,
            evaluation);
        var clarification = await DetermineClarificationAsync(
            preparation,
            candidate,
            evaluation,
            scopeResult.EnvironmentClarification,
            contextConsumed,
            contextInvalidated,
            cancellationToken);
        return CompleteReduction(
            preparation,
            candidate,
            clarification,
            contextInvalidated,
            evaluation);
    }

    private async Task<ClarificationSeed?> DetermineClarificationAsync(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        PatchEvaluation evaluation,
        ClarificationSeed? environmentClarification,
        bool contextConsumed,
        bool contextInvalidated,
        CancellationToken cancellationToken)
    {
        if (environmentClarification is not null)
        {
            return environmentClarification;
        }

        if (preparation.Clarification is not null
            && !contextConsumed
            && !contextInvalidated)
        {
            return null;
        }

        if (evaluation.EnvironmentId is null
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
        bool contextInvalidated,
        PatchEvaluation evaluation)
    {
        var changedFields = evaluation.GetChangedFields(candidate);
        var orderedOperationResults = evaluation.GetOperationResults();
        var clarificationDisposition = clarification is not null
            ? ClarificationContextDisposition.Replace
            : IsClarificationConsumed(
                preparation.Clarification,
                changedFields)
                || contextInvalidated
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
                clarification.Choices);
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

    private static bool IsClarificationConsumed(
        PreparationClarificationContext? clarification,
        IReadOnlySet<ProposalField> changedFields) =>
        clarification?.Target switch
        {
            ClarificationTarget.Environment =>
                changedFields.Contains(ProposalField.Environment),
            ClarificationTarget.Role =>
                changedFields.Contains(ProposalField.Environment)
                || changedFields.Contains(ProposalField.Role),
            _ => false,
        };

    private static bool IsClarificationConsumed(
        PreparationClarificationContext? clarification,
        IReadOnlyList<ProposalField> changedFields) =>
        IsClarificationConsumed(
            clarification,
            changedFields.ToHashSet());

    private static bool IsClarificationInvalidated(
        PreparationClarificationContext? clarification,
        DraftPatch patch,
        PatchEvaluation evaluation)
    {
        if (clarification is null)
        {
            return false;
        }

        var proposedId = clarification.Target switch
        {
            ClarificationTarget.Environment
                when patch.Environment is SetEnvironmentOperation
                {
                    Reference: ExactEnvironmentId exact,
                }
                    && evaluation.IsAuthoritativelyInvalid(ProposalField.Environment) =>
                exact.Id,
            ClarificationTarget.Role
                when patch.Role is SetRoleOperation role
                    && evaluation.IsAuthoritativelyInvalid(ProposalField.Role) =>
                role.RoleId,
            _ => null,
        };

        return proposedId is not null
            && clarification.Choices.Any(choice => string.Equals(
                choice.CanonicalId,
                proposedId,
                StringComparison.Ordinal));
    }
}

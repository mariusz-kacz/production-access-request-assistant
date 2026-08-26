using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Core.Preparations;

public sealed partial class RequestPreparationReducer
{
    private readonly PreparationScopeEvaluator scopeEvaluator;

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

        scopeEvaluator = new PreparationScopeEvaluator(
            environmentSearch,
            environmentAuthority,
            new PreparationRoleEvaluator(roleAuthority),
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
        var scope = await scopeEvaluator.EvaluateAsync(
            current,
            patch,
            cancellationToken);
        var justification = PreparationJustificationPolicy.Evaluate(
            current.Justification,
            patch.Justification);
        var candidate = CombineCandidate(
            current,
            scope.Candidate,
            justification.Justification);
        var changedFields = candidate.ChangedFieldsFrom(current)
            .OrderBy(FieldOrder)
            .ToArray();
        var contextConsumed = IsClarificationConsumed(
            preparation.Clarification,
            changedFields);
        var contextInvalidated = IsClarificationInvalidated(
            preparation.Clarification,
            scope.InvalidClarificationProposal);
        var clarification = await DetermineClarificationAsync(
            preparation,
            candidate,
            scope,
            contextConsumed,
            contextInvalidated,
            cancellationToken);

        return CompleteReduction(
            preparation,
            candidate,
            clarification.Clarification,
            contextInvalidated,
            clarification.ScopeResult,
            justification.Result,
            changedFields);
    }

    private async Task<ClarificationDecision> DetermineClarificationAsync(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        ScopeApplicationResult scope,
        bool contextConsumed,
        bool contextInvalidated,
        CancellationToken cancellationToken)
    {
        if (scope.EnvironmentClarification is not null)
        {
            return new ClarificationDecision(
                scope.EnvironmentClarification,
                scope.Result!);
        }

        if (preparation.Clarification is not null
            && !contextConsumed
            && !contextInvalidated)
        {
            return new ClarificationDecision(
                Clarification: null,
                scope.Result);
        }

        if (!scope.ShouldResolveRoleClarification
            || candidate.EnvironmentId is null)
        {
            return new ClarificationDecision(
                Clarification: null,
                scope.Result);
        }

        var roleClarification = await scopeEvaluator.ResolveRoleClarificationAsync(
            candidate.EnvironmentId,
            cancellationToken);
        if (roleClarification.Clarification is not null)
        {
            return new ClarificationDecision(
                roleClarification.Clarification,
                scope.Result?.Kind == ApplicationGroupResultKind.Applied
                    ? scope.Result
                    : new ApplicationGroupResult(
                        ApplicationGroupResultKind.NeedsClarification));
        }

        return new ClarificationDecision(
            Clarification: null,
            scope.Result?.Kind == ApplicationGroupResultKind.Applied
                ? scope.Result
                : new ApplicationGroupResult(
                    ApplicationGroupResultKind.Rejected,
                    roleClarification.RejectionReason!.Value));
    }

    private static RequestPreparationReduction CompleteReduction(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        bool contextInvalidated,
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult,
        ProposalField[] changedFields)
    {
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
            scopeResult,
            justificationResult);

        return new RequestPreparationReduction(
            candidate,
            clarificationDisposition,
            clarification,
            scopeResult,
            justificationResult,
            changedFields,
            outcome);
    }

    private static ApplicationOutcome CreateOutcome(
        RequestPreparation preparation,
        PreparationCandidate candidate,
        ClarificationSeed? clarification,
        ProposalField[] changedFields,
        ApplicationGroupResult? scopeResult,
        ApplicationGroupResult? justificationResult)
    {
        if (clarification is not null)
        {
            return new ClarificationRequired(
                clarification.Target,
                clarification.Choices,
                scopeResult!,
                justificationResult);
        }

        if (candidate.IsComplete
            && preparation.Lifecycle == PreparationLifecycle.Collecting)
        {
            return new ReadyForConfirmation(preparation.PreparationId);
        }

        return changedFields.Length > 0
            ? new DraftUpdated(scopeResult, justificationResult)
            : new DraftUnchanged(scopeResult, justificationResult);
    }

    private static RequestPreparationReduction Unchanged(
        RequestPreparation preparation,
        ApplicationOutcome outcome) =>
        new(
            preparation.Candidate,
            ClarificationContextDisposition.Preserve,
            clarification: null,
            scopeResult: null,
            justificationResult: null,
            changedFields: [],
            outcome);

    private static Failed StructuralFailure() =>
        new(
            new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                "request-preparation-proposal-structural-invalid",
                "The structured request proposal is invalid."));

    private static PreparationCandidate CombineCandidate(
        PreparationCandidate current,
        PreparationCandidate scopeCandidate,
        string? justification)
    {
        if (string.Equals(
                current.ClientId,
                scopeCandidate.ClientId,
                StringComparison.Ordinal)
            && string.Equals(
                current.EnvironmentId,
                scopeCandidate.EnvironmentId,
                StringComparison.Ordinal)
            && string.Equals(
                current.RoleId,
                scopeCandidate.RoleId,
                StringComparison.Ordinal)
            && string.Equals(
                current.IncidentId,
                scopeCandidate.IncidentId,
                StringComparison.Ordinal)
            && string.Equals(
                current.Justification,
                justification,
                StringComparison.Ordinal))
        {
            return current;
        }

        return new PreparationCandidate(
            scopeCandidate.ClientId,
            scopeCandidate.EnvironmentId,
            scopeCandidate.RoleId,
            justification,
            scopeCandidate.IncidentId);
    }

    private static bool IsClarificationConsumed(
        PreparationClarificationContext? clarification,
        IReadOnlyList<ProposalField> changedFields) =>
        clarification?.Target switch
        {
            ClarificationTarget.Environment =>
                changedFields.Contains(ProposalField.Environment),
            ClarificationTarget.Role =>
                changedFields.Contains(ProposalField.Environment)
                || changedFields.Contains(ProposalField.Role),
            _ => false,
        };

    private static bool IsClarificationInvalidated(
        PreparationClarificationContext? clarification,
        InvalidClarificationProposal? invalidProposal) =>
        clarification is not null
        && invalidProposal is not null
        && clarification.Target == invalidProposal.Target
        && clarification.Choices.Any(choice => string.Equals(
            choice.CanonicalId,
            invalidProposal.CanonicalId,
            StringComparison.Ordinal));

    private static int FieldOrder(ProposalField field) => field switch
    {
        ProposalField.Environment => 0,
        ProposalField.Incident => 1,
        ProposalField.Role => 2,
        ProposalField.Justification => 3,
        _ => int.MaxValue,
    };

    private sealed record ClarificationDecision(
        ClarificationSeed? Clarification,
        ApplicationGroupResult? ScopeResult);
}

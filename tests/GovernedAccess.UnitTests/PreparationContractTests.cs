using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class PreparationContractTests
{
    [Fact]
    public void ProposalAcceptsEveryValidActPayloadCombination()
    {
        var patch = new DraftPatch(
            environment: new SetEnvironmentOperation(
                new ExactEnvironmentId(" PROD-ALPHA-EU ")));
        var update = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.UpdateDraft,
            patch: patch);
        var discuss = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.DiscussDraft,
            discussionTopic: DiscussionTopic.CurrentDraft);
        var submission = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.RequestSubmission);
        var unrelated = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.Unrelated);
        var unclear = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.Unclear);

        Assert.Same(patch, update.Patch);
        Assert.Equal(DiscussionTopic.CurrentDraft, discuss.DiscussionTopic);
        Assert.Null(submission.Patch);
        Assert.Null(unrelated.Patch);
        Assert.Null(unclear.DiscussionTopic);
    }

    [Fact]
    public void ProposalRejectsEveryIncompatibleActPayloadCombination()
    {
        var patch = EnvironmentPatch();
        foreach (var dialogueAct in Enum.GetValues<DialogueAct>())
        {
            for (var payloadMask = 0; payloadMask < 4; payloadMask++)
            {
                var exception = Record.Exception(
                    () => new TurnProposal(
                        TurnProposal.CurrentSchemaVersion,
                        dialogueAct,
                        patch: (payloadMask & 1) == 0 ? null : patch,
                        discussionTopic: (payloadMask & 2) == 0
                            ? null
                            : DiscussionTopic.CurrentDraft));
                var expectedPayloadMask = dialogueAct switch
                {
                    DialogueAct.UpdateDraft => 1,
                    DialogueAct.DiscussDraft => 2,
                    DialogueAct.RequestSubmission
                        or DialogueAct.Unrelated
                        or DialogueAct.Unclear => 0,
                    _ => throw new InvalidOperationException(),
                };

                if (payloadMask == expectedPayloadMask)
                {
                    Assert.Null(exception);
                }
                else
                {
                    Assert.IsAssignableFrom<ArgumentException>(exception);
                }
            }
        }
    }

    [Fact]
    public void ProposalRejectsUnknownVersionsActsAndTopics()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TurnProposal(
                TurnProposal.CurrentSchemaVersion + 1,
                DialogueAct.Unclear));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TurnProposal(
                TurnProposal.CurrentSchemaVersion,
                (DialogueAct)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TurnProposal(
                TurnProposal.CurrentSchemaVersion,
                DialogueAct.DiscussDraft,
                discussionTopic: (DiscussionTopic)int.MaxValue));
    }

    [Fact]
    public void DraftPatchIsSparseAndNonempty()
    {
        Assert.Throws<ArgumentException>(() => new DraftPatch());

        var patch = new DraftPatch(
            environment: new ClearEnvironmentOperation(),
            role: new SetRoleOperation(" ProductionSupport "),
            justification: new SetJustificationOperation(
                new JustificationProposal(" Restore service. ")),
            incident: new ClearIncidentOperation());

        Assert.IsType<ClearEnvironmentOperation>(patch.Environment);
        Assert.Equal("ProductionSupport", Assert.IsType<SetRoleOperation>(patch.Role).RoleId);
        Assert.Equal(
            "Restore service.",
            Assert.IsType<SetJustificationOperation>(patch.Justification).Value.Text);
        Assert.IsType<ClearIncidentOperation>(patch.Incident);

        var roleOnly = new DraftPatch(role: new ClearRoleOperation());
        Assert.Null(roleOnly.Environment);
        Assert.Null(roleOnly.Justification);
        Assert.Null(roleOnly.Incident);
    }

    [Fact]
    public void SetAndClearOperationsNormalizeFieldSpecificPayloads()
    {
        var exact = new SetEnvironmentOperation(
            new ExactEnvironmentId(" PROD-ALPHA-EU "));
        var search = new SetEnvironmentOperation(
            new EnvironmentSearchQuery(" alpha eu primary "));
        var role = new SetRoleOperation(" ProductionReadOnly ");
        var incident = new SetIncidentOperation(" INC-1042 ");

        Assert.Equal("PROD-ALPHA-EU", Assert.IsType<ExactEnvironmentId>(exact.Reference).Id);
        Assert.Equal("alpha eu primary", Assert.IsType<EnvironmentSearchQuery>(search.Reference).Query);
        Assert.Equal("ProductionReadOnly", role.RoleId);
        Assert.Equal("INC-1042", incident.IncidentId);
    }

    [Fact]
    public void ProposalValuesRejectMissingAndStructurallyOutOfBoundsContent()
    {
        Assert.Throws<ArgumentException>(() => new ExactEnvironmentId("   "));
        Assert.Throws<ArgumentException>(() => new EnvironmentSearchQuery("   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EnvironmentSearchQuery(
                new string('q', EnvironmentSearchQuery.MaximumLength + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExactEnvironmentId(
                new string('e', PreparationCandidate.MaximumIdentifierLength + 1)));
        Assert.Throws<ArgumentException>(() => new SetRoleOperation("   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SetRoleOperation(
                new string('r', PreparationCandidate.MaximumIdentifierLength + 1)));
        Assert.Throws<ArgumentException>(() => new SetIncidentOperation("   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SetIncidentOperation(
                new string('i', PreparationCandidate.MaximumIdentifierLength + 1)));
        Assert.Throws<ArgumentException>(
            () => new JustificationProposal("   "));

        var overDomainLimit = new JustificationProposal(
            new string('j', JustificationProposal.MaximumCanonicalLength + 1));
        Assert.Equal(
            JustificationProposal.MaximumCanonicalLength + 1,
            overDomainLimit.Text.Length);
    }

    [Fact]
    public void ApplicationOutcomesCarryTypedSafePayloads()
    {
        var updated = new DraftUpdated(
            new ApplicationGroupResult(ApplicationGroupResultKind.Applied),
            justificationResult: null);
        var clarification = new ClarificationRequired(
            ClarificationTarget.Environment,
            [
                EnvironmentChoice("PROD-ALPHA-EU"),
                EnvironmentChoice("PROD-ALPHA-US"),
            ],
            new ApplicationGroupResult(
                ApplicationGroupResultKind.NeedsClarification),
            justificationResult: null);
        var discussion = new DraftDiscussion(DiscussionTopic.AllowedChanges);
        var readyId = Guid.NewGuid();
        var ready = new ReadyForConfirmation(readyId);
        var successorId = Guid.NewGuid();
        var revalidation = new ConfirmationRevalidationFailed(
            successorId,
            RevalidatedPreparationStatus.Collecting);

        Assert.Equal(ApplicationGroupResultKind.Applied, updated.ScopeResult?.Kind);
        Assert.Null(updated.JustificationResult);
        Assert.Equal(
            ["PROD-ALPHA-EU", "PROD-ALPHA-US"],
            clarification.Choices.Select(choice => choice.CanonicalId));
        Assert.Equal(DiscussionTopic.AllowedChanges, discussion.Topic);
        Assert.Equal(readyId, ready.PreparationId);
        Assert.Equal(successorId, revalidation.SuccessorPreparationId);
        Assert.Equal(RevalidatedPreparationStatus.Collecting, revalidation.SuccessorStatus);
    }

    [Fact]
    public void SoleRoleSelectionNormalizesItsSafeDisplayPayload()
    {
        var selection = new SoleRoleSelection(
            " ROLE-1 ",
            " Production read-only ");

        Assert.Equal("ROLE-1", selection.RoleId);
        Assert.Equal("Production read-only", selection.DisplayName);
    }

    [Fact]
    public void PreparationResponseBindsSoleRoleSelectionToMutationOutcomes()
    {
        var selection = new SoleRoleSelection(
            "ROLE-1",
            "Production read-only");
        var ready = new PreparationResponse(
            new ReadyForConfirmation(Guid.NewGuid()),
            selection);

        Assert.Same(selection, ready.SoleRoleSelection);
        Assert.Throws<ArgumentException>(
            () => new PreparationResponse(new UnclearGuidance(), selection));
    }

    [Fact]
    public void ClarificationOutcomePreservesCompleteBoundedAuthoritativeOrder()
    {
        var maximumChoices = Enumerable.Range(1, ClarificationRequired.MaximumChoiceCount)
            .Select(index => EnvironmentChoice($"PROD-{index}"))
            .ToArray();
        var scopeResult = new ApplicationGroupResult(
            ApplicationGroupResultKind.NeedsClarification);
        var outcome = new ClarificationRequired(
            ClarificationTarget.Environment,
            maximumChoices,
            scopeResult,
            justificationResult: null);

        Assert.Equal(maximumChoices, outcome.Choices);
        Assert.Throws<ArgumentException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                [],
                scopeResult,
                justificationResult: null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                maximumChoices.Append(EnvironmentChoice("PROD-6")),
                scopeResult,
                justificationResult: null));
        Assert.Throws<ArgumentException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                [EnvironmentChoice("PROD-1"), EnvironmentChoice("PROD-1")],
                scopeResult,
                justificationResult: null));
    }

    [Fact]
    public void ApplicationGroupResultContainsOnlySafeStructuredClassification()
    {
        var result = new ApplicationGroupResult(
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.EnvironmentQueryTooBroad);

        Assert.Equal(ApplicationGroupResultKind.Rejected, result.Kind);
        Assert.Equal(
            ApplicationGroupRejectionReason.EnvironmentQueryTooBroad,
            result.RejectionReason);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApplicationGroupResult(
                (ApplicationGroupResultKind)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApplicationGroupResult(
                ApplicationGroupResultKind.Rejected,
                (ApplicationGroupRejectionReason)int.MaxValue));
        Assert.Throws<ArgumentException>(
            () => new ApplicationGroupResult(ApplicationGroupResultKind.Rejected));
        Assert.Throws<ArgumentException>(
            () => new ApplicationGroupResult(
                ApplicationGroupResultKind.Applied,
                ApplicationGroupRejectionReason.Invalid));
    }

    private static DraftPatch EnvironmentPatch() =>
        new(
            environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-ALPHA-EU")));

    private static EnvironmentClarificationChoice EnvironmentChoice(string id) =>
        new(
            id,
            $"{id} display",
            "client-alpha",
            "Client Alpha",
            "EU",
            EnvironmentClassification.Primary);
}

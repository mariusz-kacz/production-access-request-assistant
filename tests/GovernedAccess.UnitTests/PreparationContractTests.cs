using System.Reflection;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class PreparationContractTests
{
    [Fact]
    public void DialogueActsAndTopicsAreClosedToTheSpecification()
    {
        Assert.Equal(
            [
                nameof(DialogueAct.UpdateDraft),
                nameof(DialogueAct.DiscussDraft),
                nameof(DialogueAct.RequestSubmission),
                nameof(DialogueAct.Unrelated),
                nameof(DialogueAct.Unclear),
            ],
            Enum.GetNames<DialogueAct>());
        Assert.Equal(
            [
                nameof(DiscussionTopic.CurrentDraft),
                nameof(DiscussionTopic.MissingInformation),
                nameof(DiscussionTopic.AllowedChanges),
                nameof(DiscussionTopic.ConfirmationProcess),
                nameof(DiscussionTopic.ResetInstructions),
                nameof(DiscussionTopic.Unsupported),
            ],
            Enum.GetNames<DiscussionTopic>());
    }

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
        Assert.Equal(
            [
                "DialogueAct",
                "DiscussionTopic",
                "Patch",
                "SchemaVersion",
            ],
            typeof(TurnProposal)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
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
    public void DraftPatchIsSparseNonemptyAndLimitedToFourMutableFields()
    {
        Assert.Throws<ArgumentException>(() => new DraftPatch());

        var patch = new DraftPatch(
            environment: new ClearEnvironmentOperation(),
            role: new SetRoleOperation(" ProductionSupport "),
            justification: new SetJustificationOperation(
                new JustificationProposal(" Restore service. ")),
            incident: new ClearIncidentOperation());

        Assert.Equal(
            ["Environment", "Incident", "Justification", "Role"],
            typeof(DraftPatch)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
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
    public void SetAndClearOperationsHaveClosedFieldSpecificPayloads()
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
        Assert.Empty(typeof(ClearEnvironmentOperation).GetProperties());
        Assert.Empty(typeof(ClearRoleOperation).GetProperties());
        Assert.Empty(typeof(ClearJustificationOperation).GetProperties());
        Assert.Empty(typeof(ClearIncidentOperation).GetProperties());
    }

    [Fact]
    public void ProposalValuesRejectMissingAndStructurallyOutOfBoundsContent()
    {
        Assert.Throws<ArgumentException>(() => new ExactEnvironmentId("   "));
        Assert.Throws<ArgumentException>(() => new EnvironmentSearchQuery("   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EnvironmentSearchQuery(
                new string('q', EnvironmentSearchQuery.MaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => new SetRoleOperation("   "));
        Assert.Throws<ArgumentException>(() => new SetIncidentOperation("   "));
        Assert.Throws<ArgumentException>(
            () => new JustificationProposal("   "));

        var overDomainLimit = new JustificationProposal(
            new string('j', JustificationProposal.MaximumCanonicalLength + 1));
        Assert.Equal(
            JustificationProposal.MaximumCanonicalLength + 1,
            overDomainLimit.Text.Length);
    }

    [Fact]
    public void StructuralFailuresAndOperationResultsAreClosed()
    {
        Assert.Equal(
            [
                nameof(ProposalStructuralFailure.UnknownDialogueAct),
                nameof(ProposalStructuralFailure.InvalidActPayloadCombination),
                nameof(ProposalStructuralFailure.UnknownProperty),
                nameof(ProposalStructuralFailure.UnknownField),
                nameof(ProposalStructuralFailure.UnknownOperation),
                nameof(ProposalStructuralFailure.UnknownReferenceForm),
                nameof(ProposalStructuralFailure.UnknownDiscussionTopic),
                nameof(ProposalStructuralFailure.MissingRequiredValue),
                nameof(ProposalStructuralFailure.ForbiddenValue),
                nameof(ProposalStructuralFailure.ValueOutOfBounds),
                nameof(ProposalStructuralFailure.UntranslatableProviderOutput),
            ],
            Enum.GetNames<ProposalStructuralFailure>());
        Assert.Equal(
            [
                nameof(ProposalField.Environment),
                nameof(ProposalField.Incident),
                nameof(ProposalField.Role),
                nameof(ProposalField.Justification),
            ],
            Enum.GetNames<ProposalField>());
        Assert.Equal(
            [
                nameof(OperationResultKind.Applied),
                nameof(OperationResultKind.NoOpValueEqual),
                nameof(OperationResultKind.RejectedInvalid),
                nameof(OperationResultKind.RejectedUnavailable),
                nameof(OperationResultKind.RejectedConflict),
                nameof(OperationResultKind.RejectedDependency),
                nameof(OperationResultKind.NeedsClarification),
            ],
            Enum.GetNames<OperationResultKind>());
    }

    [Fact]
    public void ApplicationOutcomesAreClosedTypedVariantsWithSafePayloads()
    {
        Assert.Equal(
            [
                nameof(ClarificationRequired),
                nameof(ConfirmationRevalidationFailed),
                nameof(ConfirmationSourceUnavailable),
                nameof(DraftDiscussion),
                nameof(DraftUnchanged),
                nameof(DraftUpdated),
                nameof(Failed),
                nameof(ReadyForConfirmation),
                nameof(ResetGuidance),
                nameof(SubmissionGuidance),
                nameof(TerminalPreparationGuidance),
                nameof(UnclearGuidance),
                nameof(UnrelatedGuidance),
            ],
            typeof(ApplicationOutcome).Assembly
                .GetTypes()
                .Where(type => type.BaseType == typeof(ApplicationOutcome))
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));

        var updated = new DraftUpdated(
            [new OperationResult(ProposalField.Environment, OperationResultKind.Applied)]);
        var clarification = new ClarificationRequired(
            ClarificationTarget.Environment,
            [
                EnvironmentChoice("PROD-ALPHA-EU"),
                EnvironmentChoice("PROD-ALPHA-US"),
            ]);
        var discussion = new DraftDiscussion(DiscussionTopic.AllowedChanges);
        var readyId = Guid.NewGuid();
        var ready = new ReadyForConfirmation(readyId);
        var successorId = Guid.NewGuid();
        var revalidation = new ConfirmationRevalidationFailed(
            successorId,
            RevalidatedPreparationStatus.Collecting);

        Assert.Single(updated.OperationResults);
        Assert.Equal(
            ["PROD-ALPHA-EU", "PROD-ALPHA-US"],
            clarification.Choices.Select(choice => choice.CanonicalId));
        Assert.Equal(DiscussionTopic.AllowedChanges, discussion.Topic);
        Assert.Equal(readyId, ready.PreparationId);
        Assert.Equal(successorId, revalidation.SuccessorPreparationId);
        Assert.Equal(RevalidatedPreparationStatus.Collecting, revalidation.SuccessorStatus);
    }

    [Fact]
    public void ClarificationOutcomePreservesCompleteBoundedAuthoritativeOrder()
    {
        var maximumChoices = Enumerable.Range(1, ClarificationRequired.MaximumChoiceCount)
            .Select(index => EnvironmentChoice($"PROD-{index}"))
            .ToArray();
        var outcome = new ClarificationRequired(
            ClarificationTarget.Environment,
            maximumChoices);

        Assert.Equal(maximumChoices, outcome.Choices);
        Assert.Throws<ArgumentException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                maximumChoices.Append(EnvironmentChoice("PROD-6"))));
        Assert.Throws<ArgumentException>(
            () => new ClarificationRequired(
                ClarificationTarget.Environment,
                [EnvironmentChoice("PROD-1"), EnvironmentChoice("PROD-1")]));
    }

    [Fact]
    public void DurablePreparationStateUsesOneOptimisticConcurrencyToken()
    {
        Assert.Equal(
            [
                "Binding",
                "Candidate",
                "Clarification",
                "ConcurrencyVersion",
                "CorrelationId",
                "CreatedAt",
                "Lifecycle",
                "MaterialChangeAttributions",
                "PredecessorPreparationId",
                "PreparationId",
                "ReadyAt",
                "ReadyDeadline",
                "TerminalAt",
                "UpdatedAt",
            ],
            typeof(RequestPreparationPersistenceState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OperationResultContainsOnlySafeStructuredClassification()
    {
        var result = new OperationResult(
            ProposalField.Environment,
            OperationResultKind.NeedsClarification);

        Assert.Equal(ProposalField.Environment, result.Field);
        Assert.Equal(OperationResultKind.NeedsClarification, result.Kind);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OperationResult(
                (ProposalField)int.MaxValue,
                OperationResultKind.Applied));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OperationResult(
                ProposalField.Role,
                (OperationResultKind)int.MaxValue));
    }

    [Fact]
    public void TargetContractsAreProviderNeutralAndIndependentOfDeliveredProposals()
    {
        var contractTypes = typeof(TurnProposal).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(TurnProposal).Namespace)
            .ToArray();
        var forbiddenTerms = new[]
        {
            "Azure",
            "EntityFramework",
            "Json",
            "Mcp",
            "Microsoft.Agents",
            "RequestCandidate",
            "RequestIntake",
            "RequestPreparationProposal",
            "Teams",
        };

        Assert.NotEmpty(contractTypes);
        Assert.All(
            contractTypes,
            type => Assert.DoesNotContain(
                forbiddenTerms,
                term => type.FullName!.Contains(term, StringComparison.OrdinalIgnoreCase)));

        var forbiddenMemberTerms = new[]
        {
            "Approver",
            "Approval",
            "Audit",
            "Client",
            "Duration",
            "Grant",
            "Json",
            "Model",
            "Mcp",
            "Provision",
            "Raw",
            "RequestId",
            "Retry",
            "Teams",
        };
        var publicContractMembers = contractTypes
            .SelectMany(
                type => type.GetMembers(
                    BindingFlags.DeclaredOnly
                    | BindingFlags.Instance
                    | BindingFlags.Public))
            .ToArray();

        Assert.DoesNotContain(
            publicContractMembers,
            member => forbiddenMemberTerms.Any(
                term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            publicContractMembers.SelectMany(GetExposedTypes),
            exposedType => forbiddenTerms.Any(
                term => exposedType.FullName?.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase) == true));
    }

    private static IEnumerable<Type> GetExposedTypes(MemberInfo member) =>
        member switch
        {
            ConstructorInfo constructor => constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
            MethodInfo method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };

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

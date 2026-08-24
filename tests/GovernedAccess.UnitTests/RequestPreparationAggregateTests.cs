using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.UnitTests;

public sealed class RequestPreparationAggregateTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RootCreationGeneratesAnImmutableRandomUuidVersion4AndEmptyCollectingState()
    {
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            CreatedAt,
            " correlation-root ");

        Assert.Equal(4, GetGuidVersion(preparation.PreparationId));
        Assert.Null(preparation.PredecessorPreparationId);
        Assert.Equal(PreparationLifecycle.Collecting, preparation.Lifecycle);
        Assert.True(preparation.Candidate.IsEmpty);
        Assert.Equal(0, preparation.CandidateVersion);
        Assert.Equal(1, preparation.ConcurrencyVersion);
        Assert.Equal(0, preparation.InterpretedTurnCount);
        Assert.Equal(CreatedAt, preparation.CreatedAt);
        Assert.Equal(CreatedAt, preparation.UpdatedAt);
        Assert.Null(preparation.ReadyAt);
        Assert.Null(preparation.ReadyDeadline);
        Assert.Null(preparation.TerminalAt);
        Assert.Null(preparation.Clarification);
        Assert.Empty(preparation.MaterialChangeAttributions);
        Assert.Equal("correlation-root", preparation.CorrelationId);
    }

    [Fact]
    public void CandidateNormalizesSafeFieldsAndRejectsIncoherentOrOversizedState()
    {
        var candidate = new PreparationCandidate(
            " client-alpha ",
            " PROD-ALPHA-EU ",
            " ProductionReadOnly ",
            " investigate the active incident ",
            " INC-1042 ");

        Assert.Equal("client-alpha", candidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", candidate.EnvironmentId);
        Assert.Equal("ProductionReadOnly", candidate.RoleId);
        Assert.Equal("investigate the active incident", candidate.Justification);
        Assert.Equal("INC-1042", candidate.IncidentId);
        Assert.True(candidate.IsComplete);

        Assert.Throws<ArgumentException>(
            () => new PreparationCandidate(
                "client-alpha",
                environmentId: null,
                roleId: null,
                justification: null,
                incidentId: null));
        Assert.Throws<ArgumentException>(
            () => new PreparationCandidate(
                clientId: null,
                environmentId: null,
                "ProductionReadOnly",
                justification: null,
                incidentId: null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                roleId: null,
                new string('j', PreparationCandidate.MaximumJustificationLength + 1),
                incidentId: null));
    }

    [Fact]
    public void CompleteInitialCandidateBecomesReadyWithThirtyMinuteDeadline()
    {
        var attribution = Attribution(ProposalField.Environment, ProposalField.Role, ProposalField.Justification);

        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            CompleteCandidate(),
            clarification: null,
            attribution,
            CreatedAt,
            "ready-root");

        Assert.Equal(PreparationLifecycle.Ready, preparation.Lifecycle);
        Assert.Equal(1, preparation.CandidateVersion);
        Assert.Equal(CreatedAt, preparation.ReadyAt);
        Assert.Equal(CreatedAt.AddMinutes(30), preparation.ReadyDeadline);
        Assert.Null(preparation.Clarification);
        Assert.Equal([attribution], preparation.MaterialChangeAttributions);
    }

    [Fact]
    public void MaterialCommitIncrementsCandidateVersionOnceAndBindsNewContextToPostCommitVersion()
    {
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            new PreparationCandidate(
                clientId: null,
                environmentId: null,
                roleId: null,
                justification: "Investigate the incident",
                incidentId: null),
            clarification: null,
            Attribution(ProposalField.Justification),
            CreatedAt,
            "initial");
        var concurrencyBefore = preparation.ConcurrencyVersion;

        preparation.ApplyCandidateChange(
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                roleId: null,
                "Investigate the incident",
                incidentId: null),
            new ClarificationSeed(
                ClarificationTarget.Role,
                ["ProductionReadOnly", "ProductionSupport"]),
            Attribution(ProposalField.Environment),
            CreatedAt.AddMinutes(1),
            "environment-and-role-context");

        Assert.Equal(2, preparation.CandidateVersion);
        Assert.Equal(concurrencyBefore + 1, preparation.ConcurrencyVersion);
        Assert.Equal(PreparationLifecycle.Collecting, preparation.Lifecycle);
        var clarification = Assert.IsType<PreparationClarificationContext>(
            preparation.Clarification);
        Assert.Equal(preparation.PreparationId, clarification.PreparationId);
        Assert.Equal(2, clarification.CandidateVersion);
        Assert.Equal(ClarificationTarget.Role, clarification.Target);
        Assert.Equal(
            ["ProductionReadOnly", "ProductionSupport"],
            clarification.OrderedCanonicalIds);
    }

    [Fact]
    public void ClarificationOnlyCommitPreservesCandidateVersionButChangesConcurrencyVersion()
    {
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            CreatedAt,
            "root");

        preparation.SetClarification(
            new ClarificationSeed(
                ClarificationTarget.Environment,
                ["PROD-ALPHA-EU", "PROD-BETA-UK"]),
            CreatedAt.AddMinutes(1),
            "clarification");

        Assert.Equal(0, preparation.CandidateVersion);
        Assert.Equal(2, preparation.ConcurrencyVersion);
        Assert.Equal(0, preparation.Clarification?.CandidateVersion);
        Assert.Equal(CreatedAt.AddMinutes(1), preparation.UpdatedAt);
    }

    [Fact]
    public void ClearingContextMakesACompleteCollectingPreparationReady()
    {
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            CompleteCandidate(),
            new ClarificationSeed(
                ClarificationTarget.Role,
                ["ProductionReadOnly", "ProductionSupport"]),
            Attribution(ProposalField.Environment, ProposalField.Role, ProposalField.Justification),
            CreatedAt,
            "root-with-context");

        preparation.ClearClarification(
            CreatedAt.AddMinutes(2),
            "context-consumed");

        Assert.Equal(PreparationLifecycle.Ready, preparation.Lifecycle);
        Assert.Equal(1, preparation.CandidateVersion);
        Assert.Null(preparation.Clarification);
        Assert.Equal(CreatedAt.AddMinutes(2), preparation.ReadyAt);
        Assert.Equal(CreatedAt.AddMinutes(32), preparation.ReadyDeadline);
    }

    [Fact]
    public void MaterialChangeRequiresExactSafeFieldAttribution()
    {
        var preparation = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");
        var candidate = new PreparationCandidate(
            clientId: null,
            environmentId: null,
            roleId: null,
            justification: "Investigate the incident",
            incidentId: null);

        Assert.Throws<ArgumentNullException>(
            () => preparation.ApplyCandidateChange(
                candidate,
                clarification: null,
                attribution: null!,
                CreatedAt.AddMinutes(1),
                "missing-attribution"));
        Assert.Throws<ArgumentException>(
            () => preparation.ApplyCandidateChange(
                candidate,
                clarification: null,
                Attribution(ProposalField.Environment),
                CreatedAt.AddMinutes(1),
                "wrong-attribution"));

        Assert.Equal(0, preparation.CandidateVersion);
        Assert.True(preparation.Candidate.IsEmpty);
    }

    [Fact]
    public void ReadyRevisionUsesDistinctSuccessorAndMandatoryPredecessor()
    {
        var ready = ReadyRoot();
        var successor = RequestPreparation.CreateRevision(
            ready,
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionSupport",
                "Investigate the active incident",
                incidentId: null),
            clarification: null,
            Attribution(ProposalField.Role),
            CreatedAt.AddMinutes(5),
            "revision");

        Assert.NotEqual(ready.PreparationId, successor.PreparationId);
        Assert.Equal(4, GetGuidVersion(successor.PreparationId));
        Assert.Equal(ready.PreparationId, successor.PredecessorPreparationId);
        Assert.Equal(PreparationLifecycle.Ready, successor.Lifecycle);
        Assert.Equal(1, successor.CandidateVersion);
        Assert.Equal(CreatedAt.AddMinutes(35), successor.ReadyDeadline);
        Assert.Equal(PreparationLifecycle.Ready, ready.Lifecycle);

        ready.MarkSuperseded(CreatedAt.AddMinutes(5), "revision-superseded");

        Assert.Equal(PreparationLifecycle.Superseded, ready.Lifecycle);
        Assert.True(ready.Candidate.IsEmpty);
    }

    [Fact]
    public void ReadyClarificationRevisionCopiesCandidateIntoCollectingSuccessor()
    {
        var ready = ReadyRoot();

        var successor = RequestPreparation.CreateRevision(
            ready,
            ready.Candidate,
            new ClarificationSeed(
                ClarificationTarget.Environment,
                ["PROD-ALPHA-EU", "PROD-BETA-UK"]),
            attribution: null,
            CreatedAt.AddMinutes(4),
            "revision-clarification");

        Assert.Equal(PreparationLifecycle.Collecting, successor.Lifecycle);
        Assert.Equal(ready.PreparationId, successor.PredecessorPreparationId);
        Assert.Equal(ready.Candidate, successor.Candidate);
        Assert.Equal(1, successor.CandidateVersion);
        Assert.Equal(1, successor.Clarification?.CandidateVersion);
        Assert.Empty(successor.MaterialChangeAttributions);
    }

    [Fact]
    public void ReadyScopeAndDeadlineRemainImmutableAcrossNonMaterialMetadataUpdates()
    {
        var ready = ReadyRoot();
        var candidate = ready.Candidate;
        var candidateVersion = ready.CandidateVersion;
        var deadline = ready.ReadyDeadline;

        ready.RecordInterpretedTurn(CreatedAt.AddMinutes(1), "discussion");

        Assert.Same(candidate, ready.Candidate);
        Assert.Equal(candidateVersion, ready.CandidateVersion);
        Assert.Equal(deadline, ready.ReadyDeadline);
        Assert.Throws<InvalidOperationException>(
            () => ready.ApplyCandidateChange(
                CompleteCandidate(),
                clarification: null,
                Attribution(ProposalField.Role),
                CreatedAt.AddMinutes(2),
                "forbidden"));
        Assert.Throws<InvalidOperationException>(
            () => ready.SetClarification(
                new ClarificationSeed(
                    ClarificationTarget.Role,
                    ["ProductionReadOnly"]),
                CreatedAt.AddMinutes(2),
                "forbidden"));
    }

    [Fact]
    public void InterpretedTurnBudgetIsPermanentAtFiftyTurnsPerPreparation()
    {
        var preparation = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");

        for (var index = 1; index <= RequestPreparation.MaximumInterpretedTurns; index++)
        {
            preparation.RecordInterpretedTurn(
                CreatedAt.AddSeconds(index),
                $"turn-{index}");
        }

        Assert.Equal(50, preparation.InterpretedTurnCount);
        Assert.False(preparation.CanInterpretTurn);
        Assert.Throws<InvalidOperationException>(
            () => preparation.RecordInterpretedTurn(
                CreatedAt.AddMinutes(2),
                "turn-exhausted"));
        Assert.Equal(50, preparation.InterpretedTurnCount);
    }

    [Fact]
    public void ClarificationIsBoundedOrderedAndUnique()
    {
        var fiveChoices = Enumerable.Range(1, RequestPreparation.MaximumClarificationChoices)
            .Select(index => $"PROD-{index}")
            .ToArray();
        var seed = new ClarificationSeed(
            ClarificationTarget.Environment,
            fiveChoices);

        Assert.Equal(fiveChoices, seed.OrderedCanonicalIds);
        Assert.Throws<ArgumentException>(
            () => new ClarificationSeed(
                ClarificationTarget.Environment,
                ["PROD-1", "PROD-1"]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ClarificationSeed(
                ClarificationTarget.Environment,
                fiveChoices.Append("PROD-6")));
        Assert.Throws<ArgumentException>(
            () => new ClarificationSeed(
                ClarificationTarget.Environment,
                []));
    }

    [Fact]
    public void AttributionIsBoundedToSafeCategoriesAndVersionMetadata()
    {
        var attribution = new MaterialChangeAttribution(
            [ProposalField.Environment, ProposalField.Role],
            " model-deployment ",
            " provider-version ",
            " prompt-v1 ",
            " schema-v1 ",
            CreatedAt,
            " correlation ");

        Assert.Equal([ProposalField.Environment, ProposalField.Role], attribution.Fields);
        Assert.Equal("model-deployment", attribution.ModelDeployment);
        Assert.Equal("provider-version", attribution.ProviderModelVersion);
        Assert.Equal("prompt-v1", attribution.PromptContractVersion);
        Assert.Equal("schema-v1", attribution.StructuredOutputSchemaVersion);
        Assert.Equal("correlation", attribution.CorrelationId);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MaterialChangeAttribution(
                [ProposalField.Environment],
                new string('m', MaterialChangeAttribution.MaximumMetadataLength + 1),
                providerModelVersion: null,
                "prompt-v1",
                "schema-v1",
                CreatedAt,
                "correlation"));
        Assert.Throws<ArgumentException>(
            () => new MaterialChangeAttribution(
                [ProposalField.Environment, ProposalField.Environment],
                "model",
                providerModelVersion: null,
                "prompt-v1",
                "schema-v1",
                CreatedAt,
                "correlation"));
    }

    [Theory]
    [InlineData(PreparationLifecycle.Submitted)]
    [InlineData(PreparationLifecycle.Superseded)]
    [InlineData(PreparationLifecycle.Expired)]
    public void TerminalTransitionsRetainTombstoneEvidenceAndClearCandidateAndContext(
        PreparationLifecycle terminalLifecycle)
    {
        var preparation = terminalLifecycle == PreparationLifecycle.Superseded
            ? RequestPreparation.CreateRoot(
                Binding(),
                CompleteCandidate(),
                new ClarificationSeed(
                    ClarificationTarget.Role,
                    ["ProductionReadOnly", "ProductionSupport"]),
                Attribution(ProposalField.Environment, ProposalField.Role, ProposalField.Justification),
                CreatedAt,
                "collecting")
            : ReadyRoot();
        var preparationId = preparation.PreparationId;
        var binding = preparation.Binding;
        var candidateVersion = preparation.CandidateVersion;
        var occurredAt = terminalLifecycle == PreparationLifecycle.Expired
            ? Assert.IsType<DateTimeOffset>(preparation.ReadyDeadline)
            : CreatedAt.AddMinutes(1);

        switch (terminalLifecycle)
        {
            case PreparationLifecycle.Submitted:
                preparation.MarkSubmitted(occurredAt, "submitted");
                break;
            case PreparationLifecycle.Superseded:
                preparation.MarkSuperseded(occurredAt, "superseded");
                break;
            case PreparationLifecycle.Expired:
                preparation.MarkExpired(occurredAt, "expired");
                break;
            default:
                throw new InvalidOperationException();
        }

        Assert.Equal(terminalLifecycle, preparation.Lifecycle);
        Assert.Equal(preparationId, preparation.PreparationId);
        Assert.Same(binding, preparation.Binding);
        Assert.Equal(candidateVersion, preparation.CandidateVersion);
        Assert.True(preparation.Candidate.IsEmpty);
        Assert.Null(preparation.Clarification);
        Assert.Equal(occurredAt, preparation.TerminalAt);
        Assert.NotEmpty(preparation.MaterialChangeAttributions);
        Assert.Throws<InvalidOperationException>(
            () => preparation.RecordInterpretedTurn(
                occurredAt.AddSeconds(1),
                "terminal-turn"));
    }

    [Fact]
    public void ReadyExpiresAtTheExactDeadlineAndCannotSubmitAtThatInstant()
    {
        var ready = ReadyRoot();
        var deadline = Assert.IsType<DateTimeOffset>(ready.ReadyDeadline);

        Assert.False(ready.IsExpired(deadline.AddTicks(-1)));
        Assert.True(ready.IsExpired(deadline));
        Assert.Throws<InvalidOperationException>(
            () => ready.MarkSubmitted(deadline, "late-submit"));

        ready.MarkExpired(deadline, "expired");

        Assert.Equal(PreparationLifecycle.Expired, ready.Lifecycle);
    }

    private static RequestPreparation ReadyRoot() =>
        RequestPreparation.CreateRoot(
            Binding(),
            CompleteCandidate(),
            clarification: null,
            Attribution(ProposalField.Environment, ProposalField.Role, ProposalField.Justification),
            CreatedAt,
            "ready");

    private static PreparationBinding Binding() =>
        new(
            " msteams ",
            " tenant-001 ",
            " actor-001 ",
            " conversation-001 ",
            " requester ");

    private static PreparationCandidate CompleteCandidate() =>
        new(
            "client-alpha",
            "PROD-ALPHA-EU",
            "ProductionReadOnly",
            "Investigate the active incident",
            incidentId: null);

    private static MaterialChangeAttribution Attribution(
        params ProposalField[] fields) =>
        new(
            fields,
            "model-deployment",
            "provider-version",
            "prompt-v1",
            "schema-v1",
            CreatedAt,
            "correlation");

    private static int GetGuidVersion(Guid value) =>
        (value.ToByteArray()[7] >> 4) & 0x0f;
}

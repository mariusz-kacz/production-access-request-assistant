using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using System.Reflection;

namespace GovernedAccess.UnitTests;

public sealed class RequestPreparationReducerTests : RequestPreparationReducerTestBase
{
    [Fact]
    public async Task ExactEnvironmentUsesOnlyExactReloadAndDerivesCanonicalClient()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        var preparation = EmptyPreparation();

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("client-beta", result.Candidate.ClientId);
        Assert.Equal(0, authority.SearchCallCount);
        Assert.Equal(["PROD-BETA-UK"], authority.EnvironmentGetCalls);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
        AssertSnapshotUnchanged(preparation, PreparationCandidate.Empty);
    }

    [Fact]
    public async Task InvalidEnvironmentRejectsDependentRoleButAppliesIndependentJustification()
    {
        var authority = new FakePreparationAuthority();
        var preparation = EmptyPreparation();

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("UNKNOWN")),
                role: new SetRoleOperation("ProductionSupport"),
                justification: Justification(" Restore customer service. ")),
            CancellationToken.None);

        Assert.Null(result.Candidate.EnvironmentId);
        Assert.Null(result.Candidate.RoleId);
        Assert.Equal("Restore customer service.", result.Candidate.Justification);
        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedUnavailable);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedDependency);
        AssertResult(result, ProposalField.Justification, OperationResultKind.Applied);
        Assert.Equal([ProposalField.Justification], result.ChangedFields);
        AssertSnapshotUnchanged(preparation, PreparationCandidate.Empty);
    }

    [Theory]
    [InlineData(0, EnvironmentSearchResultKind.NoMatches, OperationResultKind.RejectedUnavailable)]
    [InlineData(6, EnvironmentSearchResultKind.NarrowQuery, OperationResultKind.RejectedInvalid)]
    [InlineData(21, EnvironmentSearchResultKind.TooBroad, OperationResultKind.RejectedInvalid)]
    public async Task NonUniqueUnrenderableSearchResultsRejectWithoutMutation(
        int matchCount,
        EnvironmentSearchResultKind expectedKind,
        OperationResultKind expectedOperationResult)
    {
        var authority = new FakePreparationAuthority
        {
            SearchResult = SearchResult(matchCount),
        };
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate errors.");
        var preparation = Preparation(existing);

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new EnvironmentSearchQuery("alpha production"))),
            CancellationToken.None);

        Assert.Equal(expectedKind, authority.SearchResult.Kind);
        Assert.Same(existing, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Environment, expectedOperationResult);
        Assert.Empty(result.ChangedFields);
        AssertSnapshotUnchanged(preparation, existing);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task AmbiguousEnvironmentPreservesCanonicalScopeAndCreatesCompleteOrderedContext(
        int matchCount)
    {
        var authority = new FakePreparationAuthority
        {
            SearchResult = SearchResult(matchCount),
        };
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate errors.");
        var preparation = Preparation(existing);

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new EnvironmentSearchQuery("production"))),
            CancellationToken.None);

        Assert.Same(existing, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Replace, result.ClarificationDisposition);
        Assert.Equal(ClarificationTarget.Environment, result.Clarification?.Target);
        Assert.Equal(
            Enumerable.Range(1, matchCount).Select(index => $"PROD-{index:D2}"),
            result.Clarification?.OrderedCanonicalIds);
        Assert.IsType<ClarificationRequired>(result.Outcome);
        AssertResult(result, ProposalField.Environment, OperationResultKind.NeedsClarification);
        AssertSnapshotUnchanged(preparation, existing);
    }

    [Fact]
    public async Task UniqueSearchResultIsExactReloadedBeforeItBecomesCanonical()
    {
        var authority = new FakePreparationAuthority
        {
            SearchResult = SearchResult(1),
        };
        authority.Environments["PROD-01"] = Environment(
            "PROD-01",
            "client-authoritative");

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(environment: new SetEnvironmentOperation(
                new EnvironmentSearchQuery("unique"))),
            CancellationToken.None);

        Assert.Equal(1, authority.SearchCallCount);
        Assert.Equal(["PROD-01"], authority.EnvironmentGetCalls);
        Assert.Equal("PROD-01", result.Candidate.EnvironmentId);
        Assert.Equal("client-authoritative", result.Candidate.ClientId);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
    }

    [Fact]
    public async Task EnvironmentClarificationTakesPrecedenceAndRejectsDependentRole()
    {
        var authority = new FakePreparationAuthority
        {
            SearchResult = SearchResult(2),
        };

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(
                environment: new SetEnvironmentOperation(
                    new EnvironmentSearchQuery("ambiguous")),
                role: new SetRoleOperation("ProductionSupport")),
            CancellationToken.None);

        Assert.Equal(ClarificationTarget.Environment, result.Clarification?.Target);
        AssertResult(result, ProposalField.Environment, OperationResultKind.NeedsClarification);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedDependency);
        Assert.Equal(0, authority.RoleGetCallCount);
        Assert.Equal(0, authority.RoleListCallCount);
    }

    [Fact]
    public async Task EnvironmentSourceFailureDoesNotDiscardIndependentJustification()
    {
        var authority = new FakePreparationAuthority
        {
            EnvironmentFailure = Failure(
                ApplicationFailureKind.DependencyUnavailable,
                "environment-source-unavailable"),
        };

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-ALPHA-EU")),
                justification: Justification("Restore service.")),
            CancellationToken.None);

        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedUnavailable);
        AssertResult(result, ProposalField.Justification, OperationResultKind.Applied);
        Assert.Equal("Restore service.", result.Candidate.Justification);
        Assert.Equal([ProposalField.Justification], result.ChangedFields);
    }

    [Fact]
    public async Task CompatibleExplicitEnvironmentAndIncidentApplyAsOneScopeGroup()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Incidents["INC-2000"] = Incident(
            "INC-2000",
            "PROD-BETA-UK");

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-BETA-UK")),
                incident: new SetIncidentOperation("INC-2000")),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("client-beta", result.Candidate.ClientId);
        Assert.Equal("INC-2000", result.Candidate.IncidentId);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Incident, OperationResultKind.Applied);
    }

    [Fact]
    public async Task ConflictingExplicitScopeRejectsBothAndDependentRoleButAppliesJustification()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Incidents["INC-ALPHA"] = Incident(
            "INC-ALPHA",
            "PROD-ALPHA-EU");
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Old reason.",
            incidentId: "INC-OLD");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-BETA-UK")),
                role: new SetRoleOperation("ProductionSupport"),
                justification: Justification("New requester reason."),
                incident: new SetIncidentOperation("INC-ALPHA")),
            CancellationToken.None);

        Assert.Equal("PROD-ALPHA-EU", result.Candidate.EnvironmentId);
        Assert.Equal("INC-OLD", result.Candidate.IncidentId);
        Assert.Equal("ProductionReadOnly", result.Candidate.RoleId);
        Assert.Equal("New requester reason.", result.Candidate.Justification);
        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedConflict);
        AssertResult(result, ProposalField.Incident, OperationResultKind.RejectedConflict);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedDependency);
        Assert.Equal([ProposalField.Justification], result.ChangedFields);
    }

    [Fact]
    public async Task EnvironmentClearAndIncidentSetConflictWithoutClearingRetainedScope()
    {
        var authority = new FakePreparationAuthority();
        authority.Incidents["INC-2000"] = Incident(
            "INC-2000",
            "PROD-BETA-UK");
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.",
            incidentId: "INC-OLD");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(
                environment: new ClearEnvironmentOperation(),
                incident: new SetIncidentOperation("INC-2000")),
            CancellationToken.None);

        Assert.Same(existing, result.Candidate);
        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedConflict);
        AssertResult(result, ProposalField.Incident, OperationResultKind.RejectedConflict);
        Assert.Empty(result.ChangedFields);
    }

    [Theory]
    [InlineData(0, OperationResultKind.RejectedUnavailable, null)]
    [InlineData(1, OperationResultKind.Applied, "PROD-ALPHA-EU")]
    [InlineData(2, OperationResultKind.RejectedConflict, null)]
    public async Task IncidentWithoutRetainedEnvironmentUsesExactLinkCardinality(
        int linkedEnvironmentCount,
        OperationResultKind expectedResult,
        string? expectedEnvironmentId)
    {
        var authority = new FakePreparationAuthority();
        var linkedEnvironmentIds = Enumerable.Range(1, linkedEnvironmentCount)
            .Select(index => index == 1 ? "PROD-ALPHA-EU" : $"PROD-{index}")
            .ToArray();
        authority.Incidents["INC-1042"] = Incident(
            "INC-1042",
            linkedEnvironmentIds);
        authority.Environments["PROD-ALPHA-EU"] = Environment(
            "PROD-ALPHA-EU",
            "client-alpha");

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(incident: new SetIncidentOperation("INC-1042")),
            CancellationToken.None);

        Assert.Equal(expectedEnvironmentId, result.Candidate.EnvironmentId);
        Assert.Equal(expectedEnvironmentId is null ? null : "INC-1042", result.Candidate.IncidentId);
        AssertResult(result, ProposalField.Incident, expectedResult);
        Assert.Equal(expectedEnvironmentId is null ? 0 : 1, authority.EnvironmentGetCalls.Count);
    }

    [Fact]
    public async Task AcceptedEnvironmentChangeClearsIncompatibleRetainedRoleAndIncident()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Incidents["INC-ALPHA"] = Incident(
            "INC-ALPHA",
            "PROD-ALPHA-EU");
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.",
            incidentId: "INC-ALPHA");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Null(result.Candidate.RoleId);
        Assert.Null(result.Candidate.IncidentId);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Role, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Incident, OperationResultKind.Applied);
        Assert.Equal(
            [ProposalField.Environment, ProposalField.Incident, ProposalField.Role],
            result.ChangedFields);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task MissingRoleCreatesOnlyBoundedNonDestructiveRoleClarification(
        int roleCount,
        bool expectsClarification)
    {
        var authority = new FakePreparationAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", roleCount);
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(justification: Justification("Investigate.")),
            CancellationToken.None);

        Assert.Same(existing, result.Candidate);
        Assert.Equal(
            expectsClarification
                ? ClarificationContextDisposition.Replace
                : ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        Assert.Equal(
            expectsClarification ? ClarificationTarget.Role : null,
            result.Clarification?.Target);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertResult(
            result,
            ProposalField.Role,
            expectsClarification
                ? OperationResultKind.NeedsClarification
                : OperationResultKind.RejectedInvalid);
    }

    [Fact]
    public async Task UnavailableProposedRolePreservesExistingRoleAndOffersCurrentChoices()
    {
        var authority = new FakePreparationAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", 2);
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(role: new SetRoleOperation("UnavailableRole")),
            CancellationToken.None);

        Assert.Equal("ProductionReadOnly", result.Candidate.RoleId);
        Assert.Equal(ClarificationTarget.Role, result.Clarification?.Target);
        AssertResult(result, ProposalField.Role, OperationResultKind.NeedsClarification);
        Assert.Empty(result.ChangedFields);
    }

    [Fact]
    public async Task JustificationUsesNfcNormalizedLineEndingsAndRejectsOversizeWithoutTruncation()
    {
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            Candidate(
                environmentId: null,
                clientId: null,
                justification: "Caf\u00e9\nrestore service."),
            clarification: null,
            Attribution([ProposalField.Justification]),
            CreatedAt,
            "reducer-test");
        var reducer = Reducer(new FakePreparationAuthority());

        var equal = await reducer.ReduceAsync(
            preparation,
            Update(justification: Justification("  Cafe\u0301\r\nrestore service.  ")),
            CancellationToken.None);
        var oversized = await reducer.ReduceAsync(
            preparation,
            Update(justification: Justification(new string('x', 2001))),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, equal.Candidate);
        AssertResult(equal, ProposalField.Justification, OperationResultKind.NoOpValueEqual);
        Assert.Same(preparation.Candidate, oversized.Candidate);
        AssertResult(oversized, ProposalField.Justification, OperationResultKind.RejectedInvalid);
    }

    [Fact]
    public async Task ValidRoleSelectionMapsStoredOneBasedChoiceAndConsumesContext()
    {
        var authority = new FakePreparationAuthority();
        authority.Roles[("PROD-ALPHA-EU", "ProductionSupport")] = Role(
            "PROD-ALPHA-EU",
            "ProductionSupport");
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly",
            "ProductionSupport");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Role, optionIndex: 2),
            CancellationToken.None);

        Assert.Equal("ProductionSupport", result.Candidate.RoleId);
        Assert.Equal(ClarificationContextDisposition.Clear, result.ClarificationDisposition);
        Assert.Null(result.Clarification);
        Assert.Equal(1, authority.RoleGetCallCount);
        AssertResult(result, ProposalField.Role, OperationResultKind.Applied);
        Assert.IsType<ReadyForConfirmation>(result.Outcome);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task EnvironmentSelectionRunsFullPipelineAndCanReplaceContextWithRoleChoices()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.RoleLists["PROD-BETA-UK"] = Roles("PROD-BETA-UK", 2);
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: "ProductionReadOnly",
                justification: "Investigate."),
            ClarificationTarget.Environment,
            "PROD-ALPHA-US",
            "PROD-BETA-UK");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Environment, optionIndex: 2),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Null(result.Candidate.RoleId);
        Assert.Equal(ClarificationContextDisposition.Replace, result.ClarificationDisposition);
        Assert.Equal(ClarificationTarget.Role, result.Clarification?.Target);
        Assert.Equal(["Role-01", "Role-02"], result.Clarification?.OrderedCanonicalIds);
        Assert.Equal(["PROD-BETA-UK"], authority.EnvironmentGetCalls);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Theory]
    [InlineData(ClarificationTarget.Environment, 1)]
    [InlineData(ClarificationTarget.Role, 0)]
    [InlineData(ClarificationTarget.Role, 3)]
    public async Task MismatchedOrOutOfRangeSelectionPreservesCandidateAndContext(
        ClarificationTarget selectedTarget,
        int optionIndex)
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly",
            "ProductionSupport");
        var proposal = optionIndex < 1
            ? SelectWithInvalidIndex(selectedTarget, optionIndex)
            : Select(selectedTarget, optionIndex);

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            proposal,
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedInvalid);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task StaleCandidateVersionRejectsSelectionAndClearsUnusableContext()
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly");
        SetPrivateProperty(
            preparation,
            nameof(RequestPreparation.CandidateVersion),
            preparation.CandidateVersion + 1);

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Role, optionIndex: 1),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Clear, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedInvalid);
        Assert.Empty(result.ChangedFields);
    }

    [Fact]
    public async Task SelectedRoleThatIsNoLongerAssignableClearsStaleContextAndPreservesCandidate()
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: "ProductionReadOnly",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionSupport");

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Role, optionIndex: 1),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Clear, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedUnavailable);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task MissingClarificationContextRejectsWithoutGuessingOrAuthorityCalls()
    {
        var authority = new FakePreparationAuthority();
        var preparation = EmptyPreparation();

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Environment, optionIndex: 1),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedInvalid);
        Assert.Empty(authority.EnvironmentGetCalls);
    }

    [Fact]
    public async Task UnsupportedRuntimeDialogueActFailsClosedWithoutAuthorityOrCandidateChange()
    {
        var authority = new FakePreparationAuthority();
        var preparation = EmptyPreparation();
        var proposal = new TurnProposal(
            TurnProposal.CurrentSchemaVersion,
            DialogueAct.Unclear);
        SetPrivateProperty(proposal, nameof(TurnProposal.DialogueAct), (DialogueAct)999);

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            proposal,
            CancellationToken.None);

        var failed = Assert.IsType<Failed>(result.Outcome);
        Assert.Equal(
            "request-preparation-proposal-structural-invalid",
            failed.Failure.Code);
        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(0, authority.SearchCallCount);
        Assert.Empty(authority.EnvironmentGetCalls);
        Assert.Equal(0, authority.RoleGetCallCount);
        AssertSnapshotUnchanged(preparation, PreparationCandidate.Empty);
    }

    [Fact]
    public async Task MalformedRuntimePayloadCombinationFailsClosedBeforeAuthorityReads()
    {
        var authority = new FakePreparationAuthority();
        var preparation = EmptyPreparation();
        var proposal = Update(environment: new SetEnvironmentOperation(
            new ExactEnvironmentId("PROD-ALPHA-EU")));
        SetPrivateProperty<DraftPatch?>(proposal, nameof(TurnProposal.Patch), null);

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            proposal,
            CancellationToken.None);

        Assert.IsType<Failed>(result.Outcome);
        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Empty(authority.EnvironmentGetCalls);
        AssertSnapshotUnchanged(preparation, PreparationCandidate.Empty);
    }

    [Fact]
    public async Task OmittedFieldsRemainCanonicalDuringIndependentNoOp()
    {
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.",
            incidentId: "INC-1042");
        var preparation = Preparation(existing);

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Update(justification: Justification("Investigate.")),
            CancellationToken.None);

        Assert.Same(existing, result.Candidate);
        Assert.Empty(result.ChangedFields);
        AssertResult(result, ProposalField.Justification, OperationResultKind.NoOpValueEqual);
    }

    [Fact]
    public async Task EnvironmentChangeRetainsDependentsOnlyAfterExactRevalidation()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Roles[("PROD-BETA-UK", "ProductionReadOnly")] = Role(
            "PROD-BETA-UK",
            "ProductionReadOnly");
        authority.Incidents["INC-SHARED"] = Incident(
            "INC-SHARED",
            "PROD-ALPHA-EU",
            "PROD-BETA-UK");
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.",
            incidentId: "INC-SHARED");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("ProductionReadOnly", result.Candidate.RoleId);
        Assert.Equal("INC-SHARED", result.Candidate.IncidentId);
        Assert.Equal([ProposalField.Environment], result.ChangedFields);
        Assert.Equal(1, authority.RoleGetCallCount);
        Assert.Equal(1, authority.IncidentGetCallCount);
    }

    [Theory]
    [InlineData(AuthorityFailurePoint.Search)]
    [InlineData(AuthorityFailurePoint.Incident)]
    [InlineData(AuthorityFailurePoint.RoleGet)]
    [InlineData(AuthorityFailurePoint.RoleList)]
    public async Task AuthoritySourceFailuresRejectAffectedOperationsWithoutThrowing(
        AuthorityFailurePoint failurePoint)
    {
        var authority = new FakePreparationAuthority();
        var failure = Failure(
            ApplicationFailureKind.DependencyUnavailable,
            "authority-unavailable");
        TurnProposal proposal;
        ProposalField expectedField;
        switch (failurePoint)
        {
            case AuthorityFailurePoint.Search:
                authority.SearchFailure = failure;
                proposal = Update(environment: new SetEnvironmentOperation(
                    new EnvironmentSearchQuery("alpha")));
                expectedField = ProposalField.Environment;
                break;
            case AuthorityFailurePoint.Incident:
                authority.IncidentFailure = failure;
                proposal = Update(incident: new SetIncidentOperation("INC-1042"));
                expectedField = ProposalField.Incident;
                break;
            case AuthorityFailurePoint.RoleGet:
                authority.RoleFailure = failure;
                proposal = Update(role: new SetRoleOperation("ProductionSupport"));
                expectedField = ProposalField.Role;
                break;
            case AuthorityFailurePoint.RoleList:
                authority.RoleFailure = failure;
                proposal = Update(justification: Justification("Investigate."));
                expectedField = ProposalField.Role;
                break;
            default:
                throw new InvalidOperationException();
        }

        var candidate = failurePoint is AuthorityFailurePoint.RoleGet
            or AuthorityFailurePoint.RoleList
            ? Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: failurePoint == AuthorityFailurePoint.RoleGet
                    ? "ProductionReadOnly"
                    : null,
                justification: "Investigate.")
            : PreparationCandidate.Empty;
        var preparation = candidate.IsEmpty
            ? EmptyPreparation()
            : Preparation(candidate);

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            proposal,
            CancellationToken.None);

        AssertResult(result, expectedField, OperationResultKind.RejectedUnavailable);
        Assert.Same(preparation.Candidate, result.Candidate);
    }

    [Fact]
    public void ReducerBoundaryAcceptsNoRequesterTextOrProviderContracts()
    {
        var reduceMethod = typeof(RequestPreparationReducer).GetMethod(
            nameof(RequestPreparationReducer.ReduceAsync));

        Assert.NotNull(reduceMethod);
        Assert.DoesNotContain(
            reduceMethod.GetParameters(),
            parameter => parameter.ParameterType == typeof(string));
        Assert.Equal(
            [
                typeof(RequestPreparation),
                typeof(TurnProposal),
                typeof(CancellationToken),
            ],
            reduceMethod.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task SameTurnRoleIsValidatedAgainstNewEnvironment()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Roles[("PROD-BETA-UK", "ProductionSupport")] = Role(
            "PROD-BETA-UK",
            "ProductionSupport");

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-BETA-UK")),
                role: new SetRoleOperation("ProductionSupport")),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("ProductionSupport", result.Candidate.RoleId);
        Assert.Equal([("PROD-BETA-UK", "ProductionSupport")], authority.RoleGetCalls);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Role, OperationResultKind.Applied);
    }

    [Fact]
    public async Task EnvironmentAndIncidentClearApplyOneCompleteDependencyCascade()
    {
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.",
            incidentId: "INC-1042");

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            Preparation(existing),
            Update(
                environment: new ClearEnvironmentOperation(),
                incident: new ClearIncidentOperation()),
            CancellationToken.None);

        Assert.Null(result.Candidate.EnvironmentId);
        Assert.Null(result.Candidate.ClientId);
        Assert.Null(result.Candidate.RoleId);
        Assert.Null(result.Candidate.IncidentId);
        Assert.Equal("Investigate.", result.Candidate.Justification);
        AssertResult(result, ProposalField.Environment, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Incident, OperationResultKind.Applied);
        AssertResult(result, ProposalField.Role, OperationResultKind.Applied);
    }

    [Fact]
    public async Task InvalidSearchClassificationFailsClosedWithoutCandidateChange()
    {
        var authority = new FakePreparationAuthority
        {
            SearchResult = EnvironmentSearchPolicy.Search(" ", []),
        };
        var preparation = EmptyPreparation();

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new EnvironmentSearchQuery("valid proposal query"))),
            CancellationToken.None);

        AssertResult(result, ProposalField.Environment, OperationResultKind.RejectedInvalid);
        Assert.Same(preparation.Candidate, result.Candidate);
    }

    [Fact]
    public async Task AuthorityResponsesMustMatchEveryRequestedExactIdentifier()
    {
        var environmentAuthority = new FakePreparationAuthority();
        environmentAuthority.Environments["PROD-REQUESTED"] = Environment(
            "PROD-DIFFERENT",
            "client-beta");
        var environment = await Reducer(environmentAuthority).ReduceAsync(
            EmptyPreparation(),
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-REQUESTED"))),
            CancellationToken.None);

        var roleAuthority = new FakePreparationAuthority();
        roleAuthority.Roles[("PROD-ALPHA-EU", "Role-Requested")] = Role(
            "PROD-ALPHA-EU",
            "Role-Different");
        var rolePreparation = Preparation(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."));
        var role = await Reducer(roleAuthority).ReduceAsync(
            rolePreparation,
            Update(role: new SetRoleOperation("Role-Requested")),
            CancellationToken.None);

        var incidentAuthority = new FakePreparationAuthority();
        incidentAuthority.Incidents["INC-REQUESTED"] = Incident(
            "INC-DIFFERENT",
            "PROD-ALPHA-EU");
        var incident = await Reducer(incidentAuthority).ReduceAsync(
            rolePreparation,
            Update(incident: new SetIncidentOperation("INC-REQUESTED")),
            CancellationToken.None);

        AssertResult(environment, ProposalField.Environment, OperationResultKind.RejectedUnavailable);
        Assert.Null(environment.Candidate.EnvironmentId);
        AssertResult(role, ProposalField.Role, OperationResultKind.RejectedUnavailable);
        Assert.Null(role.Candidate.RoleId);
        AssertResult(incident, ProposalField.Incident, OperationResultKind.RejectedUnavailable);
        Assert.Null(incident.Candidate.IncidentId);
    }

    [Fact]
    public async Task ClarificationContextBoundToAnotherPreparationIsRejectedWithoutConsumption()
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly");
        var foreignContext = new PreparationClarificationContext(
            Guid.NewGuid(),
            preparation.CandidateVersion,
            new ClarificationSeed(
                ClarificationTarget.Role,
                ["ProductionReadOnly"]),
            CreatedAt);
        SetPrivateProperty(
            preparation,
            nameof(RequestPreparation.Clarification),
            foreignContext);

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Select(ClarificationTarget.Role, optionIndex: 1),
            CancellationToken.None);

        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        AssertResult(result, ProposalField.Role, OperationResultKind.RejectedInvalid);
        Assert.Same(foreignContext, preparation.Clarification);
    }

    [Fact]
    public async Task NoOpPatchDoesNotReplaceAnExistingHigherPriorityClarification()
    {
        var authority = new FakePreparationAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", 2);
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Environment,
            "PROD-ALPHA-US",
            "PROD-BETA-UK");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(justification: Justification("Investigate.")),
            CancellationToken.None);

        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        Assert.Null(result.Clarification);
        Assert.Equal(0, authority.RoleListCallCount);
        AssertResult(result, ProposalField.Justification, OperationResultKind.NoOpValueEqual);
        Assert.Same(preparation.Candidate, result.Candidate);
    }
}


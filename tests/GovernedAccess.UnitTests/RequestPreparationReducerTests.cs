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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        AssertJustificationResult(result, ApplicationGroupResultKind.Applied);
        Assert.Equal([ProposalField.Justification], result.ChangedFields);
        AssertSnapshotUnchanged(preparation, PreparationCandidate.Empty);
    }

    [Theory]
    [InlineData(
        0,
        EnvironmentSearchResultKind.NoMatches,
        ApplicationGroupRejectionReason.Unavailable)]
    [InlineData(
        6,
        EnvironmentSearchResultKind.TooBroad,
        ApplicationGroupRejectionReason.EnvironmentQueryTooBroad)]
    public async Task NonUniqueUnrenderableSearchResultsRejectWithoutMutation(
        int matchCount,
        EnvironmentSearchResultKind expectedKind,
        ApplicationGroupRejectionReason expectedRejectionReason)
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
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            expectedRejectionReason);
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
            result.Clarification?.Choices.Select(choice => choice.CanonicalId));
        Assert.IsType<ClarificationRequired>(result.Outcome);
        AssertScopeResult(result, ApplicationGroupResultKind.NeedsClarification);
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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(result, ApplicationGroupResultKind.NeedsClarification);
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

        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        AssertJustificationResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Conflict);
        AssertJustificationResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Conflict);
        Assert.Empty(result.ChangedFields);
    }

    [Theory]
    [InlineData(
        null,
        ApplicationGroupResultKind.Rejected,
        ApplicationGroupRejectionReason.Unavailable)]
    [InlineData(
        "PROD-ALPHA-EU",
        ApplicationGroupResultKind.Applied,
        null)]
    public async Task IncidentWithoutRetainedEnvironmentUsesNullableRelationship(
        string? relatedEnvironmentId,
        ApplicationGroupResultKind expectedResult,
        ApplicationGroupRejectionReason? expectedRejectionReason)
    {
        var authority = new FakePreparationAuthority();
        authority.Incidents["INC-1042"] = Incident(
            "INC-1042",
            relatedEnvironmentId);
        authority.Environments["PROD-ALPHA-EU"] = Environment(
            "PROD-ALPHA-EU",
            "client-alpha");

        var result = await Reducer(authority).ReduceAsync(
            EmptyPreparation(),
            Update(incident: new SetIncidentOperation("INC-1042")),
            CancellationToken.None);

        Assert.Equal(relatedEnvironmentId, result.Candidate.EnvironmentId);
        Assert.Equal(relatedEnvironmentId is null ? null : "INC-1042", result.Candidate.IncidentId);
        AssertScopeResult(result, expectedResult, expectedRejectionReason);
        Assert.Equal(relatedEnvironmentId is null ? 0 : 1, authority.EnvironmentGetCalls.Count);
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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
        Assert.Equal(
            [ProposalField.Environment, ProposalField.Incident, ProposalField.Role],
            result.ChangedFields);
    }

    [Fact]
    public async Task MissingRoleAutomaticallySelectsTheOnlyAssignableRole()
    {
        var authority = new FakePreparationAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", 1);
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(justification: Justification("Investigate.")),
            CancellationToken.None);

        Assert.Equal("Role-01", result.Candidate.RoleId);
        Assert.Null(result.Clarification);
        Assert.Equal(
            ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        Assert.Equal([ProposalField.Role], result.ChangedFields);
        Assert.Equal("Role-01", result.SoleRoleSelection?.RoleId);
        Assert.Equal("Role 1", result.SoleRoleSelection?.DisplayName);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
        Assert.IsType<ReadyForConfirmation>(result.Outcome);
    }

    [Theory]
    [InlineData(2, true)]
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
        AssertScopeResult(
            result,
            expectsClarification
                ? ApplicationGroupResultKind.NeedsClarification
                : ApplicationGroupResultKind.Rejected,
            expectsClarification
                ? null
                : ApplicationGroupRejectionReason.RoleChoiceLimitExceeded);
    }

    [Fact]
    public async Task ExplicitSoleRoleSelectionCarriesAutomaticSelectionNotice()
    {
        var authority = new FakePreparationAuthority();
        authority.Roles[("PROD-ALPHA-EU", "Role-01")] = Role(
            "PROD-ALPHA-EU",
            "Role-01");
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", 1);
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(role: new SetRoleOperation("Role-01")),
            CancellationToken.None);

        Assert.Equal("Role-01", result.Candidate.RoleId);
        Assert.Equal("Role-01", result.SoleRoleSelection?.RoleId);
        Assert.Equal("Role 1", result.SoleRoleSelection?.DisplayName);
        Assert.Equal(1, authority.RoleGetCallCount);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
    }

    [Fact]
    public async Task ExplicitRoleClearDoesNotImmediatelyReselectTheSoleRole()
    {
        var authority = new FakePreparationAuthority();
        authority.RoleLists["PROD-ALPHA-EU"] = Roles("PROD-ALPHA-EU", 1);
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "Role-01",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(role: new ClearRoleOperation()),
            CancellationToken.None);

        Assert.Null(result.Candidate.RoleId);
        Assert.Null(result.SoleRoleSelection);
        Assert.Null(result.Clarification);
        Assert.Equal([ProposalField.Role], result.ChangedFields);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
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
        AssertScopeResult(result, ApplicationGroupResultKind.NeedsClarification);
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
        AssertJustificationResult(equal, ApplicationGroupResultKind.NoOp);
        Assert.Same(preparation.Candidate, oversized.Candidate);
        AssertJustificationResult(
            oversized,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Invalid);
    }

    [Fact]
    public async Task OrdinaryExactRolePatchOutsideDisplayedChoicesIsReloadedAndConsumesContext()
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
            "ProductionReadOnly");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(role: new SetRoleOperation("ProductionSupport")),
            CancellationToken.None);

        Assert.Equal("ProductionSupport", result.Candidate.RoleId);
        Assert.Equal(ClarificationContextDisposition.Clear, result.ClarificationDisposition);
        Assert.Null(result.Clarification);
        Assert.Equal(1, authority.RoleGetCallCount);
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
        Assert.IsType<ReadyForConfirmation>(result.Outcome);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task OrdinaryExactEnvironmentPatchRunsFullPipelineAndCanCreateRoleContext()
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
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Null(result.Candidate.RoleId);
        Assert.Equal(ClarificationContextDisposition.Replace, result.ClarificationDisposition);
        Assert.Equal(ClarificationTarget.Role, result.Clarification?.Target);
        Assert.Equal(
            ["Role-01", "Role-02"],
            result.Clarification?.Choices.Select(choice => choice.CanonicalId));
        Assert.Equal(["PROD-BETA-UK"], authority.EnvironmentGetCalls);
        Assert.Equal(1, authority.RoleListCallCount);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task InvalidExactRolePatchPreservesCandidateAndActiveContext()
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
            Update(role: new SetRoleOperation("ProductionDeployment")),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(ClarificationContextDisposition.Preserve, result.ClarificationDisposition);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task AuthoritativelyStaleDisplayedRoleChoiceInvalidatesContext()
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
            Update(role: new SetRoleOperation("ProductionSupport")),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(
            ClarificationContextDisposition.Clear,
            result.ClarificationDisposition);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.NoAssignableRoles);
    }

    [Fact]
    public async Task AuthoritativelyStaleDisplayedEnvironmentChoiceInvalidatesContext()
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: null,
                clientId: null,
                justification: "Investigate."),
            ClarificationTarget.Environment,
            "PROD-BETA-UK");

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(
            ClarificationContextDisposition.Clear,
            result.ClarificationDisposition);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
    }

    [Fact]
    public async Task TransientRoleReloadFailurePreservesDisplayedChoiceContext()
    {
        var authority = new FakePreparationAuthority
        {
            RoleFailure = Failure(
                ApplicationFailureKind.DependencyUnavailable,
                "role-source-unavailable"),
        };
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionSupport");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(role: new SetRoleOperation("ProductionSupport")),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(
            ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
    }

    [Fact]
    public async Task TransientEnvironmentReloadFailurePreservesDisplayedChoiceContext()
    {
        var authority = new FakePreparationAuthority
        {
            EnvironmentFailure = Failure(
                ApplicationFailureKind.DependencyUnavailable,
                "environment-source-unavailable"),
        };
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: null,
                clientId: null,
                justification: "Investigate."),
            ClarificationTarget.Environment,
            "PROD-BETA-UK");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(
            ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
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
        AssertJustificationResult(result, ApplicationGroupResultKind.NoOp);
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
        switch (failurePoint)
        {
            case AuthorityFailurePoint.Search:
                authority.SearchFailure = failure;
                proposal = Update(environment: new SetEnvironmentOperation(
                    new EnvironmentSearchQuery("alpha")));
                break;
            case AuthorityFailurePoint.Incident:
                authority.IncidentFailure = failure;
                proposal = Update(incident: new SetIncidentOperation("INC-1042"));
                break;
            case AuthorityFailurePoint.RoleGet:
                authority.RoleFailure = failure;
                proposal = Update(role: new SetRoleOperation("ProductionSupport"));
                break;
            case AuthorityFailurePoint.RoleList:
                authority.RoleFailure = failure;
                proposal = Update(justification: Justification("Investigate."));
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

        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
    }

    [Fact]
    public async Task InvalidRoleRejectsSameTurnEnvironmentChangeButAppliesJustification()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        var existing = Candidate(
            environmentId: "PROD-ALPHA-EU",
            clientId: "client-alpha",
            roleId: "ProductionReadOnly",
            justification: "Investigate.");

        var result = await Reducer(authority).ReduceAsync(
            Preparation(existing),
            Update(
                environment: new SetEnvironmentOperation(
                    new ExactEnvironmentId("PROD-BETA-UK")),
                role: new SetRoleOperation("UnavailableRole"),
                justification: Justification("Restore customer service.")),
            CancellationToken.None);

        Assert.Equal("PROD-ALPHA-EU", result.Candidate.EnvironmentId);
        Assert.Equal("client-alpha", result.Candidate.ClientId);
        Assert.Equal("ProductionReadOnly", result.Candidate.RoleId);
        Assert.Equal("Restore customer service.", result.Candidate.Justification);
        Assert.Equal([ProposalField.Justification], result.ChangedFields);
        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        AssertJustificationResult(result, ApplicationGroupResultKind.Applied);
    }

    [Fact]
    public async Task ValidScopeAppliesWhenIndependentJustificationIsRejected()
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
                role: new SetRoleOperation("ProductionSupport"),
                justification: Justification(new string('x', 2001))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("client-beta", result.Candidate.ClientId);
        Assert.Equal("ProductionSupport", result.Candidate.RoleId);
        Assert.Null(result.Candidate.Justification);
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
        AssertJustificationResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Invalid);
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
        AssertScopeResult(result, ApplicationGroupResultKind.Applied);
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

        AssertScopeResult(
            result,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Invalid);
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

        AssertScopeResult(
            environment,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        Assert.Null(environment.Candidate.EnvironmentId);
        AssertScopeResult(
            role,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        Assert.Null(role.Candidate.RoleId);
        AssertScopeResult(
            incident,
            ApplicationGroupResultKind.Rejected,
            ApplicationGroupRejectionReason.Unavailable);
        Assert.Null(incident.Candidate.IncidentId);
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
        AssertJustificationResult(result, ApplicationGroupResultKind.NoOp);
        Assert.Same(preparation.Candidate, result.Candidate);
    }

    [Fact]
    public async Task IndependentJustificationUpdatePreservesActiveEnvironmentClarification()
    {
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: "ProductionReadOnly",
                justification: "Investigate."),
            ClarificationTarget.Environment,
            "PROD-ALPHA-US",
            "PROD-BETA-UK");

        var result = await Reducer(new FakePreparationAuthority()).ReduceAsync(
            preparation,
            Update(justification: Justification("Investigate customer impact.")),
            CancellationToken.None);

        Assert.Equal("Investigate customer impact.", result.Candidate.Justification);
        Assert.Equal(
            ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        Assert.Null(result.Clarification);
        AssertJustificationResult(result, ApplicationGroupResultKind.Applied);
        AssertSnapshotWithContextUnchanged(preparation);
    }

    [Fact]
    public async Task ValueEqualExactRolePatchPreservesActiveRoleClarification()
    {
        var authority = new FakePreparationAuthority();
        authority.Roles[("PROD-ALPHA-EU", "ProductionReadOnly")] = Role(
            "PROD-ALPHA-EU",
            "ProductionReadOnly");
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: "ProductionReadOnly",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly",
            "ProductionSupport");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(role: new SetRoleOperation("ProductionReadOnly")),
            CancellationToken.None);

        Assert.Same(preparation.Candidate, result.Candidate);
        Assert.Equal(
            ClarificationContextDisposition.Preserve,
            result.ClarificationDisposition);
        AssertScopeResult(result, ApplicationGroupResultKind.NoOp);
    }

    [Fact]
    public async Task AcceptedEnvironmentChangeClearsActiveRoleContextWhenRoleRemainsAssignable()
    {
        var authority = new FakePreparationAuthority();
        authority.Environments["PROD-BETA-UK"] = Environment(
            "PROD-BETA-UK",
            "client-beta");
        authority.Roles[("PROD-BETA-UK", "ProductionReadOnly")] = Role(
            "PROD-BETA-UK",
            "ProductionReadOnly");
        var preparation = PreparationWithClarification(
            Candidate(
                environmentId: "PROD-ALPHA-EU",
                clientId: "client-alpha",
                roleId: "ProductionReadOnly",
                justification: "Investigate."),
            ClarificationTarget.Role,
            "ProductionReadOnly",
            "ProductionSupport");

        var result = await Reducer(authority).ReduceAsync(
            preparation,
            Update(environment: new SetEnvironmentOperation(
                new ExactEnvironmentId("PROD-BETA-UK"))),
            CancellationToken.None);

        Assert.Equal("PROD-BETA-UK", result.Candidate.EnvironmentId);
        Assert.Equal("ProductionReadOnly", result.Candidate.RoleId);
        Assert.Equal(
            ClarificationContextDisposition.Clear,
            result.ClarificationDisposition);
        Assert.Null(result.Clarification);
        Assert.Equal(["PROD-BETA-UK"], authority.EnvironmentGetCalls);
        Assert.Equal(1, authority.RoleGetCallCount);
    }
}

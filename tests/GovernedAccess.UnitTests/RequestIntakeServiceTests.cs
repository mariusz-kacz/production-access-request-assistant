using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestIntakeServiceTests
{
    [Fact]
    public void CompactResultFactoriesRejectMissingRequiredEvidence()
    {
        Assert.Throws<ArgumentException>(
            () => RequestPreparationResult.CandidateRejected([]));
        Assert.Throws<ArgumentException>(
            () => RequestConfirmationResult.Submitted(Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => RequestIntakeResetResult.Reset(Guid.Empty));
    }

    [Theory]
    [InlineData(false, RequestIntakeStatus.Collecting, RequestIntakeStatus.Superseded)]
    [InlineData(false, RequestIntakeStatus.Ready, RequestIntakeStatus.Superseded)]
    [InlineData(true, RequestIntakeStatus.Ready, RequestIntakeStatus.Expired)]
    public async Task ResetTerminatesOnlyTheActivePreparationAndClearsItsCandidate(
        bool expired,
        RequestIntakeStatus initialStatus,
        RequestIntakeStatus expectedStatus)
    {
        var initial = initialStatus == RequestIntakeStatus.Ready
            ? CreateReadySession(
                expired
                    ? IntakeScenario.CurrentTime.Subtract(
                        RequestIntakeSession.ConfirmationLifetime)
                    : IntakeScenario.CurrentTime)
            : CreateCollectingSessionWithCandidate();
        var reservedRequestId = initial.ReservedRequestId;
        var scenario = new IntakeScenario(initialSession: initial);

        var result = await scenario.ResetResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestIntakeResetResultKind.Reset, result.Kind);
        Assert.Equal(initial.Id, result.IntakeId);
        Assert.Null(result.Failure);
        Assert.Equal(expectedStatus, initial.Status);
        Assert.Equal(reservedRequestId, initial.ReservedRequestId);
        Assert.Null(initial.ClientId);
        Assert.Null(initial.EnvironmentId);
        Assert.Null(initial.RequestedRoleId);
        Assert.Null(initial.Justification);
        Assert.Null(initial.IncidentId);
        Assert.Equal("reset-correlation", initial.CorrelationId);
        Assert.Equal(IntakeScenario.CurrentTime, initial.LastUpdatedAt);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Equal(0, scenario.InterpreterCallCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task ResetIsIdempotentWhenNoActivePreparationOrOnlySubmittedEvidenceExists()
    {
        var noActive = new IntakeScenario();

        var first = await noActive.ResetResultAsync(IntakeScenario.Owner);
        var repeated = await noActive.ResetResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestIntakeResetResultKind.AlreadyClear, first.Kind);
        Assert.Equal(RequestIntakeResetResultKind.AlreadyClear, repeated.Kind);
        Assert.Null(first.IntakeId);
        Assert.Null(first.Failure);
        Assert.Equal(0, noActive.SaveCount);
        Assert.Equal(0, noActive.InterpreterCallCount);

        var submitted = CreateSubmittedSession();
        var submittedScenario = new IntakeScenario(initialSession: submitted);

        var submittedResult = await submittedScenario.ResetResultAsync(
            IntakeScenario.Owner);

        Assert.Equal(
            RequestIntakeResetResultKind.AlreadyClear,
            submittedResult.Kind);
        Assert.Equal(RequestIntakeStatus.Submitted, submitted.Status);
        Assert.Equal(0, submittedScenario.SaveCount);
        Assert.Equal(0, submittedScenario.InterpreterCallCount);
        Assert.Empty(submittedScenario.Requests);
        Assert.Empty(submittedScenario.AuditEvents);
    }

    [Theory]
    [InlineData("other-actor", "conversation-001")]
    [InlineData("actor-001", "other-conversation")]
    public async Task ResetCannotSelectAnotherActorOrConversationPreparation(
        string actorId,
        string conversationId)
    {
        var initial = CreateCollectingSessionWithCandidate();
        var scenario = new IntakeScenario(initialSession: initial);
        var other = new AuthenticatedChannelActor(
            RequestIntakeSession.TeamsChannel,
            IntakeScenario.Owner.TenantId,
            actorId,
            conversationId,
            IntakeScenario.Owner.RequesterId);

        var result = await scenario.ResetResultAsync(other);

        Assert.Equal(RequestIntakeResetResultKind.AlreadyClear, result.Kind);
        Assert.Equal(RequestIntakeStatus.Collecting, initial.Status);
        Assert.NotNull(initial.ClientId);
        Assert.Equal(0, scenario.SaveCount);
        Assert.Equal(0, scenario.InterpreterCallCount);
    }

    [Fact]
    public async Task ResetRetainsTypedLoadAndSaveFailures()
    {
        var loadFailure = new ApplicationFailure(
            ApplicationFailureKind.DependencyUnavailable,
            "forced_load_failure",
            "The test load failed.");
        var loadScenario = new IntakeScenario
        {
            ActiveLoadFailure = loadFailure,
        };

        var loadResult = await loadScenario.ResetResultAsync(
            IntakeScenario.Owner);

        Assert.Equal(RequestIntakeResetResultKind.Failed, loadResult.Kind);
        Assert.Same(loadFailure, loadResult.Failure);
        Assert.Null(loadResult.IntakeId);
        Assert.Equal(0, loadScenario.SaveCount);

        var active = CreateCollectingSessionWithCandidate();
        var saveFailure = ForcedSaveFailure();
        var saveScenario = new IntakeScenario(initialSession: active)
        {
            SaveFailure = saveFailure,
        };

        var saveResult = await saveScenario.ResetResultAsync(
            IntakeScenario.Owner);

        Assert.Equal(RequestIntakeResetResultKind.Failed, saveResult.Kind);
        Assert.Same(saveFailure, saveResult.Failure);
        Assert.Equal(active.Id, saveResult.IntakeId);
        Assert.Equal(RequestIntakeStatus.Superseded, active.Status);
        Assert.Equal(1, saveScenario.SaveCount);
        Assert.Equal(0, saveScenario.InterpreterCallCount);
    }

    [Fact]
    public async Task ResetPropagatesCallerCancellationWithoutInvokingTheInterpreter()
    {
        var scenario = new IntakeScenario(
            initialSession: CreateCollectingSessionWithCandidate());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => scenario.ResetResultAsync(
                IntakeScenario.Owner,
                cancellation.Token));

        Assert.Equal(0, scenario.SaveCount);
        Assert.Equal(0, scenario.InterpreterCallCount);
        Assert.Equal(RequestIntakeStatus.Collecting, scenario.Session.Status);
    }

    [Fact]
    public async Task InvalidPartialIdentifierIsSanitizedAndRejectedWithoutRetry()
    {
        var proposal = new RequestPreparationProposal(
            RequestPreparationProposalKind.Clarification,
            new RequestCandidate(
                "ClientA",
                environmentId: null,
                requestedRoleId: null,
                justification: null,
                incidentId: null),
            new RequestClarificationProposal(
                RequestClarificationTarget.EnvironmentId,
                "Which environment should be used?"));
        var scenario = new IntakeScenario(proposal: proposal);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.CandidateRejected,
            result.Kind);
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal("clientId", error.Field);
        Assert.Equal("client_not_found", error.Code);
        Assert.Single(scenario.InterpretationTurns);
        Assert.Null(scenario.Session.ClientId);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task ValidEnvironmentReplacesClientDisplayNameWithCanonicalIdentity()
    {
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    "Client Alpha",
                    "PROD-ALPHA-EU",
                    requestedRoleId: null,
                    justification: null,
                    incidentId: null),
                new RequestClarificationProposal(
                    RequestClarificationTarget.RequestedRoleId,
                    "Which approved role is required?")));

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.ClarificationRequired,
            result.Kind);
        Assert.Equal("client-alpha", scenario.Session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", scenario.Session.EnvironmentId);
        Assert.Single(scenario.InterpretationTurns);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task InvalidIncidentIsClearedWhileValidFieldsArePreservedWithoutRetry()
    {
        var proposal = new RequestPreparationProposal(
            RequestPreparationProposalKind.Candidate,
            new RequestCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-UNKNOWN"),
            clarification: null);
        var scenario = new IntakeScenario(proposal: proposal);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.CandidateRejected,
            result.Kind);
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal("incident_not_found", error.Code);
        Assert.Single(scenario.InterpretationTurns);
        Assert.Equal("client-alpha", scenario.Session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", scenario.Session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, scenario.Session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            scenario.Session.Justification);
        Assert.Null(scenario.Session.IncidentId);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task PrepareAndConfirmPreserveOneImmutableScopeAndReservedIdentity()
    {
        var scenario = new IntakeScenario();

        var ready = await scenario.PrepareAsync();

        Assert.True(ready.IsReady);
        Assert.Equal("Collecting", scenario.StatusWhenAdded);
        Assert.Equal("Ready", scenario.IntakeStatus);
        Assert.NotEqual(Guid.Empty, ready.PreparationId);
        Assert.NotEqual(Guid.Empty, ready.ReservedRequestId);
        Assert.Equal("client-alpha", ready.ClientId);
        Assert.Equal("PROD-ALPHA-EU", ready.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, ready.RoleId);
        Assert.Equal("Investigate the active production incident.", ready.Justification);
        Assert.Equal("INC-1042", ready.IncidentId);
        Assert.Throws<InvalidOperationException>(scenario.AttemptReadyScopeChange);

        var confirmed = await scenario.ConfirmAsync(IntakeScenario.Owner);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(ready.ReservedRequestId, confirmed.RequestId);
        Assert.Equal("Submitted", scenario.IntakeStatus);
        Assert.Equal(2, scenario.SaveCount);

        var request = Assert.Single(scenario.Requests);
        Assert.Equal(ready.ReservedRequestId, request.Id);
        Assert.Equal(IntakeScenario.Owner.RequesterId, request.RequesterId);
        Assert.Equal(ready.ClientId, request.ClientId);
        Assert.Equal(ready.EnvironmentId, request.EnvironmentId);
        Assert.Equal(ready.RoleId, request.RequestedRoleId);
        Assert.Equal(ready.Justification, request.Justification);
        Assert.Equal(ready.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);

        var auditEvent = Assert.Single(scenario.AuditEvents);
        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
    }

    [Theory]
    [InlineData("other-channel", "tenant-001", "actor-001", "conversation-001", "requester")]
    [InlineData("msteams", "other-tenant", "actor-001", "conversation-001", "requester")]
    [InlineData("msteams", "tenant-001", "other-actor", "conversation-001", "requester")]
    [InlineData("msteams", "tenant-001", "actor-001", "other-conversation", "requester")]
    [InlineData("msteams", "tenant-001", "actor-001", "conversation-001", "other-requester")]
    public async Task ConfirmationRequiresTheExactAuthenticatedPreparationBinding(
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId)
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        var otherActor = new AuthenticatedChannelActor(
            channel,
            tenantId,
            channelActorId,
            conversationId,
            requesterId);

        var result = await scenario.ConfirmResultAsync(otherActor);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.Unauthorized, result.Failure!.Kind);
        Assert.Equal(RequestIntakeService.ForbiddenCode, result.Failure.Code);
        Assert.Equal("Ready", scenario.IntakeStatus);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task ConfirmationRevalidatesThePreparedScopeAgainstAuthoritativeData()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        scenario.RoleIsAvailable = false;

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.InvalidTransition, result.Failure!.Kind);
        Assert.Equal(RequestIntakeService.InvalidatedCode, result.Failure.Code);
        Assert.Equal("Invalidated", scenario.IntakeStatus);
        Assert.NotNull(scenario.Session.ReservedRequestId);
        Assert.Null(scenario.Session.ClientId);
        Assert.Null(scenario.Session.EnvironmentId);
        Assert.Null(scenario.Session.RequestedRoleId);
        Assert.Null(scenario.Session.Justification);
        Assert.Null(scenario.Session.IncidentId);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
        Assert.Equal(2, scenario.SaveCount);
    }

    [Theory]
    [InlineData(
        RequestPreparationInterpretationFailure.MalformedModelOutput,
        ApplicationFailureKind.DependencyFailure)]
    [InlineData(
        RequestPreparationInterpretationFailure.Timeout,
        ApplicationFailureKind.Timeout)]
    [InlineData(
        RequestPreparationInterpretationFailure.Cancelled,
        ApplicationFailureKind.Cancelled)]
    [InlineData(
        RequestPreparationInterpretationFailure.Unavailable,
        ApplicationFailureKind.DependencyUnavailable)]
    public async Task PreparationFailuresRetainTheirTypedCategory(
        RequestPreparationInterpretationFailure interpretationFailure,
        ApplicationFailureKind expectedFailure)
    {
        var scenario = new IntakeScenario(interpretationFailure);

        var result = await scenario.PrepareAsync();

        Assert.False(result.IsReady);
        Assert.Equal(expectedFailure, result.FailureKind);
        Assert.Equal(0, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task PreparationReturnsTypedFailureWhenTheSingleSaveFails()
    {
        var scenario = new IntakeScenario
        {
            SaveFailure = new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                "forced_save_failure",
                "The test save failed."),
        };

        var result = await scenario.PrepareAsync();

        Assert.False(result.IsReady);
        Assert.Equal(ApplicationFailureKind.DependencyFailure, result.FailureKind);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task PreparationPropagatesCallerCancellation()
    {
        var scenario = new IntakeScenario();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => scenario.PrepareAsync(cancellation.Token));

        Assert.Equal(0, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task ClarificationReplacesTheCompleteCandidateIncludingNullClearing()
    {
        var session = CreateCollectingSession();
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            IntakeScenario.CurrentTime,
            "existing-candidate");
        var clarification = new RequestClarificationProposal(
            RequestClarificationTarget.EnvironmentId,
            "Which production environment should be used?");
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    "client-alpha",
                    environmentId: null,
                    requestedRoleId: null,
                    "Investigate the active production incident.",
                    incidentId: null),
                clarification),
            initialSession: session);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.ClarificationRequired,
            result.Kind);
        Assert.Same(clarification, result.Clarification);
        Assert.Equal("client-alpha", session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            session.Justification);
        Assert.Null(session.IncidentId);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task ValidEnvironmentOptionRemainsOutsideDurableCandidateScope()
    {
        const string justification =
            "Investigate elevated error rates in the production service.";
        var clarification = new RequestClarificationProposal(
            RequestClarificationTarget.EnvironmentId,
            "Did you mean the available production environment?",
            ["PROD-ALPHA-EU"]);
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    clientId: null,
                    environmentId: null,
                    requestedRoleId: null,
                    justification,
                    incidentId: null),
                clarification));

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.ClarificationRequired,
            result.Kind);
        Assert.Same(clarification, result.Clarification);
        var choice = Assert.Single(result.EnvironmentChoices);
        Assert.Equal("PROD-ALPHA-EU", choice.EnvironmentId);
        Assert.Equal("Primary Production EU", choice.EnvironmentDisplayName);
        Assert.Equal("client-alpha", choice.ClientId);
        Assert.Equal("Client Alpha", choice.ClientDisplayName);
        Assert.Equal(["PROD-ALPHA-EU"], scenario.EnvironmentContextLookupIds);
        Assert.Equal(RequestIntakeStatus.Collecting, scenario.Session.Status);
        Assert.Null(scenario.Session.ClientId);
        Assert.Null(scenario.Session.EnvironmentId);
        Assert.Null(scenario.Session.RequestedRoleId);
        Assert.Equal(justification, scenario.Session.Justification);
        Assert.Null(scenario.Session.ReservedRequestId);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task UnknownEnvironmentOptionIsRejectedWithoutClearingValidFields()
    {
        const string justification =
            "Investigate elevated error rates in the production service.";
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    clientId: null,
                    environmentId: null,
                    requestedRoleId: null,
                    justification,
                    incidentId: null),
                new RequestClarificationProposal(
                    RequestClarificationTarget.EnvironmentId,
                    "Choose the suggested production environment.",
                    ["PROD-UNKNOWN"])));

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.CandidateRejected, result.Kind);
        Assert.Contains(
            result.ValidationErrors,
            error => error.Field == "environmentOptionIds");
        Assert.Empty(result.EnvironmentChoices);
        Assert.Equal(["PROD-UNKNOWN"], scenario.EnvironmentContextLookupIds);
        Assert.Equal(RequestIntakeStatus.Collecting, scenario.Session.Status);
        Assert.Null(scenario.Session.ClientId);
        Assert.Null(scenario.Session.EnvironmentId);
        Assert.Null(scenario.Session.RequestedRoleId);
        Assert.Equal(justification, scenario.Session.Justification);
        Assert.Null(scenario.Session.ReservedRequestId);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task DeterministicReadinessOverridesACompleteClarificationProposal()
    {
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    ProductionRoleIds.ReadOnly,
                    "Investigate the active production incident.",
                    "INC-1042"),
                new RequestClarificationProposal(
                    RequestClarificationTarget.IncidentId,
                    "Which incident is related to this request?")));

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(
            RequestPreparationResultKind.ReadyForConfirmation,
            result.Kind);
        Assert.Equal(RequestIntakeStatus.Ready, result.Session!.Status);
        Assert.NotNull(result.Session.ReservedRequestId);
    }

    [Fact]
    public async Task CandidateKindCannotOverrideDeterministicValidationRejection()
    {
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Candidate,
                new RequestCandidate(
                    "client-alpha",
                    "PROD-UNKNOWN",
                    ProductionRoleIds.ReadOnly,
                    "Investigate the active production incident.",
                    incidentId: null),
                clarification: null));

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.CandidateRejected, result.Kind);
        Assert.Contains(
            result.ValidationErrors,
            error => error.Code == "environment_not_found");
        Assert.Equal(RequestIntakeStatus.Collecting, scenario.Session.Status);
        Assert.Null(scenario.Session.ReservedRequestId);
        Assert.Null(scenario.Session.EnvironmentId);
        Assert.Equal("client-alpha", scenario.Session.ClientId);
        Assert.Equal(ProductionRoleIds.ReadOnly, scenario.Session.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            scenario.Session.Justification);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task NaturalLanguageEditCarriesReadyCandidateIntoReplacementPreparation()
    {
        var previous = CreateCollectingSession();
        previous.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            IntakeScenario.CurrentTime,
            "previous-candidate");
        var previousRequestId = Guid.NewGuid();
        previous.MarkReady(
            previousRequestId,
            IntakeScenario.CurrentTime,
            "previous-ready");
        const string revisedJustification =
            "Diagnose customer-facing errors during the active incident.";
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Candidate,
                new RequestCandidate(
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    ProductionRoleIds.ReadOnly,
                    revisedJustification,
                    "INC-1042"),
                clarification: null),
            initialSession: previous);

        var result = await scenario.PrepareResultAsync();

        var turn = Assert.Single(scenario.InterpretationTurns);
        Assert.Equal("client-alpha", turn.Candidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", turn.Candidate.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, turn.Candidate.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            turn.Candidate.Justification);
        Assert.Equal("INC-1042", turn.Candidate.IncidentId);
        Assert.Equal(RequestIntakeStatus.Superseded, previous.Status);
        Assert.Equal(previousRequestId, previous.ReservedRequestId);
        Assert.Null(previous.ClientId);
        Assert.Null(previous.EnvironmentId);
        Assert.Null(previous.RequestedRoleId);
        Assert.Null(previous.Justification);
        Assert.Null(previous.IncidentId);
        Assert.Equal(
            RequestPreparationResultKind.ReadyForConfirmation,
            result.Kind);
        Assert.NotEqual(previous.Id, result.Session!.Id);
        Assert.Equal(RequestIntakeStatus.Ready, result.Session.Status);
        Assert.Equal("client-alpha", result.Session.ClientId);
        Assert.Equal("PROD-ALPHA-EU", result.Session.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, result.Session.RequestedRoleId);
        Assert.Equal(revisedJustification, result.Session.Justification);
        Assert.Equal("INC-1042", result.Session.IncidentId);
        Assert.Equal(2, scenario.SaveCount);
    }

    [Fact]
    public async Task DraftDiscussionPreservesReadyCandidateAndConfirmationIdentity()
    {
        var ready = CreateReadySession(IntakeScenario.CurrentTime);
        var preparationId = ready.Id;
        var requestId = ready.ReservedRequestId;
        const string discussionMessage =
            "ProductionSupport is another available role for this environment.";
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    ready.ClientId,
                    ready.EnvironmentId,
                    ready.RequestedRoleId,
                    ready.Justification,
                    ready.IncidentId),
                new RequestClarificationProposal(
                    RequestClarificationTarget.RequestedRoleId,
                    discussionMessage)),
            initialSession: ready);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.DraftDiscussion, result.Kind);
        Assert.Equal(discussionMessage, result.DiscussionMessage);
        Assert.Same(ready, result.Session);
        Assert.Equal(RequestIntakeStatus.Ready, ready.Status);
        Assert.Equal(preparationId, ready.Id);
        Assert.Equal(requestId, ready.ReservedRequestId);
        Assert.Equal("client-alpha", ready.ClientId);
        Assert.Equal("PROD-ALPHA-EU", ready.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, ready.RequestedRoleId);
        Assert.Equal("INC-1042", ready.IncidentId);
        Assert.Equal(0, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task EquivalentCandidateLeavesReadyDraftActiveWithoutNewCard()
    {
        var ready = CreateReadySession(IntakeScenario.CurrentTime);
        var scenario = new IntakeScenario(initialSession: ready);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.DraftDiscussion, result.Kind);
        Assert.Equal(
            "The current draft remains ready for confirmation.",
            result.DiscussionMessage);
        Assert.Equal(RequestIntakeStatus.Ready, ready.Status);
        Assert.Equal(0, scenario.SaveCount);
    }

    [Fact]
    public async Task InvalidDiscussionOptionIsSuppressedWithoutChangingReadyDraft()
    {
        var ready = CreateReadySession(IntakeScenario.CurrentTime);
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                new RequestCandidate(
                    ready.ClientId,
                    ready.EnvironmentId,
                    ready.RequestedRoleId,
                    ready.Justification,
                    ready.IncidentId),
                new RequestClarificationProposal(
                    RequestClarificationTarget.EnvironmentId,
                    "A suggested alternative is available.",
                    ["PROD-UNKNOWN"])),
            initialSession: ready);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.DraftDiscussion, result.Kind);
        Assert.Equal(
            "The suggested alternatives could not be validated.",
            result.DiscussionMessage);
        Assert.Empty(result.EnvironmentChoices);
        Assert.Equal(
            "PROD-UNKNOWN",
            Assert.Single(scenario.EnvironmentContextLookupIds));
        Assert.Equal(RequestIntakeStatus.Ready, ready.Status);
        Assert.Equal(0, scenario.SaveCount);
    }

    [Fact]
    public async Task DiscussionFailureLeavesReadyDraftConfirmable()
    {
        var ready = CreateReadySession(IntakeScenario.CurrentTime);
        var scenario = new IntakeScenario(
            interpretationFailure:
                RequestPreparationInterpretationFailure.Unavailable,
            initialSession: ready);

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.Failed, result.Kind);
        Assert.Equal(RequestIntakeService.ModelUnavailableCode, result.Failure!.Code);
        Assert.Equal(RequestIntakeStatus.Ready, ready.Status);
        Assert.NotNull(ready.ReservedRequestId);
        Assert.Equal(0, scenario.SaveCount);
    }

    [Fact]
    public async Task ConfirmationLazilyExpiresReadySessionAtTheExactDeadline()
    {
        var readyAt = IntakeScenario.CurrentTime
            .Subtract(RequestIntakeSession.ConfirmationLifetime);
        var session = CreateReadySession(readyAt);
        var reservedRequestId = session.ReservedRequestId;
        var scenario = new IntakeScenario(initialSession: session);

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.InvalidTransition, result.Failure!.Kind);
        Assert.Equal(RequestIntakeService.ExpiredCode, result.Failure.Code);
        Assert.Equal(RequestIntakeStatus.Expired, session.Status);
        Assert.Equal(reservedRequestId, session.ReservedRequestId);
        Assert.Null(session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Null(session.Justification);
        Assert.Null(session.IncidentId);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task NewPreparationLazilyExpiresOldReadySessionBeforeStartingOver()
    {
        var previous = CreateReadySession(
            IntakeScenario.CurrentTime
                .Subtract(RequestIntakeSession.ConfirmationLifetime));
        var previousRequestId = previous.ReservedRequestId;
        var scenario = new IntakeScenario(initialSession: previous);

        var result = await scenario.PrepareResultAsync();

        var turn = Assert.Single(scenario.InterpretationTurns);
        Assert.Null(turn.Candidate.ClientId);
        Assert.Null(turn.Candidate.EnvironmentId);
        Assert.Null(turn.Candidate.RequestedRoleId);
        Assert.Null(turn.Candidate.Justification);
        Assert.Null(turn.Candidate.IncidentId);
        Assert.Equal(RequestIntakeStatus.Expired, previous.Status);
        Assert.Equal(previousRequestId, previous.ReservedRequestId);
        Assert.Null(previous.ClientId);
        Assert.Equal(
            RequestPreparationResultKind.ReadyForConfirmation,
            result.Kind);
        Assert.NotEqual(previous.Id, result.Session!.Id);
        Assert.Equal(RequestIntakeStatus.Ready, result.Session.Status);
        Assert.Equal(2, scenario.SaveCount);
    }

    [Fact]
    public async Task SubmittedConfirmationReplayReturnsTheReservedRequestIdentity()
    {
        var scenario = new IntakeScenario();
        var ready = await scenario.PrepareAsync();

        var first = await scenario.ConfirmResultAsync(IntakeScenario.Owner);
        var saveCountAfterFirstConfirmation = scenario.SaveCount;
        var replay = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Submitted, first.Kind);
        Assert.Equal(ready.ReservedRequestId, first.RequestId);
        Assert.Equal(RequestConfirmationResultKind.AlreadySubmitted, replay.Kind);
        Assert.True(replay.WasAlreadySubmitted);
        Assert.Equal(ready.ReservedRequestId, replay.RequestId);
        Assert.Equal(saveCountAfterFirstConfirmation, scenario.SaveCount);
        Assert.Single(scenario.Requests);
        Assert.Single(scenario.AuditEvents);
    }

    [Theory]
    [InlineData(false, RequestIntakeStatus.Superseded)]
    [InlineData(true, RequestIntakeStatus.Expired)]
    public async Task StartOverReturnsPersistenceFailureForTerminalLifecycleSave(
        bool expired,
        RequestIntakeStatus expectedStatus)
    {
        var readyAt = expired
            ? IntakeScenario.CurrentTime
                .Subtract(RequestIntakeSession.ConfirmationLifetime)
            : IntakeScenario.CurrentTime;
        var session = CreateReadySession(readyAt);
        var scenario = new IntakeScenario(
            proposal: new RequestPreparationProposal(
                RequestPreparationProposalKind.Candidate,
                new RequestCandidate(
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    ProductionRoleIds.ReadOnly,
                    "Verify the mitigation for the active production incident.",
                    "INC-1042"),
                clarification: null),
            initialSession: session)
        {
            SaveFailure = ForcedSaveFailure(),
        };

        var result = await scenario.PrepareResultAsync();

        Assert.Equal(RequestPreparationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.DependencyFailure, result.Failure!.Kind);
        Assert.Equal("forced_save_failure", result.Failure!.Code);
        Assert.Equal(expectedStatus, session.Status);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Same(session, scenario.Session);
    }

    [Fact]
    public async Task LazyExpiryReturnsPersistenceFailureWhenStatusCannotBeSaved()
    {
        var session = CreateReadySession(
            IntakeScenario.CurrentTime
                .Subtract(RequestIntakeSession.ConfirmationLifetime));
        var scenario = new IntakeScenario(initialSession: session)
        {
            SaveFailure = ForcedSaveFailure(),
        };

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.DependencyFailure, result.Failure!.Kind);
        Assert.Equal("forced_save_failure", result.Failure!.Code);
        Assert.Equal(RequestIntakeStatus.Expired, session.Status);
        Assert.Equal(1, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task InvalidationReturnsPersistenceFailureWhenStatusCannotBeSaved()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        scenario.RoleIsAvailable = false;
        scenario.SaveFailure = ForcedSaveFailure();

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.DependencyFailure, result.Failure!.Kind);
        Assert.Equal("forced_save_failure", result.Failure!.Code);
        Assert.Equal(RequestIntakeStatus.Invalidated, scenario.Session.Status);
        Assert.Equal(2, scenario.SaveCount);
        Assert.Empty(scenario.Requests);
        Assert.Empty(scenario.AuditEvents);
    }

    [Fact]
    public async Task SubmissionReturnsPersistenceFailureWhenAtomicSaveFails()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        scenario.SaveFailure = ForcedSaveFailure();
        scenario.RecoveryFailure = new ApplicationFailure(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_intake_concurrency_unresolved",
            "No competing confirmation was committed.");

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.DependencyFailure, result.Failure!.Kind);
        Assert.Equal("forced_save_failure", result.Failure!.Code);
        Assert.Equal(RequestIntakeStatus.Submitted, scenario.Session.Status);
        Assert.Equal(2, scenario.SaveCount);
        Assert.Equal(1, scenario.RecoveryCount);
        Assert.Single(scenario.Requests);
        Assert.Single(scenario.AuditEvents);
    }

    [Fact]
    public async Task DependencyFailureReturnsRecoveredSubmittedIdentity()
    {
        var scenario = new IntakeScenario();
        var ready = await scenario.PrepareAsync();
        scenario.SaveFailure = ForcedSaveFailure();
        scenario.RecoveredRequestId = ready.ReservedRequestId;

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.AlreadySubmitted, result.Kind);
        Assert.Equal(ready.ReservedRequestId, result.RequestId);
        Assert.Equal(1, scenario.RecoveryCount);
    }

    [Fact]
    public async Task ConcurrentConfirmationReturnsRecoveredSubmittedIdentity()
    {
        var scenario = new IntakeScenario();
        var ready = await scenario.PrepareAsync();
        scenario.SaveFailure = new ApplicationFailure(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_intake_concurrency_conflict",
            "The request intake changed while it was being saved.");
        scenario.RecoveredRequestId = ready.ReservedRequestId;

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.AlreadySubmitted, result.Kind);
        Assert.True(result.WasAlreadySubmitted);
        Assert.Equal(ready.ReservedRequestId, result.RequestId);
        Assert.Equal(1, scenario.RecoveryCount);
        Assert.Equal(ready.PreparationId, scenario.RecoverySessionId);
        Assert.Same(IntakeScenario.Owner, scenario.RecoveryActor);
    }

    [Fact]
    public async Task ConcurrentConfirmationReturnsClosedRecoveryFailure()
    {
        var scenario = new IntakeScenario();
        _ = await scenario.PrepareAsync();
        scenario.SaveFailure = new ApplicationFailure(
            ApplicationFailureKind.ConcurrencyConflict,
            "request_intake_concurrency_conflict",
            "The request intake changed while it was being saved.");
        scenario.RecoveryFailure = new ApplicationFailure(
            ApplicationFailureKind.NotFound,
            "request_intake_not_found",
            "The request intake was not found.");

        var result = await scenario.ConfirmResultAsync(IntakeScenario.Owner);

        Assert.Equal(RequestConfirmationResultKind.Failed, result.Kind);
        Assert.Equal(ApplicationFailureKind.NotFound, result.Failure!.Kind);
        Assert.Equal("request_intake_not_found", result.Failure.Code);
        Assert.Equal(1, scenario.RecoveryCount);
        Assert.Equal(Guid.Empty, result.RequestId);
    }

    [Fact]
    public void TerminalAggregateRetainsIdentityAndClearsSensitiveCandidate()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            27,
            10,
            0,
            0,
            TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            "tenant",
            "actor",
            "conversation",
            "requester",
            occurredAt,
            "created");
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            occurredAt,
            "candidate");
        session.MarkReady(requestId, occurredAt, "ready");

        session.MarkSubmitted(occurredAt.AddMinutes(1), "submitted");

        Assert.Equal(RequestIntakeStatus.Submitted, session.Status);
        Assert.Equal(requestId, session.ReservedRequestId);
        Assert.Null(session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Null(session.Justification);
        Assert.Null(session.IncidentId);
        Assert.Throws<InvalidOperationException>(
            () => session.MarkSubmitted(
                occurredAt.AddMinutes(2),
                "duplicate-submit"));
    }

    private sealed record PreparationObservation(
        bool IsReady,
        Guid PreparationId,
        Guid ReservedRequestId,
        string? ClientId,
        string? EnvironmentId,
        string? RoleId,
        string? Justification,
        string? IncidentId,
        ApplicationFailureKind? FailureKind);

    private sealed record ConfirmationObservation(
        bool IsSuccess,
        Guid? RequestId,
        ApplicationFailureKind? FailureKind);

    private sealed class IntakeScenario :
        IRequestPreparationInterpreter,
        IRequestContextReader,
        IRequestIntakeStore,
        IWorkflowStore,
        IClock
    {
        public static readonly DateTimeOffset CurrentTime =
            new(2026, 7, 27, 10, 5, 0, TimeSpan.Zero);

        private readonly RequestPreparationInterpretationResult interpretation;
        private readonly RequestIntakeService service;
        private RequestIntakeSession? session;

        public IntakeScenario(
            RequestPreparationInterpretationFailure? interpretationFailure = null,
            RequestPreparationProposal? proposal = null,
            RequestIntakeSession? initialSession = null)
        {
            if (interpretationFailure is not null
                && proposal is not null)
            {
                throw new ArgumentException(
                    "A scenario cannot define both a proposal and an interpretation failure.");
            }
            interpretation = interpretationFailure is not null
                ? new RequestPreparationInterpretationFailed(
                    interpretationFailure.Value)
                : new RequestPreparationInterpretationSucceeded(
                    proposal ?? ValidCandidateProposal());
            session = initialSession;

            var validator = new RequestValidator(this);
            var submissionService = new RequestSubmissionService(
                validator,
                this,
                this);
            service = new RequestIntakeService(
                this,
                validator,
                this,
                this,
                submissionService,
                this);
        }

        public static AuthenticatedChannelActor Owner { get; } =
            new(
                RequestIntakeSession.TeamsChannel,
                "tenant-001",
                "actor-001",
                "conversation-001",
                "requester");

        public bool RoleIsAvailable { get; set; } = true;

        public int InterpreterCallCount { get; private set; }

        public List<RequestPreparationTurn> InterpretationTurns { get; } = [];

        public List<string> EnvironmentContextLookupIds { get; } = [];

        public ApplicationFailure? ActiveLoadFailure { get; set; }

        public string? StatusWhenAdded { get; private set; }

        public string? IntakeStatus => session?.Status.ToString();

        public int SaveCount { get; private set; }

        public ApplicationFailure? SaveFailure { get; set; }

        public Guid? RecoveredRequestId { get; set; }

        public ApplicationFailure? RecoveryFailure { get; set; }

        public int RecoveryCount { get; private set; }

        public Guid? RecoverySessionId { get; private set; }

        public AuthenticatedChannelActor? RecoveryActor { get; private set; }

        public List<AccessRequest> Requests { get; } = [];

        public List<AuditEvent> AuditEvents { get; } = [];

        public RequestIntakeSession Session => session
            ?? throw new InvalidOperationException("No intake session exists.");

        public DateTimeOffset UtcNow => CurrentTime;

        public async Task<PreparationObservation> PrepareAsync(
            CancellationToken? cancellationToken = null)
        {
            var outcome = await PrepareResultAsync(cancellationToken);

            return outcome.Kind switch
            {
                RequestPreparationResultKind.ReadyForConfirmation =>
                    new PreparationObservation(
                    true,
                    outcome.Session!.Id,
                    outcome.Session.ReservedRequestId!.Value,
                    outcome.Session.ClientId,
                    outcome.Session.EnvironmentId,
                    outcome.Session.RequestedRoleId,
                    outcome.Session.Justification,
                    outcome.Session.IncidentId,
                    null),
                RequestPreparationResultKind.Failed =>
                    new PreparationObservation(
                    false,
                    Guid.Empty,
                    Guid.Empty,
                    null,
                    null,
                    null,
                    null,
                    null,
                    outcome.Failure!.Kind),
                _ => throw new InvalidOperationException(
                    "The scenario expected either readiness or a typed failure."),
            };
        }

        public Task<RequestPreparationResult> PrepareResultAsync(
            CancellationToken? cancellationToken = null) =>
            service.PrepareAsync(
                new PrepareAccessRequestCommand(
                    Owner,
                    "I need production access.",
                    "prepare-correlation"),
                cancellationToken ?? TestContext.Current.CancellationToken);

        public async Task<ConfirmationObservation> ConfirmAsync(
            AuthenticatedChannelActor actor)
        {
            var outcome = await ConfirmResultAsync(actor);

            return outcome.Kind switch
            {
                RequestConfirmationResultKind.Submitted
                    or RequestConfirmationResultKind.AlreadySubmitted =>
                    new ConfirmationObservation(
                        true,
                        outcome.RequestId,
                        null),
                RequestConfirmationResultKind.Failed =>
                    new ConfirmationObservation(
                        false,
                        null,
                        outcome.Failure!.Kind),
                _ => throw new InvalidOperationException(
                    "The scenario received an unsupported confirmation outcome."),
            };
        }

        public Task<RequestConfirmationResult> ConfirmResultAsync(
            AuthenticatedChannelActor actor)
        {
            if (session is null)
            {
                throw new InvalidOperationException(
                    "The scenario must be prepared before confirmation.");
            }

            return service.ConfirmAsync(
                new ConfirmRequestIntakeCommand(
                    actor,
                    session.Id,
                    "confirm-correlation"),
                TestContext.Current.CancellationToken);
        }

        public Task<RequestIntakeResetResult> ResetResultAsync(
            AuthenticatedChannelActor actor,
            CancellationToken? cancellationToken = null) =>
            service.ResetAsync(
                new ResetRequestIntakeCommand(
                    actor,
                    "reset-correlation"),
                cancellationToken ?? TestContext.Current.CancellationToken);

        public void AttemptReadyScopeChange()
        {
            var current = session
                ?? throw new InvalidOperationException("No intake exists.");
            current.UpdateCandidate(
                "other-client",
                "OTHER-ENVIRONMENT",
                ProductionRoleIds.Support,
                "Replace the already prepared scope.",
                incidentId: null,
                CurrentTime.AddMinutes(1),
                "forged-change");
        }

        public Task<RequestPreparationInterpretationResult> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InterpreterCallCount++;
            InterpretationTurns.Add(turn);
            return Task.FromResult(interpretation);
        }

        public void Add(RequestIntakeSession value)
        {
            StatusWhenAdded = value.Status.ToString();
            session = value;
        }

        public Task<ApplicationResult<RequestIntakeSession>>
            GetActiveAsync(
                AuthenticatedChannelActor actor,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ActiveLoadFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<RequestIntakeSession>(
                        ActiveLoadFailure));
            }

            return FromOptional(
                session is { Status: RequestIntakeStatus.Collecting or RequestIntakeStatus.Ready }
                && session.IsOwnedBy(
                    actor.Channel,
                    actor.TenantId,
                    actor.ChannelActorId,
                    actor.ConversationId,
                    actor.RequesterId)
                    ? session
                    : null,
                "active_intake_not_found",
                cancellationToken);
        }

        public Task<ApplicationResult<RequestIntakeSession>>
            GetAsync(
                Guid sessionId,
                CancellationToken cancellationToken) =>
            FromOptional(
                session?.Id == sessionId ? session : null,
                "intake_not_found",
                cancellationToken);

        public Task<ApplicationResult<Guid>> RecoverSubmittedRequestAsync(
            Guid sessionId,
            AuthenticatedChannelActor actor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoveryCount++;
            RecoverySessionId = sessionId;
            RecoveryActor = actor;

            if (RecoveryFailure is not null)
            {
                return Task.FromResult(
                    ApplicationResult.Failed<Guid>(RecoveryFailure));
            }

            if (RecoveredRequestId is not { } requestId)
            {
                throw new InvalidOperationException(
                    "The test scenario did not configure concurrency recovery.");
            }

            return Task.FromResult(ApplicationResult.Succeeded(requestId));
        }

        public Task<ApplicationResult> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(
                SaveFailure is null
                    ? ApplicationResult.Succeeded()
                    : ApplicationResult.Failed(SaveFailure));
        }

        public Task<ApplicationResult<Client>> GetClientAsync(
            string clientId,
            CancellationToken cancellationToken) =>
            Match(
                clientId,
                "client-alpha",
                new Client("client-alpha", "Client Alpha", "business-approver"),
                cancellationToken);

        public Task<ApplicationResult<ProductionEnvironment>>
            GetProductionEnvironmentAsync(
                string environmentId,
                CancellationToken cancellationToken) =>
            Match(
                environmentId,
                "PROD-ALPHA-EU",
                new ProductionEnvironment(
                    "PROD-ALPHA-EU",
                    "client-alpha",
                    "Primary Production EU"),
                cancellationToken);

        public Task<ApplicationResult<ProductionEnvironmentContext>>
            GetProductionEnvironmentContextAsync(
                string environmentId,
                CancellationToken cancellationToken)
        {
            EnvironmentContextLookupIds.Add(environmentId);
            return FromOptional(
                environmentId == "PROD-ALPHA-EU"
                    ? CreateEnvironmentContext()
                    : null,
                "environment_not_found",
                cancellationToken);
        }

        public Task<ApplicationResult<IReadOnlyList<ProductionEnvironmentContext>>>
            ListProductionEnvironmentContextsAsync(
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ProductionEnvironmentContext> contexts =
                [CreateEnvironmentContext()];
            return Task.FromResult(ApplicationResult.Succeeded(contexts));
        }

        public Task<ApplicationResult<EnvironmentRole>> GetEnvironmentRoleAsync(
            string environmentId,
            string roleId,
            CancellationToken cancellationToken) =>
            FromOptional(
                RoleIsAvailable
                && environmentId == "PROD-ALPHA-EU"
                && roleId == ProductionRoleIds.ReadOnly
                    ? new EnvironmentRole(environmentId, roleId)
                    : null,
                "role_not_found",
                cancellationToken);

        public Task<ApplicationResult<Incident>> GetIncidentAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            Match(
                incidentId,
                "INC-1042",
                new Incident(
                    "INC-1042",
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    "Active production incident",
                    IncidentStatus.Active),
                cancellationToken);

        public Task<ApplicationResult<AuthenticatedPrincipal>> GetPrincipalAsync(
            string principalId,
            CancellationToken cancellationToken) =>
            Match(
                principalId,
                Owner.RequesterId,
                new AuthenticatedPrincipal(
                    Owner.RequesterId,
                    "Requester",
                    PrincipalKind.Requester),
                cancellationToken);

        private ProductionEnvironmentContext CreateEnvironmentContext()
        {
            var environment = new ProductionEnvironment(
                "PROD-ALPHA-EU",
                "client-alpha",
                "Primary Production EU");
            EnvironmentRole[] assignedRoles = RoleIsAvailable
                ? [new EnvironmentRole(
                    environment.Id,
                    ProductionRoleIds.ReadOnly)]
                : [];
            return new ProductionEnvironmentContext(
                environment,
                new Client("client-alpha", "Client Alpha", "business-approver"),
                assignedRoles);
        }

        public void AddRequest(AccessRequest request) => Requests.Add(request);

        public void AddAuditEvent(AuditEvent auditEvent) =>
            AuditEvents.Add(auditEvent);

        public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AccessRequest>>>
            ListRequestsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddApprovalDecision(ApprovalDecision decision) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ApprovalDecision>>
            GetApprovalDecisionAsync(
                Guid requestId,
                ApprovalStage stage,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>>
            ListApprovalDecisionsAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddProvisioningOperation(ProvisioningOperation operation) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>>
            GetProvisioningOperationAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<ProvisioningOperation>>
            ReloadProvisioningOperationAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddAccessGrant(AccessGrant grant) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<AccessGrant>>
            GetAccessGrantForRequestAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationResult<IReadOnlyList<AuditEvent>>>
            ListAuditEventsAsync(
                Guid requestId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static Task<ApplicationResult<T>> Match<T>(
            string actual,
            string expected,
            T value,
            CancellationToken cancellationToken)
            where T : class =>
            FromOptional(
                string.Equals(actual, expected, StringComparison.Ordinal)
                    ? value
                    : default,
                "authoritative_record_not_found",
                cancellationToken);

        private static Task<ApplicationResult<T>> FromOptional<T>(
            T? value,
            string code,
            CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                value is null
                    ? ApplicationResult.Failed<T>(
                        new ApplicationFailure(
                            ApplicationFailureKind.NotFound,
                            code,
                            "The authoritative record was not found."))
                    : ApplicationResult.Succeeded(value));
        }

        private static RequestPreparationProposal ValidCandidateProposal() =>
            new(
                RequestPreparationProposalKind.Candidate,
                new RequestCandidate(
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    ProductionRoleIds.ReadOnly,
                    "Investigate the active production incident.",
                    "INC-1042"),
                clarification: null);
    }

    private static RequestIntakeSession CreateCollectingSession() =>
        new(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            IntakeScenario.Owner.TenantId,
            IntakeScenario.Owner.ChannelActorId,
            IntakeScenario.Owner.ConversationId,
            IntakeScenario.Owner.RequesterId,
            IntakeScenario.CurrentTime,
            "created");

    private static RequestIntakeSession CreateCollectingSessionWithCandidate()
    {
        var session = CreateCollectingSession();
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            IntakeScenario.CurrentTime,
            "candidate");
        return session;
    }

    private static RequestIntakeSession CreateReadySession(
        DateTimeOffset readyAt)
    {
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            IntakeScenario.Owner.TenantId,
            IntakeScenario.Owner.ChannelActorId,
            IntakeScenario.Owner.ConversationId,
            IntakeScenario.Owner.RequesterId,
            readyAt,
            "created");
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            readyAt,
            "candidate");
        session.MarkReady(Guid.NewGuid(), readyAt, "ready");
        return session;
    }

    private static RequestIntakeSession CreateSubmittedSession()
    {
        var session = CreateReadySession(IntakeScenario.CurrentTime);
        session.MarkSubmitted(
            IntakeScenario.CurrentTime,
            "submitted");
        return session;
    }

    private static ApplicationFailure ForcedSaveFailure() =>
        new(
            ApplicationFailureKind.DependencyFailure,
            "forced_save_failure",
            "The test save failed.");
}

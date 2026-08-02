using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.UnitTests;

public sealed class RequestPreparationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProposalKindRequiresExactlyItsMatchingClarificationPayload()
    {
        var candidate = EmptyCandidate();
        var clarification = new RequestClarificationProposal(
            RequestClarificationTarget.ClientId,
            "Which client requires production access?");

        Assert.Throws<ArgumentException>(
            () => new RequestPreparationProposal(
                RequestPreparationProposalKind.Clarification,
                candidate,
                clarification: null));
        Assert.Throws<ArgumentException>(
            () => new RequestPreparationProposal(
                RequestPreparationProposalKind.Candidate,
                candidate,
                clarification));

        var clarificationProposal = new RequestPreparationProposal(
            RequestPreparationProposalKind.Clarification,
            candidate,
            clarification);
        var candidateProposal = new RequestPreparationProposal(
            RequestPreparationProposalKind.Candidate,
            candidate,
            clarification: null);

        Assert.Same(clarification, clarificationProposal.Clarification);
        Assert.Null(candidateProposal.Clarification);
    }

    [Fact]
    public void ClarificationProposalRequiresAClosedTargetAndBoundedMessage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RequestClarificationProposal(
                (RequestClarificationTarget)int.MaxValue,
                "Clarify the request."));
        Assert.Throws<ArgumentException>(
            () => new RequestClarificationProposal(
                RequestClarificationTarget.EnvironmentId,
                "   "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RequestClarificationProposal(
                RequestClarificationTarget.EnvironmentId,
                new string(
                    'x',
                    RequestClarificationProposal.MaximumMessageLength + 1)));
    }

    [Fact]
    public void PreparationTurnSnapshotsRunScopedFeedbackAndHistoryAvailability()
    {
        var feedback = new List<RequestValidationFeedback>
        {
            new(
                "environmentId",
                "environment_not_found",
                "The environment was not found."),
        };
        var turn = new RequestPreparationTurn(
            Guid.NewGuid(),
            "  use the first environment  ",
            EmptyCandidate(),
            feedback,
            "  correlation-001  ");

        feedback.Clear();

        Assert.Equal("use the first environment", turn.LatestMessage);
        var capturedFeedback = Assert.Single(turn.ValidationFeedback);
        Assert.Equal("environmentId", capturedFeedback.Field);
        Assert.Equal("environment_not_found", capturedFeedback.Code);
        Assert.Equal(
            "The environment was not found.",
            capturedFeedback.Message);
        Assert.Equal("correlation-001", turn.CorrelationId);
    }

    [Theory]
    [InlineData(RequestIntakeStatus.Submitted)]
    [InlineData(RequestIntakeStatus.Superseded)]
    [InlineData(RequestIntakeStatus.Expired)]
    [InlineData(RequestIntakeStatus.Invalidated)]
    public void TerminalTransitionsRetainBindingAndDisposeCandidateContent(
        RequestIntakeStatus terminalStatus)
    {
        var session = CreateCollectingSession();
        var reservedRequestId = Guid.NewGuid();
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            CreatedAt.AddMinutes(1),
            "candidate");

        if (terminalStatus != RequestIntakeStatus.Superseded)
        {
            session.MarkReady(
                reservedRequestId,
                CreatedAt.AddMinutes(2),
                "ready");
        }

        switch (terminalStatus)
        {
            case RequestIntakeStatus.Submitted:
                session.MarkSubmitted(
                    CreatedAt.AddMinutes(3),
                    "submitted");
                break;
            case RequestIntakeStatus.Superseded:
                session.MarkSuperseded(
                    CreatedAt.AddMinutes(2),
                    "superseded");
                break;
            case RequestIntakeStatus.Expired:
                session.MarkExpired(
                    CreatedAt
                        .AddMinutes(2)
                        .Add(RequestIntakeSession.ConfirmationLifetime),
                    "expired");
                break;
            case RequestIntakeStatus.Invalidated:
                session.MarkInvalidated(
                    CreatedAt.AddMinutes(3),
                    "invalidated");
                break;
            default:
                throw new InvalidOperationException(
                    "The test terminal status is unsupported.");
        }

        Assert.Equal(terminalStatus, session.Status);
        Assert.Equal("tenant-001", session.TenantId);
        Assert.Equal("actor-001", session.ChannelActorId);
        Assert.Equal("conversation-001", session.ConversationId);
        Assert.Equal("requester", session.RequesterId);
        Assert.Null(session.ClientId);
        Assert.Null(session.EnvironmentId);
        Assert.Null(session.RequestedRoleId);
        Assert.Null(session.Justification);
        Assert.Null(session.IncidentId);
        Assert.Equal(
            terminalStatus == RequestIntakeStatus.Superseded
                ? null
                : reservedRequestId,
            session.ReservedRequestId);

        AssertAllLifecycleTransitionsRejected(
            session,
            session.LastUpdatedAt.AddMinutes(1));
    }

    [Theory]
    [InlineData("other-channel", "tenant-001", "actor-001", "conversation-001", "requester")]
    [InlineData("msteams", "other-tenant", "actor-001", "conversation-001", "requester")]
    [InlineData("msteams", "tenant-001", "other-actor", "conversation-001", "requester")]
    [InlineData("msteams", "tenant-001", "actor-001", "other-conversation", "requester")]
    [InlineData("msteams", "tenant-001", "actor-001", "conversation-001", "other-requester")]
    public void OwnershipRequiresEveryAuthenticatedBindingComponent(
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId)
    {
        var session = CreateCollectingSession();

        Assert.True(
            session.IsOwnedBy(
                " msteams ",
                " tenant-001 ",
                " actor-001 ",
                " conversation-001 ",
                " requester "));
        Assert.False(
            session.IsOwnedBy(
                channel,
                tenantId,
                channelActorId,
                conversationId,
                requesterId));
    }

    [Fact]
    public void ReadyLifecycleFailsClosedAtTheExactExpiryDeadline()
    {
        var session = CreateCollectingSession();
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            CreatedAt,
            "candidate");
        session.MarkReady(Guid.NewGuid(), CreatedAt, "ready");
        var deadline = Assert.IsType<DateTimeOffset>(session.ExpiresAt);

        Assert.False(session.IsExpired(deadline.AddTicks(-1)));
        Assert.True(session.IsExpired(deadline));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkSubmitted(deadline, "submitted"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkSuperseded(deadline, "superseded"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkInvalidated(deadline, "invalidated"));
        Assert.Equal(RequestIntakeStatus.Ready, session.Status);
        Assert.Equal("client-alpha", session.ClientId);

        session.MarkExpired(deadline, "expired");

        Assert.Equal(RequestIntakeStatus.Expired, session.Status);
        Assert.Null(session.ClientId);
    }

    private static RequestCandidate EmptyCandidate() =>
        new(
            clientId: null,
            environmentId: null,
            requestedRoleId: null,
            justification: null,
            incidentId: null);

    private static RequestIntakeSession CreateCollectingSession() =>
        new(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            "tenant-001",
            "actor-001",
            "conversation-001",
            "requester",
            CreatedAt,
            "created");

    private static void AssertAllLifecycleTransitionsRejected(
        RequestIntakeSession session,
        DateTimeOffset occurredAt)
    {
        Assert.Throws<InvalidOperationException>(
            () => session.UpdateCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042",
                occurredAt,
                "candidate"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkReady(Guid.NewGuid(), occurredAt, "ready"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkSubmitted(occurredAt, "submitted"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkSuperseded(occurredAt, "superseded"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkExpired(occurredAt, "expired"));
        Assert.Throws<InvalidOperationException>(
            () => session.MarkInvalidated(occurredAt, "invalidated"));
    }
}

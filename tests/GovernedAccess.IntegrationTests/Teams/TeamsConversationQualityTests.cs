using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsConversationQualityTests
{
    [Theory]
    [MemberData(nameof(RepresentativeUtterances))]
    public async Task RepresentativeUtteranceReachesAccurateCandidateWithinFiveTurns(
        string[] messages,
        string expectedRoleId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interpreter = new MafRequestPreparationInterpreter(
            new DeterministicChatClient(DeterministicChatMode.HistorySensitive),
            Options.Create(
                new TeamsAccessRequestOptions
                {
                    ModelTimeout = TimeSpan.FromSeconds(5),
                }),
            NullLoggerFactory.Instance,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());
        var intakeId = Guid.NewGuid();
        var candidate = new RequestCandidate(null, null, null, null, null);
        RequestPreparationInterpretationOutcome? final = null;

        Assert.InRange(messages.Length, 1, 5);
        foreach (var message in messages)
        {
            final = await interpreter.InterpretAsync(
                new RequestPreparationTurn(
                    intakeId,
                    message,
                    candidate,
                    validationFeedback: [],
                    Guid.NewGuid().ToString("N")),
                cancellationToken);
            Assert.Equal(
                RequestPreparationInterpretationOutcomeKind.Proposal,
                final.Kind);
            candidate = final.Proposal!.Candidate;
        }

        Assert.NotNull(final);
        Assert.Equal(RequestPreparationProposalKind.Candidate, final.Proposal!.Kind);
        Assert.Equal("client-alpha", candidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", candidate.EnvironmentId);
        Assert.Equal(expectedRoleId, candidate.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            candidate.Justification);
        Assert.Equal("INC-1042", candidate.IncidentId);
    }

    public static TheoryData<string[], string> RepresentativeUtterances =>
        new()
        {
            {
                ["Use PROD-ALPHA-EU with read-only access."],
                ProductionRoleIds.ReadOnly
            },
            {
                ["Use PROD-ALPHA-EU with support access."],
                ProductionRoleIds.Support
            },
            {
                ["Use PROD-ALPHA-EU.", "support"],
                ProductionRoleIds.Support
            },
            {
                ["I need temporary production access.", "PROD-ALPHA-EU", "the first one"],
                ProductionRoleIds.ReadOnly
            },
            {
                ["I need temporary production access.", "the first one", "the other role"],
                ProductionRoleIds.Support
            },
        };
}

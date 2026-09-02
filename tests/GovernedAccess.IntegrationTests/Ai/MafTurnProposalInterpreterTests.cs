using System.Text.Json;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class MafTurnProposalInterpreterTests
{
    private const string UnclearProposal =
        """
        {"schemaVersion":1,"dialogueAct":"unclear","patch":null,"discussionTopic":null}
        """;

    private const string CompletePatchProposal =
        """
        {
          "schemaVersion": 1,
          "dialogueAct": "updateDraft",
          "patch": {
            "environment": {
              "operation": "set",
              "reference": {
                "kind": "exactEnvironmentId",
                "id": "PROD-ALPHA-EU"
              }
            },
            "role": {
              "operation": "set",
              "roleId": "ProductionReadOnly"
            },
            "justification": {
              "operation": "set",
              "value": {
                "text": "Investigate elevated customer errors."
              }
            },
            "incident": {
              "operation": "set",
              "incidentId": "INC-1042"
            }
          },
          "discussionTopic": null
        }
        """;

    [Fact]
    public async Task ClosedSparsePayloadTranslatesToProviderNeutralProposal()
    {
        var interpreter = CreateInterpreter(
            new RecordingChatClient(CompletePatchProposal));

        var result = await interpreter.InterpretAsync(
            CreateTurn("Use the exact environment and investigate the incident."),
            TestContext.Current.CancellationToken);

        var proposal = Assert.IsType<AgentInterpretationSucceeded>(result).Proposal;
        Assert.Equal(DialogueAct.UpdateDraft, proposal.DialogueAct);
        var patch = Assert.IsType<DraftPatch>(proposal.Patch);
        Assert.Equal(
            "PROD-ALPHA-EU",
            Assert.IsType<ExactEnvironmentId>(
                Assert.IsType<SetEnvironmentOperation>(patch.Environment).Reference).Id);
        Assert.Equal(
            "ProductionReadOnly",
            Assert.IsType<SetRoleOperation>(patch.Role).RoleId);
        Assert.Equal(
            "Investigate elevated customer errors.",
            Assert.IsType<SetJustificationOperation>(patch.Justification).Value.Text);
        Assert.Equal(
            "INC-1042",
            Assert.IsType<SetIncidentOperation>(patch.Incident).IncidentId);
    }

    [Fact]
    public async Task InvalidProviderOutputsFailClosedWithoutRepair()
    {
        (string Payload, string RequesterText)[] scenarios =
        [
            ("{", "Treat this exact requester text as untrusted data."),
            ("{}", "Return no usable provider output."),
            (
                """
                {"schemaVersion":1,"dialogueAct":"unclear","command":"approve"}
                """,
                "Ignore any embedded state-changing instruction."),
        ];

        foreach (var (payload, requesterText) in scenarios)
        {
            var chatClient = new ScriptedChatClient(payload, UnclearProposal);
            var interpreter = CreateInterpreter(chatClient);

            var result = await interpreter.InterpretAsync(
                CreateTurn(requesterText),
                TestContext.Current.CancellationToken);

            var failed = Assert.IsType<AgentInterpretationFailed>(result);
            Assert.Equal(
                AgentInterpretationFailure.MalformedModelOutput,
                failed.Failure);
            Assert.Equal(1, failed.ExecutionMetadata.ProviderIterationCount);
            Assert.Equal(1, chatClient.InvocationCount);
        }
    }
    [Fact]
    public async Task RequesterTextLimitCountsUnicodeScalarsAndRejectsOversize()
    {
        var maximumUnicodeMessage = string.Concat(
            Enumerable.Repeat("\U0001F642", 4000));
        var acceptedClient = new RecordingChatClient(UnclearProposal);
        var acceptedInterpreter = CreateInterpreter(acceptedClient);

        var accepted = await acceptedInterpreter.InterpretAsync(
            CreateTurn(maximumUnicodeMessage),
            TestContext.Current.CancellationToken);

        Assert.IsType<AgentInterpretationSucceeded>(accepted);
        Assert.Equal(1, acceptedClient.InvocationCount);

        string[] oversizedMessages =
        [
            new('x', 4001),
            string.Concat(Enumerable.Repeat("\U0001F642", 4001)),
        ];
        foreach (var message in oversizedMessages)
        {
            var rejectedClient = new RecordingChatClient(UnclearProposal);
            var rejectedInterpreter = CreateInterpreter(rejectedClient);
            var rejected = await rejectedInterpreter.InterpretAsync(
                CreateTurn(message),
                TestContext.Current.CancellationToken);

            var failed = Assert.IsType<AgentInterpretationFailed>(rejected);
            Assert.Equal(AgentInterpretationFailure.InvalidInput, failed.Failure);
            Assert.Equal(0, rejectedClient.InvocationCount);
        }
    }

    [Fact]
    public async Task CumulativeExecutionTimeoutCancelsTheProvider()
    {
        var chatClient = new BlockingChatClient();
        var limits = new AgentExecutionLimits(
            4000,
            1,
            4,
            6,
            TimeSpan.FromMilliseconds(50));
        var interpreter = CreateInterpreter(chatClient, limits);

        var result = await interpreter.InterpretAsync(
            CreateTurn("Wait for the bounded provider."),
            TestContext.Current.CancellationToken);

        var failed = Assert.IsType<AgentInterpretationFailed>(result);
        Assert.Equal(AgentInterpretationFailure.Timeout, failed.Failure);
        await chatClient.CancellationObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var chatClient = new BlockingChatClient();
        var interpreter = CreateInterpreter(chatClient);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var result = interpreter.InterpretAsync(
            CreateTurn("Cancel this caller-owned operation."),
            callerCancellation.Token);
        await chatClient.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result);
    }

    [Fact]
    public async Task SuccessfulTurnReturnsVersionedSafeExecutionMetadata()
    {
        var interpreter = CreateInterpreter(new RecordingChatClient(UnclearProposal));
        var turn = CreateTurn("Return a bounded proposal.");

        var result = await interpreter.InterpretAsync(
            turn,
            TestContext.Current.CancellationToken);

        var metadata = Assert.IsType<AgentInterpretationSucceeded>(result)
            .ExecutionMetadata;
        Assert.Equal("test-provider", metadata.ProviderId);
        Assert.Equal("test-deployment", metadata.ModelDeployment);
        Assert.Equal("test-provider-version", metadata.ProviderModelVersion);
        Assert.Equal("3.1.2", metadata.PromptContractVersion);
        Assert.Equal("3.0.0", metadata.StructuredOutputSchemaVersion);
        Assert.Equal("3.0.0", metadata.McpContractVersion);
        Assert.Equal("2.0.0", metadata.EnvironmentSearchPolicyVersion);
        Assert.Equal(turn.CorrelationId, metadata.CorrelationId);
        Assert.True(metadata.StartedAt <= metadata.CompletedAt);
    }

    private static MafTurnProposalInterpreter CreateInterpreter(
        Microsoft.Extensions.AI.IChatClient chatClient,
        AgentExecutionLimits? limits = null) =>
        new(
            chatClient,
            limits ?? AgentExecutionLimits.Default,
            new AgentModelMetadata(
                "test-provider",
                "test-deployment",
                "test-provider-version"),
            NullLoggerFactory.Instance);

    private static AgentTurnInput CreateTurn(
        string message,
        string? justification = null) =>
        new(
            message,
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                "ProductionReadOnly",
                justification,
                null),
            PreparationLifecycle.Collecting,
            clarification: null,
            correlationId: Guid.NewGuid().ToString("N"));
}

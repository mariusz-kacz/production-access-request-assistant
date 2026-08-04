using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class MafRequestPreparationInterpreterSessionTests
{
    private const string ValidClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"environmentId","message":"Choose PROD-ALPHA-EU or PROD-BETA-UK."}}
        """;

    private const string EnvironmentClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"environmentId","message":"Please choose an environment explicitly: PROD-ALPHA-EU or PROD-BETA-UK."}}
        """;

    private const string RoleClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"requestedRoleId","message":"Please choose a role: ProductionReadOnly or ProductionSupport."}}
        """;

    private const string CompletedCandidateResponse =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionSupport","justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    [Fact]
    public async Task SuccessfulTurnsRestoreHistoryAndReceiveCurrentCandidateContext()
    {
        var chatClient = new ScriptedChatClient(
            ValidClarificationResponse,
            ValidClarificationResponse);
        var interpreter = CreateInterpreter(chatClient);
        var intakeId = Guid.NewGuid();
        var candidate = new RequestCandidate(
            "client-alpha",
            environmentId: null,
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042");
        var first = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "Which environment should I use?",
                candidate),
            TestContext.Current.CancellationToken);
        var second = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "second provider-bound turn",
                candidate),
            TestContext.Current.CancellationToken);

        Assert.IsType<RequestPreparationInterpretationSucceeded>(first);
        Assert.IsType<RequestPreparationInterpretationSucceeded>(second);
        Assert.Equal(2, chatClient.InvocationCount);

        using var firstContext = ParseLatestTurnContext(
            chatClient.Invocations[0].Messages);
        Assert.False(firstContext.RootElement.TryGetProperty("historyAvailable", out _));
        Assert.Equal(
            "client-alpha",
            firstContext.RootElement
                .GetProperty("currentCandidate")
                .GetProperty("clientId")
                .GetString());
        Assert.False(
            firstContext.RootElement.TryGetProperty(
                "validationFeedback",
                out _));

        using var secondContext = ParseLatestTurnContext(
            chatClient.Invocations[1].Messages);
        Assert.False(secondContext.RootElement.TryGetProperty("historyAvailable", out _));
        Assert.Contains(
            chatClient.Invocations[1].Messages,
            message => message.Role == ChatRole.User
                && message.Text!.Contains(
                    "Which environment should I use?",
                    StringComparison.Ordinal));
        Assert.Contains(
            chatClient.Invocations[1].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == ValidClarificationResponse);
    }

    [Fact]
    public async Task InstructionsRequireIdentifierLookupsAndSafeClarification()
    {
        var chatClient = new ScriptedChatClient(ValidClarificationResponse);
        var interpreter = CreateInterpreter(chatClient);

        _ = await interpreter.InterpretAsync(
            CreateTurn(Guid.NewGuid(), "Use PROD-ALPHA-EU for INC-1042."),
            TestContext.Current.CancellationToken);

        var options = Assert.IsType<ChatOptions>(
            Assert.Single(chatClient.Invocations).Options);
        var instructions = Assert.IsType<string>(options.Instructions);
        Assert.Contains("MUST call", instructions, StringComparison.Ordinal);
        Assert.Contains(
            "get_production_environment",
            instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "get_incident",
            instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "derive clientId",
            instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "relative expression",
            instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "otherwise repeat a self-contained focused clarification",
            instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedTurnDoesNotReplaceLastSavedSessionSnapshot()
    {
        var chatClient = new ScriptedChatClient(
            ValidClarificationResponse,
            "{malformed",
            ValidClarificationResponse);
        var interpreter = CreateInterpreter(chatClient);
        var intakeId = Guid.NewGuid();

        var first = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "first valid turn"),
            TestContext.Current.CancellationToken);
        var malformed = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "discard this malformed turn"),
            TestContext.Current.CancellationToken);
        var third = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "third valid turn"),
            TestContext.Current.CancellationToken);

        Assert.IsType<RequestPreparationInterpretationSucceeded>(first);
        var failure = Assert.IsType<RequestPreparationInterpretationFailed>(malformed);
        Assert.Equal(
            RequestPreparationInterpretationFailure.MalformedModelOutput,
            failure.Failure);
        Assert.IsType<RequestPreparationInterpretationSucceeded>(third);

        var thirdRequest = chatClient.Invocations[2].Messages;
        Assert.Contains(
            thirdRequest,
            message => message.Role == ChatRole.User
                && message.Text!.Contains("first valid turn", StringComparison.Ordinal));
        Assert.DoesNotContain(
            thirdRequest,
            message => message.Text?.Contains(
                "discard this malformed turn",
                StringComparison.Ordinal) == true);

        using var thirdContext = ParseLatestTurnContext(thirdRequest);
        Assert.False(thirdContext.RootElement.TryGetProperty("historyAvailable", out _));
    }

    [Fact]
    public async Task FreshSessionSendsNoPriorAssistantQuestionToProvider()
    {
        var chatClient = new ScriptedChatClient(EnvironmentClarificationResponse);
        var interpreter = CreateInterpreter(chatClient);
        var candidate = new RequestCandidate(
            "client-alpha",
            environmentId: null,
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042");

        var outcome = await interpreter.InterpretAsync(
            CreateTurn(
                Guid.NewGuid(),
                "fresh provider-bound turn",
                candidate),
            TestContext.Current.CancellationToken);

        var interpreted = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            outcome);
        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            interpreted.Proposal.Kind);
        Assert.Null(interpreted.Proposal.Candidate.EnvironmentId);
        var request = Assert.Single(chatClient.Invocations).Messages;
        Assert.DoesNotContain(
            request,
            message => message.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task ScriptedRepliesReceiveOnlyTheActiveSessionHistory()
    {
        var chatClient = new ScriptedChatClient(
            EnvironmentClarificationResponse,
            RoleClarificationResponse,
            CompletedCandidateResponse);
        var interpreter = CreateInterpreter(chatClient);
        var intakeId = Guid.NewGuid();
        var candidate = new RequestCandidate(
            "client-alpha",
            environmentId: null,
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042");

        var environmentQuestion = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "I still need to choose the scope.", candidate),
            TestContext.Current.CancellationToken);
        var environmentInterpreted =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(
                environmentQuestion);
        var roleQuestion = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "environment selection response",
                environmentInterpreted.Proposal.Candidate),
            TestContext.Current.CancellationToken);
        var roleInterpreted =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(roleQuestion);
        var completed = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "role selection response",
                roleInterpreted.Proposal.Candidate),
            TestContext.Current.CancellationToken);
        var completedInterpreted =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(completed);

        Assert.Equal(
            RequestClarificationTarget.EnvironmentId,
            environmentInterpreted.Proposal.Clarification!.Target);
        Assert.Equal(
            "PROD-ALPHA-EU",
            roleInterpreted.Proposal.Candidate.EnvironmentId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            roleInterpreted.Proposal.Clarification!.Target);
        Assert.Equal(
            RequestPreparationProposalKind.Candidate,
            completedInterpreted.Proposal.Kind);
        Assert.Equal(
            ProductionRoleIds.Support,
            completedInterpreted.Proposal.Candidate.RequestedRoleId);
        Assert.Contains(
            chatClient.Invocations[1].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == EnvironmentClarificationResponse);
        Assert.Contains(
            chatClient.Invocations[2].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == RoleClarificationResponse);
    }

    private static MafRequestPreparationInterpreter CreateInterpreter(
        IChatClient chatClient) =>
        new(
            chatClient,
            Options.Create(
                new TeamsAccessRequestOptions
                {
                }),
            NullLoggerFactory.Instance,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

    private static RequestPreparationTurn CreateTurn(
        Guid intakeId,
        string latestMessage,
        RequestCandidate? candidate = null) =>
        new(
            intakeId,
            latestMessage,
            candidate ?? new RequestCandidate(null, null, null, null, null),
            Guid.NewGuid().ToString("N"));

    private static JsonDocument ParseLatestTurnContext(
        IReadOnlyList<ChatMessage> request)
    {
        var latestUserMessage = request.Last(message => message.Role == ChatRole.User);
        return JsonDocument.Parse(latestUserMessage.Text!);
    }

}

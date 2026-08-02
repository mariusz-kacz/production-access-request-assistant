using System.Runtime.CompilerServices;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
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

    [Fact]
    public async Task SuccessfulTurnsRestoreHistoryAndReceiveCurrentApplicationContext()
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
        var feedback = new RequestValidationFeedback(
            "environmentId",
            "environment_not_found",
            "The environment was not found.");

        var first = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "Which environment should I use?",
                candidate,
                [feedback]),
            TestContext.Current.CancellationToken);
        var second = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "the first one",
                candidate,
                [feedback]),
            TestContext.Current.CancellationToken);

        Assert.Equal(RequestPreparationInterpretationOutcomeKind.Proposal, first.Kind);
        Assert.Equal(RequestPreparationInterpretationOutcomeKind.Proposal, second.Kind);
        Assert.Equal(2, chatClient.Requests.Count);

        using var firstContext = ParseLatestTurnContext(chatClient.Requests[0]);
        Assert.False(firstContext.RootElement.TryGetProperty("historyAvailable", out _));
        Assert.Equal(
            "client-alpha",
            firstContext.RootElement
                .GetProperty("currentCandidate")
                .GetProperty("clientId")
                .GetString());
        Assert.Equal(
            "environment_not_found",
            firstContext.RootElement
                .GetProperty("validationFeedback")[0]
                .GetProperty("code")
                .GetString());

        using var secondContext = ParseLatestTurnContext(chatClient.Requests[1]);
        Assert.False(secondContext.RootElement.TryGetProperty("historyAvailable", out _));
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.User
                && message.Text!.Contains(
                    "Which environment should I use?",
                    StringComparison.Ordinal));
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.Assistant
                && message.Text == ValidClarificationResponse);
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

        Assert.Equal(RequestPreparationInterpretationOutcomeKind.Proposal, first.Kind);
        Assert.Equal(
            RequestPreparationInterpretationOutcomeKind.MalformedModelOutput,
            malformed.Kind);
        Assert.Equal(RequestPreparationInterpretationOutcomeKind.Proposal, third.Kind);

        var thirdRequest = chatClient.Requests[2];
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
    public async Task RelativeReplyWithoutHistoryProducesSelfContainedClarification()
    {
        var interpreter = CreateInterpreter(
            new DeterministicChatClient(DeterministicChatMode.HistorySensitive));
        var candidate = new RequestCandidate(
            "client-alpha",
            environmentId: null,
            requestedRoleId: null,
            "Investigate the active production incident.",
            "INC-1042");

        var outcome = await interpreter.InterpretAsync(
            CreateTurn(
                Guid.NewGuid(),
                "the first one",
                candidate),
            TestContext.Current.CancellationToken);

        Assert.Equal(RequestPreparationInterpretationOutcomeKind.Proposal, outcome.Kind);
        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            outcome.Proposal!.Kind);
        Assert.Null(outcome.Proposal.Candidate.EnvironmentId);
        Assert.Contains(
            "PROD-ALPHA-EU",
            outcome.Proposal.Clarification!.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "PROD-BETA-UK",
            outcome.Proposal.Clarification.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelativeRepliesUseOnlyTheActiveSessionQuestionOrdering()
    {
        var interpreter = CreateInterpreter(
            new DeterministicChatClient(DeterministicChatMode.HistorySensitive));
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
        var roleQuestion = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "the first one",
                environmentQuestion.Proposal!.Candidate),
            TestContext.Current.CancellationToken);
        var completed = await interpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "the other role",
                roleQuestion.Proposal!.Candidate),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            RequestClarificationTarget.EnvironmentId,
            environmentQuestion.Proposal!.Clarification!.Target);
        Assert.Equal("PROD-ALPHA-EU", roleQuestion.Proposal!.Candidate.EnvironmentId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            roleQuestion.Proposal.Clarification!.Target);
        Assert.Equal(RequestPreparationProposalKind.Candidate, completed.Proposal!.Kind);
        Assert.Equal(
            ProductionRoleIds.Support,
            completed.Proposal.Candidate.RequestedRoleId);
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
        RequestCandidate? candidate = null,
        IEnumerable<RequestValidationFeedback>? validationFeedback = null) =>
        new(
            intakeId,
            latestMessage,
            candidate ?? new RequestCandidate(null, null, null, null, null),
            validationFeedback ?? [],
            Guid.NewGuid().ToString("N"));

    private static JsonDocument ParseLatestTurnContext(
        IReadOnlyList<ChatMessage> request)
    {
        var latestUserMessage = request.Last(message => message.Role == ChatRole.User);
        return JsonDocument.Parse(latestUserMessage.Text!);
    }

    private sealed class ScriptedChatClient(params string[] responses) : IChatClient
    {
        private readonly Queue<string> responses = new(responses);

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(messages.ToArray());

            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, responses.Dequeue())));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate(message.Role, message.Text);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}

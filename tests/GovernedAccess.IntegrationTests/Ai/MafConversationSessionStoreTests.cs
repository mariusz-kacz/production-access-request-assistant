using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class MafConversationSessionStoreTests
{
    private const string ClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"environmentId","message":"Choose PROD-ALPHA-EU or PROD-BETA-UK."}}
        """;

    [Fact]
    public async Task NativeSessionReuseAndFreshStoreHaveDifferentHistorySemantics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var intakeId = Guid.NewGuid();
        var activeChatClient = new DeterministicChatClient(
            DeterministicChatMode.HistorySensitive);
        var activeInterpreter = CreateInterpreter(
            activeChatClient,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

        var environmentAccepted = await activeInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "Use PROD-ALPHA-EU for this request."),
            cancellationToken);
        var durableCandidate = environmentAccepted.Proposal!.Candidate;

        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            environmentAccepted.Proposal.Kind);
        Assert.Equal("client-alpha", durableCandidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", durableCandidate.EnvironmentId);
        Assert.Null(durableCandidate.RequestedRoleId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            environmentAccepted.Proposal.Clarification!.Target);

        var continued = await activeInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "the first one",
                durableCandidate),
            cancellationToken);

        Assert.Equal(
            RequestPreparationProposalKind.Candidate,
            continued.Proposal!.Kind);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            continued.Proposal.Candidate.RequestedRoleId);
        Assert.Null(continued.Proposal.Clarification);
        Assert.Equal(2, activeChatClient.RequestCount);

        var restartedChatClient = new DeterministicChatClient(
            DeterministicChatMode.HistorySensitive);
        var restartedInterpreter = CreateInterpreter(
            restartedChatClient,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

        var recovered = await restartedInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "the first one",
                durableCandidate),
            cancellationToken);

        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            recovered.Proposal!.Kind);
        Assert.Equal("client-alpha", recovered.Proposal.Candidate.ClientId);
        Assert.Equal(
            "PROD-ALPHA-EU",
            recovered.Proposal.Candidate.EnvironmentId);
        Assert.Null(recovered.Proposal.Candidate.RequestedRoleId);
        Assert.Equal(
            durableCandidate.Justification,
            recovered.Proposal.Candidate.Justification);
        Assert.Equal(
            durableCandidate.IncidentId,
            recovered.Proposal.Candidate.IncidentId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            recovered.Proposal.Clarification!.Target);
        Assert.Contains(
            "ProductionReadOnly",
            recovered.Proposal.Clarification.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProductionSupport",
            recovered.Proposal.Clarification.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, restartedChatClient.RequestCount);
    }

    [Fact]
    public async Task IndependentIntakesNeverShareQuestionOrdering()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interpreter = CreateInterpreter(
            new DeterministicChatClient(DeterministicChatMode.HistorySensitive),
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());
        var intakeA = Guid.NewGuid();
        var intakeB = Guid.NewGuid();

        var firstA = await interpreter.InterpretAsync(
            CreateTurn(intakeA, "I need temporary production access."),
            cancellationToken);
        var firstB = await interpreter.InterpretAsync(
            CreateTurn(intakeB, "Use PROD-ALPHA-EU."),
            cancellationToken);
        var secondA = await interpreter.InterpretAsync(
            CreateTurn(intakeA, "the first one", firstA.Proposal!.Candidate),
            cancellationToken);
        var secondB = await interpreter.InterpretAsync(
            CreateTurn(intakeB, "the first one", firstB.Proposal!.Candidate),
            cancellationToken);

        Assert.Equal(
            RequestClarificationTarget.EnvironmentId,
            firstA.Proposal!.Clarification!.Target);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            firstB.Proposal!.Clarification!.Target);
        Assert.Equal("PROD-ALPHA-EU", secondA.Proposal!.Candidate.EnvironmentId);
        Assert.Null(secondA.Proposal.Candidate.RequestedRoleId);
        Assert.Equal("PROD-ALPHA-EU", secondB.Proposal!.Candidate.EnvironmentId);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            secondB.Proposal.Candidate.RequestedRoleId);
    }

    [Fact]
    public async Task ExactPerIntakeGateSerializesTurnsWithoutBlockingOtherIntakes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new ConcurrentTurnProbeChatClient();
        var coordinator = new MafConversationTurnCoordinator();
        var interpreter = CreateInterpreter(
            chatClient,
            new InMemoryAgentSessionStore(),
            coordinator);
        var intakeA = Guid.NewGuid();
        var intakeB = Guid.NewGuid();

        var firstA = interpreter.InterpretAsync(
            CreateTurn(intakeA, ConcurrentTurnProbeChatClient.FirstATurn),
            cancellationToken);
        await chatClient.FirstAEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);

        var secondA = interpreter.InterpretAsync(
            CreateTurn(intakeA, ConcurrentTurnProbeChatClient.SecondATurn),
            cancellationToken);
        var firstB = interpreter.InterpretAsync(
            CreateTurn(intakeB, ConcurrentTurnProbeChatClient.FirstBTurn),
            cancellationToken);

        await chatClient.FirstBEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);

        Assert.False(chatClient.SecondAEntered.Task.IsCompleted);
        Assert.False(secondA.IsCompleted);
        Assert.True(chatClient.ObservedCrossIntakeOverlap);
        Assert.Equal(2, coordinator.GateCount);

        chatClient.ReleaseFirstA.TrySetResult();

        var outcomes = await Task.WhenAll(firstA, secondA, firstB);
        Assert.All(
            outcomes,
            outcome => Assert.Equal(
                RequestPreparationInterpretationOutcomeKind.Proposal,
                outcome.Kind));
        await chatClient.SecondAEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);
        Assert.Equal(1, chatClient.MaximumConcurrentIntakeATurns);

        var secondARequest = chatClient.Requests[ConcurrentTurnProbeChatClient.SecondATurn];
        Assert.Contains(
            secondARequest,
            message => message.Role == ChatRole.User
                && message.Text?.Contains(
                    ConcurrentTurnProbeChatClient.FirstATurn,
                    StringComparison.Ordinal) == true);
        Assert.Contains(
            secondARequest,
            message => message.Role == ChatRole.Assistant
                && message.Text == ClarificationResponse);
    }

    [Fact]
    public async Task FailedTurnDoesNotReplaceLastSuccessfullySavedSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new UnavailableMiddleTurnChatClient();
        var interpreter = CreateInterpreter(
            chatClient,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());
        var intakeId = Guid.NewGuid();

        var first = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "first-saved-turn"),
            cancellationToken);
        var failed = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "failed-unsaved-turn"),
            cancellationToken);
        var third = await interpreter.InterpretAsync(
            CreateTurn(intakeId, "third-saved-turn"),
            cancellationToken);

        Assert.Equal(
            RequestPreparationInterpretationOutcomeKind.Proposal,
            first.Kind);
        Assert.Equal(
            RequestPreparationInterpretationOutcomeKind.Unavailable,
            failed.Kind);
        Assert.Equal(
            RequestPreparationInterpretationOutcomeKind.Proposal,
            third.Kind);

        var thirdRequest = chatClient.Requests[2];
        Assert.Contains(
            thirdRequest,
            message => message.Role == ChatRole.User
                && message.Text?.Contains(
                    "first-saved-turn",
                    StringComparison.Ordinal) == true);
        Assert.Contains(
            thirdRequest,
            message => message.Role == ChatRole.Assistant
                && message.Text == ClarificationResponse);
        Assert.DoesNotContain(
            thirdRequest,
            message => message.Text?.Contains(
                "failed-unsaved-turn",
                StringComparison.Ordinal) == true);
    }

    private static MafRequestPreparationInterpreter CreateInterpreter(
        IChatClient chatClient,
        AgentSessionStore sessionStore,
        MafConversationTurnCoordinator coordinator) =>
        new(
            chatClient,
            Options.Create(
                new TeamsAccessRequestOptions
                {
                }),
            NullLoggerFactory.Instance,
            sessionStore,
            coordinator);

    private static RequestPreparationTurn CreateTurn(
        Guid intakeId,
        string latestMessage,
        RequestCandidate? candidate = null) =>
        new(
            intakeId,
            latestMessage,
            candidate ?? new RequestCandidate(null, null, null, null, null),
            validationFeedback: [],
            Guid.NewGuid().ToString("N"));

    private abstract class RecordingChatClient : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(
                messages,
                options,
                cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate(message.Role, message.Text);
            }
        }

        public abstract Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default);

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;
        }

        public void Dispose()
        {
        }

        protected static ChatResponse Response() =>
            new(new ChatMessage(ChatRole.Assistant, ClarificationResponse));

        protected static string ReadLatestMessage(
            IReadOnlyList<ChatMessage> request)
        {
            var latestUserMessage = request.Last(
                message => message.Role == ChatRole.User);
            using var envelope = System.Text.Json.JsonDocument.Parse(
                latestUserMessage.Text!);
            return envelope.RootElement
                .GetProperty("latestMessage")
                .GetString()!;
        }
    }

    private sealed class ConcurrentTurnProbeChatClient : RecordingChatClient
    {
        public const string FirstATurn = "intake-a-first";
        public const string SecondATurn = "intake-a-second";
        public const string FirstBTurn = "intake-b-first";

        private readonly ConcurrentDictionary<
            string,
            IReadOnlyList<ChatMessage>> requests = new();
        private int activeIntakeATurns;
        private int activeTurns;
        private int maximumConcurrentIntakeATurns;
        private int observedCrossIntakeOverlap;

        public TaskCompletionSource FirstAEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstA { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondAEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstBEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentDictionary<string, IReadOnlyList<ChatMessage>> Requests =>
            requests;

        public int MaximumConcurrentIntakeATurns =>
            Volatile.Read(ref maximumConcurrentIntakeATurns);

        public bool ObservedCrossIntakeOverlap =>
            Volatile.Read(ref observedCrossIntakeOverlap) == 1;

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = messages.ToArray();
            var latestMessage = ReadLatestMessage(request);
            Assert.True(requests.TryAdd(latestMessage, request));

            var active = Interlocked.Increment(ref activeTurns);
            var isIntakeA = latestMessage.StartsWith(
                "intake-a-",
                StringComparison.Ordinal);
            if (isIntakeA)
            {
                var activeA = Interlocked.Increment(ref activeIntakeATurns);
                UpdateMaximum(ref maximumConcurrentIntakeATurns, activeA);
            }

            try
            {
                switch (latestMessage)
                {
                    case FirstATurn:
                        FirstAEntered.TrySetResult();
                        await ReleaseFirstA.Task.WaitAsync(cancellationToken);
                        break;

                    case SecondATurn:
                        SecondAEntered.TrySetResult();
                        break;

                    case FirstBTurn:
                        if (active > 1)
                        {
                            Interlocked.Exchange(
                                ref observedCrossIntakeOverlap,
                                1);
                        }

                        FirstBEntered.TrySetResult();
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unexpected concurrency probe turn '{latestMessage}'.");
                }

                return Response();
            }
            finally
            {
                if (isIntakeA)
                {
                    Interlocked.Decrement(ref activeIntakeATurns);
                }

                Interlocked.Decrement(ref activeTurns);
            }
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            var current = Volatile.Read(ref maximum);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref maximum,
                    candidate,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class UnavailableMiddleTurnChatClient : RecordingChatClient
    {
        private readonly List<IReadOnlyList<ChatMessage>> requests = [];
        private int requestCount;

        public List<IReadOnlyList<ChatMessage>> Requests => requests;

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Add(messages.ToArray());
            var currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 2)
            {
                throw new HttpRequestException(
                    "The scripted middle turn is unavailable.");
            }

            return Task.FromResult(Response());
        }
    }
}

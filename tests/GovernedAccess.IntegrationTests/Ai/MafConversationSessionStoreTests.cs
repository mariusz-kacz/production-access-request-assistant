using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Ai;

public sealed class MafConversationSessionStoreTests
{
    private const string ClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":null,"requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"environmentId","message":"Choose PROD-ALPHA-EU or PROD-BETA-UK.","environmentOptionIds":[]}}
        """;

    private const string RoleClarificationResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":null,"justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":{"target":"requestedRoleId","message":"Choose ProductionReadOnly or ProductionSupport.","environmentOptionIds":[]}}
        """;

    private const string CompleteReadOnlyResponse =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    private const string IntakeAEnvironmentResponse =
        """
        {"kind":"clarification","candidate":{"clientId":null,"environmentId":null,"requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"environmentId","message":"intake-a-environment-question","environmentOptionIds":[]}}
        """;

    private const string IntakeBRoleResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"requestedRoleId","message":"intake-b-role-question","environmentOptionIds":[]}}
        """;

    private const string IntakeARoleResponse =
        """
        {"kind":"clarification","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"requestedRoleId","message":"intake-a-role-question","environmentOptionIds":[]}}
        """;

    private const string IntakeBCompleteResponse =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":null,"incidentId":null},"clarification":null}
        """;

    [Fact]
    public async Task NativeSessionReuseRestoresHistoryWhileFreshStoreStartsWithoutIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var intakeId = Guid.NewGuid();
        var activeChatClient = new ScriptedChatClient(
            RoleClarificationResponse,
            CompleteReadOnlyResponse);
        var activeInterpreter = CreateInterpreter(
            activeChatClient,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

        var environmentAccepted = await activeInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "Use PROD-ALPHA-EU to investigate the active incident."),
            cancellationToken);
        var environmentProposal =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(
                environmentAccepted).Proposal;
        var durableCandidate = environmentProposal.Candidate;

        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            environmentProposal.Kind);
        Assert.Equal("client-alpha", durableCandidate.ClientId);
        Assert.Equal("PROD-ALPHA-EU", durableCandidate.EnvironmentId);
        Assert.Null(durableCandidate.RequestedRoleId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            environmentProposal.Clarification!.Target);

        var continued = await activeInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "select ProductionReadOnly",
                durableCandidate),
            cancellationToken);
        var continuedProposal =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(
                continued).Proposal;

        Assert.Equal(
            RequestPreparationProposalKind.Candidate,
            continuedProposal.Kind);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            continuedProposal.Candidate.RequestedRoleId);
        Assert.Null(continuedProposal.Clarification);
        Assert.Equal(2, activeChatClient.InvocationCount);
        Assert.Contains(
            activeChatClient.Invocations[1].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == RoleClarificationResponse);

        var currentTurnMessage = activeChatClient.Invocations[1].Messages.Last(
            message => message.Role == ChatRole.User);
        using var currentTurn = JsonDocument.Parse(currentTurnMessage.Text!);
        var serializedCandidate = currentTurn.RootElement
            .GetProperty("currentCandidate");
        Assert.Equal(
            durableCandidate.ClientId,
            serializedCandidate.GetProperty("clientId").GetString());
        Assert.Equal(
            durableCandidate.EnvironmentId,
            serializedCandidate.GetProperty("environmentId").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            serializedCandidate.GetProperty("requestedRoleId").ValueKind);
        Assert.Equal(
            durableCandidate.Justification,
            serializedCandidate.GetProperty("justification").GetString());
        Assert.Equal(
            durableCandidate.IncidentId,
            serializedCandidate.GetProperty("incidentId").GetString());

        var restartedChatClient = new ScriptedChatClient(
            RoleClarificationResponse);
        var restartedInterpreter = CreateInterpreter(
            restartedChatClient,
            new InMemoryAgentSessionStore(),
            new MafConversationTurnCoordinator());

        var recovered = await restartedInterpreter.InterpretAsync(
            CreateTurn(
                intakeId,
                "select ProductionReadOnly",
                durableCandidate),
            cancellationToken);
        var recoveredProposal =
            Assert.IsType<RequestPreparationInterpretationSucceeded>(
                recovered).Proposal;

        Assert.Equal(
            RequestPreparationProposalKind.Clarification,
            recoveredProposal.Kind);
        Assert.Equal("client-alpha", recoveredProposal.Candidate.ClientId);
        Assert.Equal(
            "PROD-ALPHA-EU",
            recoveredProposal.Candidate.EnvironmentId);
        Assert.Null(recoveredProposal.Candidate.RequestedRoleId);
        Assert.Equal(
            durableCandidate.Justification,
            recoveredProposal.Candidate.Justification);
        Assert.Equal(
            durableCandidate.IncidentId,
            recoveredProposal.Candidate.IncidentId);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            recoveredProposal.Clarification!.Target);
        Assert.Contains(
            "ProductionReadOnly",
            recoveredProposal.Clarification.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProductionSupport",
            recoveredProposal.Clarification.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, restartedChatClient.InvocationCount);
        Assert.DoesNotContain(
            restartedChatClient.Invocations[0].Messages,
            message => message.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task IndependentIntakesNeverShareQuestionOrdering()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new ScriptedChatClient(
            IntakeAEnvironmentResponse,
            IntakeBRoleResponse,
            IntakeARoleResponse,
            IntakeBCompleteResponse);
        var interpreter = CreateInterpreter(
            chatClient,
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
        var firstAProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            firstA).Proposal;
        var firstBProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            firstB).Proposal;
        var secondA = await interpreter.InterpretAsync(
            CreateTurn(intakeA, "intake-a-second", firstAProposal.Candidate),
            cancellationToken);
        var secondB = await interpreter.InterpretAsync(
            CreateTurn(intakeB, "intake-b-second", firstBProposal.Candidate),
            cancellationToken);
        var secondAProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            secondA).Proposal;
        var secondBProposal = Assert.IsType<RequestPreparationInterpretationSucceeded>(
            secondB).Proposal;

        Assert.Equal(
            RequestClarificationTarget.EnvironmentId,
            firstAProposal.Clarification!.Target);
        Assert.Equal(
            RequestClarificationTarget.RequestedRoleId,
            firstBProposal.Clarification!.Target);
        Assert.Equal("PROD-ALPHA-EU", secondAProposal.Candidate.EnvironmentId);
        Assert.Null(secondAProposal.Candidate.RequestedRoleId);
        Assert.Equal("PROD-ALPHA-EU", secondBProposal.Candidate.EnvironmentId);
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            secondBProposal.Candidate.RequestedRoleId);
        Assert.Contains(
            chatClient.Invocations[2].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == IntakeAEnvironmentResponse);
        Assert.DoesNotContain(
            chatClient.Invocations[2].Messages,
            message => message.Text == IntakeBRoleResponse);
        Assert.Contains(
            chatClient.Invocations[3].Messages,
            message => message.Role == ChatRole.Assistant
                && message.Text == IntakeBRoleResponse);
        Assert.DoesNotContain(
            chatClient.Invocations[3].Messages,
            message => message.Text == IntakeAEnvironmentResponse);
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
            outcome => Assert.IsType<RequestPreparationInterpretationSucceeded>(
                outcome));
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

        Assert.IsType<RequestPreparationInterpretationSucceeded>(first);
        var failure = Assert.IsType<RequestPreparationInterpretationFailed>(failed);
        Assert.Equal(
            RequestPreparationInterpretationFailure.Unavailable,
            failure.Failure);
        Assert.IsType<RequestPreparationInterpretationSucceeded>(third);

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

using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsRequestHandlerTests
{
    [Fact]
    public void ConfirmationPayloadIsParsedOnlyAtTheAgentIngress()
    {
        var preparationId = Guid.NewGuid();

        Assert.True(
            TeamsAccessRequestAgent.TryParseConfirmationData(
                new
                {
                    schemaVersion = TeamsAdaptiveCardRenderer.ContractSchemaVersion,
                    preparationId = preparationId.ToString("D"),
                },
                out var parsedPreparationId));
        Assert.Equal(preparationId, parsedPreparationId);
        Assert.False(
            TeamsAccessRequestAgent.TryParseConfirmationData(
                new
                {
                    schemaVersion = TeamsAdaptiveCardRenderer.ContractSchemaVersion,
                    preparedRequestId = preparationId.ToString("D"),
                },
                out _));
    }

    [Fact]
    public async Task EveryNonblankNonCommandMessageGoesThroughPreparationOrchestration()
    {
        string[] messages =
        [
            "/new please",
            "1",
            "PROD-ALPHA-EU",
            "wybierz środowisko odzyskiwania",
        ];

        foreach (var message in messages)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var orchestrator = new StubOrchestrator();
            var confirmation = new StubConfirmation();
            var handler = CreateHandler(orchestrator, confirmation);

            var result = await handler.HandleMessageAsync(
                Context(),
                $"  {message}  ",
                "correlation",
                cancellationToken);

            Assert.Equal([message], orchestrator.ProcessedMessages);
            Assert.Equal(0, orchestrator.ResetCount);
            Assert.Equal(0, confirmation.CallCount);
            Assert.IsType<TeamsTextResponse>(result);
        }
    }

    [Fact]
    public async Task ExactNewCommandResetsWithoutCallingTheInterpreter()
    {
        foreach (var message in new[] { "/new", " /NEW " })
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var orchestrator = new StubOrchestrator();
            var handler = CreateHandler(orchestrator, new StubConfirmation());

            var result = await handler.HandleMessageAsync(
                Context(),
                message,
                "correlation",
                cancellationToken);

            Assert.Equal(1, orchestrator.ResetCount);
            Assert.Empty(orchestrator.ProcessedMessages);
            var text = Assert.IsType<TeamsRetiringTextResponse>(result);
            Assert.Contains(
                "Started a new request",
                text.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BlankTransportPayloadHasNoSemanticSideEffect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orchestrator = new StubOrchestrator();
        var confirmation = new StubConfirmation();
        var handler = CreateHandler(orchestrator, confirmation);

        var result = await handler.HandleMessageAsync(
            Context(),
            "  ",
            "correlation",
            cancellationToken);

        Assert.Empty(orchestrator.ProcessedMessages);
        Assert.Equal(0, orchestrator.ResetCount);
        Assert.Equal(0, confirmation.CallCount);
        Assert.Contains(
            "Describe",
            Assert.IsType<TeamsTextResponse>(result).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadyOutcomeUsesAuthoritativeCardAssembly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ready = CreateReadyPreparation("Original justification");
        var orchestrator = new StubOrchestrator
        {
            ProcessResult = Result(
                new PreparationSnapshot(ready),
                new ReadyForConfirmation(ready.PreparationId)),
        };
        var reviewService = new StubReviewService();
        var handler = CreateHandler(
            orchestrator,
            new StubConfirmation(),
            reviewService);

        var result = await handler.HandleMessageAsync(
            Context(),
            "prepare access",
            "correlation",
            cancellationToken);

        var card = Assert.IsType<TeamsDraftCardResponse>(result);
        Assert.Equal(ready.PreparationId, card.PreparationId);
        Assert.Equal([ready.PreparationId], reviewService.PreparationIds);
    }

    [Fact]
    public async Task ReadyRevisionInvalidatesThePreviouslyTrackedCard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var original = CreateReadyPreparation("Original justification");
        var revisedCandidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            "ROLE-1",
            "Revised justification",
            incidentId: null);
        var revised = RequestPreparation.CreateRevision(
            original,
            revisedCandidate,
            clarification: null,
            Attribution([ProposalField.Justification]),
            new DateTimeOffset(2026, 8, 26, 12, 5, 0, TimeSpan.Zero),
            "revision");
        var orchestrator = new StubOrchestrator
        {
            ProcessResult = Result(
                new PreparationSnapshot(revised),
                new ReadyForConfirmation(revised.PreparationId)),
        };
        var handler = CreateHandler(orchestrator, new StubConfirmation());

        var result = await handler.HandleMessageAsync(
            Context(),
            "change the justification",
            "correlation",
            cancellationToken);

        Assert.IsType<TeamsReplacementDraftCardResponse>(result);
    }

    [Fact]
    public async Task ConfirmationReceivesThePreparationIdParsedAtIngress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var preparationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var confirmation = new StubConfirmation
        {
            Result = new PreparationConfirmationSubmitted(
                CreateRequest(
                    requestId,
                    preparationId,
                    RequestStatus.AwaitingBusinessApproval),
                WasAlreadySubmitted: false),
        };
        var handler = CreateHandler(new StubOrchestrator(), confirmation);

        var accepted = await handler.HandleConfirmationAsync(
            Context(),
            preparationId,
            "correlation",
            cancellationToken);

        Assert.Equal(1, confirmation.CallCount);
        Assert.Equal(preparationId, confirmation.LastPreparationId);
        var card = Assert.IsType<TeamsTerminalCardResponse>(accepted);
        Assert.Contains(
            requestId.ToString("D"),
            CardJson(card),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmationReplayPreservesPersistedRequestStatus()
    {
        var preparationId = Guid.NewGuid();
        var request = CreateRequest(
            Guid.NewGuid(),
            preparationId,
            RequestStatus.Active);
        var handler = CreateHandler(
            new StubOrchestrator(),
            new StubConfirmation
            {
                Result = new PreparationConfirmationSubmitted(
                    request,
                    WasAlreadySubmitted: true),
            });

        var result = await handler.HandleConfirmationAsync(
            Context(),
            preparationId,
            "replay",
            TestContext.Current.CancellationToken);

        var card = Assert.IsType<TeamsTerminalCardResponse>(result);
        Assert.Contains(
            request.Id.ToString("D"),
            CardJson(card),
            StringComparison.Ordinal);
        Assert.Contains("active", CardJson(card), StringComparison.Ordinal);
    }

    private static TeamsRequestHandler CreateHandler(
        IRequestPreparationOrchestrator orchestrator,
        IPreparationConfirmationService confirmationService,
        IPreparationReviewService? reviewService = null) =>
        new(
            orchestrator,
            new TeamsResponsePresenter(
                reviewService ?? new StubReviewService()),
            confirmationService,
            NullLogger<TeamsRequestHandler>.Instance);

    private static TeamsAuthenticatedContext Context() =>
        new(
            new TeamsConversationReference(
                PreparationBinding.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                "requester"),
            "en-US");

    private static PreparationTurnResult Result(
        PreparationSnapshot? preparation,
        ApplicationOutcome outcome) =>
        new(preparation, new PreparationResponse(outcome));

    private static RequestPreparation CreateReadyPreparation(
        string justification) =>
        RequestPreparation.CreateRoot(
            new PreparationBinding(
                PreparationBinding.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                "requester"),
            new PreparationCandidate(
                "CLIENT-1",
                "PROD-1",
                "ROLE-1",
                justification,
                incidentId: null),
            clarification: null,
            Attribution(
                [
                    ProposalField.Environment,
                    ProposalField.Role,
                    ProposalField.Justification,
                ]),
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "root");

    private static AccessRequest CreateRequest(
        Guid requestId,
        Guid preparationId,
        RequestStatus status)
    {
        var request = new AccessRequest(
            requestId,
            preparationId,
            "requester",
            new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero),
            "confirmation");
        request.Status = status;
        return request;
    }

    private static MaterialChangeAttribution Attribution(
        IEnumerable<ProposalField> fields) =>
        new(
            fields,
            "test-model",
            providerModelVersion: null,
            "test-prompt",
            "test-schema",
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "attribution");

    private static string CardJson(TeamsCardResponse result) =>
        ((System.Text.Json.JsonElement)result.Card.Content!).GetRawText();

    private sealed class StubOrchestrator : IRequestPreparationOrchestrator
    {
        internal List<string> ProcessedMessages { get; } = [];

        internal int ResetCount { get; private set; }

        internal PreparationTurnResult ProcessResult { get; init; } =
            Result(preparation: null, new UnclearGuidance());

        public Task<PreparationTurnResult> ProcessTurnAsync(
            PreparationBinding binding,
            string latestRequesterText,
            string correlationId,
            CancellationToken cancellationToken)
        {
            ProcessedMessages.Add(latestRequesterText);
            return Task.FromResult(ProcessResult);
        }

        public Task<PreparationTurnResult> ResetAsync(
            PreparationBinding binding,
            string correlationId,
            CancellationToken cancellationToken)
        {
            ResetCount++;
            return Task.FromResult(
                Result(preparation: null, new ResetGuidance()));
        }
    }

    private sealed class StubReviewService : IPreparationReviewService
    {
        internal List<Guid> PreparationIds { get; } = [];

        public Task<ApplicationResult<PreparationReview>> LoadAsync(
            PreparationSnapshot preparation,
            CancellationToken cancellationToken)
        {
            PreparationIds.Add(preparation.PreparationId);
            var candidate = preparation.Candidate;
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new PreparationReview(
                        preparation.PreparationId,
                        "Demo Requester",
                        preparation.Binding.RequesterId,
                        "Client One",
                        candidate.ClientId!,
                        "Production One",
                        candidate.EnvironmentId!,
                        "Read only",
                        candidate.RoleId!,
                        IncidentDisplayName: null,
                        IncidentId: null,
                        candidate.Justification!,
                        preparation.ReadyDeadline!.Value)));
        }
    }

    private sealed class StubConfirmation : IPreparationConfirmationService
    {
        internal int CallCount { get; private set; }

        internal Guid LastPreparationId { get; private set; }

        internal PreparationConfirmationResult Result { get; init; } =
            new PreparationConfirmationFailed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "not-configured",
                    "Not configured."));

        public Task<PreparationConfirmationResult> ConfirmAsync(
            PreparationConfirmationCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPreparationId = command.PreparationId;
            return Task.FromResult(Result);
        }
    }
}

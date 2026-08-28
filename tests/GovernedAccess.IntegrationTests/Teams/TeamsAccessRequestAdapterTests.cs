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

public sealed class TeamsAccessRequestAdapterTests
{
    [Theory]
    [InlineData("/new please")]
    [InlineData("1")]
    [InlineData("PROD-ALPHA-EU")]
    [InlineData("wybierz środowisko odzyskiwania")]
    public async Task EveryNonblankNonCommandMessageGoesThroughPreparationOrchestration(
        string message)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orchestrator = new StubOrchestrator();
        var confirmation = new StubConfirmation();
        var adapter = CreateAdapter(orchestrator, confirmation);

        var result = await adapter.HandleMessageAsync(
            Context(),
            $"  {message}  ",
            "correlation",
            cancellationToken);

        Assert.Equal([message], orchestrator.ProcessedMessages);
        Assert.Equal(0, orchestrator.ResetCount);
        Assert.Equal(0, confirmation.CallCount);
        Assert.Equal(TeamsAdapterResultKind.Text, result.Kind);
    }

    [Theory]
    [InlineData("/new")]
    [InlineData(" /NEW ")]
    public async Task ExactNewCommandResetsWithoutCallingTheInterpreter(string message)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orchestrator = new StubOrchestrator();
        var adapter = CreateAdapter(orchestrator, new StubConfirmation());

        var result = await adapter.HandleMessageAsync(
            Context(),
            message,
            "correlation",
            cancellationToken);

        Assert.Equal(1, orchestrator.ResetCount);
        Assert.Empty(orchestrator.ProcessedMessages);
        Assert.True(result.InvalidatesTrackedCard);
        Assert.Contains("Started a new request", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankTransportPayloadHasNoSemanticSideEffect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var orchestrator = new StubOrchestrator();
        var confirmation = new StubConfirmation();
        var adapter = CreateAdapter(orchestrator, confirmation);

        var result = await adapter.HandleMessageAsync(
            Context(),
            "  ",
            "correlation",
            cancellationToken);

        Assert.Empty(orchestrator.ProcessedMessages);
        Assert.Equal(0, orchestrator.ResetCount);
        Assert.Equal(0, confirmation.CallCount);
        Assert.Contains("Describe", result.Message, StringComparison.Ordinal);
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
        var cardFactory = new StubCardFactory();
        var adapter = CreateAdapter(
            orchestrator,
            new StubConfirmation(),
            cardFactory);

        var result = await adapter.HandleMessageAsync(
            Context(),
            "prepare access",
            "correlation",
            cancellationToken);

        Assert.Equal(TeamsAdapterResultKind.Card, result.Kind);
        Assert.NotNull(result.Card);
        Assert.Equal(ready.PreparationId, result.PreparationId);
        Assert.Equal([ready.PreparationId], cardFactory.PreparationIds);
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
        var adapter = CreateAdapter(orchestrator, new StubConfirmation());

        var result = await adapter.HandleMessageAsync(
            Context(),
            "change the justification",
            "correlation",
            cancellationToken);

        Assert.True(result.InvalidatesTrackedCard);
    }

    [Fact]
    public async Task ConfirmationAcceptsOnlyTheFinalClosedPayload()
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
        var adapter = CreateAdapter(new StubOrchestrator(), confirmation);

        var accepted = await adapter.HandleConfirmationAsync(
            Context(),
            new
            {
                schemaVersion = 1,
                preparationId = preparationId.ToString("D"),
            },
            "correlation",
            cancellationToken);
        var rejectedLegacy = await adapter.HandleConfirmationAsync(
            Context(),
            new
            {
                schemaVersion = 1,
                preparedRequestId = preparationId.ToString("D"),
            },
            "correlation",
            cancellationToken);

        Assert.Equal(1, confirmation.CallCount);
        Assert.Equal(preparationId, confirmation.LastPreparationId);
        Assert.Equal(TeamsAdapterResultKind.Card, accepted.Kind);
        Assert.Contains(requestId.ToString("D"), CardJson(accepted), StringComparison.Ordinal);
        Assert.Equal(TeamsAdapterResultKind.InvalidAction, rejectedLegacy.Kind);
    }

    [Fact]
    public async Task ConfirmationReplayPreservesPersistedRequestStatus()
    {
        var preparationId = Guid.NewGuid();
        var request = CreateRequest(
            Guid.NewGuid(),
            preparationId,
            RequestStatus.Active);
        var adapter = CreateAdapter(
            new StubOrchestrator(),
            new StubConfirmation
            {
                Result = new PreparationConfirmationSubmitted(
                    request,
                    WasAlreadySubmitted: true),
            });

        var result = await adapter.HandleConfirmationAsync(
            Context(),
            new
            {
                schemaVersion = 1,
                preparationId = preparationId.ToString("D"),
            },
            "replay",
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamsAdapterResultKind.Card, result.Kind);
        Assert.Contains(
            request.Id.ToString("D"),
            CardJson(result),
            StringComparison.Ordinal);
        Assert.Contains("active", CardJson(result), StringComparison.Ordinal);
    }

    private static TeamsAccessRequestAdapter CreateAdapter(
        IRequestPreparationOrchestrator orchestrator,
        IPreparationConfirmationService confirmationService,
        IPreparedRequestCardFactory? cardFactory = null) =>
        new(
            orchestrator,
            cardFactory ?? new StubCardFactory(),
            confirmationService,
            NullLogger<TeamsAccessRequestAdapter>.Instance);

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

    private static string CardJson(TeamsAdapterResult result) =>
        ((System.Text.Json.JsonElement)result.Card!.Content!).GetRawText();

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

    private sealed class StubCardFactory : IPreparedRequestCardFactory
    {
        internal List<Guid> PreparationIds { get; } = [];

        public Task<ApplicationResult<Attachment>> CreateAsync(
            PreparationSnapshot preparation,
            string locale,
            CancellationToken cancellationToken)
        {
            PreparationIds.Add(preparation.PreparationId);
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    TeamsAdaptiveCardRenderer.CreateStatusCard(
                        "Ready",
                        preparation.PreparationId.ToString("D"))));
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

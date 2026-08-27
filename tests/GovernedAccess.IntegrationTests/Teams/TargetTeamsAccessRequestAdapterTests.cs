using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TargetTeamsAccessRequestAdapterTests
{
    [Theory]
    [InlineData("/new please")]
    [InlineData("1")]
    [InlineData("PROD-ALPHA-EU")]
    [InlineData("wybierz środowisko odzyskiwania")]
    public async Task EveryNonblankNonCommandMessageGoesThroughTargetOrchestration(
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
        Assert.Equal(TargetTeamsAdapterResultKind.Text, result.Kind);
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
    public async Task ReadyOutcomeUsesTargetAuthoritativeCardAssembly()
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

        Assert.Equal(TargetTeamsAdapterResultKind.Card, result.Kind);
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
            Result = TargetConfirmationResult.Submitted(
                requestId,
                wasAlreadySubmitted: false),
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
        Assert.Equal(TargetTeamsAdapterResultKind.Card, accepted.Kind);
        Assert.Contains(requestId.ToString("D"), CardJson(accepted), StringComparison.Ordinal);
        Assert.Equal(TargetTeamsAdapterResultKind.InvalidAction, rejectedLegacy.Kind);
    }

    private static TargetTeamsAccessRequestAdapter CreateAdapter(
        ITargetRequestPreparationOrchestrator orchestrator,
        ITargetRequestConfirmation confirmation,
        ITargetPreparedRequestCardFactory? cardFactory = null) =>
        new(
            orchestrator,
            cardFactory ?? new StubCardFactory(),
            confirmation,
            NullLogger<TargetTeamsAccessRequestAdapter>.Instance);

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

    private static string CardJson(TargetTeamsAdapterResult result) =>
        ((System.Text.Json.JsonElement)result.Card!.Content!).GetRawText();

    private sealed class StubOrchestrator : ITargetRequestPreparationOrchestrator
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

    private sealed class StubCardFactory : ITargetPreparedRequestCardFactory
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
                        new TeamsStatusCardPresentation(
                            "Ready",
                            preparation.PreparationId.ToString("D")))));
        }
    }

    private sealed class StubConfirmation : ITargetRequestConfirmation
    {
        internal int CallCount { get; private set; }

        internal Guid LastPreparationId { get; private set; }

        internal TargetConfirmationResult Result { get; init; } =
            TargetConfirmationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "not-configured",
                    "Not configured."));

        public Task<TargetConfirmationResult> ConfirmAsync(
            PreparationBinding binding,
            Guid preparationId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPreparationId = preparationId;
            return Task.FromResult(Result);
        }
    }
}

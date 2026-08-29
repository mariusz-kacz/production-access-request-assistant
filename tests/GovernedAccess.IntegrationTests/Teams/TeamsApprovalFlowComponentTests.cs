using System.Collections.Concurrent;
using System.Text.Json;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Web.Teams;
using GovernedAccess.Workflow.Persistence;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsApprovalFlowComponentTests
{
    private const string CompleteProposal =
        """
        {"schemaVersion":1,"dialogueAct":"updateDraft","patch":{"environment":{"operation":"set","reference":{"kind":"exactEnvironmentId","id":"PROD-ALPHA-EU"}},"role":{"operation":"set","roleId":"ProductionReadOnly"},"justification":{"operation":"set","value":{"text":"Investigate elevated customer errors."}},"incident":{"operation":"set","incidentId":"INC-1042"}},"discussionTopic":null}
        """;

    [Fact]
    public async Task ConfirmationDriftReturnsAnExplanatorySuccessorCardAndRetiresTheOriginal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            new RecordingChatClient(CompleteProposal));
        await factory.ResetDatabaseAsync(cancellationToken);
        var adapter = new RecordingChannelAdapter();

        await SendMessageAsync(
            factory,
            adapter,
            "Prepare the incident investigation request.",
            cancellationToken);
        var tracker = factory.Services.GetRequiredService<TeamsDraftCardTracker>();
        Assert.True(tracker.TryGet(Conversation(), out var originalCard));

        await using (var driftScope = factory.Services.CreateAsyncScope())
        {
            var referenceContext = driftScope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            var affected = await referenceContext.Database.ExecuteSqlRawAsync(
                "UPDATE Incidents SET IsActive = 0 WHERE Id = 'INC-1042'",
                cancellationToken);
            Assert.Equal(1, affected);
        }

        var replacementResponse = await ConfirmAsync(
            factory,
            adapter,
            originalCard.PreparationId,
            cancellationToken);
        var replacementCard = GetInvokeCard(replacementResponse);

        Assert.Contains(
            "Authoritative production context changed",
            replacementCard.GetRawText(),
            StringComparison.Ordinal);
        Assert.True(tracker.TryGet(Conversation(), out var successorCard));
        Assert.NotEqual(originalCard.PreparationId, successorCard.PreparationId);
        Assert.Equal(originalCard.ActivityId, successorCard.ActivityId);

        await using (var verificationScope = factory.Services.CreateAsyncScope())
        {
            var preparationStore = verificationScope.ServiceProvider
                .GetRequiredService<IRequestPreparationStore>();
            var predecessor = await preparationStore.GetAsync(
                originalCard.PreparationId,
                cancellationToken);
            var successor = await preparationStore.GetActiveAsync(
                Binding(),
                cancellationToken);
            var workflowContext = verificationScope.ServiceProvider
                .GetRequiredService<WorkflowDbContext>();

            Assert.Equal(
                PreparationLifecycle.Superseded,
                predecessor.Value.Lifecycle);
            Assert.Equal(
                PreparationLifecycle.Ready,
                successor.Value.Lifecycle);
            Assert.Equal(
                originalCard.PreparationId,
                successor.Value.PredecessorPreparationId);
            Assert.Equal(successorCard.PreparationId, successor.Value.PreparationId);
            Assert.Empty(
                await workflowContext.Set<AccessRequest>()
                    .AsNoTracking()
                    .ToArrayAsync(cancellationToken));
        }

        var staleResponse = await ConfirmAsync(
            factory,
            adapter,
            originalCard.PreparationId,
            cancellationToken);
        Assert.Equal(
            "application/vnd.microsoft.activity.message",
            staleResponse.Type);
        Assert.True(tracker.TryGet(Conversation(), out var stillActive));
        Assert.Equal(successorCard, stillActive);
    }

    [Fact]
    public async Task SubmittedReceiptIsNotTrackedOrRewrittenWhenANewDraftStarts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            new RecordingChatClient(CompleteProposal));
        await factory.ResetDatabaseAsync(cancellationToken);
        var adapter = new RecordingChannelAdapter();

        await SendMessageAsync(
            factory,
            adapter,
            "Prepare the incident investigation request.",
            cancellationToken);
        var tracker = factory.Services.GetRequiredService<TeamsDraftCardTracker>();
        Assert.True(tracker.TryGet(Conversation(), out var readyCard));

        var submittedResponse = await ConfirmAsync(
            factory,
            adapter,
            readyCard.PreparationId,
            cancellationToken);
        var submittedCard = GetInvokeCard(submittedResponse);

        Assert.Contains(
            "Request submitted",
            submittedCard.GetRawText(),
            StringComparison.Ordinal);
        Assert.False(tracker.TryGet(Conversation(), out _));
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        await SendMessageAsync(
            factory,
            adapter,
            "/new",
            cancellationToken);
        await SendMessageAsync(
            factory,
            adapter,
            "Prepare another incident investigation request.",
            cancellationToken);

        Assert.Empty(adapter.UpdatedActivities);
        Assert.True(
            tracker.TryGet(Conversation(), out var newReadyCard),
            $"Expected a tracked successor draft. Sent activities: {string.Join(" | ", adapter.SentActivities.Select(activity => $"{activity.Type}:{activity.Text}:attachments={activity.Attachments?.Count ?? 0}"))}");
        Assert.NotEqual(readyCard.PreparationId, newReadyCard.PreparationId);
        var sentReadyCards = adapter.SentActivities
            .Where(activity => activity.Attachments is { Count: > 0 })
            .ToArray();
        Assert.Equal(2, sentReadyCards.Length);
        Assert.Equal(newReadyCard.ActivityId, sentReadyCards[^1].Id);

        var replayResponse = await ConfirmAsync(
            factory,
            adapter,
            readyCard.PreparationId,
            cancellationToken);
        Assert.Contains(
            "Request already submitted",
            GetInvokeCard(replayResponse).GetRawText(),
            StringComparison.Ordinal);
        Assert.True(tracker.TryGet(Conversation(), out var stillActiveCard));
        Assert.Equal(newReadyCard, stillActiveCard);
        Assert.Empty(adapter.UpdatedActivities);
    }

    private static async Task SendMessageAsync(
        GovernedAccessWebFactory factory,
        RecordingChannelAdapter adapter,
        string message,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var activity = new FakeTeamsActivityBuilder()
            .WithText(message)
            .Build();
        _ = await adapter.ProcessActivityAsync(
            activity.Identity,
            activity.Activity,
            scope.ServiceProvider
                .GetRequiredService<TeamsAccessRequestAgent>()
                .OnTurnAsync,
            cancellationToken);
    }

    private static async Task<AdaptiveCardInvokeResponse> ConfirmAsync(
        GovernedAccessWebFactory factory,
        RecordingChannelAdapter adapter,
        Guid preparationId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var activity = new FakeTeamsActivityBuilder()
            .WithInvokeData(value: null)
            .Build();
        using var turnContext = new TurnContext(
            adapter,
            activity.Activity,
            activity.Identity);
        return await scope.ServiceProvider
            .GetRequiredService<TeamsAccessRequestAgent>()
            .OnConfirmAndSubmitAsync(
                turnContext,
                null!,
                new
                {
                    schemaVersion =
                        TeamsAdaptiveCardRenderer.ContractSchemaVersion,
                    preparationId = preparationId.ToString("D"),
                },
                cancellationToken);
    }

    private static JsonElement GetInvokeCard(AdaptiveCardInvokeResponse response)
    {
        Assert.Equal(
            TeamsAdaptiveCardRenderer.AdaptiveCardContentType,
            response.Type);
        var json = Assert.IsType<string>(response.Value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static TeamsConversationReference Conversation() =>
        new(
            PreparationBinding.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            "requester");

    private static PreparationBinding Binding() =>
        new(
            PreparationBinding.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            "requester");

    private sealed class RecordingChannelAdapter : ChannelAdapter
    {
        private readonly ConcurrentQueue<IActivity> sentActivities = [];
        private readonly ConcurrentQueue<IActivity> updatedActivities = [];
        private int nextActivityId;

        internal IReadOnlyList<IActivity> SentActivities =>
            sentActivities.ToArray();

        internal IReadOnlyList<IActivity> UpdatedActivities =>
            updatedActivities.ToArray();

        public override Task<ResourceResponse[]> SendActivitiesAsync(
            ITurnContext turnContext,
            IActivity[] activities,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var responses = new ResourceResponse[activities.Length];
            for (var index = 0; index < activities.Length; index++)
            {
                var activity = activities[index];
                var activityId = $"sent-{Interlocked.Increment(ref nextActivityId)}";
                activity.Id = activityId;
                sentActivities.Enqueue(activity);
                responses[index] = new ResourceResponse(activityId);
            }

            return Task.FromResult(responses);
        }

        public override Task<ResourceResponse> UpdateActivityAsync(
            ITurnContext turnContext,
            IActivity activity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updatedActivities.Enqueue(activity);
            return Task.FromResult(new ResourceResponse(activity.Id));
        }
    }
}

using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Teams;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsResponsePresenterTests
{
    [Fact]
    public async Task ClarificationContainsOnlyBoundedAuthoritativeChoices()
    {
        ClarificationChoice[] choices =
        [
            new EnvironmentClarificationChoice(
                "PROD-1",
                "<b>Primary</b>",
                "CLIENT-1",
                "Client & One",
                "westeurope",
                EnvironmentClassification.Primary),
            new EnvironmentClarificationChoice(
                "PROD-2",
                "Recovery",
                "CLIENT-1",
                "Client & One",
                "northeurope",
                EnvironmentClassification.Recovery),
        ];
        var result = Result(
            new ClarificationRequired(
                ClarificationTarget.Environment,
                choices,
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.NeedsClarification),
                justificationResult: null));

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        var message = Message(presentation);
        Assert.Contains("PROD-1", message, StringComparison.Ordinal);
        Assert.Contains("PROD-2", message, StringComparison.Ordinal);
        Assert.Contains("Client & One", message, StringComparison.Ordinal);
        Assert.Contains("<b>Primary</b>", message, StringComparison.Ordinal);
        Assert.DoesNotContain("model", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadyOutcomeUsesTheAuthoritativeSnapshotAndSafeRoleSelection()
    {
        var preparation = CreatePreparation(
            new PreparationCandidate(
                "CLIENT-1",
                "PROD-1",
                "ROLE-1",
                "Investigate the production fault.",
                incidentId: null));
        var snapshot = new PreparationSnapshot(preparation);

        var presentation = await CreatePresenter().PresentTurnAsync(
            new PreparationTurnResult(
                snapshot,
                new PreparationResponse(
                    new ReadyForConfirmation(preparation.PreparationId),
                    new SoleRoleSelection("ROLE-1", "Production read-only"))),
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        var response = Assert.IsType<TeamsDraftCardResponse>(presentation);
        var card = Assert.IsType<JsonElement>(response.Card.Content);
        Assert.Equal(snapshot.PreparationId, response.PreparationId);
        Assert.Contains("CLIENT-1", card.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("PROD-1", card.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("ROLE-1", card.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentDraftDiscussionUsesCanonicalFactsWithoutModelProse()
    {
        var candidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            "ROLE-1",
            "Investigate </TextBlock> exactly",
            "INC-1");

        var presentation = await CreatePresenter().PresentTurnAsync(
            Result(new DraftDiscussion(DiscussionTopic.CurrentDraft), candidate),
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        var message = Message(presentation);
        Assert.Contains("CLIENT-1", message, StringComparison.Ordinal);
        Assert.Contains("PROD-1", message, StringComparison.Ordinal);
        Assert.Contains("ROLE-1", message, StringComparison.Ordinal);
        Assert.Contains("INC-1", message, StringComparison.Ordinal);
        Assert.Contains(
            "Investigate </TextBlock> exactly",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("model", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrencyFailureDoesNotExposeInternalDetailsOrClaimMutation()
    {
        var presentation = await CreatePresenter().PresentTurnAsync(
            Result(
                new Failed(
                    new ApplicationFailure(
                        ApplicationFailureKind.ConcurrencyConflict,
                        "preparation-stale",
                        "internal details"))),
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        var message = Message(presentation);
        Assert.Contains("try again", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No request was submitted", message, StringComparison.Ordinal);
        Assert.DoesNotContain("internal details", message, StringComparison.Ordinal);
    }

    private static PreparationTurnResult Result(
        ApplicationOutcome outcome,
        PreparationCandidate? candidate = null)
    {
        var preparation = candidate is null
            ? null
            : new PreparationSnapshot(CreatePreparation(candidate));
        return new PreparationTurnResult(
            preparation,
            new PreparationResponse(outcome));
    }

    private static TeamsResponsePresenter CreatePresenter() =>
        new(new StubReviewService());

    private static string Message(TeamsResponse response) =>
        Assert.IsAssignableFrom<TeamsMessageResponse>(response).Message;

    private static RequestPreparation CreatePreparation(
        PreparationCandidate candidate) =>
        RequestPreparation.CreateRoot(
            new PreparationBinding(
                PreparationBinding.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                "requester"),
            candidate,
            clarification: null,
            attribution: candidate.IsEmpty
                ? null
                : new MaterialChangeAttribution(
                    candidate.ChangedFieldsFrom(PreparationCandidate.Empty),
                    "test-model",
                    providerModelVersion: null,
                    "test-prompt",
                    "test-schema",
                    new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
                    "test-correlation"),
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "test-correlation");

    private sealed class StubReviewService : IPreparationReviewService
    {
        public Task<ApplicationResult<PreparationReview>> LoadAsync(
            PreparationSnapshot preparation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = preparation.Candidate;
            return Task.FromResult(
                ApplicationResult.Succeeded(
                    new PreparationReview(
                        preparation.PreparationId,
                        "Demo Requester",
                        preparation.Binding.RequesterId,
                        "Client One",
                        candidate.ClientId!,
                        "Primary Production",
                        candidate.EnvironmentId!,
                        "Read only",
                        candidate.RoleId!,
                        IncidentDisplayName: null,
                        IncidentId: null,
                        candidate.Justification!,
                        preparation.ReadyDeadline!.Value)));
        }
    }
}

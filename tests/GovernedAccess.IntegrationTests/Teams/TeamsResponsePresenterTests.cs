using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsResponsePresenterTests
{
    [Fact]
    public async Task ClarificationUsesOnlyAuthoritativeChoicesAndBoundsTheList()
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
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.Applied)));

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            "pl-PL",
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamsResponseKind.Text, presentation.Kind);
        Assert.Equal(InputHints.ExpectingInput, presentation.InputHint);
        Assert.Contains(
            "Choose one by replying with its number, name, or exact ID:",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "1. Client & One (CLIENT-1) — <b>Primary</b> (PROD-1), westeurope, primary",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.Contains("2. Client & One", presentation.Message, StringComparison.Ordinal);
        Assert.StartsWith(
            $"I updated the operational justification.{Environment.NewLine}I found more than one matching production environment.",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Scope:", presentation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("model", presentation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoleClarificationExplainsWhyTheRequesterMustChoose()
    {
        ClarificationChoice[] choices =
        [
            new RoleClarificationChoice("ROLE-1", "Read only"),
            new RoleClarificationChoice("ROLE-2", "Support"),
        ];
        var result = Result(
            new ClarificationRequired(
                ClarificationTarget.Role,
                choices,
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.NeedsClarification),
                justificationResult: null));

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "This environment has more than one available role. "
            + "Choose one by replying with its number, name, or exact ID:",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.Contains("1. Read only (ROLE-1)", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("2. Support (ROLE-2)", presentation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftProgressUsesNaturalGroupResultsAndCanonicalMissingFields()
    {
        var candidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            roleId: null,
            justification: null,
            incidentId: null);
        var result = Result(
            new DraftUpdated(
                new ApplicationGroupResult(ApplicationGroupResultKind.Applied),
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.Rejected,
                    ApplicationGroupRejectionReason.Invalid)),
            candidate);

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "I updated the request scope. "
            + "I couldn't update the operational justification because some of the information wasn't valid. "
            + "I still need the requested role and operational justification.",
            presentation.Message);
        Assert.DoesNotContain("Environment result", presentation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Role result", presentation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnchangedScopeExplainsThatTheDraftAlreadyMatches()
    {
        var candidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            roleId: null,
            justification: null,
            incidentId: null);
        var result = Result(
            new DraftUnchanged(
                new ApplicationGroupResult(ApplicationGroupResultKind.NoOp),
                justificationResult: null),
            candidate);

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "The request scope already matches the draft. "
            + "I still need the requested role and operational justification.",
            presentation.Message);
    }

    [Theory]
    [InlineData(ApplicationGroupRejectionReason.Invalid, "some of the information wasn't valid")]
    [InlineData(ApplicationGroupRejectionReason.Unavailable, "the source I use to verify it is temporarily unavailable")]
    [InlineData(ApplicationGroupRejectionReason.Conflict, "the requested details conflict with each other")]
    [InlineData(ApplicationGroupRejectionReason.MissingDependency, "some required information is missing")]
    [InlineData(ApplicationGroupRejectionReason.EnvironmentQueryTooBroad, "the environment description matched too many environments")]
    [InlineData(ApplicationGroupRejectionReason.NoAssignableRoles, "the selected environment has no roles available for assignment")]
    [InlineData(ApplicationGroupRejectionReason.RoleChoiceLimitExceeded, "the selected environment has too many roles to show as choices")]
    public async Task RejectedScopeExplainsWhyItCouldNotBeUpdated(
        ApplicationGroupRejectionReason reason,
        string explanation)
    {
        var result = Result(
            new DraftUnchanged(
                new ApplicationGroupResult(
                    ApplicationGroupResultKind.Rejected,
                    reason),
                justificationResult: null));

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            $"I couldn't update the request scope because {explanation}. "
            + "I still need the production environment, requested role, and operational justification.",
            presentation.Message);
    }

    [Fact]
    public async Task AutomaticRoleSelectionExplainsWhyTheRoleWasSet()
    {
        var candidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            "ROLE-1",
            justification: null,
            incidentId: null);
        var result = Result(
            new DraftUpdated(
                new ApplicationGroupResult(ApplicationGroupResultKind.Applied),
                justificationResult: null),
            candidate,
            new SoleRoleSelection("ROLE-1", "Production read-only"));

        var presentation = await CreatePresenter().PresentTurnAsync(
            result,
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "Only Production read-only (ROLE-1) is available for this environment, so I selected it for the draft.",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "I still need the requested role",
            presentation.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuidanceOutcomesRenderFixedApplicationProse()
    {
        (Type OutcomeType, string Expected)[] scenarios =
        [
            (typeof(SubmissionGuidance), "complete the missing details"),
            (typeof(UnrelatedGuidance), "temporary production access"),
            (typeof(UnclearGuidance), "rephrase"),
            (typeof(ResetGuidance), "Started a new request"),
            (typeof(TerminalPreparationGuidance), "/new"),
            (typeof(ConfirmationSourceUnavailable), "try confirmation again"),
        ];

        foreach (var (outcomeType, expected) in scenarios)
        {
            var outcome = (ApplicationOutcome)Activator.CreateInstance(outcomeType)!;
            var presentation = await CreatePresenter().PresentTurnAsync(
                Result(outcome),
                TeamsLocale.Default,
                invalidatesTrackedCard: false,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                expected,
                presentation.Message,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReadyOutcomeUsesTheAuthoritativeSnapshotForCardAssembly()
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
                    new ReadyForConfirmation(preparation.PreparationId))),
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamsResponseKind.Card, presentation.Kind);
        Assert.NotNull(presentation.Card);
        Assert.Equal(snapshot.PreparationId, presentation.PreparationId);
        Assert.Null(presentation.Message);
        Assert.True(presentation.TrackAsActiveDraft);
    }

    [Fact]
    public async Task ReadyOutcomeCarriesAutomaticRoleSelectionExplanationWithTheCard()
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
                    new SoleRoleSelection(
                        "ROLE-1",
                        "Production read-only"))),
            TeamsLocale.Default,
            invalidatesTrackedCard: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamsResponseKind.Card, presentation.Kind);
        Assert.NotNull(presentation.Card);
        Assert.Null(presentation.Message);
        Assert.True(presentation.TrackAsActiveDraft);
        var card = Assert.IsType<JsonElement>(presentation.Card.Content);
        Assert.Contains(
            "Only Production read-only (ROLE-1) is available for this environment, so I selected it for the draft.",
            card.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentDraftDiscussionRendersCanonicalFactsWithoutModelProse()
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

        Assert.Contains("Client: CLIENT-1", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("Environment: PROD-1", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("Requested role: ROLE-1", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("Incident: INC-1", presentation.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Justification: Investigate </TextBlock> exactly",
            presentation.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrencyFailureRendersRetryWithoutClaimingMutation()
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

        Assert.Contains("changed while this message was processed", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("try again", presentation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal details", presentation.Message, StringComparison.Ordinal);
    }

    private static PreparationTurnResult Result(
        ApplicationOutcome outcome,
        PreparationCandidate? candidate = null,
        SoleRoleSelection? soleRoleSelection = null)
    {
        var preparation = candidate is null
            ? null
            : new PreparationSnapshot(CreatePreparation(candidate));
        return new PreparationTurnResult(
            preparation,
            new PreparationResponse(outcome, soleRoleSelection));
    }

    private static TeamsResponsePresenter CreatePresenter() =>
        new(new StubReviewService());

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

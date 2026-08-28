using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsResponseRendererTests
{
    [Fact]
    public void ClarificationUsesOnlyAuthoritativeChoicesAndBoundsTheList()
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

        var presentation = TeamsResponseRenderer.Render(
            result,
            "pl-PL");

        Assert.Equal(TeamsResponseKind.Text, presentation.Kind);
        Assert.Equal(InputHints.ExpectingInput, presentation.InputHint);
        Assert.Equal(TeamsLocale.Default, presentation.Locale);
        Assert.Contains("Choose one environment", presentation.Message, StringComparison.Ordinal);
        Assert.Contains(
            "1. Client & One (CLIENT-1) — <b>Primary</b> (PROD-1), westeurope, primary",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.Contains("2. Client & One", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("Justification: updated.", presentation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("model", presentation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DraftProgressUsesOnlyCompactGroupResultsAndCanonicalMissingFields()
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

        var presentation = TeamsResponseRenderer.Render(
            result,
            TeamsLocale.Default);

        Assert.Contains("Scope: updated.", presentation.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Justification: rejected (invalid).",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Still needed: requested role and operational justification.",
            presentation.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Environment result", presentation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Role result", presentation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(SubmissionGuidance), "complete the missing details")]
    [InlineData(typeof(UnrelatedGuidance), "temporary production access")]
    [InlineData(typeof(UnclearGuidance), "rephrase")]
    [InlineData(typeof(ResetGuidance), "Started a new request")]
    [InlineData(typeof(TerminalPreparationGuidance), "/new")]
    [InlineData(typeof(ConfirmationSourceUnavailable), "try confirmation again")]
    public void GuidanceOutcomesRenderFixedApplicationProse(
        Type outcomeType,
        string expected)
    {
        var outcome = (ApplicationOutcome)Activator.CreateInstance(outcomeType)!;

        var presentation = TeamsResponseRenderer.Render(
            Result(outcome),
            TeamsLocale.Default);

        Assert.Contains(expected, presentation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadyOutcomeCarriesOnlyTheAuthoritativeSnapshotToCardAssembly()
    {
        var preparation = CreatePreparation(
            new PreparationCandidate(
                "CLIENT-1",
                "PROD-1",
                "ROLE-1",
                "Investigate the production fault.",
                incidentId: null));
        var snapshot = new PreparationSnapshot(preparation);

        var presentation = TeamsResponseRenderer.Render(
            new PreparationTurnResult(
                snapshot,
                new PreparationResponse(
                    new ReadyForConfirmation(preparation.PreparationId))),
            TeamsLocale.Default);

        Assert.Equal(TeamsResponseKind.ReadyCard, presentation.Kind);
        Assert.Same(snapshot, presentation.Preparation);
        Assert.Null(presentation.Message);
    }

    [Fact]
    public void CurrentDraftDiscussionRendersCanonicalFactsWithoutModelProse()
    {
        var candidate = new PreparationCandidate(
            "CLIENT-1",
            "PROD-1",
            "ROLE-1",
            "Investigate </TextBlock> exactly",
            "INC-1");

        var presentation = TeamsResponseRenderer.Render(
            Result(new DraftDiscussion(DiscussionTopic.CurrentDraft), candidate),
            TeamsLocale.Default);

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
    public void ConcurrencyFailureRendersRetryWithoutClaimingMutation()
    {
        var presentation = TeamsResponseRenderer.Render(
            Result(
                new Failed(
                    new ApplicationFailure(
                        ApplicationFailureKind.ConcurrencyConflict,
                        "preparation-stale",
                        "internal details"))),
            TeamsLocale.Default);

        Assert.Contains("changed while this message was processed", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("try again", presentation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal details", presentation.Message, StringComparison.Ordinal);
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
                    candidate.IsComplete
                        ? candidate.IncidentId is null
                            ? [
                                ProposalField.Environment,
                                ProposalField.Role,
                                ProposalField.Justification,
                            ]
                            : [
                                ProposalField.Environment,
                                ProposalField.Incident,
                                ProposalField.Role,
                                ProposalField.Justification,
                            ]
                        : [ProposalField.Environment],
                    "test-model",
                    providerModelVersion: null,
                    "test-prompt",
                    "test-schema",
                    new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
                    "test-correlation"),
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            "test-correlation");
}

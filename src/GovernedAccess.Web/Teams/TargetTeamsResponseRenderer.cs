using System.Text;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal enum TargetTeamsResponseKind
{
    Text,
    ReadyCard,
}

internal sealed record TargetTeamsResponsePresentation(
    TargetTeamsResponseKind Kind,
    string? Message,
    string InputHint,
    string Locale,
    PreparationSnapshot? Preparation);

internal static class TargetTeamsResponseRenderer
{
    internal static TargetTeamsResponsePresentation Render(
        PreparationTurnResult result,
        string? locale)
    {
        ArgumentNullException.ThrowIfNull(result);
        var normalizedLocale = TeamsLocale.Resolve(locale);
        return result.Response.Outcome switch
        {
            DraftUpdated updated => Text(
                RenderDraftResult("Draft updated.", updated.ScopeResult, updated.JustificationResult, result.Preparation),
                InputHints.AcceptingInput,
                normalizedLocale),
            ClarificationRequired clarification => Text(
                RenderClarification(clarification),
                InputHints.ExpectingInput,
                normalizedLocale),
            DraftUnchanged unchanged => Text(
                RenderDraftResult("The draft was not changed.", unchanged.ScopeResult, unchanged.JustificationResult, result.Preparation),
                InputHints.AcceptingInput,
                normalizedLocale),
            DraftDiscussion discussion => Text(
                RenderDiscussion(discussion.Topic, result.Preparation),
                InputHints.AcceptingInput,
                normalizedLocale),
            SubmissionGuidance when IsReady(result.Preparation) =>
                Ready(result.Preparation!, normalizedLocale),
            SubmissionGuidance => Text(
                $"Complete the missing details before submitting. {RenderMissing(result.Preparation)}",
                InputHints.ExpectingInput,
                normalizedLocale),
            UnrelatedGuidance => Text(
                "I can help prepare a temporary production access request. Describe the environment, requested role, and operational justification.",
                InputHints.ExpectingInput,
                normalizedLocale),
            UnclearGuidance => Text(
                "I could not safely determine the requested change. Please rephrase it with the production environment, role, or justification you want to set or discuss.",
                InputHints.ExpectingInput,
                normalizedLocale),
            ResetGuidance => Text(
                "Started a new request. Send a production environment, requested role, and operational justification when you are ready.",
                InputHints.ExpectingInput,
                normalizedLocale),
            ReadyForConfirmation ready => RenderReady(
                ready,
                result.Preparation,
                normalizedLocale),
            ConfirmationRevalidationFailed revalidation =>
                RenderRevalidation(revalidation, result.Preparation, normalizedLocale),
            ConfirmationSourceUnavailable => Text(
                "Authoritative production context is temporarily unavailable. No request was submitted; try confirmation again before the current deadline.",
                InputHints.AcceptingInput,
                normalizedLocale),
            TerminalPreparationGuidance => Text(
                "This preparation can no longer be changed or submitted. Send /new to start a new request.",
                InputHints.AcceptingInput,
                normalizedLocale),
            Failed failed => Text(
                RenderFailure(failed.Failure),
                InputHints.AcceptingInput,
                normalizedLocale),
            _ => throw new InvalidOperationException(
                "The target preparation outcome is unsupported."),
        };
    }

    private static TargetTeamsResponsePresentation RenderReady(
        ReadyForConfirmation outcome,
        PreparationSnapshot? preparation,
        string locale)
    {
        if (!IsReady(preparation)
            || preparation!.PreparationId != outcome.PreparationId)
        {
            throw new InvalidOperationException(
                "A ready outcome must reference its exact ready preparation snapshot.");
        }

        return Ready(preparation, locale);
    }

    private static TargetTeamsResponsePresentation RenderRevalidation(
        ConfirmationRevalidationFailed outcome,
        PreparationSnapshot? preparation,
        string locale)
    {
        if (preparation is null
            || preparation.PreparationId != outcome.SuccessorPreparationId)
        {
            throw new InvalidOperationException(
                "A revalidation outcome must include its exact successor snapshot.");
        }

        return outcome.SuccessorStatus == RevalidatedPreparationStatus.Ready
            ? Ready(
                preparation,
                locale,
                "Authoritative production context changed. Review the corrected replacement card before confirming again.")
            : Text(
                $"Authoritative production context changed, so no request was submitted. {RenderMissing(preparation)}",
                InputHints.ExpectingInput,
                locale);
    }

    private static string RenderDraftResult(
        string heading,
        ApplicationGroupResult? scope,
        ApplicationGroupResult? justification,
        PreparationSnapshot? preparation)
    {
        var message = new StringBuilder(heading);
        AppendGroup(message, "Scope", scope);
        AppendGroup(message, "Justification", justification);
        message.Append(' ');
        message.Append(RenderMissing(preparation));
        return message.ToString();
    }

    private static void AppendGroup(
        StringBuilder message,
        string group,
        ApplicationGroupResult? result)
    {
        if (result is null)
        {
            return;
        }

        if (message.Length > 0)
        {
            message.Append(' ');
        }
        message.Append(group);
        message.Append(": ");
        message.Append(result.Kind switch
        {
            ApplicationGroupResultKind.Applied => "updated.",
            ApplicationGroupResultKind.NoOp => "unchanged.",
            ApplicationGroupResultKind.NeedsClarification =>
                "needs clarification.",
            ApplicationGroupResultKind.Rejected =>
                $"rejected ({RenderReason(result.RejectionReason!.Value)}).",
            _ => throw new InvalidOperationException(
                "The application-group result is unsupported."),
        });
    }

    private static string RenderReason(
        ApplicationGroupRejectionReason reason) =>
        reason switch
        {
            ApplicationGroupRejectionReason.Invalid => "invalid",
            ApplicationGroupRejectionReason.Unavailable => "source unavailable",
            ApplicationGroupRejectionReason.Conflict => "conflict",
            ApplicationGroupRejectionReason.MissingDependency =>
                "missing dependency",
            ApplicationGroupRejectionReason.EnvironmentQueryTooBroad =>
                "environment query too broad",
            ApplicationGroupRejectionReason.NoAssignableRoles =>
                "no assignable roles",
            ApplicationGroupRejectionReason.RoleChoiceLimitExceeded =>
                "too many role choices",
            _ => throw new InvalidOperationException(
                "The application-group rejection reason is unsupported."),
        };

    private static string RenderClarification(ClarificationRequired clarification)
    {
        var message = new StringBuilder();
        AppendGroup(message, "Scope", clarification.ScopeResult);
        AppendGroup(
            message,
            "Justification",
            clarification.JustificationResult);
        if (message.Length > 0)
        {
            message.AppendLine();
        }

        message.Append(
            clarification.Target == ClarificationTarget.Environment
                ? "Choose one environment by replying with its number, name, or exact ID:"
                : "Choose one requested role by replying with its number, name, or exact ID:");

        for (var index = 0; index < clarification.Choices.Count; index++)
        {
            message.AppendLine();
            message.Append(index + 1);
            message.Append(". ");
            message.Append(RenderChoice(clarification.Choices[index]));
        }

        return message.ToString();
    }

    private static string RenderChoice(ClarificationChoice choice) =>
        choice switch
        {
            EnvironmentClarificationChoice environment =>
                $"{environment.ClientDisplayName} ({environment.ClientId}) — {environment.DisplayName} ({environment.CanonicalId}), {environment.Region}, {environment.Classification.ToString().ToLowerInvariant()}",
            RoleClarificationChoice role =>
                $"{role.DisplayName} ({role.CanonicalId})",
            _ => throw new InvalidOperationException(
                "The clarification choice type is unsupported."),
        };

    private static string RenderDiscussion(
        DiscussionTopic topic,
        PreparationSnapshot? preparation) =>
        topic switch
        {
            DiscussionTopic.CurrentDraft =>
                RenderCurrentDraft(preparation),
            DiscussionTopic.MissingInformation => RenderMissing(preparation),
            DiscussionTopic.AllowedChanges =>
                "You can change the production environment, optional incident, requested role, or operational justification before confirmation.",
            DiscussionTopic.ConfirmationProcess =>
                "When the draft is complete, review its card and select Confirm and submit. That creates a request for human approval; it does not approve or grant access.",
            DiscussionTopic.ResetInstructions =>
                "Send /new by itself to discard the active preparation and start a clean one.",
            DiscussionTopic.Unsupported =>
                "I can discuss only preparation of this temporary production access request.",
            _ => throw new InvalidOperationException(
                "The discussion topic is unsupported."),
        };

    private static string RenderCurrentDraft(PreparationSnapshot? preparation)
    {
        if (preparation is null)
        {
            return RenderMissing(preparation);
        }

        var candidate = preparation.Candidate;
        return string.Join(
            Environment.NewLine,
            "Current canonical draft:",
            $"Client: {candidate.ClientId ?? "Not selected"}",
            $"Environment: {candidate.EnvironmentId ?? "Not selected"}",
            $"Requested role: {candidate.RoleId ?? "Not selected"}",
            $"Incident: {candidate.IncidentId ?? "No incident"}",
            $"Justification: {candidate.Justification ?? "Not provided"}",
            RenderMissing(preparation));
    }

    private static string RenderMissing(PreparationSnapshot? preparation)
    {
        var candidate = preparation?.Candidate ?? PreparationCandidate.Empty;
        var missing = new List<string>(3);
        if (candidate.EnvironmentId is null)
        {
            missing.Add("production environment");
        }

        if (candidate.RoleId is null)
        {
            missing.Add("requested role");
        }

        if (candidate.Justification is null)
        {
            missing.Add("operational justification");
        }

        return missing.Count == 0
            ? "The canonical draft is complete."
            : $"Still needed: {JoinItems(missing)}.";
    }

    private static string JoinItems(List<string> items) =>
        items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };

    private static string RenderFailure(ApplicationFailure failure) =>
        failure.Kind switch
        {
            ApplicationFailureKind.ConcurrencyConflict =>
                "The preparation changed while this message was processed. No request was submitted; please try again.",
            ApplicationFailureKind.Timeout =>
                "Request preparation timed out. No request was submitted; please try again.",
            ApplicationFailureKind.Cancelled =>
                "Request preparation was cancelled. No request was submitted; send the request again when ready.",
            ApplicationFailureKind.DependencyUnavailable
                or ApplicationFailureKind.DependencyFailure =>
                "Request preparation is temporarily unavailable. No request was submitted; please try again later.",
            ApplicationFailureKind.InvalidTransition =>
                "This preparation can no longer be updated. Send /new to start a new request.",
            _ =>
                "The request could not be prepared safely. No request was submitted.",
        };

    private static bool IsReady(PreparationSnapshot? preparation) =>
        preparation is
        {
            Lifecycle: PreparationLifecycle.Ready,
            ReadyDeadline: not null,
        } && preparation.Candidate.IsComplete;

    private static TargetTeamsResponsePresentation Text(
        string message,
        string inputHint,
        string locale) =>
        new(
            TargetTeamsResponseKind.Text,
            message,
            inputHint,
            locale,
            Preparation: null);

    private static TargetTeamsResponsePresentation Ready(
        PreparationSnapshot preparation,
        string locale,
        string? message = null) =>
        new(
            TargetTeamsResponseKind.ReadyCard,
            message,
            InputHints.AcceptingInput,
            locale,
            preparation);
}

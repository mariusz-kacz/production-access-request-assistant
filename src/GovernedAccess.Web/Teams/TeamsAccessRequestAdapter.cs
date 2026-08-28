using System.Diagnostics;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Web.Ai;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal sealed partial class TeamsAccessRequestAdapter(
    IRequestPreparationOrchestrator orchestrator,
    TeamsResponsePresenter presenter,
    IPreparationConfirmationService confirmationService,
    ILogger<TeamsAccessRequestAdapter> logger)
{
    private const string NewRequestCommand = "/new";

    internal async Task<TeamsResponse> HandleMessageAsync(
        TeamsAuthenticatedContext context,
        string? message,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var latestMessage = message?.Trim();
        if (string.IsNullOrEmpty(latestMessage))
        {
            return TeamsResponse.CreateText(
                "Describe the temporary production access you need, including the production environment, requested role, and operational justification.",
                InputHints.ExpectingInput);
        }

        var binding = CreateBinding(context.Conversation);
        var isReset = string.Equals(
            latestMessage,
            NewRequestCommand,
            StringComparison.OrdinalIgnoreCase);
        var startedAt = Stopwatch.GetTimestamp();
        var result = isReset
            ? await orchestrator.ResetAsync(
                binding,
                correlationId,
                cancellationToken)
            : await orchestrator.ProcessTurnAsync(
                binding,
                latestMessage,
                correlationId,
                cancellationToken);
        var response = await presenter.PresentTurnAsync(
            result,
            context.Locale,
            invalidatesTrackedCard: isReset,
            cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMs =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogTurnCompleted(
                logger,
                isReset ? "Reset" : "Prepare",
                result.Response.Outcome.GetType().Name,
                durationMs,
                correlationId,
                context.Channel,
                context.TenantId,
                context.ChannelActorId,
                context.ConversationId,
                context.RequesterId,
                result.Preparation?.PreparationId);
        }

        return response;
    }

    internal async Task<TeamsResponse> HandleConfirmationAsync(
        TeamsAuthenticatedContext context,
        object? data,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!TryReadConfirmationData(data, out var preparationId))
        {
            return TeamsResponse.CreateInvalidAction(
                "The confirmation action is invalid. No request was submitted.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await confirmationService.ConfirmAsync(
            new PreparationConfirmationCommand(
                CreateBinding(context.Conversation),
                preparationId,
                correlationId),
            cancellationToken);
        var response = await presenter.PresentConfirmationAsync(
            result,
            context.Locale,
            preparationId,
            cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMs =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var outcome = ConfirmationOutcome(result);
            var submittedRequest = result as PreparationConfirmationSubmitted;
            var failure = (result as PreparationConfirmationFailed)?.Failure;
            LogConfirmationCompleted(
                logger,
                outcome,
                durationMs,
                correlationId,
                context.Channel,
                context.TenantId,
                context.ChannelActorId,
                context.ConversationId,
                context.RequesterId,
                preparationId,
                submittedRequest?.Request.Id,
                failure?.Kind,
                failure?.Code);
        }

        return response;
    }

    private static string ConfirmationOutcome(
        PreparationConfirmationResult result) =>
        result switch
        {
            PreparationConfirmationSubmitted { WasAlreadySubmitted: true } =>
                "AlreadySubmitted",
            PreparationConfirmationSubmitted => "Submitted",
            PreparationConfirmationRevalidationFailed => "RevalidationFailed",
            PreparationConfirmationSourceUnavailable => "SourceUnavailable",
            PreparationConfirmationFailed => "Failed",
            _ => throw new InvalidOperationException(
                "The preparation-confirmation result is unsupported."),
        };

    private static PreparationBinding CreateBinding(
        TeamsConversationReference conversation) =>
        new(
            conversation.Channel,
            conversation.TenantId,
            conversation.ChannelActorId,
            conversation.ConversationId,
            conversation.RequesterId);

    private static bool TryReadConfirmationData(
        object? data,
        out Guid preparationId)
    {
        preparationId = Guid.Empty;
        try
        {
            var element = data switch
            {
                JsonElement jsonElement => jsonElement,
                JsonDocument jsonDocument => jsonDocument.RootElement,
                not null => JsonSerializer.SerializeToElement(data),
                _ => default,
            };
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = element.EnumerateObject().ToArray();
            return properties.Length == 2
                && element.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.TryGetInt32(out var version)
                && version == TeamsAdaptiveCardRenderer.ContractSchemaVersion
                && element.TryGetProperty(
                    "preparationId",
                    out var preparationIdProperty)
                && preparationIdProperty.ValueKind == JsonValueKind.String
                && Guid.TryParseExact(
                    preparationIdProperty.GetString(),
                    "D",
                    out preparationId)
                && preparationId != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    [LoggerMessage(
        EventId = 1101,
        EventName = "TeamsPreparationCompleted",
        Level = LogLevel.Information,
        Message = "Teams intake {Transition} completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; preparation {PreparationId}.")]
    private static partial void LogTurnCompleted(
        ILogger logger,
        string transition,
        string outcome,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid? preparationId);

    [LoggerMessage(
        EventId = 1102,
        EventName = "TeamsConfirmationCompleted",
        Level = LogLevel.Information,
        Message = "Teams confirmation completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; preparation {PreparationId}; request {RequestId}; failure kind {FailureKind}; failure code {FailureCode}.")]
    private static partial void LogConfirmationCompleted(
        ILogger logger,
        string outcome,
        double durationMs,
        string correlationId,
        string channel,
        string tenantId,
        string channelActorId,
        string conversationId,
        string requesterId,
        Guid preparationId,
        Guid? requestId,
        ApplicationFailureKind? failureKind,
        string? failureCode);
}

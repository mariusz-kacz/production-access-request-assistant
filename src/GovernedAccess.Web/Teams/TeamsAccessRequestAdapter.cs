using System.Diagnostics;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.Web.Ai;
using Microsoft.Agents.Core.Models;

namespace GovernedAccess.Web.Teams;

internal enum TeamsAdapterResultKind
{
    Text,
    Card,
    InvalidAction,
}

internal sealed record TeamsAdapterResult(
    TeamsAdapterResultKind Kind,
    string? Message,
    Attachment? Card,
    string InputHint,
    bool InvalidatesTrackedCard,
    Guid? PreparationId);

internal sealed partial class TeamsAccessRequestAdapter(
    IRequestPreparationOrchestrator orchestrator,
    IPreparedRequestCardFactory cardFactory,
    IPreparationConfirmationService confirmationService,
    ILogger<TeamsAccessRequestAdapter> logger)
{
    private const string NewRequestCommand = "/new";

    internal async Task<TeamsAdapterResult> HandleMessageAsync(
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
            return Text(
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
        var response = await CreateResultAsync(
            result,
            context.Locale,
            isReset,
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

    internal async Task<TeamsAdapterResult> HandleConfirmationAsync(
        TeamsAuthenticatedContext context,
        object? data,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!TryReadConfirmationData(data, out var preparationId))
        {
            return new TeamsAdapterResult(
                TeamsAdapterResultKind.InvalidAction,
                "The confirmation action is invalid. No request was submitted.",
                Card: null,
                InputHints.IgnoringInput,
                InvalidatesTrackedCard: false,
                PreparationId: null);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await confirmationService.ConfirmAsync(
            new PreparationConfirmationCommand(
                CreateBinding(context.Conversation),
                preparationId,
                correlationId),
            cancellationToken);
        var response = await RenderConfirmationAsync(
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

    private async Task<TeamsAdapterResult> CreateResultAsync(
        PreparationTurnResult result,
        string locale,
        bool invalidatesTrackedCard,
        CancellationToken cancellationToken)
    {
        var presentation = TeamsResponseRenderer.Render(result, locale);
        invalidatesTrackedCard |=
            result.Preparation?.PredecessorPreparationId is not null;
        if (presentation.Kind == TeamsResponseKind.Text)
        {
            return Text(
                presentation.Message!,
                presentation.InputHint,
                invalidatesTrackedCard,
                result.Preparation?.PreparationId);
        }

        var cardResult = await cardFactory.CreateAsync(
            presentation.Preparation!,
            presentation.Locale,
            cancellationToken);
        if (cardResult.IsFailure)
        {
            var failurePresentation = TeamsResponseRenderer.Render(
                new PreparationTurnResult(
                    presentation.Preparation,
                    new PreparationResponse(
                        new Failed(cardResult.Failure!))),
                locale);
            return Text(
                failurePresentation.Message!,
                failurePresentation.InputHint,
                invalidatesTrackedCard,
                presentation.Preparation!.PreparationId);
        }

        return new TeamsAdapterResult(
            TeamsAdapterResultKind.Card,
            presentation.Message,
            cardResult.Value,
            presentation.InputHint,
            invalidatesTrackedCard,
            presentation.Preparation!.PreparationId);
    }

    private async Task<TeamsAdapterResult> RenderConfirmationAsync(
        PreparationConfirmationResult result,
        string locale,
        Guid preparationId,
        CancellationToken cancellationToken) =>
        result switch
        {
            PreparationConfirmationSubmitted submitted =>
                Submitted(submitted, preparationId),
            PreparationConfirmationRevalidationFailed revalidationFailed =>
                await CreateResultAsync(
                    revalidationFailed.Revalidation,
                    locale,
                    invalidatesTrackedCard: true,
                    cancellationToken),
            PreparationConfirmationSourceUnavailable =>
                Text(
                    TeamsResponseRenderer.Render(
                        new PreparationTurnResult(
                            preparation: null,
                            new PreparationResponse(
                                new ConfirmationSourceUnavailable())),
                        locale).Message!,
                    InputHints.AcceptingInput),
            PreparationConfirmationFailed failed => Text(
                TeamsResponseRenderer.Render(
                    new PreparationTurnResult(
                        preparation: null,
                        new PreparationResponse(new Failed(failed.Failure))),
                    locale).Message!,
                InputHints.AcceptingInput),
            _ => throw new InvalidOperationException(
                "The preparation-confirmation result is unsupported."),
        };

    private static TeamsAdapterResult Submitted(
        PreparationConfirmationSubmitted result,
        Guid preparationId)
    {
        var title = result.WasAlreadySubmitted
            ? "Request already submitted"
            : "Request submitted";
        return new TeamsAdapterResult(
            TeamsAdapterResultKind.Card,
            Message: null,
            TeamsAdaptiveCardRenderer.CreateStatusCard(
                title,
                $"Request {result.Request.Id:D} is {StatusText(result.Request.Status)}."),
            InputHints.IgnoringInput,
            InvalidatesTrackedCard: true,
            preparationId);
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

    private static string StatusText(RequestStatus status) =>
        status switch
        {
            RequestStatus.AwaitingBusinessApproval =>
                "awaiting business approval; access is not yet approved or granted",
            RequestStatus.AwaitingDevOpsApproval =>
                "awaiting DevOps approval; access is not yet granted",
            RequestStatus.Rejected => "rejected; access was not granted",
            RequestStatus.ProvisioningFailed =>
                "in provisioning-failed state; access was not granted",
            RequestStatus.Active => "active",
            _ => throw new InvalidOperationException(
                "The request status is unsupported."),
        };

    private static TeamsAdapterResult Text(
        string message,
        string inputHint,
        bool invalidatesTrackedCard = false,
        Guid? preparationId = null) =>
        new(
            TeamsAdapterResultKind.Text,
            message,
            Card: null,
            inputHint,
            invalidatesTrackedCard,
            preparationId);

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

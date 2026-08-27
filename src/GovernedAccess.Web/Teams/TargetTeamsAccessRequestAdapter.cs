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

internal enum TargetTeamsAdapterResultKind
{
    Text,
    Card,
    InvalidAction,
}

internal sealed record TargetTeamsAdapterResult(
    TargetTeamsAdapterResultKind Kind,
    string? Message,
    Attachment? Card,
    string InputHint,
    bool InvalidatesTrackedCard,
    Guid? PreparationId);

internal interface ITargetRequestConfirmation
{
    Task<TargetConfirmationResult> ConfirmAsync(
        PreparationBinding binding,
        Guid preparationId,
        string correlationId,
        CancellationToken cancellationToken);
}

internal enum TargetConfirmationResultKind
{
    Submitted,
    AlreadySubmitted,
    RevalidationFailed,
    SourceUnavailable,
    Failed,
}

internal sealed record TargetConfirmationResult
{
    private TargetConfirmationResult(
        TargetConfirmationResultKind kind,
        Guid requestId,
        RequestStatus? requestStatus,
        PreparationTurnResult? revalidation,
        ApplicationFailure? failure)
    {
        Kind = kind;
        RequestId = requestId;
        RequestStatus = requestStatus;
        Revalidation = revalidation;
        Failure = failure;
    }

    internal TargetConfirmationResultKind Kind { get; }

    internal Guid RequestId { get; }

    internal RequestStatus? RequestStatus { get; }

    internal PreparationTurnResult? Revalidation { get; }

    internal ApplicationFailure? Failure { get; }

    internal static TargetConfirmationResult Submitted(
        Guid requestId,
        RequestStatus requestStatus,
        bool wasAlreadySubmitted)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "A submitted target confirmation requires a request identifier.",
                nameof(requestId));
        }

        if (!Enum.IsDefined(requestStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(requestStatus));
        }

        return new(
            wasAlreadySubmitted
                ? TargetConfirmationResultKind.AlreadySubmitted
                : TargetConfirmationResultKind.Submitted,
            requestId,
            requestStatus,
            revalidation: null,
            failure: null);
    }

    internal static TargetConfirmationResult RevalidationFailed(
        PreparationTurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Response.Outcome is not ConfirmationRevalidationFailed)
        {
            throw new ArgumentException(
                "A target confirmation revalidation result requires the matching typed outcome.",
                nameof(result));
        }

        return new(
            TargetConfirmationResultKind.RevalidationFailed,
            Guid.Empty,
            requestStatus: null,
            result,
            failure: null);
    }

    internal static TargetConfirmationResult SourceUnavailable() =>
        new(
            TargetConfirmationResultKind.SourceUnavailable,
            Guid.Empty,
            requestStatus: null,
            revalidation: null,
            failure: null);

    internal static TargetConfirmationResult Failed(
        ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            TargetConfirmationResultKind.Failed,
            Guid.Empty,
            requestStatus: null,
            revalidation: null,
            failure);
    }
}

internal sealed partial class TargetTeamsAccessRequestAdapter(
    ITargetRequestPreparationOrchestrator orchestrator,
    ITargetPreparedRequestCardFactory cardFactory,
    ITargetRequestConfirmation confirmation,
    ILogger<TargetTeamsAccessRequestAdapter> logger)
{
    private const string NewRequestCommand = "/new";

    internal async Task<TargetTeamsAdapterResult> HandleMessageAsync(
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

    internal async Task<TargetTeamsAdapterResult> HandleConfirmationAsync(
        TeamsAuthenticatedContext context,
        object? data,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!TryReadConfirmationData(data, out var preparationId))
        {
            return new TargetTeamsAdapterResult(
                TargetTeamsAdapterResultKind.InvalidAction,
                "The confirmation action is invalid. No request was submitted.",
                Card: null,
                InputHints.IgnoringInput,
                InvalidatesTrackedCard: false,
                PreparationId: null);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await confirmation.ConfirmAsync(
            CreateBinding(context.Conversation),
            preparationId,
            correlationId,
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
            LogConfirmationCompleted(
                logger,
                result.Kind,
                durationMs,
                correlationId,
                context.Channel,
                context.TenantId,
                context.ChannelActorId,
                context.ConversationId,
                context.RequesterId,
                preparationId,
                result.RequestId == Guid.Empty ? null : result.RequestId,
                result.Failure?.Kind,
                result.Failure?.Code);
        }

        return response;
    }

    private async Task<TargetTeamsAdapterResult> CreateResultAsync(
        PreparationTurnResult result,
        string locale,
        bool invalidatesTrackedCard,
        CancellationToken cancellationToken)
    {
        var presentation = TargetTeamsResponseRenderer.Render(result, locale);
        invalidatesTrackedCard |=
            result.Preparation?.PredecessorPreparationId is not null;
        if (presentation.Kind == TargetTeamsResponseKind.Text)
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
            var failurePresentation = TargetTeamsResponseRenderer.Render(
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

        return new TargetTeamsAdapterResult(
            TargetTeamsAdapterResultKind.Card,
            presentation.Message,
            cardResult.Value,
            presentation.InputHint,
            invalidatesTrackedCard,
            presentation.Preparation!.PreparationId);
    }

    private async Task<TargetTeamsAdapterResult> RenderConfirmationAsync(
        TargetConfirmationResult result,
        string locale,
        Guid preparationId,
        CancellationToken cancellationToken) =>
        result.Kind switch
        {
            TargetConfirmationResultKind.Submitted
                or TargetConfirmationResultKind.AlreadySubmitted =>
                Submitted(result, preparationId),
            TargetConfirmationResultKind.RevalidationFailed =>
                await CreateResultAsync(
                    result.Revalidation!,
                    locale,
                    invalidatesTrackedCard: true,
                    cancellationToken),
            TargetConfirmationResultKind.SourceUnavailable =>
                Text(
                    TargetTeamsResponseRenderer.Render(
                        new PreparationTurnResult(
                            preparation: null,
                            new PreparationResponse(
                                new ConfirmationSourceUnavailable())),
                        locale).Message!,
                    InputHints.AcceptingInput),
            TargetConfirmationResultKind.Failed => Text(
                TargetTeamsResponseRenderer.Render(
                    new PreparationTurnResult(
                        preparation: null,
                        new PreparationResponse(new Failed(result.Failure!))),
                    locale).Message!,
                InputHints.AcceptingInput),
            _ => throw new InvalidOperationException(
                "The target confirmation result is unsupported."),
        };

    private static TargetTeamsAdapterResult Submitted(
        TargetConfirmationResult result,
        Guid preparationId)
    {
        var title = result.Kind == TargetConfirmationResultKind.AlreadySubmitted
            ? "Request already submitted"
            : "Request submitted";
        return new TargetTeamsAdapterResult(
            TargetTeamsAdapterResultKind.Card,
            Message: null,
            TeamsAdaptiveCardRenderer.CreateStatusCard(
                new TeamsStatusCardPresentation(
                    title,
                    $"Request {result.RequestId:D} is {StatusText(result.RequestStatus!.Value)}.")),
            InputHints.IgnoringInput,
            InvalidatesTrackedCard: true,
            preparationId);
    }

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

    private static TargetTeamsAdapterResult Text(
        string message,
        string inputHint,
        bool invalidatesTrackedCard = false,
        Guid? preparationId = null) =>
        new(
            TargetTeamsAdapterResultKind.Text,
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
        EventName = "TargetTeamsPreparationCompleted",
        Level = LogLevel.Information,
        Message = "Target Teams intake {Transition} completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; preparation {PreparationId}.")]
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
        EventName = "TargetTeamsConfirmationCompleted",
        Level = LogLevel.Information,
        Message = "Target Teams confirmation completed with {Outcome} in {DurationMs} ms. CorrelationId {CorrelationId}; channel {Channel}; tenant {TenantId}; actor {ChannelActorId}; conversation {ConversationId}; requester {RequesterId}; preparation {PreparationId}; request {RequestId}; failure kind {FailureKind}; failure code {FailureCode}.")]
    private static partial void LogConfirmationCompleted(
        ILogger logger,
        TargetConfirmationResultKind outcome,
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

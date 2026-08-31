using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.Web.Ai;

internal sealed partial class ModelCallLoggingChatClient(
    IChatClient innerClient,
    ILogger<ModelCallLoggingChatClient> logger)
    : DelegatingChatClient(innerClient)
{
    private static long nextOperationId;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var messageList = messages as IReadOnlyList<ChatMessage>
            ?? messages.ToArray();
        var operationId = BeginOperation(messageList, options);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var response = await base.GetResponseAsync(
                messageList,
                options,
                cancellationToken);
            CompleteOperation(
                operationId,
                startedAt,
                "Succeeded",
                response.Usage,
                exception: null);
            return response;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Cancelled",
                usage: null,
                exception);
            throw;
        }
        catch (TimeoutException exception)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "TimedOut",
                usage: null,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Failed",
                usage: null,
                exception);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate>
        GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var messageList = messages as IReadOnlyList<ChatMessage>
            ?? messages.ToArray();
        var operationId = BeginOperation(messageList, options);
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "Stopped";
        Exception? failure = null;
        await using var enumerator = base.GetStreamingResponseAsync(
                messageList,
                options,
                cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException exception)
                    when (cancellationToken.IsCancellationRequested)
                {
                    outcome = "Cancelled";
                    failure = exception;
                    throw;
                }
                catch (TimeoutException exception)
                {
                    outcome = "TimedOut";
                    failure = exception;
                    throw;
                }
                catch (Exception exception)
                {
                    outcome = "Failed";
                    failure = exception;
                    throw;
                }

                if (!hasNext)
                {
                    outcome = "Succeeded";
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            CompleteOperation(
                operationId,
                startedAt,
                outcome,
                usage: null,
                failure);
        }
    }

    private long BeginOperation(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options)
    {
        var operationId = Interlocked.Increment(ref nextOperationId);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var messageTextCharacters = messages.Sum(
                static message => (long)(message.Text?.Length ?? 0));
            LogModelCallStarted(
                logger,
                operationId,
                messages.Count,
                messageTextCharacters,
                options?.Instructions?.Length ?? 0,
                options?.Tools?.Count ?? 0,
                options?.ResponseFormat is not null);
        }
        return operationId;
    }

    private void CompleteOperation(
        long operationId,
        long startedAt,
        string outcome,
        UsageDetails? usage,
        Exception? exception)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var durationMilliseconds =
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var failureType = exception?.GetType().Name ?? "None";
        var innerFailureType =
            exception?.InnerException?.GetType().Name ?? "None";
        LogModelCallCompleted(
            logger,
            operationId,
            durationMilliseconds,
            outcome,
            failureType,
            innerFailureType,
            usage?.InputTokenCount ?? -1,
            usage?.OutputTokenCount ?? -1,
            usage?.TotalTokenCount ?? -1);
    }

    [LoggerMessage(
        EventId = 4030,
        Level = LogLevel.Information,
        Message = "Model call {OperationId} started with {MessageCount} messages, {MessageTextCharacters} message text characters, {InstructionsCharacters} instruction characters, {ToolCount} tools, and response format present {HasResponseFormat}.")]
    private static partial void LogModelCallStarted(
        ILogger logger,
        long operationId,
        int messageCount,
        long messageTextCharacters,
        int instructionsCharacters,
        int toolCount,
        bool hasResponseFormat);

    [LoggerMessage(
        EventId = 4031,
        Level = LogLevel.Information,
        Message = "Model call {OperationId} completed in {DurationMilliseconds} ms with outcome {Outcome}, failure types {FailureType}/{InnerFailureType}, and token counts input={InputTokenCount}, output={OutputTokenCount}, total={TotalTokenCount}.")]
    private static partial void LogModelCallCompleted(
        ILogger logger,
        long operationId,
        double durationMilliseconds,
        string outcome,
        string failureType,
        string innerFailureType,
        long inputTokenCount,
        long outputTokenCount,
        long totalTokenCount);
}

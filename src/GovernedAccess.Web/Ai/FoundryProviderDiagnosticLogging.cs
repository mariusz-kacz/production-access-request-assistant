using System.Diagnostics;
using Azure.Core;
using Microsoft.Extensions.Logging;
using System.ClientModel.Primitives;

namespace GovernedAccess.Web.Ai;

internal sealed partial class FoundryTokenCredentialLoggingDecorator(
    TokenCredential innerCredential,
    ILogger<FoundryTokenCredentialLoggingDecorator> logger)
    : TokenCredential
{
    private static long nextOperationId;

    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var operationId = BeginOperation();
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var token = innerCredential.GetToken(
                requestContext,
                cancellationToken);
            CompleteOperation(
                operationId,
                startedAt,
                "Succeeded",
                "None");
            return token;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Cancelled",
                exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Failed",
                exception.GetType().Name);
            throw;
        }
    }

    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var operationId = BeginOperation();
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var token = await innerCredential.GetTokenAsync(
                requestContext,
                cancellationToken);
            CompleteOperation(
                operationId,
                startedAt,
                "Succeeded",
                "None");
            return token;
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Cancelled",
                exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            CompleteOperation(
                operationId,
                startedAt,
                "Failed",
                exception.GetType().Name);
            throw;
        }
    }

    private long BeginOperation()
    {
        var operationId = Interlocked.Increment(ref nextOperationId);
        LogTokenAcquisitionStarted(logger, operationId);
        return operationId;
    }

    private void CompleteOperation(
        long operationId,
        long startedAt,
        string outcome,
        string failureType)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var durationMilliseconds =
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LogTokenAcquisitionCompleted(
            logger,
            operationId,
            durationMilliseconds,
            outcome,
            failureType);
    }

    [LoggerMessage(
        EventId = 4032,
        Level = LogLevel.Information,
        Message = "Foundry token acquisition {OperationId} started.")]
    private static partial void LogTokenAcquisitionStarted(
        ILogger logger,
        long operationId);

    [LoggerMessage(
        EventId = 4033,
        Level = LogLevel.Information,
        Message = "Foundry token acquisition {OperationId} completed in {DurationMilliseconds} ms with outcome {Outcome} and failure type {FailureType}.")]
    private static partial void LogTokenAcquisitionCompleted(
        ILogger logger,
        long operationId,
        double durationMilliseconds,
        string outcome,
        string failureType);
}

internal sealed partial class FoundryHttpPipelineLoggingPolicy(
    ILogger<FoundryHttpPipelineLoggingPolicy> logger)
    : PipelinePolicy
{
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        var attemptNumber = GetNextAttemptNumber(message);
        var requestId = message.Request.ClientRequestId;
        var startedAt = Stopwatch.GetTimestamp();
        LogHttpAttemptStarted(logger, requestId, attemptNumber);

        try
        {
            ProcessNext(message, pipeline, currentIndex);
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Response",
                message.Response?.Status ?? 0,
                "None");
        }
        catch (OperationCanceledException exception)
            when (message.CancellationToken.IsCancellationRequested)
        {
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Cancelled",
                message.Response?.Status ?? 0,
                exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Failed",
                message.Response?.Status ?? 0,
                exception.GetType().Name);
            throw;
        }
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        var attemptNumber = GetNextAttemptNumber(message);
        var requestId = message.Request.ClientRequestId;
        var startedAt = Stopwatch.GetTimestamp();
        LogHttpAttemptStarted(logger, requestId, attemptNumber);

        try
        {
            await ProcessNextAsync(message, pipeline, currentIndex);
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Response",
                message.Response?.Status ?? 0,
                "None");
        }
        catch (OperationCanceledException exception)
            when (message.CancellationToken.IsCancellationRequested)
        {
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Cancelled",
                message.Response?.Status ?? 0,
                exception.GetType().Name);
            throw;
        }
        catch (Exception exception)
        {
            CompleteAttempt(
                requestId,
                attemptNumber,
                startedAt,
                "Failed",
                message.Response?.Status ?? 0,
                exception.GetType().Name);
            throw;
        }
    }

    private static int GetNextAttemptNumber(PipelineMessage message)
    {
        if (!message.TryGetProperty(
                typeof(HttpAttemptState),
                out var stateValue)
            || stateValue is not HttpAttemptState state)
        {
            state = new HttpAttemptState();
            message.SetProperty(typeof(HttpAttemptState), state);
        }

        return Interlocked.Increment(ref state.AttemptNumber);
    }

    private void CompleteAttempt(
        string requestId,
        int attemptNumber,
        long startedAt,
        string outcome,
        int statusCode,
        string failureType)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var durationMilliseconds =
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        LogHttpAttemptCompleted(
            logger,
            requestId,
            attemptNumber,
            durationMilliseconds,
            outcome,
            statusCode,
            failureType);
    }

    [LoggerMessage(
        EventId = 4034,
        Level = LogLevel.Information,
        Message = "Foundry HTTP request {RequestId} attempt {AttemptNumber} started.")]
    private static partial void LogHttpAttemptStarted(
        ILogger logger,
        string requestId,
        int attemptNumber);

    [LoggerMessage(
        EventId = 4035,
        Level = LogLevel.Information,
        Message = "Foundry HTTP request {RequestId} attempt {AttemptNumber} completed in {DurationMilliseconds} ms with outcome {Outcome}, status {StatusCode}, and failure type {FailureType}.")]
    private static partial void LogHttpAttemptCompleted(
        ILogger logger,
        string requestId,
        int attemptNumber,
        double durationMilliseconds,
        string outcome,
        int statusCode,
        string failureType);

    private sealed class HttpAttemptState
    {
        internal int AttemptNumber;
    }
}

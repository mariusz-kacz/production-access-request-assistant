using System.Collections.Concurrent;
using System.Diagnostics;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Web.Provisioning;

public enum SyntheticAccessProvisioningBehavior
{
    Succeed,
    Fail,
    LoseResponseAfterCreate,
    WaitForTimeout,
}

/// <summary>
/// Test and demonstration control for the synthetic provider boundary. A behavior
/// change affects calls that start after the change; an in-flight call keeps the
/// behavior and delay it captured at its start.
/// </summary>
public sealed class SyntheticAccessProvisionerControl
{
    private readonly object sync = new();
    private SyntheticAccessProvisioningBehavior behavior =
        SyntheticAccessProvisioningBehavior.Succeed;
    private TimeSpan delay;

    public void Configure(
        SyntheticAccessProvisioningBehavior behavior,
        TimeSpan? delay = null)
    {
        if (!Enum.IsDefined(behavior))
        {
            throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null);
        }

        var configuredDelay = delay ?? TimeSpan.Zero;
        if (configuredDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                configuredDelay,
                "Synthetic provisioning delay cannot be negative.");
        }

        lock (sync)
        {
            this.behavior = behavior;
            this.delay = configuredDelay;
        }
    }

    public void Reset() => Configure(SyntheticAccessProvisioningBehavior.Succeed);

    internal SyntheticAccessProvisionerSettings Capture()
    {
        lock (sync)
        {
            return new SyntheticAccessProvisionerSettings(behavior, delay);
        }
    }
}

internal sealed record SyntheticAccessProvisionerSettings(
    SyntheticAccessProvisioningBehavior Behavior,
    TimeSpan Delay);

/// <summary>
/// Local synthetic provider with request-ID get-or-create semantics. Provider state
/// is deliberately separate from workflow persistence so a response can be lost and
/// a retry can still recover the same grant.
/// </summary>
public sealed partial class SyntheticAccessProvisioner(
    IClock clock,
    IHostApplicationLifetime applicationLifetime,
    SyntheticAccessProvisionerControl control,
    ILogger<SyntheticAccessProvisioner> logger) : IAccessProvisioner
{
    public static readonly TimeSpan ProvisioningTimeout = TimeSpan.FromSeconds(10);

    public const string SuccessCode = "synthetic_provisioning_succeeded";

    public const string FailureCode = "synthetic_provisioning_failed";

    public const string LostResponseCode = "synthetic_provisioning_response_lost";

    public const string ScopeMismatchCode = "synthetic_provisioning_scope_mismatch";

    public const string TimeoutCode = "synthetic_provisioning_timeout";

    public const string CancelledCode = "synthetic_provisioning_cancelled";

    private readonly ConcurrentDictionary<Guid, SyntheticGrant> grants = new();

    public async Task<AccessProvisioningOutcome> GetOrCreateAsync(
        AccessProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = Stopwatch.GetTimestamp();
        var settings = control.Capture();
        var outcome = await ExecuteAsync(request, settings, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var durationMilliseconds =
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var outcomeCode = GetOutcomeCode(outcome);
            LogProvisioningCompleted(
                logger,
                request.RequestId,
                request.CorrelationId,
                durationMilliseconds,
                outcomeCode);
        }

        return outcome;
    }

    private async Task<AccessProvisioningOutcome> ExecuteAsync(
        AccessProvisioningRequest request,
        SyntheticAccessProvisionerSettings settings,
        CancellationToken cancellationToken)
    {
        using var externalCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            applicationLifetime.ApplicationStopping);
        using var deadline = new CancellationTokenSource(ProvisioningTimeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            externalCancellation.Token,
            deadline.Token);

        try
        {
            if (settings.Delay > TimeSpan.Zero)
            {
                await Task.Delay(settings.Delay, operationCancellation.Token);
            }

            if (settings.Behavior == SyntheticAccessProvisioningBehavior.WaitForTimeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, operationCancellation.Token);
            }

            operationCancellation.Token.ThrowIfCancellationRequested();

            if (settings.Behavior == SyntheticAccessProvisioningBehavior.Fail)
            {
                return Failed(
                    ApplicationFailureKind.DependencyFailure,
                    FailureCode,
                    "Synthetic provisioning failed safely.");
            }

            var grant = grants.GetOrAdd(
                request.RequestId,
                _ => new SyntheticGrant(
                    Guid.NewGuid(),
                    request.RequesterId,
                    request.EnvironmentId,
                    request.RoleId,
                    clock.UtcNow.ToUniversalTime()));

            if (!grant.Matches(request))
            {
                return Failed(
                    ApplicationFailureKind.DependencyFailure,
                    ScopeMismatchCode,
                    "The existing synthetic grant does not match the requested scope.");
            }

            operationCancellation.Token.ThrowIfCancellationRequested();

            if (settings.Behavior ==
                SyntheticAccessProvisioningBehavior.LoseResponseAfterCreate)
            {
                return Failed(
                    ApplicationFailureKind.DependencyFailure,
                    LostResponseCode,
                    "The synthetic provisioning response was lost. Retrying is safe.");
            }

            return new AccessProvisioningSucceeded(grant.Id, grant.ActivatedAt);
        }
        catch (OperationCanceledException)
            when (operationCancellation.IsCancellationRequested)
        {
            return new AccessProvisioningFailed(ApplicationFailure.FromCancellation(
                CancelledCode,
                "Synthetic provisioning was cancelled.",
                TimeoutCode,
                "Synthetic provisioning exceeded its 10-second timeout.",
                externalCancellation.Token));
        }
    }

    private static string GetOutcomeCode(AccessProvisioningOutcome outcome) => outcome switch
    {
        AccessProvisioningSucceeded => SuccessCode,
        AccessProvisioningFailed failed => failed.Failure.Code,
        _ => "synthetic_provisioning_unknown",
    };

    private static AccessProvisioningFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        new(new ApplicationFailure(kind, code, message));

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        SkipEnabledCheck = true,
        Message = "Synthetic provisioning for request {RequestId} and correlation {CorrelationId} completed in {DurationMilliseconds} ms with outcome {Outcome}.")]
    private static partial void LogProvisioningCompleted(
        ILogger logger,
        Guid requestId,
        string correlationId,
        double durationMilliseconds,
        string outcome);

    private sealed record SyntheticGrant(
        Guid Id,
        string RequesterId,
        string EnvironmentId,
        string RoleId,
        DateTimeOffset ActivatedAt)
    {
        public bool Matches(AccessProvisioningRequest request)
        {
            return RequesterId == request.RequesterId
                && EnvironmentId == request.EnvironmentId
                && RoleId == request.RoleId;
        }
    }
}

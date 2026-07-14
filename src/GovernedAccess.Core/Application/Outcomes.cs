using System.Diagnostics.CodeAnalysis;

namespace GovernedAccess.Core.Application;

/// <summary>
/// Classifies expected application failures independently of any transport or provider SDK.
/// </summary>
public enum ApplicationFailureKind
{
    InvalidInput,
    NotFound,
    Unauthenticated,
    Unauthorized,
    StaleVersion,
    InvalidTransition,
    ConcurrencyConflict,
    Timeout,
    Cancelled,
    DependencyUnavailable,
    DependencyFailure,
}

/// <summary>
/// Represents an expected, safe-to-report operation failure.
/// </summary>
public sealed class ApplicationFailure
{
    public ApplicationFailure(
        ApplicationFailureKind kind,
        string code,
        string message,
        int? currentVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (currentVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentVersion),
                currentVersion,
                "A current request version must be positive.");
        }

        Kind = kind;
        Code = code;
        Message = message;
        CurrentVersion = currentVersion;
    }

    public ApplicationFailureKind Kind { get; }

    public string Code { get; }

    public string Message { get; }

    public int? CurrentVersion { get; }

    /// <summary>
    /// Distinguishes caller or host cancellation from an internally enforced timeout.
    /// Call this only when a linked operation has observed cancellation.
    /// </summary>
    public static ApplicationFailure FromCancellation(
        string cancelledCode,
        string cancelledMessage,
        string timeoutCode,
        string timeoutMessage,
        CancellationToken callerCancellationToken)
    {
        return callerCancellationToken.IsCancellationRequested
            ? new ApplicationFailure(
                ApplicationFailureKind.Cancelled,
                cancelledCode,
                cancelledMessage)
            : new ApplicationFailure(
                ApplicationFailureKind.Timeout,
                timeoutCode,
                timeoutMessage);
    }
}

/// <summary>
/// Represents the outcome of an application command that has no response value.
/// </summary>
public sealed class ApplicationResult
{
    private ApplicationResult(ApplicationFailure? failure)
    {
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public bool IsFailure => Failure is not null;

    public ApplicationFailure? Failure { get; }

    public static ApplicationResult Succeeded() => new(null);

    public static ApplicationResult Failed(ApplicationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ApplicationResult(failure);
    }

    public static ApplicationResult<T> Succeeded<T>(T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ApplicationResult<T>(value);
    }

    public static ApplicationResult<T> Failed<T>(ApplicationFailure failure)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ApplicationResult<T>(failure);
    }
}

/// <summary>
/// Represents either a non-null application response value or an expected failure.
/// </summary>
/// <typeparam name="T">The successful response type.</typeparam>
public sealed class ApplicationResult<T>
    where T : notnull
{
    private readonly T? value;

    internal ApplicationResult(T value)
    {
        this.value = value;
    }

    internal ApplicationResult(ApplicationFailure failure)
    {
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public bool IsFailure => Failure is not null;

    public ApplicationFailure? Failure { get; }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("A failed result does not contain a value.");

    public bool TryGetValue([MaybeNullWhen(false)] out T result)
    {
        result = value;
        return IsSuccess;
    }
}

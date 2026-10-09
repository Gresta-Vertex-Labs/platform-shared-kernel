using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Primitives.Telemetry;

/// <summary>
/// Records a failed operation on an <see cref="Activity"/> the one way every package agrees on, and returns the
/// matching <see cref="WellKnownOutcomes"/> value so the span and its metric tag come from a single call.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is recorded.</b> A failed <see cref="Result"/> sets the span status to <c>Error</c> described by
/// <see cref="Error.Code"/>, plus the tags <see cref="WellKnownTagKeys.ErrorCode"/> (the code) and
/// <see cref="WellKnownTagKeys.ErrorType"/> (the <see cref="Errors.ErrorType"/> member name, for example
/// <c>NotFound</c>). An exception sets the status described by the exception's full type name, never its message;
/// <see cref="WellKnownTagKeys.ErrorType"/> is that full name; the full exception event (type, message, stack) is
/// added through <see cref="Activity.AddException(Exception, in TagList, DateTimeOffset)"/>; and
/// <see cref="WellKnownTagKeys.ErrorCode"/> is added when the exception implements <see cref="IHasError"/>.
/// </para>
/// <para>
/// <b>Meaning of <c>error.type</c>.</b> For a <c>Result</c> failure it is the <see cref="Errors.ErrorType"/> name, not
/// an exception type. Either way the value set is bounded (an enum or a CLR type), so it is safe as a metric
/// dimension.
/// </para>
/// <para>
/// <b>Cancellation is not a failure.</b> An <see cref="OperationCanceledException"/> (including
/// <see cref="TaskCanceledException"/>) leaves the status <c>Unset</c>, adds no event and no <c>error.*</c> tag, and
/// returns <see cref="WellKnownOutcomes.Canceled"/>. <c>Ok</c> is never set: success leaves the span untouched.
/// </para>
/// <para>
/// <b>No-ops.</b> A <see langword="null"/>, stopped (<see cref="Activity.IsStopped"/>) or non-sampled
/// (<see cref="Activity.IsAllDataRequested"/> false) activity is never touched, but the outcome is still returned.
/// Passing a <see langword="null"/> <see cref="Error"/> or exception is a programming error and throws
/// <see cref="ArgumentNullException"/>, as does reading the error of an uninitialised <c>default(Result)</c>.
/// </para>
/// </remarks>
public static class ActivityFailureExtensions
{
    private const string OtherErrorType = "_OTHER";

    /// <summary>Records a failed operation described by <paramref name="error"/> and returns <see cref="WellKnownOutcomes.Failure"/>.</summary>
    /// <param name="activity">The span to record on; may be <see langword="null"/>.</param>
    /// <param name="error">The failure.</param>
    /// <returns><see cref="WellKnownOutcomes.Failure"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static string RecordFailure(this Activity? activity, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (IsRecording(activity))
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Code);
            activity.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            activity.SetTag(WellKnownTagKeys.ErrorType, ErrorTypeName(error.Type));
        }

        return WellKnownOutcomes.Failure;
    }

    /// <summary>
    /// Records an exception: status, <c>error.type</c>, <c>error.code</c> when available, and the exception event.
    /// Cancellation records nothing.
    /// </summary>
    /// <param name="activity">The span to record on; may be <see langword="null"/>.</param>
    /// <param name="exception">The exception that ended the operation.</param>
    /// <returns><see cref="WellKnownOutcomes.Canceled"/> for an <see cref="OperationCanceledException"/>, otherwise <see cref="WellKnownOutcomes.Exception"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static string RecordException(this Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
            return WellKnownOutcomes.Canceled;

        if (IsRecording(activity))
        {
            var typeName = exception.GetType().FullName ?? exception.GetType().Name;
            activity.SetStatus(ActivityStatusCode.Error, typeName);
            activity.SetTag(WellKnownTagKeys.ErrorType, typeName);
            if (exception is IHasError { Error: { } error })
                activity.SetTag(WellKnownTagKeys.ErrorCode, error.Code);
            AddEvents(activity, exception);
        }

        return WellKnownOutcomes.Exception;
    }

    /// <summary>Records <paramref name="result"/>: success touches nothing, failure is recorded by <see cref="RecordFailure"/>.</summary>
    /// <param name="activity">The span to record on; may be <see langword="null"/>.</param>
    /// <param name="result">The outcome of the operation.</param>
    /// <returns><see cref="WellKnownOutcomes.Success"/> or <see cref="WellKnownOutcomes.Failure"/>.</returns>
    public static string RecordResult(this Activity? activity, Result result)
        => result.IsSuccess ? WellKnownOutcomes.Success : activity.RecordFailure(result.Error);

    /// <summary>Records <paramref name="result"/>: success touches nothing, failure is recorded by <see cref="RecordFailure"/>.</summary>
    /// <typeparam name="T">The value type of the result.</typeparam>
    /// <param name="activity">The span to record on; may be <see langword="null"/>.</param>
    /// <param name="result">The outcome of the operation.</param>
    /// <returns><see cref="WellKnownOutcomes.Success"/> or <see cref="WellKnownOutcomes.Failure"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static string RecordResult<T>(this Activity? activity, Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? WellKnownOutcomes.Success : activity.RecordFailure(result.Error);
    }

    /// <summary>
    /// Adds the exception event(s) only, without status or tags: one event per inner exception of a flattened
    /// <see cref="AggregateException"/>. Cancellation adds nothing.
    /// </summary>
    /// <param name="activity">The span to record on; may be <see langword="null"/>.</param>
    /// <param name="exception">The exception to record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static void AddExceptionEvents(this Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException || !IsRecording(activity))
            return;

        AddEvents(activity, exception);
    }

    private static void AddEvents(Activity activity, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
                activity.AddException(inner);
        }
        else
        {
            activity.AddException(exception);
        }
    }

    private static bool IsRecording([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Activity? activity)
        => activity is { IsStopped: false, IsAllDataRequested: true };

    // A switch over literals rather than Enum.ToString(): allocation-free, and it keeps the tag value independent of
    // member renames. A new ErrorType member falls to "_OTHER" until it is mapped; a test enumerates the enum.
    private static string ErrorTypeName(Errors.ErrorType type) => type switch
    {
        Errors.ErrorType.None => "None",
        Errors.ErrorType.Unexpected => "Unexpected",
        Errors.ErrorType.Validation => "Validation",
        Errors.ErrorType.NotFound => "NotFound",
        Errors.ErrorType.Conflict => "Conflict",
        Errors.ErrorType.Unauthorized => "Unauthorized",
        Errors.ErrorType.BusinessRule => "BusinessRule",
        Errors.ErrorType.Forbidden => "Forbidden",
        Errors.ErrorType.Unavailable => "Unavailable",
        Errors.ErrorType.Timeout => "Timeout",
        _ => OtherErrorType,
    };
}

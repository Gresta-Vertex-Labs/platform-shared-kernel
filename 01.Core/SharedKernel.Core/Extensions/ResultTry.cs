using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Exception-boundary entry points that invoke a delegate and convert any thrown exception into a
/// <see cref="Result{T}"/> failure instead of letting it propagate.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Try{T}(Func{T})"/> / <see cref="TryAsync{T}(Func{Task{T}})"/> as the sanctioned seam
/// for the one legitimate place Result-oriented code still touches a throwing third-party SDK call or a
/// BCL method with no <see cref="Result{T}"/>-returning equivalent. Every call site otherwise
/// hand-rolling <c>try</c>/<c>catch</c>-to-<see cref="Result{T}"/> translation should route through this
/// instead.
/// </para>
/// <para>
/// The delegate is always invoked. On normal completion, the returned value becomes
/// <see cref="Result{T}.Success(T)"/>. On any thrown exception — never rethrown — the exception is
/// translated to <see cref="Result{T}.Failure(Error)"/>, either via the caller-supplied
/// <c>onException</c> mapper, or, when none is supplied, a default mapping that produces
/// <see cref="Error.Unexpected(string, string)"/> with code <see cref="ErrorCodes.Unexpected.Default"/>.
/// </para>
/// <para>
/// <b>
/// SECURITY (P-510/WO-083): THE DEFAULT MAPPING'S <see cref="Error.Message"/> IS ALWAYS THE FIXED,
/// SAFE <see cref="DefaultUnexpectedMessage"/> STRING — IT NEVER CONTAINS THE CAUGHT EXCEPTION'S RAW
/// TYPE NAME OR MESSAGE. A caught exception (e.g. from a database driver or third-party SDK) can carry
/// sensitive text — connection-string fragments, usernames, internal hostnames — that must never reach
/// an HTTP response via <c>Error.ToProblemDetails()</c>. The raw exception detail is instead recorded on
/// the AMBIENT <see cref="Activity"/> via <see cref="Activity.AddException(Exception, in TagList, DateTimeOffset)"/>
/// (a real .NET 8+ BCL member — no new dependency), the SAME ambient OpenTelemetry trace-context channel
/// this platform's Logging Conventions already use for CorrelationId/TraceId/SpanId propagation. When
/// <see cref="Activity.Current"/> is <c>null</c> (no active span), the exception detail is recorded
/// nowhere — a documented, accepted limitation. A caller wanting a guaranteed capture path must supply
/// its own <c>onException</c> mapper, which receives the raw exception exactly as before and is entirely
/// unaffected by this redaction.
/// </b>
/// </para>
/// <para>
/// An <see cref="AggregateException"/> (e.g., one caught from a <c>Task.Wait()</c>/<c>.Result</c>-style
/// call, or a <c>Task.WhenAll</c> await) is flattened via <see cref="AggregateException.Flatten"/> before
/// being recorded — one <see cref="Activity.AddException(Exception, in TagList, DateTimeOffset)"/> call
/// per flattened inner exception, so every inner exception is individually represented on the trace, not
/// just the generic outer aggregate. This flattening applies only to the default mapper; a caller-supplied
/// <c>onException</c> mapper receives the raw, unflattened exception and is free to apply its own
/// flattening strategy.
/// </para>
/// <para>
/// <b>
/// CANCELLATION (P-510/WO-083): EVERY CATCH CLAUSE ON THIS TYPE — INCLUDING THE CUSTOM-<c>onException</c>
/// OVERLOADS — EXCLUDES <see cref="OperationCanceledException"/> (AND ITS SUBCLASS
/// <see cref="TaskCanceledException"/>). A GENUINE CANCELLATION MUST ALWAYS PROPAGATE AS A THROWN
/// EXCEPTION; IT IS NEVER CONVERTED INTO A <see cref="Result{T}"/> FAILURE, REGARDLESS OF WHICH MAPPER
/// (DEFAULT OR CALLER-SUPPLIED) WOULD OTHERWISE HANDLE IT. A CLIENT DISCONNECT / REQUEST-ABORT MUST NOT
/// BECOME A SYNTHESIZED FAILURE RESULT THAT LOOKS LIKE AN ORDINARY 500.
/// </b>
/// </para>
/// <para>
/// <see cref="TryAsync{T}(Func{Task{T}})"/> is a genuine <c>async</c>/<c>await</c> method — the one
/// documented exception to this domain's "avoid async/await when only awaiting the input" railway rule
/// (see <see cref="ResultExtensions"/>'s async overloads). Catching an exception thrown during an awaited
/// operation requires the <c>try</c>/<c>catch</c> to wrap the <c>await</c> itself, which is impossible
/// without a genuine async state machine.
/// </para>
/// </remarks>
public static class ResultTry
{
    /// <summary>
    /// The fixed, safe <see cref="Error.Message"/> produced by the default exception mapping. Never
    /// varies with the caught exception's own type or message — see the SECURITY remarks on this type.
    /// </summary>
    public const string DefaultUnexpectedMessage = "An unexpected error occurred while executing the operation.";

    /// <summary>
    /// Invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/> on normal
    /// completion or <see cref="Result{T}.Failure(Error)"/> (via the default exception mapping) if it
    /// throws. Never rethrows.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The delegate to invoke inside the exception boundary.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <c>null</c>.</exception>
    public static Result<T> Try<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return Result<T>.Success(operation());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(MapException(exception));
        }
    }

    /// <summary>
    /// Invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/> on normal
    /// completion or <see cref="Result{T}.Failure(Error)"/> (via <paramref name="onException"/>) if it
    /// throws. Never rethrows — except a thrown <see cref="OperationCanceledException"/>, which always
    /// propagates uncaught, bypassing <paramref name="onException"/> entirely (see the CANCELLATION
    /// remarks on this type).
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The delegate to invoke inside the exception boundary.</param>
    /// <param name="onException">
    /// Maps a thrown exception to an <see cref="Error"/>. Receives the raw exception — including a raw,
    /// unflattened <see cref="AggregateException"/> when one is thrown. Never invoked for a thrown
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <c>null</c>.
    /// </exception>
    public static Result<T> Try<T>(Func<T> operation, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);

        try
        {
            return Result<T>.Success(operation());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(onException(exception));
        }
    }

    /// <summary>
    /// Asynchronously invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/>
    /// on normal completion or <see cref="Result{T}.Failure(Error)"/> (via the default exception mapping)
    /// if it throws or its returned task faults. Never rethrows.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The async delegate to invoke inside the exception boundary.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <c>null</c>.</exception>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            var value = await operation().ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(MapException(exception));
        }
    }

    /// <summary>
    /// Asynchronously invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/>
    /// on normal completion or <see cref="Result{T}.Failure(Error)"/> (via <paramref name="onException"/>)
    /// if it throws or its returned task faults. Never rethrows — except a thrown or faulted-with
    /// <see cref="OperationCanceledException"/>, which always propagates uncaught, bypassing
    /// <paramref name="onException"/> entirely (see the CANCELLATION remarks on this type).
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The async delegate to invoke inside the exception boundary.</param>
    /// <param name="onException">
    /// Maps a thrown exception to an <see cref="Error"/>. Receives the raw exception — including a raw,
    /// unflattened <see cref="AggregateException"/> when one is thrown. Never invoked for a thrown
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <c>null</c>.
    /// </exception>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> operation, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);

        try
        {
            var value = await operation().ConfigureAwait(false);
            return Result<T>.Success(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(onException(exception));
        }
    }

    /// <summary>
    /// The default exception-to-<see cref="Error"/> mapping used when the caller supplies no custom
    /// <c>onException</c> delegate. The returned <see cref="Error.Message"/> is ALWAYS the fixed
    /// <see cref="DefaultUnexpectedMessage"/> string — see the SECURITY remarks on this type. Flattens an
    /// <see cref="AggregateException"/> so every inner exception is individually recorded on the ambient
    /// <see cref="Activity"/>, not just the generic outer aggregate.
    /// </summary>
    private static Error MapException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                Activity.Current?.AddException(inner);
            }
        }
        else
        {
            Activity.Current?.AddException(exception);
        }

        return Error.Unexpected(ErrorCodes.Unexpected.Default, DefaultUnexpectedMessage);
    }
}

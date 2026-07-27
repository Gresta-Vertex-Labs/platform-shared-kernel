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
/// <see cref="Error.Unexpected(string, string)"/> with code <see cref="ErrorCodes.Unexpected.Default"/>
/// and a message of the form <c>"{ExceptionType}: {ExceptionMessage}"</c>.
/// </para>
/// <para>
/// An <see cref="AggregateException"/> (e.g., one caught from a <c>Task.Wait()</c>/<c>.Result</c>-style
/// call, or a <c>Task.WhenAll</c> await) is flattened via <see cref="AggregateException.Flatten"/> before
/// the default message is constructed, so every inner exception's type and message is represented —
/// never just the generic outer aggregate message. This flattening applies only to the default mapper; a
/// caller-supplied <c>onException</c> mapper receives the raw, unflattened exception and is free to apply
/// its own flattening strategy.
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
        catch (Exception exception)
        {
            return Result<T>.Failure(MapException(exception));
        }
    }

    /// <summary>
    /// Invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/> on normal
    /// completion or <see cref="Result{T}.Failure(Error)"/> (via <paramref name="onException"/>) if it
    /// throws. Never rethrows.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The delegate to invoke inside the exception boundary.</param>
    /// <param name="onException">
    /// Maps a thrown exception to an <see cref="Error"/>. Receives the raw exception — including a raw,
    /// unflattened <see cref="AggregateException"/> when one is thrown.
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
        catch (Exception exception)
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
        catch (Exception exception)
        {
            return Result<T>.Failure(MapException(exception));
        }
    }

    /// <summary>
    /// Asynchronously invokes <paramref name="operation"/>, returning <see cref="Result{T}.Success(T)"/>
    /// on normal completion or <see cref="Result{T}.Failure(Error)"/> (via <paramref name="onException"/>)
    /// if it throws or its returned task faults. Never rethrows.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The async delegate to invoke inside the exception boundary.</param>
    /// <param name="onException">
    /// Maps a thrown exception to an <see cref="Error"/>. Receives the raw exception — including a raw,
    /// unflattened <see cref="AggregateException"/> when one is thrown.
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
        catch (Exception exception)
        {
            return Result<T>.Failure(onException(exception));
        }
    }

    /// <summary>
    /// The default exception-to-<see cref="Error"/> mapping used when the caller supplies no custom
    /// <c>onException</c> delegate. Flattens an <see cref="AggregateException"/> so every inner exception
    /// is represented in the resulting message.
    /// </summary>
    private static Error MapException(Exception exception)
    {
        var message = exception is AggregateException aggregate
            ? FormatFlattened(aggregate.Flatten())
            : FormatSingle(exception);

        return Error.Unexpected(ErrorCodes.Unexpected.Default, message);
    }

    private static string FormatFlattened(AggregateException flattened)
        => string.Join("; ", flattened.InnerExceptions.Select(FormatSingle));

    private static string FormatSingle(Exception exception)
        => $"{exception.GetType().Name}: {exception.Message}";
}

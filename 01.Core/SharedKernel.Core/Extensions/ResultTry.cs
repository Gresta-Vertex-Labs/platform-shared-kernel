using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Exception boundaries: run a delegate and turn a thrown exception into a failed <see cref="Result"/> or
/// <see cref="Result{T}"/> instead of letting it propagate.
/// </summary>
/// <remarks>
/// <para>
/// Use these where result-oriented code has to call something that throws, such as a third-party SDK or a
/// BCL method with no result-returning equivalent, instead of hand-writing <c>try</c>/<c>catch</c>.
/// </para>
/// <para>
/// <b>Default mapping.</b> Without an <c>onException</c> mapper, a thrown exception becomes
/// <see cref="Error.Unexpected(string, string)"/> with code <see cref="ErrorCodes.Unexpected.Default"/> and
/// the fixed message <see cref="DefaultUnexpectedMessage"/>. The exception's own type and message are never
/// copied into the error: they can contain connection-string fragments, user names, or internal host
/// names, and an error's message can reach an HTTP response. The exception is recorded on the current
/// <see cref="Activity"/> instead, one entry per inner exception of an <see cref="AggregateException"/>.
/// When there is no current activity it is recorded nowhere; pass an <c>onException</c> mapper to capture
/// it yourself.
/// </para>
/// <para>
/// <b>Cancellation.</b> <see cref="OperationCanceledException"/>, including
/// <see cref="TaskCanceledException"/>, is never caught, with or without a mapper. A cancelled request
/// must surface as cancellation, not as a failure that looks like an ordinary error.
/// </para>
/// </remarks>
public static class ResultTry
{
    /// <summary>
    /// The message of the error produced by the default exception mapping. It never varies with the
    /// exception caught.
    /// </summary>
    public const string DefaultUnexpectedMessage = "An unexpected error occurred while executing the operation.";

    /// <summary>Runs <paramref name="operation"/> and returns its value as a success, or the exception as a failure.</summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The delegate to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Result<T> Try<T>(Func<T> operation)
        => Try(operation, MapException);

    /// <summary>Runs <paramref name="operation"/> and returns its value as a success, or the exception mapped by <paramref name="onException"/>.</summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The delegate to run.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged, including an unflattened <see cref="AggregateException"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
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

    /// <summary>Runs <paramref name="operation"/> and returns a success, or the exception as a failure.</summary>
    /// <param name="operation">The delegate to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Result Try(Action operation)
        => Try(operation, MapException);

    /// <summary>Runs <paramref name="operation"/> and returns a success, or the exception mapped by <paramref name="onException"/>.</summary>
    /// <param name="operation">The delegate to run.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    public static Result Try(Action operation, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);

        try
        {
            operation();
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(onException(exception));
        }
    }

    /// <summary>Awaits <paramref name="operation"/> and returns its value as a success, or the exception as a failure.</summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The asynchronous delegate to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Task<Result<T>> TryAsync<T>(Func<Task<T>> operation)
        => TryAsync(operation, MapException);

    /// <summary>Awaits <paramref name="operation"/> and returns its value as a success, or the exception mapped by <paramref name="onException"/>.</summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The asynchronous delegate to run.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged, including an unflattened <see cref="AggregateException"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    public static Task<Result<T>> TryAsync<T>(Func<Task<T>> operation, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);
        return Core(operation, onException);

        static async Task<Result<T>> Core(Func<Task<T>> operation, Func<Exception, Error> onException)
        {
            try
            {
                return Result<T>.Success(await operation().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Result<T>.Failure(onException(exception));
            }
        }
    }

    /// <summary>Awaits <paramref name="operation"/> and returns a success, or the exception as a failure.</summary>
    /// <param name="operation">The asynchronous delegate to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Task<Result> TryAsync(Func<Task> operation)
        => TryAsync(operation, MapException);

    /// <summary>Awaits <paramref name="operation"/> and returns a success, or the exception mapped by <paramref name="onException"/>.</summary>
    /// <param name="operation">The asynchronous delegate to run.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    public static Task<Result> TryAsync(Func<Task> operation, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);
        return Core(operation, onException);

        static async Task<Result> Core(Func<Task> operation, Func<Exception, Error> onException)
        {
            try
            {
                await operation().ConfigureAwait(false);
                return Result.Success();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Result.Failure(onException(exception));
            }
        }
    }

    /// <summary>
    /// Awaits <paramref name="operation"/> with <paramref name="cancellationToken"/> and returns its value as a
    /// success, or the exception as a failure.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> without running the delegate when the token is already
    /// cancelled.
    /// </remarks>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The asynchronous delegate to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">The token passed to <paramref name="operation"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Task<Result<T>> TryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => TryAsync(operation, MapException, cancellationToken);

    /// <summary>
    /// Awaits <paramref name="operation"/> with <paramref name="cancellationToken"/> and returns its value as a
    /// success, or the exception mapped by <paramref name="onException"/>.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> without running the delegate when the token is already
    /// cancelled.
    /// </remarks>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="operation">The asynchronous delegate to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged.</param>
    /// <param name="cancellationToken">The token passed to <paramref name="operation"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    public static Task<Result<T>> TryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, Error> onException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);
        return TryAsync(() => Start(operation, cancellationToken), onException);
    }

    /// <summary>
    /// Awaits <paramref name="operation"/> with <paramref name="cancellationToken"/> and returns a success, or
    /// the exception as a failure.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> without running the delegate when the token is already
    /// cancelled.
    /// </remarks>
    /// <param name="operation">The asynchronous delegate to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">The token passed to <paramref name="operation"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is <see langword="null"/>.</exception>
    public static Task<Result> TryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        => TryAsync(operation, MapException, cancellationToken);

    /// <summary>
    /// Awaits <paramref name="operation"/> with <paramref name="cancellationToken"/> and returns a success, or
    /// the exception mapped by <paramref name="onException"/>.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="OperationCanceledException"/> without running the delegate when the token is already
    /// cancelled.
    /// </remarks>
    /// <param name="operation">The asynchronous delegate to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="onException">Maps the exception to an error. Receives it unchanged.</param>
    /// <param name="cancellationToken">The token passed to <paramref name="operation"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    public static Task<Result> TryAsync(
        Func<CancellationToken, Task> operation,
        Func<Exception, Error> onException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onException);
        return TryAsync(() => Start(operation, cancellationToken), onException);
    }

    private static Task<T> Start<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation(cancellationToken);
    }

    private static Task Start(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation(cancellationToken);
    }

    private static Error MapException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
                Activity.Current?.AddException(inner);
        }
        else
        {
            Activity.Current?.AddException(exception);
        }

        return Error.Unexpected(ErrorCodes.Unexpected.Default, DefaultUnexpectedMessage);
    }
}

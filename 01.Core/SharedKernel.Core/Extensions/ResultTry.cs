using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Exception boundaries: run code that throws and get a <see cref="Result"/> or <see cref="Result{T}"/> back
/// instead of an exception.
/// </summary>
/// <remarks>
/// <para>
/// Use these where result-oriented code must call something that reports failure by throwing, such as a
/// third-party SDK, a database driver, or a BCL method with no result-returning equivalent. They replace a
/// hand-written <c>try</c>/<c>catch</c> that converts to a result.
/// </para>
/// <para>
/// <b>Default mapping.</b> Without an <c>onException</c> mapper, any exception becomes
/// <see cref="Error.Unexpected(string, string)"/> with code <see cref="ErrorCodes.Unexpected.Default"/> and the
/// fixed message <see cref="DefaultUnexpectedMessage"/>. The exception's type and message are never copied into
/// the error, because they can contain connection strings, user names, or host names, and an error message can
/// reach an HTTP response. The exception is recorded on <see cref="Activity.Current"/> instead (one event per
/// inner exception of an <see cref="AggregateException"/>), so it appears in distributed traces. When no
/// activity is current it is recorded nowhere; supply a mapper to capture or log it yourself.
/// </para>
/// <para>
/// <b>Custom mapping.</b> An <c>onException</c> mapper receives the exception unchanged, including an unflattened
/// <see cref="AggregateException"/>, and its <see cref="Error"/> becomes the failure. An exception thrown by the
/// mapper propagates.
/// </para>
/// <para>
/// <b>Cancellation.</b> <see cref="OperationCanceledException"/>, including <see cref="TaskCanceledException"/>,
/// is never caught, with or without a mapper. A cancelled request surfaces as cancellation, not as a failure that
/// looks like an ordinary error.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Default mapping: unexpected.exception with a safe message; the exception goes to the trace.
/// Result&lt;Invoice&gt; invoice = await ResultTry.TryAsync(
///     ct =&gt; billingClient.GetInvoiceAsync(invoiceId, ct),
///     cancellationToken);
///
/// // Custom mapping: translate a known exception into a meaningful error.
/// Result&lt;Customer&gt; customer = ResultTry.Try(
///     () =&gt; crm.GetCustomer(customerId),
///     ex =&gt; ex is CrmNotFoundException
///         ? Error.NotFound("customer.not_found", $"Customer {customerId} does not exist.")
///         : Error.Unexpected(ErrorCodes.Unexpected.Default, ResultTry.DefaultUnexpectedMessage));
/// </code>
/// </example>
public static class ResultTry
{
    /// <summary>
    /// The message of the error produced by the default mapping: <c>"An unexpected error occurred while executing
    /// the operation."</c> It never varies with the exception caught.
    /// </summary>
    public const string DefaultUnexpectedMessage = "An unexpected error occurred while executing the operation.";

    /// <summary>Runs <paramref name="operation"/> and captures its value, or the exception it throws, as a result.</summary>
    /// <typeparam name="T">The type of the value <paramref name="operation"/> returns.</typeparam>
    /// <param name="operation">The code to run.</param>
    /// <returns>A success carrying the returned value, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="operation"/> threw it; cancellation is never caught.</exception>
    public static Result<T> Try<T>(Func<T> operation)
        => Try(operation, MapException);

    /// <summary>Runs <paramref name="operation"/> and captures its value, or the exception it throws mapped by <paramref name="onException"/>, as a result.</summary>
    /// <typeparam name="T">The type of the value <paramref name="operation"/> returns.</typeparam>
    /// <param name="operation">The code to run.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <returns>A success carrying the returned value, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="operation"/> threw it; cancellation is never caught.</exception>
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

    /// <summary>Runs <paramref name="operation"/> and captures its completion, or the exception it throws, as a result.</summary>
    /// <param name="operation">The code to run.</param>
    /// <returns>A success when <paramref name="operation"/> completes, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="operation"/> threw it; cancellation is never caught.</exception>
    public static Result Try(Action operation)
        => Try(operation, MapException);

    /// <summary>Runs <paramref name="operation"/> and captures its completion, or the exception it throws mapped by <paramref name="onException"/>, as a result.</summary>
    /// <param name="operation">The code to run.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <returns>A success when <paramref name="operation"/> completes, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="operation"/> threw it; cancellation is never caught.</exception>
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

    /// <summary>Awaits <paramref name="operation"/> and captures its value, or the exception it throws, as a result.</summary>
    /// <remarks>An exception thrown synchronously by the delegate and one thrown from the awaited task are handled the same way.</remarks>
    /// <typeparam name="T">The type of the value the task produces.</typeparam>
    /// <param name="operation">The asynchronous code to run.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the operation was cancelled.</exception>
    public static Task<Result<T>> TryAsync<T>(Func<Task<T>> operation)
        => TryAsync(operation, MapException);

    /// <summary>Awaits <paramref name="operation"/> and captures its value, or the exception it throws mapped by <paramref name="onException"/>, as a result.</summary>
    /// <remarks>An exception thrown synchronously by the delegate and one thrown from the awaited task are handled the same way.</remarks>
    /// <typeparam name="T">The type of the value the task produces.</typeparam>
    /// <param name="operation">The asynchronous code to run.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the operation was cancelled.</exception>
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

    /// <summary>Awaits <paramref name="operation"/> and captures its completion, or the exception it throws, as a result.</summary>
    /// <param name="operation">The asynchronous code to run.</param>
    /// <returns>A task whose result is a success when the operation completes, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the operation was cancelled.</exception>
    public static Task<Result> TryAsync(Func<Task> operation)
        => TryAsync(operation, MapException);

    /// <summary>Awaits <paramref name="operation"/> and captures its completion, or the exception it throws mapped by <paramref name="onException"/>, as a result.</summary>
    /// <param name="operation">The asynchronous code to run.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <returns>A task whose result is a success when the operation completes, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the operation was cancelled.</exception>
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
    /// Awaits <paramref name="operation"/>, passing it <paramref name="cancellationToken"/>, and captures its value,
    /// or the exception it throws, as a result.
    /// </summary>
    /// <remarks>The delegate is not called when <paramref name="cancellationToken"/> is already cancelled.</remarks>
    /// <typeparam name="T">The type of the value the task produces.</typeparam>
    /// <param name="operation">The asynchronous code to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">The token to pass to <paramref name="operation"/>.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the token was cancelled before the call or the operation was cancelled.</exception>
    public static Task<Result<T>> TryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => TryAsync(operation, MapException, cancellationToken);

    /// <summary>
    /// Awaits <paramref name="operation"/>, passing it <paramref name="cancellationToken"/>, and captures its value,
    /// or the exception it throws mapped by <paramref name="onException"/>, as a result.
    /// </summary>
    /// <remarks>The delegate is not called when <paramref name="cancellationToken"/> is already cancelled.</remarks>
    /// <typeparam name="T">The type of the value the task produces.</typeparam>
    /// <param name="operation">The asynchronous code to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <param name="cancellationToken">The token to pass to <paramref name="operation"/>.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the token was cancelled before the call or the operation was cancelled.</exception>
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
    /// Awaits <paramref name="operation"/>, passing it <paramref name="cancellationToken"/>, and captures its
    /// completion, or the exception it throws, as a result.
    /// </summary>
    /// <remarks>The delegate is not called when <paramref name="cancellationToken"/> is already cancelled.</remarks>
    /// <param name="operation">The asynchronous code to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">The token to pass to <paramref name="operation"/>.</param>
    /// <returns>A task whose result is a success when the operation completes, or a failure produced by the default mapping.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the token was cancelled before the call or the operation was cancelled.</exception>
    public static Task<Result> TryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        => TryAsync(operation, MapException, cancellationToken);

    /// <summary>
    /// Awaits <paramref name="operation"/>, passing it <paramref name="cancellationToken"/>, and captures its
    /// completion, or the exception it throws mapped by <paramref name="onException"/>, as a result.
    /// </summary>
    /// <remarks>The delegate is not called when <paramref name="cancellationToken"/> is already cancelled.</remarks>
    /// <param name="operation">The asynchronous code to run. Receives <paramref name="cancellationToken"/>.</param>
    /// <param name="onException">Maps the thrown exception to the failure's error.</param>
    /// <param name="cancellationToken">The token to pass to <paramref name="operation"/>.</param>
    /// <returns>A task whose result is a success when the operation completes, or a failure carrying <paramref name="onException"/>'s error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="onException"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when the token was cancelled before the call or the operation was cancelled.</exception>
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

using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Railway-oriented operations on <see cref="Result{T}"/> and <see cref="Result"/>: chain steps that can fail
/// without writing an <c>if (result.IsFailure)</c> after each one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Short-circuiting.</b> A failure skips every later success-path step (<c>Map</c>, <c>Bind</c>,
/// <c>Ensure</c>, <c>Tap</c>) and flows through unchanged, so the first failure is the one that reaches the end
/// of the chain. <c>MapError</c> and <c>TapError</c> run only on failure; <c>Match</c> runs exactly one branch.
/// </para>
/// <list type="table">
///   <listheader><term>Operation</term><description>On success / on failure</description></listheader>
///   <item><term><c>Map</c></term><description>Transforms the value / passes the error through.</description></item>
///   <item><term><c>Bind</c></term><description>Runs the next result-returning step / passes the error through.</description></item>
///   <item><term><c>Ensure</c></term><description>Fails with the given error when a condition does not hold / passes the error through.</description></item>
///   <item><term><c>Tap</c></term><description>Runs a side effect and returns the result / skipped.</description></item>
///   <item><term><c>TapError</c></term><description>Skipped / runs a side effect and returns the result.</description></item>
///   <item><term><c>MapError</c></term><description>Passes the value through / transforms the error.</description></item>
///   <item><term><c>Match</c></term><description>Folds either branch into one value.</description></item>
///   <item><term><c>GetValueOrThrow</c>, <c>ThrowIfFailure</c></term><description>Returns the value / throws the matching <see cref="SharedKernelException"/>.</description></item>
/// </list>
/// <para>
/// <b>Async.</b> Every operation also accepts a <see cref="Task{TResult}"/> or <see cref="ValueTask{TResult}"/>
/// source. A <see cref="Task{TResult}"/> source and a plain result take synchronous or <see cref="Task"/>-returning
/// steps; a <see cref="ValueTask{TResult}"/> source takes synchronous or <see cref="ValueTask"/>-returning steps.
/// Never both on one source, which is what lets an <c>async</c> lambda bind to exactly one overload. Awaiting a
/// chain rethrows a faulted source's original exception and a cancelled source's
/// <see cref="OperationCanceledException"/>; neither becomes a failed result.
/// </para>
/// <para>
/// <b>Exceptions.</b> Every operation throws <see cref="ArgumentNullException"/> for a <see langword="null"/>
/// source <see cref="Result{T}"/> or delegate, before running anything. An exception thrown by a step
/// propagates unchanged; wrap throwing calls with <see cref="ResultTry"/> to turn them into failures.
/// Operations on a non-generic <see cref="Result"/> that read its error throw <see cref="InvalidOperationException"/>
/// for an uninitialized <c>default(Result)</c>, which is neither a success nor a valid failure.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Result&lt;ReceiptDto&gt; receipt = await orders.FindAsync(orderId, ct)            // Task&lt;Result&lt;Order&gt;&gt;
///     .Ensure(order =&gt; order.IsOpen, OrderErrors.Closed)
///     .Bind(order =&gt; payments.ChargeAsync(order, ct))                          // Task&lt;Result&lt;Payment&gt;&gt;
///     .Map(payment =&gt; payment.ToReceipt())
///     .TapError(error =&gt; Log.CheckoutFailed(logger, error.Code));
/// </code>
/// </example>
public static partial class ResultExtensions
{
    // -------------------------------------------------------------------------
    // Result<T>
    // -------------------------------------------------------------------------

    /// <summary>Transforms the success value, leaving a failure unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The success type of the returned result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">The transformation. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns>A success carrying <paramref name="map"/>'s output, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="map"/> is <see langword="null"/>.</exception>
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Transforms the error of a failure, leaving a success unchanged.</summary>
    /// <remarks>Use it to translate a lower layer's error into one that makes sense to this layer's caller.</remarks>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">The transformation. Runs only when <paramref name="result"/> failed.</param>
    /// <returns>A failure carrying <paramref name="map"/>'s output, or <paramref name="result"/> itself when it succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="map"/> is <see langword="null"/>.</exception>
    public static Result<T> MapError<T>(this Result<T> result, Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.IsFailure ? Result<T>.Failure(map(result.Error)) : result;
    }

    /// <summary>Runs the next step that can fail, passing it the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The success type of the next step.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The next step. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns><paramref name="bind"/>'s result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="bind"/> is <see langword="null"/>.</exception>
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Runs the next step, which returns no value, passing it the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The next step. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns><paramref name="bind"/>'s result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="bind"/> is <see langword="null"/>.</exception>
    public static Result Bind<T>(this Result<T> result, Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result.Failure(result.Error);
    }

    /// <summary>Folds the result into a single value by running exactly one of two functions.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <typeparam name="TOut">The type of the folded value.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Produces the value from the success value.</param>
    /// <param name="onFailure">Produces the value from the error.</param>
    /// <returns>The output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/>, <paramref name="onSuccess"/>, or <paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }

    /// <summary>Runs a side effect with the success value, then returns the result unchanged.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect, such as logging or publishing an event. Runs only on success.</param>
    /// <returns><paramref name="result"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    /// <summary>Runs a side effect with the error of a failure, then returns the result unchanged.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect, such as logging the error code. Runs only on failure.</param>
    /// <returns><paramref name="result"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Result<T> TapError<T>(this Result<T> result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsFailure)
            action(result.Error);

        return result;
    }

    /// <summary>Turns a success into a failure when the success value does not satisfy a condition.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition the value must satisfy. Runs only on success.</param>
    /// <param name="error">The error to fail with when <paramref name="predicate"/> returns <see langword="false"/>.</param>
    /// <returns>
    /// <paramref name="result"/> when it failed or the condition holds; otherwise a failure carrying
    /// <paramref name="error"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/>, <paramref name="predicate"/>, or <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.IsFailure || predicate(result.Value) ? result : Result<T>.Failure(error);
    }

    /// <summary>
    /// Turns a success into a failure when the success value does not satisfy a condition, building the error
    /// from the rejected value.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition the value must satisfy. Runs only on success.</param>
    /// <param name="errorFactory">Builds the error from the rejected value. Runs only when the condition fails.</param>
    /// <returns>
    /// <paramref name="result"/> when it failed or the condition holds; otherwise a failure carrying the error
    /// from <paramref name="errorFactory"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/>, <paramref name="predicate"/>, or <paramref name="errorFactory"/> is <see langword="null"/>.</exception>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Error> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);
        return result.IsFailure || predicate(result.Value) ? result : Result<T>.Failure(errorFactory(result.Value));
    }

    /// <summary>Returns the success value, or throws the exception that matches the error.</summary>
    /// <remarks>
    /// The bridge from the result railway to code that works with exceptions, such as a constructor or a test.
    /// The exception type follows <see cref="ErrorExceptionExtensions.ToException(Error)"/>.
    /// </remarks>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <returns>The success value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="SharedKernelException"><paramref name="result"/> failed. The subclass matches the error's <see cref="Error.Type"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="result"/> failed with <see cref="Error.None"/>.</exception>
    public static T GetValueOrThrow<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? result.Value : throw result.Error.ToException();
    }

    // -------------------------------------------------------------------------
    // Result
    // -------------------------------------------------------------------------

    /// <summary>Produces a value after a success, leaving a failure unchanged.</summary>
    /// <typeparam name="TOut">The success type of the returned result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">Produces the value. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns>A success carrying <paramref name="map"/>'s output, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>.</exception>
    public static Result<TOut> Map<TOut>(this Result result, Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess ? Result<TOut>.Success(map()) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Transforms the error of a failure, leaving a success unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="map">The transformation. Runs only when <paramref name="result"/> failed.</param>
    /// <returns>A failure carrying <paramref name="map"/>'s output, or <paramref name="result"/> itself when it succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>.</exception>
    public static Result MapError(this Result result, Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsFailure ? Result.Failure(map(result.Error)) : result;
    }

    /// <summary>Runs the next step that can fail after a success.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The next step. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns><paramref name="bind"/>'s result, or <paramref name="result"/> itself when it failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>.</exception>
    public static Result Bind(this Result result, Func<Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind() : result;
    }

    /// <summary>Runs the next step, which produces a value, after a success.</summary>
    /// <typeparam name="TOut">The success type of the next step.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The next step. Runs only when <paramref name="result"/> succeeded.</param>
    /// <returns><paramref name="bind"/>'s result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>.</exception>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind() : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Folds the result into a single value by running exactly one of two functions.</summary>
    /// <typeparam name="TOut">The type of the folded value.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Produces the value from the error.</param>
    /// <returns>The output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }

    /// <summary>Runs exactly one of two actions depending on the outcome.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Runs on success.</param>
    /// <param name="onFailure">Runs on failure, receiving the error.</param>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public static void Match(this Result result, Action onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (result.IsSuccess)
            onSuccess();
        else
            onFailure(result.Error);
    }

    /// <summary>Runs a side effect after a success, then returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect. Runs only on success.</param>
    /// <returns><paramref name="result"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public static Result Tap(this Result result, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
            action();

        return result;
    }

    /// <summary>Runs a side effect with the error of a failure, then returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect. Runs only on failure.</param>
    /// <returns><paramref name="result"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public static Result TapError(this Result result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsFailure)
            action(result.Error);

        return result;
    }

    /// <summary>Turns a success into a failure when a condition does not hold.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition that must hold. Runs only on success.</param>
    /// <param name="error">The error to fail with when <paramref name="predicate"/> returns <see langword="false"/>.</param>
    /// <returns><paramref name="result"/> when it failed or the condition holds; otherwise a failure carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Ensure(this Result result, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.IsFailure || predicate() ? result : Result.Failure(error);
    }

    /// <summary>Throws the exception that matches the error when the result is a failure.</summary>
    /// <remarks>The exception type follows <see cref="ErrorExceptionExtensions.ToException(Error)"/>.</remarks>
    /// <param name="result">The source result.</param>
    /// <exception cref="SharedKernelException"><paramref name="result"/> failed. The subclass matches the error's <see cref="Error.Type"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="result"/> failed with <see cref="Error.None"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> is an uninitialized <c>default(Result)</c>.</exception>
    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
            throw result.Error.ToException();
    }
}

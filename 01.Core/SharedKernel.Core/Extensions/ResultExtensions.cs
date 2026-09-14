using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Railway-oriented extension methods for <see cref="Result{T}"/> and <see cref="Result"/>.
/// </summary>
/// <remarks>
/// <para>
/// A failure short-circuits every operation: the continuation is not called and the original
/// <see cref="Error"/> flows through unchanged. <c>Tap</c> and <c>TapError</c> run a side effect and return
/// the result they were given.
/// </para>
/// <para>
/// Every operation has asynchronous overloads. A <see cref="Task{TResult}"/> source accepts synchronous or
/// <see cref="Task"/>-returning continuations; a <see cref="ValueTask{TResult}"/> source accepts synchronous
/// or <see cref="ValueTask"/>-returning continuations; a plain result accepts <see cref="Task"/>-returning
/// continuations. Keeping the continuation's awaitable type the same as the source's is what lets an
/// <c>async</c> lambda bind to exactly one overload.
/// </para>
/// <para>
/// Awaiting an asynchronous overload rethrows the original exception of a faulted source and
/// <see cref="OperationCanceledException"/> for a cancelled one. Neither is ever converted into a failed
/// result.
/// </para>
/// </remarks>
public static partial class ResultExtensions
{
    // -------------------------------------------------------------------------
    // Result<T>
    // -------------------------------------------------------------------------

    /// <summary>Projects the success value through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The input success type.</typeparam>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">The projection applied to the success value.</param>
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    public static Result<T> MapError<T>(this Result<T> result, Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.IsFailure ? Result<T>.Failure(map(result.Error)) : result;
    }

    /// <summary>Chains a result-returning operation onto the success value.</summary>
    /// <typeparam name="T">The input success type.</typeparam>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The operation to run on the success value.</param>
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Chains an operation that returns a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The input success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The operation to run on the success value.</param>
    public static Result Bind<T>(this Result<T> result, Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind(result.Value) : Result.Failure(result.Error);
    }

    /// <summary>Folds the result into a single value.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <typeparam name="TOut">The folded output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }

    /// <summary>Runs <paramref name="action"/> on the success value, then returns the result unchanged.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    /// <summary>Runs <paramref name="action"/> on the error of a failed result, then returns the result unchanged.</summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect to run on failure, such as logging.</param>
    public static Result<T> TapError<T>(this Result<T> result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsFailure)
            action(result.Error);

        return result;
    }

    /// <summary>
    /// Turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/>
    /// returns <see langword="false"/>.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition the success value must satisfy.</param>
    /// <param name="error">The error to return when the condition is not satisfied.</param>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.IsFailure || predicate(result.Value) ? result : Result<T>.Failure(error);
    }

    /// <summary>
    /// Turns a success into a failure carrying the error built by <paramref name="errorFactory"/> when
    /// <paramref name="predicate"/> returns <see langword="false"/>.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition the success value must satisfy.</param>
    /// <param name="errorFactory">Builds the error from the rejected value.</param>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Error> errorFactory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorFactory);
        return result.IsFailure || predicate(result.Value) ? result : Result<T>.Failure(errorFactory(result.Value));
    }

    /// <summary>
    /// Returns the success value, or throws the exception that matches the error. The bridge from the
    /// result railway to code that expects exceptions.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <exception cref="SharedKernelException">
    /// Thrown when the result is a failure; the subclass is chosen by <see cref="ErrorExceptionExtensions.ToException(Error)"/>.
    /// </exception>
    public static T GetValueOrThrow<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? result.Value : throw result.Error.ToException();
    }

    // -------------------------------------------------------------------------
    // Result
    // -------------------------------------------------------------------------

    /// <summary>Produces a value from <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">Produces the success value.</param>
    public static Result<TOut> Map<TOut>(this Result result, Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess ? Result<TOut>.Success(map()) : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    public static Result MapError(this Result result, Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsFailure ? Result.Failure(map(result.Error)) : result;
    }

    /// <summary>Chains a result-returning operation after a success.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    public static Result Bind(this Result result, Func<Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind() : result;
    }

    /// <summary>Chains an operation that returns a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? bind() : Result<TOut>.Failure(result.Error);
    }

    /// <summary>Folds the result into a single value.</summary>
    /// <typeparam name="TOut">The folded output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? onSuccess() : onFailure(result.Error);
    }

    /// <summary>Runs <paramref name="onSuccess"/> or <paramref name="onFailure"/> depending on the result.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Runs on success.</param>
    /// <param name="onFailure">Runs on failure, receiving the error.</param>
    public static void Match(this Result result, Action onSuccess, Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (result.IsSuccess)
            onSuccess();
        else
            onFailure(result.Error);
    }

    /// <summary>Runs <paramref name="action"/> on success, then returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    public static Result Tap(this Result result, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
            action();

        return result;
    }

    /// <summary>Runs <paramref name="action"/> on the error of a failed result, then returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The side effect to run on failure, such as logging.</param>
    public static Result TapError(this Result result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsFailure)
            action(result.Error);

        return result;
    }

    /// <summary>
    /// Turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/>
    /// returns <see langword="false"/>.
    /// </summary>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    public static Result Ensure(this Result result, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.IsFailure || predicate() ? result : Result.Failure(error);
    }

    /// <summary>
    /// Throws the exception that matches the error when the result is a failure. The bridge from the result
    /// railway to code that expects exceptions.
    /// </summary>
    /// <param name="result">The source result.</param>
    /// <exception cref="SharedKernelException">
    /// Thrown when the result is a failure; the subclass is chosen by <see cref="ErrorExceptionExtensions.ToException(Error)"/>.
    /// </exception>
    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
            throw result.Error.ToException();
    }
}

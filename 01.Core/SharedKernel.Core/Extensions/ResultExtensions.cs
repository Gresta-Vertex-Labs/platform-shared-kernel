using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Railway-oriented extension methods for <see cref="Result{T}"/> and <see cref="Result"/>.
/// All methods are static and AOT-safe. Async overloads avoid unnecessary state machine
/// allocation on the outer extension body — only the continuation lambda is async when required.
/// </summary>
public static class ResultExtensions
{
    // -------------------------------------------------------------------------
    // Synchronous extensions on Result<T>
    // -------------------------------------------------------------------------

    /// <summary>
    /// Projects the success value of <paramref name="result"/> through <paramref name="map"/>.
    /// If the result is a failure, the error is forwarded unchanged.
    /// </summary>
    /// <typeparam name="T">The input success type.</typeparam>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">A projection applied to the success value.</param>
    public static Result<TOut> Map<T, TOut>(
        this Result<T> result,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsSuccess
            ? Result<TOut>.Success(map(result.Value))
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>
    /// Projects the error of a failed <paramref name="result"/> through <paramref name="map"/>.
    /// If the result is a success, the value is forwarded unchanged.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">A projection applied to the error.</param>
    public static Result<T> MapError<T>(
        this Result<T> result,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return result.IsFailure
            ? Result<T>.Failure(map(result.Error))
            : result;
    }

    /// <summary>
    /// Chains a result-returning operation after a successful <paramref name="result"/>.
    /// Short-circuits on failure — <paramref name="bind"/> is never called when the input is a failure.
    /// </summary>
    /// <typeparam name="T">The input success type.</typeparam>
    /// <typeparam name="TOut">The output success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">A function that returns a new result from the success value.</param>
    public static Result<TOut> Bind<T, TOut>(
        this Result<T> result,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess
            ? bind(result.Value)
            : Result<TOut>.Failure(result.Error);
    }

    /// <summary>
    /// Folds the result into a single value by applying either <paramref name="onSuccess"/>
    /// or <paramref name="onFailure"/>.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <typeparam name="TOut">The folded output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Applied when the result is a success.</param>
    /// <param name="onFailure">Applied when the result is a failure.</param>
    public static TOut Match<T, TOut>(
        this Result<T> result,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Error);
    }

    /// <summary>
    /// Executes a side-effecting <paramref name="action"/> if the result is a success,
    /// then returns the original result unchanged.
    /// </summary>
    /// <typeparam name="T">The success type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">A side-effecting action applied to the success value.</param>
    public static Result<T> Tap<T>(
        this Result<T> result,
        Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    // -------------------------------------------------------------------------
    // Void Match on non-generic Result
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes either <paramref name="onSuccess"/> or <paramref name="onFailure"/> depending
    /// on the state of the non-generic <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The source void result.</param>
    /// <param name="onSuccess">Action executed when the result is a success.</param>
    /// <param name="onFailure">Action executed when the result is a failure, receiving the error.</param>
    public static void Match(
        this Result result,
        Action onSuccess,
        Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (result.IsSuccess)
            onSuccess();
        else
            onFailure(result.Error);
    }

    // -------------------------------------------------------------------------
    // Async extensions on Task<Result<T>>
    // Outer extension bodies do NOT use async/await to avoid needless state machines.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asynchronously projects the success value using a synchronous <paramref name="map"/> function.
    /// </summary>
    public static Task<Result<TOut>> Map<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return resultTask.ContinueWith(
            t => t.Result.Map(map),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Asynchronously projects the success value using an async <paramref name="map"/> function.
    /// </summary>
    public static Task<Result<TOut>> Map<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return resultTask.ContinueWith(async t =>
        {
            var result = t.Result;
            return result.IsSuccess
                ? Result<TOut>.Success(await map(result.Value).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
    }

    /// <summary>
    /// Asynchronously projects the error using a synchronous <paramref name="map"/> function.
    /// </summary>
    public static Task<Result<T>> MapError<T>(
        this Task<Result<T>> resultTask,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return resultTask.ContinueWith(
            t => t.Result.MapError(map),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Asynchronously chains a synchronous result-returning operation.
    /// </summary>
    public static Task<Result<TOut>> Bind<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return resultTask.ContinueWith(
            t => t.Result.Bind(bind),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Asynchronously chains an async result-returning operation.
    /// </summary>
    public static Task<Result<TOut>> Bind<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return resultTask.ContinueWith(t =>
        {
            var result = t.Result;
            return result.IsSuccess
                ? bind(result.Value)
                : Task.FromResult(Result<TOut>.Failure(result.Error));
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
    }

    /// <summary>
    /// Asynchronously folds the result using synchronous selector functions.
    /// </summary>
    public static Task<TOut> Match<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return resultTask.ContinueWith(
            t => t.Result.Match(onSuccess, onFailure),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Asynchronously folds the result using async selector functions.
    /// </summary>
    public static Task<TOut> Match<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<TOut>> onSuccess,
        Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return resultTask.ContinueWith(t =>
        {
            var result = t.Result;
            return result.IsSuccess
                ? onSuccess(result.Value)
                : onFailure(result.Error);
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
    }

    /// <summary>
    /// Asynchronously executes a synchronous side-effecting action on success, then forwards the result.
    /// </summary>
    public static Task<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask,
        Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return resultTask.ContinueWith(
            t => t.Result.Tap(action),
            TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Asynchronously executes an async side-effecting action on success, then forwards the result.
    /// </summary>
    public static Task<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask,
        Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return resultTask.ContinueWith(async t =>
        {
            var result = t.Result;
            if (result.IsSuccess)
                await action(result.Value).ConfigureAwait(false);

            return result;
        }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
    }
}

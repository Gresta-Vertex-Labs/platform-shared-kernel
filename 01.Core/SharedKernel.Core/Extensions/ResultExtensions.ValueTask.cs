using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

public static partial class ResultExtensions
{
    // -------------------------------------------------------------------------
    // ValueTask<Result<T>> and ValueTask<Result> sources
    // -------------------------------------------------------------------------

    /// <summary>Awaits the result, then projects the success value through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the success value.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Map<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<T>> resultTask, Func<T, TOut> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(map);
        }
    }

    /// <summary>Awaits the result, then projects the success value through the asynchronous <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The asynchronous projection applied to the success value.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Map<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask<TOut>> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess
                ? Result<TOut>.Success(await map(result.Value).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    /// <returns>A task whose result is a failure carrying the transformed error, or the source result when it succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> MapError<T>(
        this ValueTask<Result<T>> resultTask,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<Error, Error> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.MapError(map);
        }
    }

    /// <summary>Awaits the result, then chains a result-returning operation onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on the success value.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Bind<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<T>> resultTask, Func<T, Result<TOut>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous result-returning operation onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on the success value.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Bind<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask<Result<TOut>>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await bind(result.Value).ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then chains an operation returning a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on the success value.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Bind<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result> Core(ValueTask<Result<T>> resultTask, Func<T, Result> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous operation returning a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on the success value.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Bind<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask<Result>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await bind(result.Value).ConfigureAwait(false) : Result.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the error.</param>
    /// <returns>A task whose result is the output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<TOut> Match<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(ValueTask<Result<T>> resultTask, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(onSuccess, onFailure);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value with asynchronous selectors.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the error.</param>
    /// <returns>A task whose result is the output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<TOut> Match<T, TOut>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask<TOut>> onSuccess,
        Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await onSuccess(result.Value).ConfigureAwait(false) : await onFailure(result.Error).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the success value and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> Tap<T>(
        this ValueTask<Result<T>> resultTask,
        Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Action<T> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Tap(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the success value and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> Tap<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await action(result.Value).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on failure.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> TapError<T>(
        this ValueTask<Result<T>> resultTask,
        Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Action<Error> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.TapError(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> TapError<T>(
        this ValueTask<Result<T>> resultTask,
        Func<Error, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<Error, ValueTask> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The condition the success value must satisfy.</param>
    /// <param name="error">The error to return when the condition is not satisfied.</param>
    /// <returns>A task whose result is the source result when it failed or the condition holds; otherwise a failure carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> Ensure<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<T, bool> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Ensure(predicate, error);
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The asynchronous condition the success value must satisfy.</param>
    /// <param name="error">The error to return when the condition is not satisfied.</param>
    /// <returns>A task whose result is the source result when it failed or the condition holds; otherwise a failure carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<T>> Ensure<T>(
        this ValueTask<Result<T>> resultTask,
        Func<T, ValueTask<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result<T>> Core(ValueTask<Result<T>> resultTask, Func<T, ValueTask<bool>> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                return result;

            return await predicate(result.Value).ConfigureAwait(false) ? result : Result<T>.Failure(error);
        }
    }

    /// <summary>Awaits the result, then returns the success value, or throws the exception that matches the error.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <returns>A task whose result is the success value.</returns>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    /// <exception cref="SharedKernelException">Awaiting the returned task throws it when the result failed. The subclass matches the error's <see cref="Error.Type"/>.</exception>
    public static ValueTask<T> GetValueOrThrow<T>(
        this ValueTask<Result<T>> resultTask)
    {
        return Core(resultTask);

        static async ValueTask<T> Core(ValueTask<Result<T>> resultTask)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.GetValueOrThrow();
        }
    }

    /// <summary>Awaits the result, then produces a value from <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">Produces the success value.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Map<TOut>(
        this ValueTask<Result> resultTask,
        Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result> resultTask, Func<TOut> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(map);
        }
    }

    /// <summary>Awaits the result, then produces a value from the asynchronous <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">Produces the success value asynchronously.</param>
    /// <returns>A task whose result is a success carrying the produced value, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Map<TOut>(
        this ValueTask<Result> resultTask,
        Func<ValueTask<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result> resultTask, Func<ValueTask<TOut>> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? Result<TOut>.Success(await map().ConfigureAwait(false)) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    /// <returns>A task whose result is a failure carrying the transformed error, or the source result when it succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> MapError(
        this ValueTask<Result> resultTask,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<Error, Error> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.MapError(map);
        }
    }

    /// <summary>Awaits the result, then chains a result-returning operation after a success.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Bind(
        this ValueTask<Result> resultTask,
        Func<Result> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<Result> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous result-returning operation after a success.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Bind(
        this ValueTask<Result> resultTask,
        Func<ValueTask<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<ValueTask<Result>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await bind().ConfigureAwait(false) : result;
        }
    }

    /// <summary>Awaits the result, then chains an operation returning a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Bind<TOut>(
        this ValueTask<Result> resultTask,
        Func<Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result> resultTask, Func<Result<TOut>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous operation returning a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    /// <returns>A task whose result is the next step's result, or a failure carrying the original error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bind"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result<TOut>> Bind<TOut>(
        this ValueTask<Result> resultTask,
        Func<ValueTask<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async ValueTask<Result<TOut>> Core(ValueTask<Result> resultTask, Func<ValueTask<Result<TOut>>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await bind().ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Applied to the error.</param>
    /// <returns>A task whose result is the output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<TOut> Match<TOut>(
        this ValueTask<Result> resultTask,
        Func<TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(ValueTask<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(onSuccess, onFailure);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value with asynchronous selectors.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Applied to the error.</param>
    /// <returns>A task whose result is the output of whichever function ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onSuccess"/> or <paramref name="onFailure"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<TOut> Match<TOut>(
        this ValueTask<Result> resultTask,
        Func<ValueTask<TOut>> onSuccess,
        Func<Error, ValueTask<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async ValueTask<TOut> Core(ValueTask<Result> resultTask, Func<ValueTask<TOut>> onSuccess, Func<Error, ValueTask<TOut>> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await onSuccess().ConfigureAwait(false) : await onFailure(result.Error).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on success and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Tap(
        this ValueTask<Result> resultTask,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Action action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Tap(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on success and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Tap(
        this ValueTask<Result> resultTask,
        Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<ValueTask> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await action().ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on failure.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> TapError(
        this ValueTask<Result> resultTask,
        Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Action<Error> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.TapError(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    /// <returns>A task whose result is the source result, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> TapError(
        this ValueTask<Result> resultTask,
        Func<Error, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<Error, ValueTask> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await action(result.Error).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    /// <returns>A task whose result is the source result when it failed or the condition holds; otherwise a failure carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Ensure(
        this ValueTask<Result> resultTask,
        Func<bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<bool> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Ensure(predicate, error);
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The asynchronous condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    /// <returns>A task whose result is the source result when it failed or the condition holds; otherwise a failure carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="error"/> is <see langword="null"/>. Thrown synchronously, before anything runs.</exception>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    public static ValueTask<Result> Ensure(
        this ValueTask<Result> resultTask,
        Func<ValueTask<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async ValueTask<Result> Core(ValueTask<Result> resultTask, Func<ValueTask<bool>> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                return result;

            return await predicate().ConfigureAwait(false) ? result : Result.Failure(error);
        }
    }

    /// <summary>Awaits the result, then throws the exception that matches the error when the result is a failure.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <returns>A task that completes when the result succeeded.</returns>
    /// <exception cref="OperationCanceledException">Awaiting the returned task throws it when <paramref name="resultTask"/> was cancelled. A faulted source rethrows its own exception.</exception>
    /// <exception cref="SharedKernelException">Awaiting the returned task throws it when the result failed. The subclass matches the error's <see cref="Error.Type"/>.</exception>
    public static ValueTask ThrowIfFailure(
        this ValueTask<Result> resultTask)
    {
        return Core(resultTask);

        static async ValueTask Core(ValueTask<Result> resultTask)
        {
            var result = await resultTask.ConfigureAwait(false);
            result.ThrowIfFailure();
        }
    }
}

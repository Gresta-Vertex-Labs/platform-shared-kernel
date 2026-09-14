using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

public static partial class ResultExtensions
{
    // -------------------------------------------------------------------------
    // Task<Result<T>> and Task<Result> sources
    // -------------------------------------------------------------------------

    /// <summary>Awaits the result, then projects the success value through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the success value.</param>
    public static Task<Result<TOut>> Map<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result<TOut>> Core(Task<Result<T>> resultTask, Func<T, TOut> map)
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
    public static Task<Result<TOut>> Map<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result<TOut>> Core(Task<Result<T>> resultTask, Func<T, Task<TOut>> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess
                ? Result<TOut>.Success(await Returned(map(result.Value)).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    public static Task<Result<T>> MapError<T>(
        this Task<Result<T>> resultTask,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Func<Error, Error> map)
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
    public static Task<Result<TOut>> Bind<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result<TOut>> Core(Task<Result<T>> resultTask, Func<T, Result<TOut>> bind)
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
    public static Task<Result<TOut>> Bind<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result<TOut>> Core(Task<Result<T>> resultTask, Func<T, Task<Result<TOut>>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(bind(result.Value)).ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then chains an operation returning a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on the success value.</param>
    public static Task<Result> Bind<T>(
        this Task<Result<T>> resultTask,
        Func<T, Result> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result> Core(Task<Result<T>> resultTask, Func<T, Result> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous operation returning a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on the success value.</param>
    public static Task<Result> Bind<T>(
        this Task<Result<T>> resultTask,
        Func<T, Task<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result> Core(Task<Result<T>> resultTask, Func<T, Task<Result>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(bind(result.Value)).ConfigureAwait(false) : Result.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static Task<TOut> Match<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async Task<TOut> Core(Task<Result<T>> resultTask, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
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
    public static Task<TOut> Match<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, Task<TOut>> onSuccess,
        Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async Task<TOut> Core(Task<Result<T>> resultTask, Func<T, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(onSuccess(result.Value)).ConfigureAwait(false) : await Returned(onFailure(result.Error)).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the success value and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    public static Task<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask,
        Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Action<T> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Tap(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the success value and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    public static Task<Result<T>> Tap<T>(
        this Task<Result<T>> resultTask,
        Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Func<T, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await Returned(action(result.Value)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on failure.</param>
    public static Task<Result<T>> TapError<T>(
        this Task<Result<T>> resultTask,
        Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Action<Error> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.TapError(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    public static Task<Result<T>> TapError<T>(
        this Task<Result<T>> resultTask,
        Func<Error, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Func<Error, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await Returned(action(result.Error)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The condition the success value must satisfy.</param>
    /// <param name="error">The error to return when the condition is not satisfied.</param>
    public static Task<Result<T>> Ensure<T>(
        this Task<Result<T>> resultTask,
        Func<T, bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Func<T, bool> predicate, Error error)
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
    public static Task<Result<T>> Ensure<T>(
        this Task<Result<T>> resultTask,
        Func<T, Task<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async Task<Result<T>> Core(Task<Result<T>> resultTask, Func<T, Task<bool>> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                return result;

            return await Returned(predicate(result.Value)).ConfigureAwait(false) ? result : Result<T>.Failure(error);
        }
    }

    /// <summary>Awaits the result, then returns the success value, or throws the exception that matches the error.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    public static Task<T> GetValueOrThrow<T>(
        this Task<Result<T>> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return Core(resultTask);

        static async Task<T> Core(Task<Result<T>> resultTask)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.GetValueOrThrow();
        }
    }

    /// <summary>Awaits the result, then produces a value from <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">Produces the success value.</param>
    public static Task<Result<TOut>> Map<TOut>(
        this Task<Result> resultTask,
        Func<TOut> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result<TOut>> Core(Task<Result> resultTask, Func<TOut> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(map);
        }
    }

    /// <summary>Awaits the result, then produces a value from the asynchronous <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">Produces the success value asynchronously.</param>
    public static Task<Result<TOut>> Map<TOut>(
        this Task<Result> resultTask,
        Func<Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result<TOut>> Core(Task<Result> resultTask, Func<Task<TOut>> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? Result<TOut>.Success(await Returned(map()).ConfigureAwait(false)) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then projects the error of a failed result through <paramref name="map"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="map">The projection applied to the error.</param>
    public static Task<Result> MapError(
        this Task<Result> resultTask,
        Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(map);
        return Core(resultTask, map);

        static async Task<Result> Core(Task<Result> resultTask, Func<Error, Error> map)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.MapError(map);
        }
    }

    /// <summary>Awaits the result, then chains a result-returning operation after a success.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    public static Task<Result> Bind(
        this Task<Result> resultTask,
        Func<Result> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result> Core(Task<Result> resultTask, Func<Result> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous result-returning operation after a success.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    public static Task<Result> Bind(
        this Task<Result> resultTask,
        Func<Task<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result> Core(Task<Result> resultTask, Func<Task<Result>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(bind()).ConfigureAwait(false) : result;
        }
    }

    /// <summary>Awaits the result, then chains an operation returning a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The operation to run on success.</param>
    public static Task<Result<TOut>> Bind<TOut>(
        this Task<Result> resultTask,
        Func<Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result<TOut>> Core(Task<Result> resultTask, Func<Result<TOut>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(bind);
        }
    }

    /// <summary>Awaits the result, then chains an asynchronous operation returning a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    public static Task<Result<TOut>> Bind<TOut>(
        this Task<Result> resultTask,
        Func<Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(resultTask, bind);

        static async Task<Result<TOut>> Core(Task<Result> resultTask, Func<Task<Result<TOut>>> bind)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(bind()).ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Awaits the result, then folds the result into a single value.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static Task<TOut> Match<TOut>(
        this Task<Result> resultTask,
        Func<TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async Task<TOut> Core(Task<Result> resultTask, Func<TOut> onSuccess, Func<Error, TOut> onFailure)
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
    public static Task<TOut> Match<TOut>(
        this Task<Result> resultTask,
        Func<Task<TOut>> onSuccess,
        Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(resultTask, onSuccess, onFailure);

        static async Task<TOut> Core(Task<Result> resultTask, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.IsSuccess ? await Returned(onSuccess()).ConfigureAwait(false) : await Returned(onFailure(result.Error)).ConfigureAwait(false);
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on success and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on success.</param>
    public static Task<Result> Tap(
        this Task<Result> resultTask,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result> Core(Task<Result> resultTask, Action action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Tap(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on success and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    public static Task<Result> Tap(
        this Task<Result> resultTask,
        Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result> Core(Task<Result> resultTask, Func<Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsSuccess)
                await Returned(action()).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then runs <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The side effect to run on failure.</param>
    public static Task<Result> TapError(
        this Task<Result> resultTask,
        Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result> Core(Task<Result> resultTask, Action<Error> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.TapError(action);
        }
    }

    /// <summary>Awaits the result, then runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    public static Task<Result> TapError(
        this Task<Result> resultTask,
        Func<Error, Task> action)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(action);
        return Core(resultTask, action);

        static async Task<Result> Core(Task<Result> resultTask, Func<Error, Task> action)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                await Returned(action(result.Error)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    public static Task<Result> Ensure(
        this Task<Result> resultTask,
        Func<bool> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async Task<Result> Core(Task<Result> resultTask, Func<bool> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Ensure(predicate, error);
        }
    }

    /// <summary>Awaits the result, then turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    /// <param name="predicate">The asynchronous condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    public static Task<Result> Ensure(
        this Task<Result> resultTask,
        Func<Task<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(resultTask, predicate, error);

        static async Task<Result> Core(Task<Result> resultTask, Func<Task<bool>> predicate, Error error)
        {
            var result = await resultTask.ConfigureAwait(false);
            if (result.IsFailure)
                return result;

            return await Returned(predicate()).ConfigureAwait(false) ? result : Result.Failure(error);
        }
    }

    /// <summary>Awaits the result, then throws the exception that matches the error when the result is a failure.</summary>
    /// <param name="resultTask">The asynchronous source result.</param>
    public static Task ThrowIfFailure(
        this Task<Result> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return Core(resultTask);

        static async Task Core(Task<Result> resultTask)
        {
            var result = await resultTask.ConfigureAwait(false);
            result.ThrowIfFailure();
        }
    }

    // -------------------------------------------------------------------------
    // Plain result sources with Task-returning continuations
    // -------------------------------------------------------------------------

    /// <summary>Projects the success value through the asynchronous <paramref name="map"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">The asynchronous projection applied to the success value.</param>
    public static Task<Result<TOut>> Map<T, TOut>(
        this Result<T> result,
        Func<T, Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return Core(result, map);

        static async Task<Result<TOut>> Core(Result<T> result, Func<T, Task<TOut>> map)
        {
            return result.IsSuccess
                ? Result<TOut>.Success(await Returned(map(result.Value)).ConfigureAwait(false))
                : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Chains an asynchronous result-returning operation onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The asynchronous operation to run on the success value.</param>
    public static Task<Result<TOut>> Bind<T, TOut>(
        this Result<T> result,
        Func<T, Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(result, bind);

        static async Task<Result<TOut>> Core(Result<T> result, Func<T, Task<Result<TOut>>> bind)
        {
            return result.IsSuccess ? await Returned(bind(result.Value)).ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Chains an asynchronous operation returning a non-generic <see cref="Result"/> onto the success value.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The asynchronous operation to run on the success value.</param>
    public static Task<Result> Bind<T>(
        this Result<T> result,
        Func<T, Task<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return Core(result, bind);

        static async Task<Result> Core(Result<T> result, Func<T, Task<Result>> bind)
        {
            return result.IsSuccess ? await Returned(bind(result.Value)).ConfigureAwait(false) : Result.Failure(result.Error);
        }
    }

    /// <summary>Folds the result into a single value with asynchronous selectors.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Applied to the success value.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static Task<TOut> Match<T, TOut>(
        this Result<T> result,
        Func<T, Task<TOut>> onSuccess,
        Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(result, onSuccess, onFailure);

        static async Task<TOut> Core(Result<T> result, Func<T, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
        {
            return result.IsSuccess ? await Returned(onSuccess(result.Value)).ConfigureAwait(false) : await Returned(onFailure(result.Error)).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the asynchronous <paramref name="action"/> on the success value and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    public static Task<Result<T>> Tap<T>(
        this Result<T> result,
        Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        return Core(result, action);

        static async Task<Result<T>> Core(Result<T> result, Func<T, Task> action)
        {
            if (result.IsSuccess)
                await Returned(action(result.Value)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    public static Task<Result<T>> TapError<T>(
        this Result<T> result,
        Func<Error, Task> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        return Core(result, action);

        static async Task<Result<T>> Core(Result<T> result, Func<Error, Task> action)
        {
            if (result.IsFailure)
                await Returned(action(result.Error)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <typeparam name="T">The success type of the source result.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The asynchronous condition the success value must satisfy.</param>
    /// <param name="error">The error to return when the condition is not satisfied.</param>
    public static Task<Result<T>> Ensure<T>(
        this Result<T> result,
        Func<T, Task<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error);

        static async Task<Result<T>> Core(Result<T> result, Func<T, Task<bool>> predicate, Error error)
        {
            if (result.IsFailure)
                return result;

            return await Returned(predicate(result.Value)).ConfigureAwait(false) ? result : Result<T>.Failure(error);
        }
    }

    /// <summary>Produces a value from the asynchronous <paramref name="map"/> when the result is a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="map">Produces the success value asynchronously.</param>
    public static Task<Result<TOut>> Map<TOut>(
        this Result result,
        Func<Task<TOut>> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Core(result, map);

        static async Task<Result<TOut>> Core(Result result, Func<Task<TOut>> map)
        {
            return result.IsSuccess ? Result<TOut>.Success(await Returned(map()).ConfigureAwait(false)) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Chains an asynchronous result-returning operation after a success.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    public static Task<Result> Bind(
        this Result result,
        Func<Task<Result>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(result, bind);

        static async Task<Result> Core(Result result, Func<Task<Result>> bind)
        {
            return result.IsSuccess ? await Returned(bind()).ConfigureAwait(false) : result;
        }
    }

    /// <summary>Chains an asynchronous operation returning a <see cref="Result{T}"/> after a success.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="bind">The asynchronous operation to run on success.</param>
    public static Task<Result<TOut>> Bind<TOut>(
        this Result result,
        Func<Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return Core(result, bind);

        static async Task<Result<TOut>> Core(Result result, Func<Task<Result<TOut>>> bind)
        {
            return result.IsSuccess ? await Returned(bind()).ConfigureAwait(false) : Result<TOut>.Failure(result.Error);
        }
    }

    /// <summary>Folds the result into a single value with asynchronous selectors.</summary>
    /// <typeparam name="TOut">The output type.</typeparam>
    /// <param name="result">The source result.</param>
    /// <param name="onSuccess">Produces the value on success.</param>
    /// <param name="onFailure">Applied to the error.</param>
    public static Task<TOut> Match<TOut>(
        this Result result,
        Func<Task<TOut>> onSuccess,
        Func<Error, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return Core(result, onSuccess, onFailure);

        static async Task<TOut> Core(Result result, Func<Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
        {
            return result.IsSuccess ? await Returned(onSuccess()).ConfigureAwait(false) : await Returned(onFailure(result.Error)).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the asynchronous <paramref name="action"/> on success and returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The asynchronous side effect to run on success.</param>
    public static Task<Result> Tap(
        this Result result,
        Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(result, action);

        static async Task<Result> Core(Result result, Func<Task> action)
        {
            if (result.IsSuccess)
                await Returned(action()).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Runs the asynchronous <paramref name="action"/> on the error of a failed result and returns the result unchanged.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="action">The asynchronous side effect to run on failure.</param>
    public static Task<Result> TapError(
        this Result result,
        Func<Error, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Core(result, action);

        static async Task<Result> Core(Result result, Func<Error, Task> action)
        {
            if (result.IsFailure)
                await Returned(action(result.Error)).ConfigureAwait(false);

            return result;
        }
    }

    /// <summary>Turns a success into a failure carrying <paramref name="error"/> when the asynchronous <paramref name="predicate"/> returns <see langword="false"/>.</summary>
    /// <param name="result">The source result.</param>
    /// <param name="predicate">The asynchronous condition that must hold.</param>
    /// <param name="error">The error to return when the condition does not hold.</param>
    public static Task<Result> Ensure(
        this Result result,
        Func<Task<bool>> predicate,
        Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return Core(result, predicate, error);

        static async Task<Result> Core(Result result, Func<Task<bool>> predicate, Error error)
        {
            if (result.IsFailure)
                return result;

            return await Returned(predicate()).ConfigureAwait(false) ? result : Result.Failure(error);
        }
    }

    private static Task Returned(Task? task)
        => task ?? throw new InvalidOperationException("The delegate returned a null Task.");

    private static Task<TResult> Returned<TResult>(Task<TResult>? task)
        => task ?? throw new InvalidOperationException("The delegate returned a null Task.");
}

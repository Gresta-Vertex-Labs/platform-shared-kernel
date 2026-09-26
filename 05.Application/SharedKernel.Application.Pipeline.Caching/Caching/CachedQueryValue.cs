using SharedKernel.Application.Caching;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>
/// Caches the value of a successful <c>Result&lt;TValue&gt;</c> and rebuilds the result on the way out.
/// </summary>
/// <typeparam name="TValue">The query's value type.</typeparam>
internal static class CachedQueryValue<TValue>
{
    internal static ValueTask<TResponse> ExecuteAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        bool refresh,
        Func<TValue, bool> shouldCache,
        CacheExecution execution,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Holds when the query is only an ICacheableQuery<TValue>; a type that also declares a
        // second, different IRequest<> response would otherwise fail with an unclear cast.
        if (typeof(TResponse) != typeof(Result<TValue>))
        {
            throw new InvalidOperationException(
                $"A query implementing ICacheableQuery<{typeof(TValue).Name}> must return Result<{typeof(TValue).Name}>, "
                    + $"but its pipeline returns {typeof(TResponse).Name}.");
        }

        return refresh
            ? RefreshAsync(cache, key, policy, shouldCache, execution, next, cancellationToken)
            : GetOrSetAsync(cache, key, policy, shouldCache, execution, next, cancellationToken);
    }

    private static async ValueTask<TResponse> GetOrSetAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        Func<TValue, bool> shouldCache,
        CacheExecution execution,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        // A failure is never written, so it cannot come back from the cache: it is returned from the
        // factory run by this call and carried out in this local. This relies on a contract the
        // abstraction states and CachingBehaviorConcurrencyTests pins against a real cache — a
        // factory that calls SkipCaching does NOT hand its value to concurrent waiters; each waiter
        // runs the factory itself. An ICacheService that instead broadcast the skipped value would
        // hand every waiter a default value here, so the pinning test is load-bearing, not decoration.
        Result<TValue>? failure = null;
        var factoryRan = false;
        var rejectedByPredicate = false;

        var value = await cache.GetOrSetAsync<TValue>(
            key,
            async (context, _) =>
            {
                factoryRan = true;
                var result = Unwrap<TResponse>(await next().ConfigureAwait(false));

                if (result.IsFailure)
                {
                    failure = result;
                    context.SkipCaching();
                    return default!;
                }

                if (!shouldCache(result.Value))
                {
                    rejectedByPredicate = true;
                    context.SkipCaching();
                }

                return result.Value;
            },
            policy,
            cancellationToken).ConfigureAwait(false);

        execution.Outcome = failure is not null
            ? CacheOutcome.NotCachedFailure
            : rejectedByPredicate
                ? CacheOutcome.NotCachedByPredicate
                : factoryRan
                    ? CacheOutcome.Miss
                    : CacheOutcome.Hit;

        return Wrap<TResponse>(failure ?? Result<TValue>.Success(value));
    }

    private static async ValueTask<TResponse> RefreshAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        Func<TValue, bool> shouldCache,
        CacheExecution execution,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        var result = Unwrap<TResponse>(await next().ConfigureAwait(false));

        if (result.IsFailure)
        {
            // The existing entry is deliberately left alone: a failed refresh is not evidence that
            // the cached value is wrong, and dropping it would turn a transient fault into a
            // cache-wide miss storm.
            execution.Outcome = CacheOutcome.NotCachedFailure;
            return Wrap<TResponse>(result);
        }

        if (!shouldCache(result.Value))
        {
            execution.Outcome = CacheOutcome.NotCachedByPredicate;
            return Wrap<TResponse>(result);
        }

        await cache.SetAsync(key, result.Value, policy, cancellationToken).ConfigureAwait(false);
        execution.Outcome = CacheOutcome.Refreshed;
        return Wrap<TResponse>(result);
    }

    private static Result<TValue> Unwrap<TResponse>(TResponse response) => (Result<TValue>)(object)response!;

    private static TResponse Wrap<TResponse>(Result<TValue> result) => (TResponse)(object)result;
}

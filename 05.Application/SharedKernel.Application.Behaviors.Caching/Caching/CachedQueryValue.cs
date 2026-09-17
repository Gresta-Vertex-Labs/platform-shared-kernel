using MediatR;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Caches the value of a successful <c>Result&lt;TValue&gt;</c> and rebuilds the result on the way out.
/// </summary>
/// <typeparam name="TValue">The query's value type.</typeparam>
internal static class CachedQueryValue<TValue>
{
    internal static async ValueTask<TResponse> GetOrSetAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        RequestHandlerDelegate<TResponse> next,
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

        // A failure is never cached, so it cannot come back from the cache: it is returned from the
        // factory run by this call. A waiting identical query finds nothing cached and runs the handler itself.
        Result<TValue>? failure = null;

        var value = await cache.GetOrSetAsync<TValue>(
            key,
            async (context, _) =>
            {
                var result = (Result<TValue>)(object)(await next().ConfigureAwait(false))!;
                if (result.IsSuccess)
                    return result.Value;

                failure = result;
                context.SkipCaching();
                return default!;
            },
            policy,
            cancellationToken).ConfigureAwait(false);

        return (TResponse)(object)(failure ?? Result<TValue>.Success(value));
    }
}

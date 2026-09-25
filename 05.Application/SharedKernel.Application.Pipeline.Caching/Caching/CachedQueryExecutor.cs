using System.Collections.Concurrent;
using SharedKernel.Application.Caching;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>
/// Runs a cacheable query through the cache with its value type <c>TValue</c> known, although the
/// caching behavior only knows the query type and its <c>Result&lt;TValue&gt;</c> response type.
/// </summary>
/// <typeparam name="TResponse">The query's response type.</typeparam>
/// <remarks>
/// The value type is read once per query type from its <see cref="ICacheableQuery{TValue}"/>
/// declaration, and the closed executor is cached; every later execution of that query type is an
/// ordinary virtual call.
/// </remarks>
internal abstract class CachedQueryExecutor<TResponse>
{
    private static readonly ConcurrentDictionary<Type, CachedQueryExecutor<TResponse>> Executors = new();

    public static CachedQueryExecutor<TResponse> For(Type queryType) =>
        Executors.GetOrAdd(queryType, static type => Create(type));

    public abstract ValueTask<TResponse> ExecuteAsync(
        ICacheableQuery query,
        ICacheService cache,
        string key,
        CachePolicy policy,
        CacheExecution execution,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken);

    private static CachedQueryExecutor<TResponse> Create(Type queryType)
    {
        var valueTypes = queryType.GetInterfaces()
            .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICacheableQuery<>))
            .Select(static i => i.GetGenericArguments()[0])
            .ToArray();

        if (valueTypes.Length != 1)
        {
            throw new InvalidOperationException(
                $"{queryType.Name} must implement ICacheableQuery<TValue> exactly once to be cached, "
                    + $"but implements it {valueTypes.Length} time(s).");
        }

        return (CachedQueryExecutor<TResponse>)Activator.CreateInstance(
            typeof(CachedQueryExecutor<,>).MakeGenericType(typeof(TResponse), valueTypes[0]))!;
    }
}

/// <summary>The closed executor for a query whose value type is <typeparamref name="TValue"/>.</summary>
/// <typeparam name="TResponse">The query's response type.</typeparam>
/// <typeparam name="TValue">The query's value type.</typeparam>
internal sealed class CachedQueryExecutor<TResponse, TValue> : CachedQueryExecutor<TResponse>
{
    public override ValueTask<TResponse> ExecuteAsync(
        ICacheableQuery query,
        ICacheService cache,
        string key,
        CachePolicy policy,
        CacheExecution execution,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        var typed = (ICacheableQuery<TValue>)query;
        return CachedQueryValue<TValue>.ExecuteAsync(
            cache, key, policy, typed.RefreshCache, typed.ShouldCache, execution, next, cancellationToken);
    }
}

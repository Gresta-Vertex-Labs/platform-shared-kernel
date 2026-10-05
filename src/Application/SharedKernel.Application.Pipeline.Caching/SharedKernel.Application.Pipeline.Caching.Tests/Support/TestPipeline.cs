using SharedKernel.Application.Caching;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Pipeline.Caching;
using SharedKernel.Application.Pipeline.Caching.Shared;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Execution.Context;

namespace SharedKernel.Application.Pipeline.Caching.Tests.Support;

/// <summary>
/// Constructs the two behaviors with their telemetry dependencies, so a test names only what it is
/// actually varying.
/// </summary>
internal static class TestPipeline
{
    private static readonly ServiceProvider MetricsProvider =
        new ServiceCollection().AddMetrics().BuildServiceProvider();

    internal static CachingBehaviorsMetrics Metrics() =>
        new(MetricsProvider.GetRequiredService<IMeterFactory>());

    internal static CachingBehavior<TRequest, TResponse> Caching<TRequest, TResponse>(
        FakeCacheService cache,
        IRequestContext? requestContext = null)
        where TRequest : IQueryBase, ICacheableQuery, IRequest<TResponse>
        => new(
            cache,
            new FakeCacheKeyProvider(),
            Metrics(),
            NullLogger<CachingBehavior<TRequest, TResponse>>.Instance,
            requestContext);

    internal static CacheInvalidationBehavior<TRequest, TResponse> Invalidation<TRequest, TResponse>(
        FakeCacheService cache,
        ICommandScope commandScope,
        IRequestContext? requestContext = null)
        where TRequest : ICommandBase, IInvalidatesCache, IRequest<TResponse>
        => new(
            cache,
            new FakeCacheKeyProvider(),
            commandScope,
            Metrics(),
            NullLogger<CacheInvalidationBehavior<TRequest, TResponse>>.Instance,
            requestContext);
}

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Caching.FusionCache.Health;

/// <summary>
/// The cache's <see cref="IReadinessProbe"/>, named <see cref="CacheReadinessProbeNames.Cache"/>: reads a
/// synthetic key through the registered <see cref="ICacheService"/> with a short timeout.
/// </summary>
/// <remarks>
/// A failed read is reported as <see cref="ReadinessStatus.Degraded"/>, never
/// <see cref="ReadinessStatus.Unhealthy"/>: FusionCache's in-memory layer and fail-safe may still serve data
/// while the distributed layer is unavailable, and taking the instance out of rotation for a transient cache
/// blip would do more harm than the blip. The Redis connection itself has its own probe in
/// <c>SharedKernel.Caching.Redis.Core</c>.
/// </remarks>
internal sealed class CacheReadinessProbe(IServiceProvider services) : IReadinessProbe
{
    private const string ProbeKey = "__sharedkernel_health_probe__";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public string Name => CacheReadinessProbeNames.Cache;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeoutCts = new CancellationTokenSource(ProbeTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Resolved here, not injected: the cache may connect to its distributed layer when built, and a host
            // constructs every probe just to read its name.
            var cacheService = services.GetRequiredService<ICacheService>();
            await cacheService.TryGetAsync<string>(ProbeKey, linkedCts.Token).ConfigureAwait(false);

            return ReadinessReport.Healthy("Cache probe succeeded.", latency: Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReadinessReport.Degraded(
                $"Cache probe failed ({ex.GetType().Name}); fail-safe may still serve stale data.",
                latency: Stopwatch.GetElapsedTime(started));
        }
    }
}

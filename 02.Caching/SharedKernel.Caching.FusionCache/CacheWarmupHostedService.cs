using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Caching.FusionCache;

/// <summary>
/// Runs every registered <see cref="ICacheWarmupStrategy"/> once, in ascending
/// <see cref="ICacheWarmupStrategy.Order"/>. A failing strategy is logged and the next one runs.
/// </summary>
/// <remarks>
/// With <see cref="CachingOptions.WaitForWarmup"/>, warmup runs in <see cref="StartingAsync"/>, which the
/// host completes for every lifecycle service before it starts any hosted service, including the web
/// server, so no traffic and no readiness arrive until the cache is warm. Otherwise warmup runs in the
/// background from <see cref="StartAsync"/> and is cancelled on shutdown.
/// </remarks>
internal sealed partial class CacheWarmupHostedService : IHostedLifecycleService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CachingOptions> _options;
    private readonly ILogger<CacheWarmupHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task _backgroundWarmup = Task.CompletedTask;

    public CacheWarmupHostedService(
        IServiceProvider serviceProvider,
        IOptions<CachingOptions> options,
        ILogger<CacheWarmupHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.WaitForWarmup)
            return;

        Log.WaitingForWarmupCompletion(_logger);
        await RunWarmupAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.WaitForWarmup)
            _backgroundWarmup = Task.Run(() => RunBackgroundWarmupAsync(_stopping.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _backgroundWarmup.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _stopping.Dispose();

    private async Task RunBackgroundWarmupAsync(CancellationToken ct)
    {
        try
        {
            await RunWarmupAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown before warmup finished.
        }
    }

    private async Task RunWarmupAsync(CancellationToken ct)
    {
        var strategies = _serviceProvider
            .GetServices<ICacheWarmupStrategy>()
            .OrderBy(s => s.Order)
            .ToList();

        if (strategies.Count == 0)
        {
            Log.NoWarmupStrategiesRegistered(_logger);
            return;
        }

        var cache = _serviceProvider.GetRequiredService<ICacheService>();

        Log.WarmupStarting(_logger, strategies.Count);

        foreach (var strategy in strategies)
        {
            if (ct.IsCancellationRequested)
            {
                Log.WarmupCancelled(_logger, strategy.Name);
                ct.ThrowIfCancellationRequested();
            }

            var sw = Stopwatch.StartNew();
            Log.StrategyExecuting(_logger, strategy.Name, strategy.Order);

            try
            {
                await strategy.WarmupAsync(cache, ct).ConfigureAwait(false);
                Log.StrategyCompleted(_logger, strategy.Name, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.StrategyFailed(_logger, strategy.Name, sw.ElapsedMilliseconds, ex);
            }
        }

        Log.WarmupCompleted(_logger);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 0, Level = LogLevel.Information,
            Message = "Cache warmup: WaitForWarmup is on; host startup waits until warmup completes")]
        internal static partial void WaitingForWarmupCompletion(ILogger logger);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 1, Level = LogLevel.Information,
            Message = "Cache warmup: no strategies registered")]
        internal static partial void NoWarmupStrategiesRegistered(ILogger logger);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 2, Level = LogLevel.Information,
            Message = "Cache warmup starting with {StrategyCount} strategies")]
        internal static partial void WarmupStarting(ILogger logger, int strategyCount);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 3, Level = LogLevel.Warning,
            Message = "Cache warmup cancelled before strategy {StrategyName}")]
        internal static partial void WarmupCancelled(ILogger logger, string strategyName);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 4, Level = LogLevel.Information,
            Message = "Cache warmup running strategy {StrategyName} (order {Order})")]
        internal static partial void StrategyExecuting(ILogger logger, string strategyName, int order);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 5, Level = LogLevel.Information,
            Message = "Cache warmup strategy {StrategyName} completed in {ElapsedMs} ms")]
        internal static partial void StrategyCompleted(ILogger logger, string strategyName, long elapsedMs);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 6, Level = LogLevel.Error,
            Message = "Cache warmup strategy {StrategyName} failed after {ElapsedMs} ms; continuing with the next strategy")]
        internal static partial void StrategyFailed(ILogger logger, string strategyName, long elapsedMs, Exception exception);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 7, Level = LogLevel.Information,
            Message = "Cache warmup completed")]
        internal static partial void WarmupCompleted(ILogger logger);
    }
}

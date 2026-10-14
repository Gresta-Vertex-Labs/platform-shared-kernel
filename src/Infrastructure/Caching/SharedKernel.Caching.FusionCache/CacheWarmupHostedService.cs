using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Diagnostics;

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
internal sealed class CacheWarmupHostedService : IHostedLifecycleService, IDisposable
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

        FusionCacheLog.WaitingForWarmup(_logger);
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
            FusionCacheLog.NoWarmupStrategies(_logger);
            return;
        }

        var cache = _serviceProvider.GetRequiredService<ICacheService>();

        FusionCacheLog.WarmupStarting(_logger, strategies.Count);

        foreach (var strategy in strategies)
        {
            if (ct.IsCancellationRequested)
            {
                FusionCacheLog.WarmupCancelled(_logger, strategy.Name);
                ct.ThrowIfCancellationRequested();
            }

            var sw = Stopwatch.StartNew();
            FusionCacheLog.StrategyExecuting(_logger, strategy.Name, strategy.Order);

            try
            {
                await strategy.WarmupAsync(cache, ct).ConfigureAwait(false);
                FusionCacheLog.StrategyCompleted(_logger, strategy.Name, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                FusionCacheLog.StrategyFailed(_logger, strategy.Name, sw.ElapsedMilliseconds, ex);
            }
        }

        FusionCacheLog.WarmupCompleted(_logger);
    }
}

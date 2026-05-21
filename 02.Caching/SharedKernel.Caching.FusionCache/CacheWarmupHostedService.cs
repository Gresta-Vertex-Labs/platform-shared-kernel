using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using System.Diagnostics;

namespace SharedKernel.Caching.FusionCache;

/// <summary>
/// A hosted service that executes all registered <see cref="ICacheWarmupStrategy"/> instances
/// at service startup, ordered by <see cref="ICacheWarmupStrategy.Order"/> ascending.
/// </summary>
/// <remarks>
/// <para>
/// Each strategy is executed sequentially. If a strategy throws, the exception is caught,
/// logged at <see cref="LogLevel.Error"/>, and execution continues with the next strategy.
/// A failed strategy never crashes the host or aborts remaining strategies.
/// </para>
/// <para>
/// When <c>CachingOptions.WaitForWarmup</c> is <see langword="true"/>, this service implements
/// <see cref="IHostedLifecycleService"/> and delays the host's <c>StartedAsync</c> phase until
/// all warmup strategies have completed, ensuring Kubernetes readiness probes do not pass
/// until the cache is primed.
/// </para>
/// <para>
/// Register via <c>ICachingBuilder.AddCacheWarmup&lt;TStrategy&gt;()</c>.
/// </para>
/// </remarks>
public sealed class CacheWarmupHostedService : BackgroundService, IHostedLifecycleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CachingOptions> _options;
    private readonly ILogger<CacheWarmupHostedService> _logger;

    // TaskCompletionSource initialized at construction time so StartedAsync can always await
    // the correct Task regardless of the execution order of StartAsync / ExecuteAsync.
    private readonly TaskCompletionSource _warmupCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initialises a new instance of <see cref="CacheWarmupHostedService"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// The DI service provider used to resolve <see cref="ICacheWarmupStrategy"/> and
    /// <see cref="ICacheService"/> instances.
    /// </param>
    /// <param name="options">Caching configuration options.</param>
    /// <param name="logger">Logger for warmup lifecycle events.</param>
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

    // -------------------------------------------------------------------------
    // IHostedLifecycleService — all except StartedAsync are no-ops
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// When <c>CachingOptions.WaitForWarmup</c> is <see langword="true"/>, awaits warmup
    /// completion before the host signals readiness. This ensures Kubernetes readiness probes
    /// do not pass until all registered warmup strategies have finished.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token that is cancelled when the host is stopping.
    /// </param>
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.WaitForWarmup)
        {
            _logger.LogInformation(
                "CacheWarmupHostedService: WaitForWarmup=true — awaiting warmup completion before host signals readiness.");

            await _warmupCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // BackgroundService
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves all registered <see cref="ICacheWarmupStrategy"/> instances, orders them by
    /// <see cref="ICacheWarmupStrategy.Order"/> ascending, and executes each sequentially.
    /// Per-strategy exceptions are logged and do not abort execution of subsequent strategies.
    /// </summary>
    /// <param name="stoppingToken">A token that fires when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunWarmupAsync(stoppingToken).ConfigureAwait(false);
            _warmupCompletion.TrySetResult();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _warmupCompletion.TrySetCanceled(stoppingToken);
        }
        catch (Exception ex)
        {
            // Warmup-level unhandled exception (beyond per-strategy isolation) — complete the
            // TCS so StartedAsync doesn't hang, then re-throw to let the host handle it.
            _warmupCompletion.TrySetException(ex);
            throw;
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
            _logger.LogInformation("CacheWarmupHostedService: No warmup strategies registered.");
            return;
        }

        var cache = _serviceProvider.GetRequiredService<ICacheService>();

        _logger.LogInformation(
            "CacheWarmupHostedService: Starting cache warmup — {StrategyCount} strategy(ies) registered.",
            strategies.Count);

        foreach (var strategy in strategies)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "CacheWarmupHostedService: Warmup cancelled before executing strategy '{StrategyName}'.",
                    strategy.Name);
                break;
            }

            var sw = Stopwatch.StartNew();
            _logger.LogInformation(
                "CacheWarmupHostedService: Executing strategy '{StrategyName}' (Order={Order}).",
                strategy.Name,
                strategy.Order);

            try
            {
                await strategy.WarmupAsync(cache, ct).ConfigureAwait(false);
                sw.Stop();

                _logger.LogInformation(
                    "CacheWarmupHostedService: Strategy '{StrategyName}' completed in {ElapsedMs}ms.",
                    strategy.Name,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(
                    ex,
                    "CacheWarmupHostedService: Strategy '{StrategyName}' failed after {ElapsedMs}ms — continuing with next strategy.",
                    strategy.Name,
                    sw.ElapsedMilliseconds);
            }
        }

        _logger.LogInformation("CacheWarmupHostedService: Cache warmup complete.");
    }
}

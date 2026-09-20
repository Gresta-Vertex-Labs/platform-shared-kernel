using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// Warms <see cref="EncryptionKeyRingCache"/> before the host accepts traffic, then refreshes it periodically so a
/// key rotated at the key service becomes decryptable without a restart.
/// </summary>
/// <remarks>
/// Registered automatically by <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c> when it had to bridge an
/// asynchronous-only key provider — see <see cref="EncryptionKeyRingCache"/>. <see cref="StartAsync"/> awaits the
/// first refresh, so a failure there (including the wrapped provider being unreachable at startup) fails host
/// startup rather than accepting traffic with a cold, unusable cache. A <see cref="PeriodicTimer"/> driven by the
/// injected <see cref="TimeProvider"/> then calls <see cref="EncryptionKeyRingCache.RefreshAsync"/> every
/// <see cref="EncryptionOptions.KeyRingRefreshInterval"/>; a failed refresh is logged and the previous snapshot
/// stays in use.
/// </remarks>
internal sealed class EncryptionKeyRingRefreshHostedService : IHostedService, IDisposable
{
    private readonly EncryptionKeyRingCache _cache;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EncryptionKeyRingRefreshHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _refreshLoop;

    public EncryptionKeyRingRefreshHostedService(
        EncryptionKeyRingCache cache,
        TimeSpan refreshInterval,
        TimeProvider timeProvider,
        ILogger<EncryptionKeyRingRefreshHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshInterval, TimeSpan.Zero);
        _cache = cache;
        _refreshInterval = refreshInterval;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _refreshLoop = RefreshLoopAsync(_stopping.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_refreshLoop is null)
            return;

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _refreshLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_refreshInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _cache.RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    EncryptionLog.KeyRingRefreshFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }
}

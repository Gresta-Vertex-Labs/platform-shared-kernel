using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Warms <see cref="PreWarmedEncryptionKeyProvider"/>'s current encryption key before the host accepts traffic, then
/// refreshes it periodically so a key rotated at the key service becomes current without a restart.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically by
/// <c>EfCorePersistenceBuilder&lt;TContext&gt;.WithExternalEncryptionKeyProvider&lt;TProvider&gt;(refreshInterval)</c>.
/// <see cref="StartAsync"/> awaits the first warm — mirroring <c>MigrationAndSeedHostedService&lt;TContext&gt;</c>'s
/// "block readiness until done" shape — so a failure there (including an unregistered <c>TProvider</c>) fails host
/// startup. After that a <see cref="PeriodicTimer"/> driven by the injected <see cref="TimeProvider"/> calls
/// <see cref="PreWarmedEncryptionKeyProvider.RefreshCurrentAsync"/> every refresh interval. A failed refresh is
/// logged (EventId <c>6011</c>) and the last warmed keys stay in use.
/// </para>
/// <para><see cref="StopAsync"/> cancels the refresh loop and waits for it to finish.</para>
/// </remarks>
internal sealed class EncryptionKeyPreWarmingHostedService : IHostedService, IDisposable
{
    private readonly PreWarmedEncryptionKeyProvider _provider;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _refreshLoop;

    /// <summary>Initialises a new <see cref="EncryptionKeyPreWarmingHostedService"/>.</summary>
    /// <param name="provider">The provider to warm on startup and refresh periodically.</param>
    /// <param name="refreshInterval">How often the current key is refreshed.</param>
    /// <param name="timeProvider">The clock driving the refresh timer.</param>
    /// <param name="logger">Logger for failed refreshes. Defaults to a no-op logger.</param>
    public EncryptionKeyPreWarmingHostedService(
        PreWarmedEncryptionKeyProvider provider,
        TimeSpan refreshInterval,
        TimeProvider timeProvider,
        ILogger<EncryptionKeyPreWarmingHostedService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshInterval, TimeSpan.Zero);
        _provider = provider;
        _refreshInterval = refreshInterval;
        _timeProvider = timeProvider;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _provider.WarmCurrentAsync(cancellationToken).ConfigureAwait(false);
        _refreshLoop = RefreshLoopAsync(_stopping.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_refreshLoop is null)
        {
            return;
        }

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
                    await _provider.RefreshCurrentAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    PersistenceLog.EncryptionKeyRefreshFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// Loads the key ring before the host takes traffic and refreshes it every
/// <see cref="EncryptionOptions.KeyRefreshInterval"/>, when keys come from an asynchronous-only provider.
/// </summary>
/// <remarks>
/// Does nothing for a synchronous provider. A failed first load fails host startup. A failed refresh keeps the
/// keys loaded earlier and logs a warning; once the last success is older than
/// <see cref="EncryptionOptions.MaxKeyStaleness"/> it logs an error on every failure, and the key-ring probe
/// reports unhealthy.
/// </remarks>
internal sealed class KeyRingRefreshHostedService : IHostedService, IDisposable
{
    private readonly FieldKeyRing _keyRing;
    private readonly EncryptionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KeyRingRefreshHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public KeyRingRefreshHostedService(
        FieldKeyRing keyRing,
        IOptions<EncryptionOptions> options,
        ILogger<KeyRingRefreshHostedService> logger,
        TimeProvider? timeProvider = null)
    {
        _keyRing = keyRing;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_keyRing.IsBridged)
            return;

        await _keyRing.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _loop = RefreshLoopAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_loop is null)
            return;

        await _stopping.CancelAsync().ConfigureAwait(false);
        await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.KeyRefreshInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _keyRing.RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    EncryptionLog.KeyRefreshFailed(_logger, exception);
                    if (_keyRing.LastRefreshedAt is { } last
                        && _timeProvider.GetUtcNow() - last is var age && age > _options.MaxKeyStaleness)
                    {
                        EncryptionLog.KeysStale(_logger, age);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

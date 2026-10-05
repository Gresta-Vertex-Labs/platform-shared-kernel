using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;
using SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption.KeyRing;

/// <summary>
/// The encryption keys field encryption uses: the configured key source, served synchronously to the
/// save/materialization path and asynchronously to the maintenance job.
/// </summary>
/// <remarks>
/// <para>
/// EF Core materializes rows synchronously, so decryption needs keys without awaiting. A provider that is also an
/// <see cref="ISynchronousEncryptionKeyProvider"/> (for example <see cref="StaticEncryptionKeyProvider"/>) is used
/// directly. An asynchronous-only provider (a KMS) is bridged: keys are loaded into an in-memory snapshot at
/// startup, refreshed every <see cref="EncryptionOptions.KeyRefreshInterval"/> by
/// <see cref="KeyRingRefreshHostedService"/>, and a key id the snapshot does not hold is fetched in the background
/// the first time a value needs it (at most once per <see cref="MissFetchCooldown"/> per id). That one read fails
/// closed with <see cref="EncryptionKeyNotFoundException"/>; the key never is awaited from inside a query.
/// </para>
/// </remarks>
internal sealed class FieldKeyRing
{
    internal static readonly TimeSpan MissFetchCooldown = TimeSpan.FromSeconds(30);

    private readonly Lazy<IEncryptionKeyProvider> _provider;
    private readonly Lazy<ISynchronousEncryptionKeyProvider?> _synchronous;
    private readonly EncryptionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FieldKeyRing> _logger;
    private readonly ConcurrentDictionary<string, long> _missFetchTimestamps = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Snapshot? _snapshot;

    public FieldKeyRing(
        IServiceProvider services,
        FieldEncryptionSettings settings,
        IOptions<EncryptionOptions> options,
        ILogger<FieldKeyRing> logger)
    {
        _options = options.Value;
        _timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        _logger = logger;
        _provider = new Lazy<IEncryptionKeyProvider>(() => settings.ResolveKeyProvider(services));
        _synchronous = new Lazy<ISynchronousEncryptionKeyProvider?>(() => _provider.Value switch
        {
            ISynchronousEncryptionKeyProvider synchronous => synchronous,
            _ when !settings.HasExplicitKeySource => services.GetService<ISynchronousEncryptionKeyProvider>(),
            _ => null,
        });
    }

    /// <summary>The asynchronous key provider, the source of truth.</summary>
    public IEncryptionKeyProvider Provider => _provider.Value;

    /// <summary>Whether keys are served from a refreshed snapshot of an asynchronous-only provider.</summary>
    public bool IsBridged => _synchronous.Value is null;

    /// <summary>When the snapshot was last refreshed successfully, or <see langword="null"/> before the first refresh.</summary>
    public DateTimeOffset? LastRefreshedAt => _snapshot?.RefreshedAt;

    /// <summary>The key new values are encrypted with.</summary>
    public CryptographicKey GetCurrentKey()
    {
        if (_synchronous.Value is { } synchronous)
            return synchronous.GetCurrentKey();

        return EnsureSnapshot().Current;
    }

    /// <summary>Returns the key with <paramref name="keyId"/>, or <see langword="null"/> when it is not available now.</summary>
    public CryptographicKey? GetKey(string keyId)
    {
        if (_synchronous.Value is { } synchronous)
            return synchronous.GetKey(keyId);

        if (EnsureSnapshot().ByKeyId.TryGetValue(keyId, out var key))
            return key;

        ScheduleMissFetch(keyId);
        return null;
    }

    /// <summary>Reloads the current key and <see cref="EncryptionOptions.AdditionalDecryptionKeyIds"/>. A no-op when not bridged.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsBridged)
            return;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await Provider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
            var byKeyId = new Dictionary<string, CryptographicKey>(StringComparer.Ordinal);

            // Keep every key already loaded (including ones fetched on a miss): key ids are immutable, and dropping
            // one would make values encrypted with it unreadable until the next miss fetch.
            if (_snapshot is { } previous)
            {
                foreach (var (id, key) in previous.ByKeyId)
                    byKeyId[id] = key;
            }

            byKeyId[current.Id] = current;

            foreach (var keyId in _options.AdditionalDecryptionKeyIds)
            {
                if (byKeyId.ContainsKey(keyId))
                    continue;

                if (await Provider.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false) is { } key)
                    byKeyId[key.Id] = key;
            }

            _snapshot = new Snapshot(current, byKeyId, _timeProvider.GetUtcNow());
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Loads one missing key into the snapshot. Returns whether the provider knew it.</summary>
    internal async Task<bool> FetchKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        var key = await Provider.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (key is null)
            return false;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot ?? throw new InvalidOperationException("The key ring is not loaded.");
            var byKeyId = new Dictionary<string, CryptographicKey>(snapshot.ByKeyId, StringComparer.Ordinal) { [key.Id] = key };
            _snapshot = snapshot with { ByKeyId = byKeyId };
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // Normally warmed by KeyRingRefreshHostedService before the host takes traffic. A process without a host (a
    // console tool, a test) warms here once, blocking on the provider for that single startup call only.
    private Snapshot EnsureSnapshot()
    {
        if (_snapshot is { } snapshot)
            return snapshot;

        Task.Run(() => RefreshAsync(CancellationToken.None)).GetAwaiter().GetResult();
        return _snapshot ?? throw new InvalidOperationException("The encryption key ring could not be loaded.");
    }

    private void ScheduleMissFetch(string keyId)
    {
        var now = _timeProvider.GetTimestamp();
        var last = _missFetchTimestamps.GetOrAdd(keyId, static _ => long.MinValue);
        if (last != long.MinValue && _timeProvider.GetElapsedTime(last, now) < MissFetchCooldown)
            return;

        if (!_missFetchTimestamps.TryUpdate(keyId, now, last))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await FetchKeyAsync(keyId, CancellationToken.None).ConfigureAwait(false))
                    EncryptionLog.KeyFetchedOnMiss(_logger, keyId);
                else
                    EncryptionLog.KeyFetchOnMissFailed(_logger, keyId, null);
            }
            catch (Exception exception)
            {
                EncryptionLog.KeyFetchOnMissFailed(_logger, keyId, exception);
            }
        });
    }

    private sealed record Snapshot(
        CryptographicKey Current,
        IReadOnlyDictionary<string, CryptographicKey> ByKeyId,
        DateTimeOffset RefreshedAt);
}

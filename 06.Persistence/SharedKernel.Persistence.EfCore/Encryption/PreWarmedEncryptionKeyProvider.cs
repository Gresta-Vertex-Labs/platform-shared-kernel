using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Bridges an asynchronous (KMS/HSM-backed) <see cref="IEncryptionKeyProvider"/> to the synchronous
/// <see cref="ISynchronousEncryptionKeyProvider"/> contract that <see cref="EncryptedValueConverter"/> needs,
/// by serving keys only from an in-memory cache that is warmed asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// EF Core value converters are synchronous, so they can never await a key service. This type's synchronous
/// members (<see cref="GetCurrentKey"/>/<see cref="GetKey"/>) never await <see cref="Inner"/>: they read only
/// from the warm cache. The cache is filled by
/// <list type="bullet">
///   <item><description><see cref="WarmCurrentAsync"/>, run by <see cref="Interceptors.EncryptionKeyPreWarmingInterceptor"/>
///   before every write and read that could reach an encrypted property, and at host startup;</description></item>
///   <item><description><see cref="RefreshCurrentAsync"/>, run periodically by
///   <see cref="EncryptionKeyPreWarmingHostedService"/>, so a key rotated at the key service becomes current
///   without a restart (previously warmed keys are kept, so existing payloads still decrypt);</description></item>
///   <item><description>an on-demand background warm scheduled by <see cref="GetKey"/> on a miss, so a payload
///   encrypted under an older key id fails once and succeeds on a later read.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Fails closed, never blocks:</strong> a miss on <see cref="GetKey"/> returns <see langword="null"/>
/// (surfacing as <see cref="EncryptionKeyNotFoundException"/>) after scheduling the background warm. A miss on
/// <see cref="GetCurrentKey"/> throws <see cref="InvalidOperationException"/>; under normal operation that is
/// unreachable, because the startup hosted service warms the current key before the host accepts traffic.
/// </para>
/// <para>
/// <strong>Bounded on-demand warming:</strong> key ids come from stored data, which anyone with database write access
/// can forge. On-demand warms are therefore deduplicated per id, capped at <see cref="MaxPendingWarms"/> distinct
/// ids in flight, skipped for ids that are not valid key ids (whitespace, control characters, or longer than
/// <see cref="CryptographicKey.MaxIdLength"/> UTF-8 bytes), and an id the key service reports as unknown is not
/// looked up again until <c>unknownKeyRetryDelay</c> has passed (at most <see cref="MaxRememberedUnknownKeyIds"/>
/// such ids are remembered).
/// </para>
/// <para>Registered as a SINGLETON — the warm cache is process-lifetime state, not per-request state.</para>
/// </remarks>
internal sealed class PreWarmedEncryptionKeyProvider : ISynchronousEncryptionKeyProvider, IDisposable
{
    /// <summary>The most distinct key ids warmed on demand at the same time.</summary>
    internal const int MaxPendingWarms = 64;

    /// <summary>The most key ids remembered as unknown to the key service.</summary>
    internal const int MaxRememberedUnknownKeyIds = 1024;

    private readonly IEncryptionKeyProvider _inner;
    private readonly IEncryptionVersionOverride _versionOverride;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _unknownKeyRetryDelay;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, CryptographicKey> _warmCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _pendingWarms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _unknownKeyIds = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposal = new();
    private volatile string? _currentVersionTag;
    private int _pendingWarmCount;

    /// <summary>Initialises a new <see cref="PreWarmedEncryptionKeyProvider"/>.</summary>
    /// <param name="inner">
    /// The wrapped provider — never awaited from <see cref="GetCurrentKey"/>/<see cref="GetKey"/>.
    /// </param>
    /// <param name="versionOverride">
    /// The same rotation-target-version accessor <see cref="EncryptedValueConverter"/> consults, so a rotation
    /// batch's <c>OverrideVersion</c> resolves consistently through this provider too.
    /// </param>
    /// <param name="timeProvider">Clock for the unknown-key-id retry delay. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">Logger for failed on-demand warms. Defaults to a no-op logger.</param>
    /// <param name="unknownKeyRetryDelay">
    /// How long a key id the key service reported as unknown is not looked up again. Defaults to 5 minutes.
    /// </param>
    public PreWarmedEncryptionKeyProvider(
        IEncryptionKeyProvider inner,
        IEncryptionVersionOverride versionOverride,
        TimeProvider? timeProvider = null,
        ILogger<PreWarmedEncryptionKeyProvider>? logger = null,
        TimeSpan? unknownKeyRetryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(versionOverride);
        _inner = inner;
        _versionOverride = versionOverride;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _unknownKeyRetryDelay = unknownKeyRetryDelay ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// The wrapped provider — exposed only so tests can prove it is never invoked from the synchronous members.
    /// </summary>
    internal IEncryptionKeyProvider Inner => _inner;

    /// <inheritdoc />
    /// <remarks>
    /// Never touches <see cref="Inner"/>. Resolves the version as
    /// <c>versionOverride.OverrideVersion ?? currentVersionTag</c> and returns its warm cache entry.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The resolved version has not been warmed.</exception>
    public CryptographicKey GetCurrentKey()
    {
        var tag = _versionOverride.OverrideVersion ?? _currentVersionTag;

        if (tag is not null && _warmCache.TryGetValue(tag, out var key))
        {
            return key;
        }

        throw new InvalidOperationException(
            tag is null
                ? "PreWarmedEncryptionKeyProvider has not been warmed yet — no current encryption " +
                  "key version is known. This should be unreachable under normal operation: " +
                  "WithExternalEncryptionKeyProvider<TProvider>() registers a readiness gate " +
                  "(EncryptionKeyPreWarmingHostedService) that warms the current key before the " +
                  "host accepts traffic. If you see this, a startup-ordering invariant was broken."
                : $"PreWarmedEncryptionKeyProvider has no warm cache entry for encryption key " +
                  $"version '{tag}'. This provider never performs I/O from GetCurrentKey — " +
                  "the version must be warmed first via WarmCurrentAsync (automatic, via " +
                  "EncryptionKeyPreWarmingInterceptor/EncryptionKeyPreWarmingHostedService).");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never awaits <see cref="Inner"/>. A miss returns <see langword="null"/> — surfacing downstream as
    /// <see cref="EncryptionKeyNotFoundException"/> — and schedules a bounded background warm for
    /// <paramref name="keyId"/>, so a later read can succeed.
    /// </remarks>
    public CryptographicKey? GetKey(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (_warmCache.TryGetValue(keyId, out var key))
        {
            return key;
        }

        ScheduleWarm(keyId);
        return null;
    }

    /// <summary>
    /// Warms the current key — resolved as <c>versionOverride.OverrideVersion ?? currentVersionTag</c> when already
    /// known, or fetched from <see cref="Inner"/> otherwise — updating the current version tag only when resolving
    /// without an active override. No-op (never calls <see cref="Inner"/>) when the resolved version is already warm.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while resolving the current key from <see cref="Inner"/>.</param>
    /// <returns>A task that completes when the key is warm.</returns>
    public async ValueTask WarmCurrentAsync(CancellationToken cancellationToken = default)
    {
        var overrideTag = _versionOverride.OverrideVersion;
        if (overrideTag is not null)
        {
            await WarmVersionAsync(overrideTag, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_currentVersionTag is { } currentTag && _warmCache.ContainsKey(currentTag))
        {
            return;
        }

        await RefreshCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches the current key from <see cref="Inner"/> unconditionally and makes it current, keeping every
    /// previously warmed key so payloads encrypted under them still decrypt. On failure nothing changes.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while resolving the current key from <see cref="Inner"/>.</param>
    /// <returns>A task that completes when the refreshed key is current.</returns>
    public async ValueTask RefreshCurrentAsync(CancellationToken cancellationToken = default)
    {
        var key = await _inner.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        _warmCache[key.Id] = key;
        _unknownKeyIds.TryRemove(key.Id, out _);
        _currentVersionTag = key.Id;
    }

    /// <summary>
    /// Warms one specific key version — used to pre-warm a rotation batch's target version or a decrypt path
    /// expecting an older version. No-op (never calls <see cref="Inner"/>) when <paramref name="keyId"/> is already
    /// warm; an id <see cref="Inner"/> does not know caches nothing.
    /// </summary>
    /// <param name="keyId">The key version identifier to warm.</param>
    /// <param name="cancellationToken">A token to observe while resolving the key from <see cref="Inner"/>.</param>
    /// <returns>A task that completes when the key is warm, or known to be absent.</returns>
    public async ValueTask WarmVersionAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (_warmCache.ContainsKey(keyId))
        {
            return;
        }

        var key = await _inner.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (key is not null)
        {
            _warmCache[key.Id] = key;
            _unknownKeyIds.TryRemove(keyId, out _);
        }
        else
        {
            RememberUnknown(keyId);
        }
    }

    /// <summary>Completes when every on-demand warm scheduled so far has finished. For tests.</summary>
    internal Task WaitForPendingWarmsAsync() => Task.WhenAll(_pendingWarms.Values);

    /// <inheritdoc />
    /// <remarks>Cancels in-flight on-demand warms and stops scheduling new ones.</remarks>
    public void Dispose() => _disposal.Cancel();

    private void ScheduleWarm(string keyId)
    {
        if (_disposal.IsCancellationRequested || !IsValidKeyId(keyId) || IsKnownUnknown(keyId))
        {
            return;
        }

        if (Interlocked.Increment(ref _pendingWarmCount) > MaxPendingWarms)
        {
            Interlocked.Decrement(ref _pendingWarmCount);
            return;
        }

        var cancellationToken = _disposal.Token;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingWarms.TryAdd(keyId, completion.Task))
        {
            Interlocked.Decrement(ref _pendingWarmCount);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await WarmVersionAsync(keyId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                PersistenceLog.EncryptionKeyOnDemandWarmFailed(_logger, ex);
            }
            finally
            {
                _pendingWarms.TryRemove(keyId, out _);
                Interlocked.Decrement(ref _pendingWarmCount);
                completion.TrySetResult();
            }
        });
    }

    private bool IsKnownUnknown(string keyId)
    {
        if (!_unknownKeyIds.TryGetValue(keyId, out var retryAfter))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() < retryAfter)
        {
            return true;
        }

        _unknownKeyIds.TryRemove(keyId, out _);
        return false;
    }

    private void RememberUnknown(string keyId)
    {
        var now = _timeProvider.GetUtcNow();

        if (_unknownKeyIds.Count >= MaxRememberedUnknownKeyIds)
        {
            foreach (var entry in _unknownKeyIds)
            {
                if (entry.Value <= now)
                {
                    _unknownKeyIds.TryRemove(entry.Key, out _);
                }
            }

            if (_unknownKeyIds.Count >= MaxRememberedUnknownKeyIds)
            {
                return;
            }
        }

        _unknownKeyIds[keyId] = now + _unknownKeyRetryDelay;
    }

    private static bool IsValidKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || Encoding.UTF8.GetByteCount(keyId) > CryptographicKey.MaxIdLength)
        {
            return false;
        }

        foreach (var c in keyId)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}

using SharedKernel.Cryptography.Internal;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Caches the keys an <see cref="IEncryptionKeyProvider"/> resolves, so a remote key service is not called for
/// every encryption.
/// </summary>
/// <remarks>
/// <para>
/// <b>Expiry.</b> A key is served from cache for the time to live and fetched again after it, so a rotation of the
/// current key takes effect within that time. An expired key is never served, including when the refresh fails:
/// the failure is thrown.
/// </para>
/// <para>
/// <b>Single flight.</b> Concurrent requests for the same key share one call to the inner provider.
/// </para>
/// <para>
/// <b>Bounds.</b> Unknown key ids are not cached, and at most <c>maxEntries</c> keys are held. Key ids come from
/// payloads and may be chosen by an attacker; neither can grow memory without limit.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton&lt;IEncryptionKeyProvider&gt;(sp => new CachedEncryptionKeyProvider(
///     sp.GetRequiredService&lt;AzureKeyVaultEncryptionKeyProvider&gt;(),
///     TimeProvider.System,
///     timeToLive: TimeSpan.FromMinutes(5)));
/// </code>
/// </example>
public sealed class CachedEncryptionKeyProvider : IEncryptionKeyProvider
{
    /// <summary>The default maximum number of cached keys.</summary>
    public const int DefaultMaxEntries = 1024;

    private readonly IEncryptionKeyProvider _inner;
    private readonly SingleFlightCache<bool, CryptographicKey?> _currentKey;
    private readonly SingleFlightCache<string, CryptographicKey?> _keysById;

    /// <summary>Creates the cache.</summary>
    /// <param name="inner">The provider to cache.</param>
    /// <param name="timeProvider">The clock used for expiry.</param>
    /// <param name="timeToLive">How long a resolved key is served before it is fetched again. Must be positive.</param>
    /// <param name="maxEntries">The most keys held at once. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> or <paramref name="maxEntries"/> is not positive.</exception>
    public CachedEncryptionKeyProvider(
        IEncryptionKeyProvider inner,
        TimeProvider timeProvider,
        TimeSpan timeToLive,
        int maxEntries = DefaultMaxEntries)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _currentKey = new SingleFlightCache<bool, CryptographicKey?>(
            timeProvider,
            timeToLive,
            maxEntries: 1,
            shouldCache: static key => key is not null);
        _keysById = new SingleFlightCache<string, CryptographicKey?>(
            timeProvider,
            timeToLive,
            maxEntries,
            shouldCache: static key => key is not null,
            comparer: StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        CryptographicKey? key = await _currentKey
            .GetOrAddAsync(true, async token => await _inner.GetCurrentKeyAsync(token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        return key ?? throw new InvalidOperationException("The inner key provider returned no current key.");
    }

    /// <inheritdoc />
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return _keysById.GetOrAddAsync(keyId, token => _inner.GetKeyAsync(keyId, token), cancellationToken);
    }
}

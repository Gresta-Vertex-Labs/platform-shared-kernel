namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Determines whether a given <see cref="IEncryptionKeyProvider"/> is safe to call from a
/// genuinely synchronous code path — i.e. whether it can be trusted to never block the calling
/// thread on a network/IPC round trip.
/// </summary>
/// <remarks>
/// This is a <b>static, provider-identity check</b> — it inspects what kind of provider is
/// registered, never how "warm" any particular cache entry happens to be at call time. In
/// particular, a <see cref="CachedEncryptionKeyProvider"/> wrapping a KMS-backed inner provider
/// always reports <see langword="false"/>, even on a call that would in fact hit a warm cache
/// entry — a cache miss can still occur on the very next call, and the whole point of this gate
/// is to make that possibility structural rather than a matter of runtime luck. Callers wanting a
/// provider that always reports <see langword="true"/> must supply one that itself never performs
/// blocking I/O, not merely one that usually doesn't.
/// </remarks>
public static class EncryptionKeyProviderCapabilities
{
    /// <summary>
    /// Reports whether <paramref name="provider"/> can be genuinely trusted never to block the
    /// calling thread on a network/IPC round trip.
    /// </summary>
    /// <param name="provider">The provider to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="provider"/> directly implements
    /// <see cref="ISynchronousEncryptionKeyProvider"/>, or when it is a
    /// <see cref="CachedEncryptionKeyProvider"/> whose <see cref="CachedEncryptionKeyProvider.Inner"/>
    /// recursively satisfies this same check (unwrapping through any depth of nested
    /// <see cref="CachedEncryptionKeyProvider"/> decorators). <see langword="false"/> for every
    /// other case, including any unrecognized third-party decorator wrapping a genuinely
    /// synchronous provider — this method fails toward requiring the <c>*Async</c> overloads,
    /// never toward silently permitting a blocking bridge.
    /// </returns>
    public static bool IsGenuinelySynchronous(IEncryptionKeyProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider switch
        {
            ISynchronousEncryptionKeyProvider => true,
            CachedEncryptionKeyProvider cached => IsGenuinelySynchronous(cached.Inner),
            _ => false,
        };
    }
}

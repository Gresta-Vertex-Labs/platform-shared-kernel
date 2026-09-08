namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Determines whether a given <see cref="IAsymmetricKeyProvider"/> is safe to call from a
/// genuinely synchronous code path — i.e. whether it can be trusted to never block the calling
/// thread on a network/IPC round trip.
/// </summary>
/// <remarks>
/// This is a <b>direct, provider-identity check only</b> — unlike its symmetric-encryption
/// counterpart, <see cref="Symmetric.EncryptionKeyProviderCapabilities"/>, this method performs
/// no decorator-unwrapping. No caching decorator (the asymmetric analog of
/// <see cref="Symmetric.CachedEncryptionKeyProvider"/>) exists for
/// <see cref="IAsymmetricKeyProvider"/> as of this phase (P-493/WO-081), so there is nothing to
/// unwrap through. Should such a decorator be introduced in a future phase, this method's
/// behavior would need to be revisited alongside it — it must never be widened speculatively
/// ahead of that decorator actually existing.
/// </remarks>
public static class AsymmetricKeyProviderCapabilities
{
    /// <summary>
    /// Reports whether <paramref name="provider"/> can be genuinely trusted never to block the
    /// calling thread on a network/IPC round trip.
    /// </summary>
    /// <param name="provider">The provider to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="provider"/> directly implements
    /// <see cref="ISynchronousAsymmetricKeyProvider"/>; <see langword="false"/> for every other
    /// case — this method fails toward requiring the <c>*Async</c> overloads, never toward
    /// silently permitting a blocking bridge.
    /// </returns>
    public static bool IsGenuinelySynchronous(IAsymmetricKeyProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider is ISynchronousAsymmetricKeyProvider;
    }
}

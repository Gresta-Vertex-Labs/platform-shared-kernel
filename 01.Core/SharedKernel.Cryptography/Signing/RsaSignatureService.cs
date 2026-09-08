using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using RSA (2048-bit minimum) with PSS padding and SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// <b>(P-493/WO-081)</b> Whether the synchronous <see cref="Sign(byte[], string)"/>/
/// <see cref="Verify(byte[], byte[], string)"/> members are usable at all is decided exactly
/// once, at construction time: <see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>
/// is evaluated against the supplied <see cref="IAsymmetricKeyProvider"/> and cached for the
/// lifetime of this instance, mirroring <c>AesGcmEncryptionService</c>'s P-492 gating exactly.
/// When it reports <see langword="true"/>, both sync members bridge onto the provider via
/// <c>.GetAwaiter().GetResult()</c>, which is genuinely non-blocking for key resolution. When it
/// reports <see langword="false"/>, both sync members throw <see cref="NotSupportedException"/>
/// immediately instead of silently blocking a real thread; the caller must use
/// <see cref="SignAsync(byte[], string, CancellationToken)"/>/
/// <see cref="VerifyAsync(byte[], byte[], string, CancellationToken)"/> instead.
/// </para>
/// <para>
/// <b>THE RSA INSTANCE RETURNED BY <see cref="IAsymmetricKeyProvider.GetRsaKeyAsync"/> IS NOT
/// DISPOSED BY THIS SERVICE.</b> It is not caller-owned — see
/// <see cref="IAsymmetricKeyProvider"/>'s type-level remarks. Lifecycle ownership remains with
/// the provider, which may return the same cached instance across many calls; disposing it here
/// would risk an <see cref="ObjectDisposedException"/> on the provider's next use of that same
/// instance.
/// </para>
/// </remarks>
public sealed class RsaSignatureService : IAsymmetricSignatureService
{
    private const int MinimumKeySizeBits = 2048;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;
    private static readonly RSASignaturePadding Padding = RSASignaturePadding.Pss;

    private readonly IAsymmetricKeyProvider _keyProvider;
    private readonly bool _isKeyProviderGenuinelySynchronous;

    /// <summary>Creates a new <see cref="RsaSignatureService"/>.</summary>
    /// <param name="keyProvider">Resolves RSA key pairs by key id.</param>
    public RsaSignatureService(IAsymmetricKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;

        // Computed once, here, and cached for the lifetime of this instance — never re-evaluated
        // per call. See AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous.
        _isKeyProviderGenuinelySynchronous = AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(keyProvider);
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] data, string keyId)
    {
        ThrowIfNotGenuinelySynchronous(nameof(SignAsync));
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        // GENUINELY NON-BLOCKING: the registered IAsymmetricKeyProvider was confirmed genuinely
        // synchronous at construction time (see _isKeyProviderGenuinelySynchronous) — this
        // GetAwaiter().GetResult() observes an already-completed ValueTask, it never schedules or
        // waits on a continuation.
        RSA rsa = _keyProvider.GetRsaKeyAsync(keyId).GetAwaiter().GetResult();
        EnsureMinimumKeySize(rsa);
        return rsa.SignData(data, HashAlgorithm, Padding);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SignAsync(byte[] data, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        RSA rsa = await _keyProvider.GetRsaKeyAsync(keyId, ct).ConfigureAwait(false);
        EnsureMinimumKeySize(rsa);

        // No async BCL overload exists for SignData — see IAsymmetricSignatureService's
        // type-level remarks. Only key resolution above is genuinely asynchronous.
        return rsa.SignData(data, HashAlgorithm, Padding);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, string keyId)
    {
        ThrowIfNotGenuinelySynchronous(nameof(VerifyAsync));
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        // GENUINELY NON-BLOCKING: see Sign's equivalent comment above.
        RSA rsa = _keyProvider.GetRsaKeyAsync(keyId).GetAwaiter().GetResult();
        EnsureMinimumKeySize(rsa);
        return rsa.VerifyData(data, signature, HashAlgorithm, Padding);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        RSA rsa = await _keyProvider.GetRsaKeyAsync(keyId, ct).ConfigureAwait(false);
        EnsureMinimumKeySize(rsa);

        // No async BCL overload exists for VerifyData — see IAsymmetricSignatureService's
        // type-level remarks. Only key resolution above is genuinely asynchronous.
        return rsa.VerifyData(data, signature, HashAlgorithm, Padding);
    }

    /// <summary>
    /// Guards a retained synchronous member: throws when the registered
    /// <see cref="IAsymmetricKeyProvider"/> was not confirmed genuinely synchronous at
    /// construction time (<see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>),
    /// instead of silently bridging onto it via <c>.GetAwaiter().GetResult()</c>.
    /// </summary>
    /// <param name="asyncMemberName">
    /// The name of this member's <c>*Async</c> counterpart, surfaced in the exception message.
    /// </param>
    private void ThrowIfNotGenuinelySynchronous(string asyncMemberName)
    {
        if (!_isKeyProviderGenuinelySynchronous)
        {
            throw new NotSupportedException(
                $"The registered {nameof(IAsymmetricKeyProvider)} does not implement " +
                $"{nameof(ISynchronousAsymmetricKeyProvider)}, so it cannot be trusted never to " +
                "block the calling thread on a network/IPC round trip. Call " +
                $"{asyncMemberName} instead.");
        }
    }

    private static void EnsureMinimumKeySize(RSA rsa)
    {
        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new CryptographicException(
                $"RSA key size {rsa.KeySize} bits is below the minimum required {MinimumKeySizeBits} bits.");
        }
    }
}

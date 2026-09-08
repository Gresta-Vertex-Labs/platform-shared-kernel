using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using ECDSA on the P-256 curve with SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// <b>(P-493/WO-081)</b> Whether the synchronous <see cref="Sign(byte[], string)"/>/
/// <see cref="Verify(byte[], byte[], string)"/> members are usable at all is decided exactly
/// once, at construction time — mirrors <see cref="RsaSignatureService"/>'s gating exactly. See
/// its type-level remarks for the full explanation.
/// </para>
/// <para>
/// <b>THE ECDSA INSTANCE RETURNED BY <see cref="IAsymmetricKeyProvider.GetEcdsaKeyAsync"/> IS
/// NOT DISPOSED BY THIS SERVICE.</b> It is not caller-owned — see
/// <see cref="IAsymmetricKeyProvider"/>'s type-level remarks. Lifecycle ownership remains with
/// the provider, which may return the same cached instance across many calls; disposing it here
/// would risk an <see cref="ObjectDisposedException"/> on the provider's next use of that same
/// instance.
/// </para>
/// <para>
/// <b>(P-493/WO-081)</b> A minimum key-size check (<see cref="EnsureMinimumKeySize(ECDsa)"/>,
/// 256 bits — matching the P-256 curve this service is documented to expect) is now applied on
/// both <see cref="Sign(byte[], string)"/> and <see cref="Verify(byte[], byte[], string)"/> (and
/// their async counterparts). Previously no minimum key-size check existed for ECDSA at all —
/// this closes a silent accept-anything gap for a provider that hands back a weaker-than-expected
/// curve, mirroring the check <see cref="RsaSignatureService"/> already applied to RSA (now
/// applied there to <c>Verify</c> as well, for the same reason — see its remarks).
/// </para>
/// </remarks>
public sealed class EcdsaSignatureService : IAsymmetricSignatureService
{
    private const int MinimumKeySizeBits = 256;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    private readonly IAsymmetricKeyProvider _keyProvider;
    private readonly bool _isKeyProviderGenuinelySynchronous;

    /// <summary>Creates a new <see cref="EcdsaSignatureService"/>.</summary>
    /// <param name="keyProvider">Resolves ECDSA key pairs by key id.</param>
    public EcdsaSignatureService(IAsymmetricKeyProvider keyProvider)
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
        ECDsa ecdsa = _keyProvider.GetEcdsaKeyAsync(keyId).GetAwaiter().GetResult();
        EnsureMinimumKeySize(ecdsa);
        return ecdsa.SignData(data, HashAlgorithm);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SignAsync(byte[] data, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        ECDsa ecdsa = await _keyProvider.GetEcdsaKeyAsync(keyId, ct).ConfigureAwait(false);
        EnsureMinimumKeySize(ecdsa);

        // No async BCL overload exists for SignData — see IAsymmetricSignatureService's
        // type-level remarks. Only key resolution above is genuinely asynchronous.
        return ecdsa.SignData(data, HashAlgorithm);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, string keyId)
    {
        ThrowIfNotGenuinelySynchronous(nameof(VerifyAsync));
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        // GENUINELY NON-BLOCKING: see Sign's equivalent comment above.
        ECDsa ecdsa = _keyProvider.GetEcdsaKeyAsync(keyId).GetAwaiter().GetResult();
        EnsureMinimumKeySize(ecdsa);
        return ecdsa.VerifyData(data, signature, HashAlgorithm);
    }

    /// <inheritdoc />
    public async ValueTask<bool> VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        ECDsa ecdsa = await _keyProvider.GetEcdsaKeyAsync(keyId, ct).ConfigureAwait(false);
        EnsureMinimumKeySize(ecdsa);

        // No async BCL overload exists for VerifyData — see IAsymmetricSignatureService's
        // type-level remarks. Only key resolution above is genuinely asynchronous.
        return ecdsa.VerifyData(data, signature, HashAlgorithm);
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

    private static void EnsureMinimumKeySize(ECDsa ecdsa)
    {
        if (ecdsa.KeySize < MinimumKeySizeBits)
        {
            throw new CryptographicException(
                $"ECDSA key size {ecdsa.KeySize} bits is below the minimum required {MinimumKeySizeBits} bits.");
        }
    }
}

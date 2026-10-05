using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>An asymmetric key bound to one <see cref="SignatureAlgorithm"/>, able to sign and verify digests.</summary>
/// <remarks>
/// <para>
/// The algorithm belongs to the key, never to the caller, so a signature can never be checked with a weaker
/// algorithm than the key was issued for.
/// </para>
/// <para>
/// Create local keys with <see cref="FromRsa"/> and <see cref="FromECDsa"/>. A key held in a key management service
/// derives from this class and signs remotely; <c>SharedKernel.Cryptography.KeyVault.Azure</c> does so for Azure
/// Key Vault.
/// </para>
/// </remarks>
public abstract class SigningKey : IDisposable
{
    /// <summary>The smallest RSA key accepted: 2048 bits.</summary>
    public const int MinimumRsaKeySize = 2048;

    /// <summary>Initializes the key.</summary>
    /// <param name="keyId">The key id. Must not be empty.</param>
    /// <param name="algorithm">The algorithm this key signs with.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="algorithm"/> is not defined.</exception>
    protected SigningKey(string keyId, SignatureAlgorithm algorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (!Enum.IsDefined(algorithm))
        {
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown signature algorithm.");
        }

        KeyId = keyId;
        Algorithm = algorithm;
    }

    /// <summary>The key id.</summary>
    public string KeyId { get; }

    /// <summary>The algorithm this key signs and verifies with.</summary>
    public SignatureAlgorithm Algorithm { get; }

    /// <summary>The digest algorithm <see cref="Algorithm"/> uses.</summary>
    public HashAlgorithmName HashAlgorithm => GetHashAlgorithm(Algorithm);

    /// <summary>Signs a digest computed with <see cref="HashAlgorithm"/>.</summary>
    /// <param name="hash">The digest.</param>
    /// <param name="cancellationToken">A token to cancel a remote signing call.</param>
    /// <returns>The signature. ECDSA signatures use the IEEE P1363 format unless the key was created otherwise.</returns>
    public abstract ValueTask<byte[]> SignHashAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken = default);

    /// <summary>Verifies a signature over a digest computed with <see cref="HashAlgorithm"/>.</summary>
    /// <param name="hash">The digest.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="cancellationToken">A token to cancel a remote verification call.</param>
    /// <returns><see langword="true"/> when the signature is valid. A malformed signature returns <see langword="false"/>.</returns>
    public abstract ValueTask<bool> VerifyHashAsync(
        ReadOnlyMemory<byte> hash,
        ReadOnlyMemory<byte> signature,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a key over a local <see cref="RSA"/> instance.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="rsa">The key. Holds a private key to sign; a public key is enough to verify.</param>
    /// <param name="algorithm">One of the <c>PS</c> or <c>RS</c> algorithms.</param>
    /// <param name="ownsKey">Whether disposing the signing key disposes <paramref name="rsa"/>.</param>
    /// <returns>The signing key.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="algorithm"/> is not an RSA algorithm, or the key is smaller than <see cref="MinimumRsaKeySize"/> bits.
    /// </exception>
    public static SigningKey FromRsa(string keyId, RSA rsa, SignatureAlgorithm algorithm, bool ownsKey = true)
    {
        ArgumentNullException.ThrowIfNull(rsa);

        if (!IsRsa(algorithm))
        {
            throw new ArgumentException($"{algorithm} is not an RSA algorithm.", nameof(algorithm));
        }

        if (rsa.KeySize < MinimumRsaKeySize)
        {
            throw new ArgumentException($"The RSA key is {rsa.KeySize} bits; at least {MinimumRsaKeySize} are required.", nameof(rsa));
        }

        return new RsaSigningKey(keyId, rsa, algorithm, ownsKey);
    }

    /// <summary>
    /// Creates a key over a local <see cref="ECDsa"/> instance. The algorithm follows the curve: P-256 is
    /// <see cref="SignatureAlgorithm.ES256"/>, P-384 <see cref="SignatureAlgorithm.ES384"/>, P-521
    /// <see cref="SignatureAlgorithm.ES512"/>.
    /// </summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="ecdsa">The key. Holds a private key to sign; a public key is enough to verify.</param>
    /// <param name="signatureFormat">
    /// The signature encoding. Defaults to IEEE P1363, which JWS and most key services use; use
    /// <see cref="DSASignatureFormat.Rfc3279DerSequence"/> for X.509 and TLS interoperability.
    /// </param>
    /// <param name="ownsKey">Whether disposing the signing key disposes <paramref name="ecdsa"/>.</param>
    /// <returns>The signing key.</returns>
    /// <exception cref="ArgumentException">The key is not on the P-256, P-384 or P-521 curve.</exception>
    public static SigningKey FromECDsa(
        string keyId,
        ECDsa ecdsa,
        DSASignatureFormat signatureFormat = DSASignatureFormat.IeeeP1363FixedFieldConcatenation,
        bool ownsKey = true)
    {
        ArgumentNullException.ThrowIfNull(ecdsa);

        SignatureAlgorithm algorithm = GetEcdsaAlgorithm(ecdsa.ExportParameters(includePrivateParameters: false).Curve)
            ?? throw new ArgumentException("The ECDSA key must be on the P-256, P-384 or P-521 curve.", nameof(ecdsa));

        return new EcdsaSigningKey(keyId, ecdsa, algorithm, signatureFormat, ownsKey);
    }

    /// <summary>Returns the digest algorithm <paramref name="algorithm"/> uses.</summary>
    /// <param name="algorithm">The signature algorithm.</param>
    /// <returns>SHA-256, SHA-384 or SHA-512.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="algorithm"/> is not defined.</exception>
    public static HashAlgorithmName GetHashAlgorithm(SignatureAlgorithm algorithm) => algorithm switch
    {
        SignatureAlgorithm.PS256 or SignatureAlgorithm.RS256 or SignatureAlgorithm.ES256 => HashAlgorithmName.SHA256,
        SignatureAlgorithm.PS384 or SignatureAlgorithm.RS384 or SignatureAlgorithm.ES384 => HashAlgorithmName.SHA384,
        SignatureAlgorithm.PS512 or SignatureAlgorithm.RS512 or SignatureAlgorithm.ES512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown signature algorithm."),
    };

    /// <summary>Returns the ECDSA algorithm for a named NIST curve, or <see langword="null"/> for any other curve.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The algorithm, or <see langword="null"/>.</returns>
    public static SignatureAlgorithm? GetEcdsaAlgorithm(ECCurve curve)
    {
        if (!curve.IsNamed || curve.Oid is null)
        {
            return null;
        }

        // Windows can report only the friendly name, other platforms only the value.
        return (curve.Oid.Value, curve.Oid.FriendlyName) switch
        {
            ("1.2.840.10045.3.1.7", _) or (_, "nistP256" or "ECDSA_P256" or "secp256r1") => SignatureAlgorithm.ES256,
            ("1.3.132.0.34", _) or (_, "nistP384" or "ECDSA_P384" or "secp384r1") => SignatureAlgorithm.ES384,
            ("1.3.132.0.35", _) or (_, "nistP521" or "ECDSA_P521" or "secp521r1") => SignatureAlgorithm.ES512,
            _ => null,
        };
    }

    /// <summary>Releases the key.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by the key.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Throws when <paramref name="hash"/> is not the length of a <see cref="HashAlgorithm"/> digest.</summary>
    /// <param name="hash">The digest.</param>
    /// <returns><paramref name="hash"/>.</returns>
    /// <exception cref="ArgumentException">The digest has the wrong length.</exception>
    protected ReadOnlySpan<byte> EnsureHashLength(ReadOnlySpan<byte> hash)
    {
        int expected = GetHashLength(Algorithm);
        if (hash.Length != expected)
        {
            throw new ArgumentException($"{Algorithm} requires a {expected}-byte digest; got {hash.Length} bytes.", nameof(hash));
        }

        return hash;
    }

    internal static int GetHashLength(SignatureAlgorithm algorithm) => GetHashAlgorithm(algorithm).Name switch
    {
        "SHA256" => 32,
        "SHA384" => 48,
        _ => 64,
    };

    internal static bool IsRsa(SignatureAlgorithm algorithm) =>
        algorithm is >= SignatureAlgorithm.PS256 and <= SignatureAlgorithm.RS512;

    internal static RSASignaturePadding GetRsaPadding(SignatureAlgorithm algorithm) =>
        algorithm is >= SignatureAlgorithm.PS256 and <= SignatureAlgorithm.PS512
            ? RSASignaturePadding.Pss
            : RSASignaturePadding.Pkcs1;

    private sealed class RsaSigningKey(string keyId, RSA rsa, SignatureAlgorithm algorithm, bool ownsKey)
        : SigningKey(keyId, algorithm)
    {
        public override ValueTask<byte[]> SignHashAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken = default) =>
            new(rsa.SignHash(EnsureHashLength(hash.Span), HashAlgorithm, GetRsaPadding(Algorithm)));

        public override ValueTask<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default) =>
            new(rsa.VerifyHash(EnsureHashLength(hash.Span), signature.Span, HashAlgorithm, GetRsaPadding(Algorithm)));

        protected override void Dispose(bool disposing)
        {
            if (disposing && ownsKey)
            {
                rsa.Dispose();
            }
        }
    }

    private sealed class EcdsaSigningKey(
        string keyId,
        ECDsa ecdsa,
        SignatureAlgorithm algorithm,
        DSASignatureFormat signatureFormat,
        bool ownsKey)
        : SigningKey(keyId, algorithm)
    {
        public override ValueTask<byte[]> SignHashAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken = default) =>
            new(ecdsa.SignHash(EnsureHashLength(hash.Span), signatureFormat));

        public override ValueTask<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default) =>
            new(ecdsa.VerifyHash(EnsureHashLength(hash.Span), signature.Span, signatureFormat));

        protected override void Dispose(bool disposing)
        {
            if (disposing && ownsKey)
            {
                ecdsa.Dispose();
            }
        }
    }
}

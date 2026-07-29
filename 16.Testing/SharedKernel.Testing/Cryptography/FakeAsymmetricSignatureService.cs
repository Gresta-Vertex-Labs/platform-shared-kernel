using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// Algorithm-agnostic in-memory test double for <see cref="IAsymmetricSignatureService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Computes a deterministic <c>HMACSHA256(data)</c> keyed by <c>SHA256(UTF8.GetBytes(keyId))</c> — a
/// stable, internally-derived per-<c>keyId</c> pseudo-key. NEVER performs real RSA/ECDSA key
/// generation or signing.
/// </para>
/// <para>
/// A single instance works uniformly across the unkeyed default AND both
/// <see cref="SharedKernel.Cryptography.Extensions.CryptographyServiceCollectionExtensions.RsaSignatureServiceKey"/>/
/// <see cref="SharedKernel.Cryptography.Extensions.CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey"/>
/// keyed DI slots that <c>AddSharedKernelCryptography()</c> establishes — a test resolving either
/// keyed service gets a working fake with zero call-site change from production.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> Signatures are HMAC-derived pseudo-signatures, not real
/// RSA/ECDSA — a signature produced by this fake never verifies against a real
/// <c>RsaSignatureService</c>/<c>EcdsaSignatureService</c>, and wiring this into a production DI
/// container would silently replace asymmetric non-repudiation with a shared-derivation HMAC scheme.
/// </para>
/// </remarks>
public sealed class FakeAsymmetricSignatureService : IAsymmetricSignatureService
{
    private readonly ConcurrentQueue<(byte[] Data, string KeyId)> _signedPayloads = new();

    /// <summary>Every (data, keyId) pair ever signed via <see cref="Sign"/>, append-only.</summary>
    public IReadOnlyList<(byte[] Data, string KeyId)> SignedPayloads => _signedPayloads.ToArray();

    /// <inheritdoc />
    public byte[] Sign(byte[] data, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyId);

        byte[] signature = HMACSHA256.HashData(DeriveKey(keyId), data);
        _signedPayloads.Enqueue((data, keyId));
        return signature;
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, string keyId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(keyId);

        byte[] expected = HMACSHA256.HashData(DeriveKey(keyId), data);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }

    private static byte[] DeriveKey(string keyId) => SHA256.HashData(Encoding.UTF8.GetBytes(keyId));
}

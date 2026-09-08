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
/// <para>
/// <b>(P-502/WO-081)</b> <see cref="SignAsync(byte[], string, CancellationToken)"/>/
/// <see cref="VerifyAsync(byte[], byte[], string, CancellationToken)"/> delegate to the exact same
/// internal HMAC computation as the retained synchronous <see cref="Sign"/>/<see cref="Verify"/> —
/// guaranteeing byte-identical results between the sync and async call shapes for the same input,
/// mirroring <c>AesGcmEncryptionService</c>'s own shared-core discipline. <see cref="SignedPayloads"/>
/// is shared unchanged across both call shapes.
/// </para>
/// <para>
/// <b>SCOPE NOTE:</b> this fake is algorithm-agnostic and keyId-derived — it never models a real
/// RSA/ECDSA key or its bit length, so it structurally CANNOT represent <c>SK.01.P493</c>'s
/// <c>Verify</c>-side <c>EnsureMinimumKeySize</c> parity fix (an attacker-supplied signature against
/// an undersized key). A test wanting to prove that specific regression must compose the REAL
/// <c>RsaSignatureService</c>/<c>EcdsaSignatureService</c> against
/// <see cref="FakeAsymmetricKeyProvider"/> instead of this fake.
/// </para>
/// </remarks>
public sealed class FakeAsymmetricSignatureService : IAsymmetricSignatureService
{
    private readonly ConcurrentQueue<(byte[] Data, string KeyId)> _signedPayloads = new();

    /// <summary>Every (data, keyId) pair ever signed via <see cref="Sign"/> or <see cref="SignAsync"/>, append-only.</summary>
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

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — delegates
    /// to <see cref="Sign"/>, guaranteeing a byte-identical signature for the same input. See class
    /// remarks.
    /// </remarks>
    public ValueTask<byte[]> SignAsync(byte[] data, string keyId, CancellationToken ct = default) =>
        new(Sign(data, keyId));

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously via an already-completed <see cref="ValueTask{TResult}"/> — delegates
    /// to <see cref="Verify"/>, guaranteeing an identical result for the same input. See class
    /// remarks.
    /// </remarks>
    public ValueTask<bool> VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default) =>
        new(Verify(data, signature, keyId));

    private static byte[] DeriveKey(string keyId) => SHA256.HashData(Encoding.UTF8.GetBytes(keyId));
}

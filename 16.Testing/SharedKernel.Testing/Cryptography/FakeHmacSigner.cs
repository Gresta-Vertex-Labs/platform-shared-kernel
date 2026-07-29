using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IHmacSigner"/>.
/// </summary>
/// <remarks>
/// <para>
/// Behaviorally identical to production's <c>HmacSha256Signer</c> — real HMACSHA256 with
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> for
/// <see cref="Verify"/> — since HMAC is already fast/deterministic and there is no cost to skip.
/// Shipped for platform-completeness (every <c>01.Core</c>-DI-registered abstraction gets a fake, no
/// exceptions) and call introspection via <see cref="SignedPayloads"/>/<see cref="VerifiedPayloads"/>.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> Although the HMACSHA256 computation itself is
/// algorithmically identical to production, this type must still never be wired into a production DI
/// container — <c>16.Testing</c> packages are never referenced by production code (root
/// <c>CLAUDE.md</c> hard rule), and its unbounded <see cref="SignedPayloads"/>/
/// <see cref="VerifiedPayloads"/> introspection lists would leak memory outside a test's short
/// lifetime.
/// </para>
/// </remarks>
public sealed class FakeHmacSigner : IHmacSigner
{
    private readonly ConcurrentQueue<(byte[] Data, byte[] Secret)> _signedPayloads = new();
    private readonly ConcurrentQueue<(byte[] Data, byte[] Secret)> _verifiedPayloads = new();

    /// <summary>Every (data, secret) pair ever signed via <see cref="Sign"/>, append-only.</summary>
    public IReadOnlyList<(byte[] Data, byte[] Secret)> SignedPayloads => _signedPayloads.ToArray();

    /// <summary>Every (data, secret) pair ever verified via <see cref="Verify"/>, append-only.</summary>
    public IReadOnlyList<(byte[] Data, byte[] Secret)> VerifiedPayloads => _verifiedPayloads.ToArray();

    /// <inheritdoc />
    public byte[] Sign(byte[] data, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(secret);

        _signedPayloads.Enqueue((data, secret));
        return HMACSHA256.HashData(secret, data);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(secret);

        _verifiedPayloads.Enqueue((data, secret));
        byte[] expected = HMACSHA256.HashData(secret, data);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }
}

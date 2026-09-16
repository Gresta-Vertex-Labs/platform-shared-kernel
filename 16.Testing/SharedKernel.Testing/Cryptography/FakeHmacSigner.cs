using System.Collections.Concurrent;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>A test double for <see cref="IHmacSigner"/> that computes real HMAC-SHA256 and records every call.</summary>
/// <remarks>Enforces the same 32-byte minimum key length as <see cref="HmacSha256Signer"/>.</remarks>
public sealed class FakeHmacSigner : IHmacSigner
{
    private readonly HmacSha256Signer _inner = new();
    private readonly ConcurrentQueue<(byte[] Data, byte[] Key)> _signed = new();
    private readonly ConcurrentQueue<(byte[] Data, byte[] Key)> _verified = new();

    /// <summary>Every signing call, with its data and key.</summary>
    public IReadOnlyList<(byte[] Data, byte[] Key)> SignedPayloads => [.. _signed];

    /// <summary>Every verification call, with its data and key.</summary>
    public IReadOnlyList<(byte[] Data, byte[] Key)> VerifiedPayloads => [.. _verified];

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key)
    {
        byte[] signature = _inner.Sign(data, key);
        _signed.Enqueue((data.ToArray(), key.ToArray()));
        return signature;
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> key)
    {
        bool valid = _inner.Verify(data, signature, key);
        _verified.Enqueue((data.ToArray(), key.ToArray()));
        return valid;
    }
}

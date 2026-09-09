using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using HMACSHA256. <see cref="Verify"/> uses
/// <see cref="CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
/// for a timing-attack-resistant comparison — never <c>==</c> or <c>SequenceEqual</c>.
/// </summary>
public sealed class HmacSha256Signer : IHmacSigner
{
    /// <inheritdoc />
    public byte[] Sign(byte[] data, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(secret);

        return HMACSHA256.HashData(secret, data);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the internal <c>expected</c> comparison buffer is
    /// zeroed via <see cref="CryptographicOperations.ZeroMemory"/> once the comparison has run —
    /// this package fully owns its lifetime and never hands it back to the caller. Unlike
    /// <see cref="Verify"/>, <see cref="Sign"/>'s return value IS the caller's needed output and
    /// must never be zeroed.
    /// </b>
    /// </remarks>
    public bool Verify(byte[] data, byte[] signature, byte[] secret) =>
        Verify(data, signature, secret, captureExpectedForTesting: null);

    /// <summary>
    /// Test-only seam (P-524/WO-083), gated via <c>InternalsVisibleTo</c> to this package's own
    /// <c>.Tests</c> project: identical to <see cref="Verify(byte[], byte[], byte[])"/>, except a
    /// caller-supplied callback is invoked with the freshly-computed <c>expected</c> buffer
    /// BEFORE it is zeroed, letting a test capture the exact same array reference and assert it is
    /// genuinely all-zero bytes once this call returns. Never invoked by any production code path —
    /// the public <see cref="Verify(byte[], byte[], byte[])"/> overload always passes
    /// <see langword="null"/>.
    /// </summary>
    internal bool Verify(byte[] data, byte[] signature, byte[] secret, Action<byte[]>? captureExpectedForTesting)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] expected = HMACSHA256.HashData(secret, data);
        captureExpectedForTesting?.Invoke(expected);

        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }
}
